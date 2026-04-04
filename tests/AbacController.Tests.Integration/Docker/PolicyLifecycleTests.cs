using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Api.Grpc;
using AbacController.Tests.Integration.Fixtures;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.Data.Sqlite;

namespace AbacController.Tests.Integration.Docker;

[Collection("Docker")]
public sealed class PolicyLifecycleTests
{
    private readonly AbacContainerFixture _fixture;
    private readonly HttpClient _http;

    public PolicyLifecycleTests(AbacContainerFixture fixture)
    {
        _fixture = fixture;
        _http = fixture.HttpClient;
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    [Fact]
    public async Task PolicyLifecycle_EndToEndDockerFlow_ExpectedBehavior()
    {
        await ImportSpifAsync();

        var policySetId = $"policy-set-{Guid.NewGuid():N}";
        var policyId = $"policy-{Guid.NewGuid():N}";

        await CreatePolicySetAsync(policySetId);
        await CreatePolicyAsync(policyId, policySetId);
        var version = await CreatePolicyVersionAsync(policyId);
        Assert.Equal(1, version.GetProperty("versionNumber").GetInt32());
        Assert.True(version.GetProperty("isActive").GetBoolean());

        var versionsResponse = await _http.GetAsync($"/api/v1/pap/policies/{policyId}/versions");
        Assert.Equal(HttpStatusCode.OK, versionsResponse.StatusCode);
        var versions = await versionsResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(versions);
        Assert.Single(versions);

        var authZenResponse = await _http.PostAsJsonAsync("/access/v1/evaluation", BuildAuthZenEvaluationRequest("engineering"));
        Assert.Equal(HttpStatusCode.OK, authZenResponse.StatusCode);
        Assert.True(authZenResponse.Headers.Contains("X-ABAC-Decision-Id"));

        var authZenBody = await authZenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(authZenBody);
        Assert.True(authZenBody.RootElement.GetProperty("decision").GetBoolean());
        Assert.Contains("Permitted by", authZenBody.RootElement.GetProperty("context").GetProperty("reason_admin").GetString());

        using var grpcChannel = CreateGrpcChannel();
        var systemClient = new SystemApi.SystemApiClient(grpcChannel);
        var pepClient = new PepApi.PepApiClient(grpcChannel);
        var papClient = new PapApi.PapApiClient(grpcChannel);

        var grpcStatus = await systemClient.GetStatusAsync(new GetStatusRequestMessage());
        Assert.True(grpcStatus.Ready);
        Assert.Contains("stanag4774-xml", grpcStatus.RegisteredLabelCodecs);

        var grpcSpifs = await papClient.ListSpifsAsync(new ListSpifsRequestMessage());
        Assert.Contains(grpcSpifs.Spifs, spif => spif.PolicyOid == "1.2.3.4");

        var encodedLabel = await pepClient.EncodeLabelAsync(new EncodeLabelRequestMessage
        {
            CodecId = "stanag4774-xml",
            PolicyOid = "1.2.3.4",
            Label = BuildGrpcSecurityLabel()
        });
        Assert.True(encodedLabel.Success, encodedLabel.Error);
        Assert.Contains("<", encodedLabel.Content);

        var bindResponse = await _http.PostAsJsonAsync("/api/v1/pep/metadata/bind", new
        {
            bindingId = "bind-001",
            labelXml = encodedLabel.Content,
            mediaType = "text/plain",
            payloadText = "hello integration"
        });
        Assert.Equal(HttpStatusCode.OK, bindResponse.StatusCode);
        var boundDoc = await bindResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(boundDoc);
        var envelopeXml = boundDoc.RootElement.GetProperty("envelopeXml").GetString();
        Assert.False(string.IsNullOrWhiteSpace(envelopeXml));

        var unbindResponse = await _http.PostAsJsonAsync("/api/v1/pep/metadata/unbind", new
        {
            envelopeXml
        });
        Assert.Equal(HttpStatusCode.OK, unbindResponse.StatusCode);
        var unbound = await unbindResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(unbound);
        Assert.Equal("hello integration", unbound.RootElement.GetProperty("payloadText").GetString());
        Assert.Equal("1.2.3.4", unbound.RootElement.GetProperty("label").GetProperty("policyOid").GetString());

        var auditCount = await WaitForAuditDecisionAsync("user-123", "doc-123", "Permit");
        Assert.True(auditCount > 0);

        var pepCodecs = await pepClient.ListCodecsAsync(new ListCodecsRequestMessage());
        Assert.Contains("stanag4774-xml", pepCodecs.CodecIds);
    }

    private async Task ImportSpifAsync()
    {
        var response = await _http.PostAsJsonAsync("/api/v1/pap/spifs/import", new
        {
            xml = TestSpifSamples.BasicPolicy,
            activate = true,
            setAsDefault = true,
            importedBy = "docker-integration"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task CreatePolicySetAsync(string policySetId)
    {
        var response = await _http.PutAsJsonAsync($"/api/v1/pap/policy-sets/{policySetId}", new
        {
            id = policySetId,
            name = "Docker Integration Policy Set",
            description = "Exercises real Docker lifecycle",
            combiningAlgorithm = "deny-overrides",
            isActive = true,
            policies = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task CreatePolicyAsync(string policyId, string policySetId)
    {
        var response = await _http.PutAsJsonAsync($"/api/v1/pap/policies/{policyId}", new
        {
            id = policyId,
            policySetId,
            name = "Permit engineering readers",
            format = "native",
            versions = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> CreatePolicyVersionAsync(string policyId)
    {
        var policyDocument = JsonSerializer.Serialize(new
        {
            id = "permit-engineering-read",
            effect = "Permit",
            conditions = new object[]
            {
                new { path = "subject.department", equals = "engineering", caseInsensitive = true },
                new { path = "action.name", equals = "read", caseInsensitive = true },
                new { path = "resource.type", equals = "document", caseInsensitive = true }
            },
            obligations = new object[]
            {
                new { id = "log-access", attributes = new { channel = "audit" } }
            }
        });

        var response = await _http.PostAsJsonAsync($"/api/v1/pap/policies/{policyId}/versions", new
        {
            content = policyDocument,
            createdBy = "docker-integration",
            activate = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var version = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(version);
        return version.RootElement.Clone();
    }

    private object BuildAuthZenEvaluationRequest(string department) => new
    {
        requestId = Guid.NewGuid().ToString("N"),
        subject = new
        {
            type = "user",
            id = "user-123",
            properties = new
            {
                department,
                securityClearance = new
                {
                    policyOid = "1.2.3.4",
                    classificationLacvs = new[] { 3 },
                    categoryTagSets = new object[]
                    {
                        new
                        {
                            tagSetOid = "1.2.3.4.1",
                            tags = new object[]
                            {
                                new { tagOid = "SCI", tagType = "Restrictive", bits = new[] { 10 } },
                                new { tagOid = "REL TO", tagType = "Enumerated", enumeratedValues = new[] { 20 } }
                            }
                        }
                    }
                }
            }
        },
        action = new
        {
            name = "read",
            properties = new { }
        },
        resource = new
        {
            type = "document",
            id = "doc-123",
            properties = new
            {
                securityLabel = new
                {
                    policyOid = "1.2.3.4",
                    policyName = "TEST",
                    classificationLacv = 3,
                    classificationName = "SECRET",
                    categoryTagSets = new object[]
                    {
                        new
                        {
                            tagSetOid = "1.2.3.4.1",
                            tags = new object[]
                            {
                                new
                                {
                                    name = "SCI",
                                    tagOid = "SCI",
                                    tagType = "Restrictive",
                                    bits = new[] { 10 },
                                    categories = new object[]
                                    {
                                        new { name = "ALPHA", lacv = 10 }
                                    }
                                },
                                new
                                {
                                    name = "REL TO",
                                    tagOid = "REL TO",
                                    tagType = "Enumerated",
                                    enumType = "Permissive",
                                    enumeratedValues = new[] { 20 },
                                    categories = new object[]
                                    {
                                        new { name = "USA", lacv = 20 }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    };

    private SecurityLabelMessage BuildGrpcSecurityLabel()
    {
        var label = new SecurityLabelMessage
        {
            PolicyOid = "1.2.3.4",
            PolicyName = "TEST",
            ClassificationLacv = 3,
            ClassificationName = "SECRET"
        };

        var tagSet = new LabelCategoryTagSetMessage { TagSetOid = "1.2.3.4.1" };
        tagSet.Tags.Add(new LabelCategoryTagMessage
        {
            Name = "SCI",
            TagOid = "SCI",
            TagType = TagTypeMessage.Restrictive,
            Bits = { 10 },
            Categories = { new LabelCategoryMessage { Name = "ALPHA", Lacv = 10 } }
        });
        tagSet.Tags.Add(new LabelCategoryTagMessage
        {
            Name = "REL TO",
            TagOid = "REL TO",
            TagType = TagTypeMessage.Enumerated,
            EnumType = EnumTypeMessage.Permissive,
            EnumeratedValues = { 20 },
            Categories = { new LabelCategoryMessage { Name = "USA", Lacv = 20 } }
        });
        label.CategoryTagSets.Add(tagSet);
        return label;
    }

    private async Task<int> WaitForAuditDecisionAsync(string subjectId, string resourceId, string decision)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(20);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            try
            {
                await using var connection = new SqliteConnection($"Data Source={_fixture.DatabaseFilePath}");
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM AuditEvents WHERE SubjectId = $subjectId AND ResourceId = $resourceId AND Decision = $decision";
                command.Parameters.AddWithValue("$subjectId", subjectId);
                command.Parameters.AddWithValue("$resourceId", resourceId);
                command.Parameters.AddWithValue("$decision", decision);

                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (count > 0)
                {
                    return count;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(1000);
        }

        var logs = await _fixture.GetLogsAsync();
        throw new Xunit.Sdk.XunitException($"Expected audit record was not persisted in time. Logs:\n{logs}\nLast error: {lastError?.Message}");
    }

    private GrpcChannel CreateGrpcChannel()
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true
        };

        return GrpcChannel.ForAddress(_fixture.GrpcUrl, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
    }
}
