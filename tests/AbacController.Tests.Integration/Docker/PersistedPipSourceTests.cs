using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Api.Grpc;
using AbacController.Tests.Integration.Fixtures;
using Grpc.Net.Client;

namespace AbacController.Tests.Integration.Docker;

[Collection("Docker")]
public sealed class PersistedPipSourceTests
{
    private readonly AbacContainerFixture _fixture;
    private readonly HttpClient _http;

    public PersistedPipSourceTests(AbacContainerFixture fixture)
    {
        _fixture = fixture;
        _http = fixture.HttpClient;
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    [Fact]
    public async Task PersistedStaticSource_ResolvesThroughRuntime_AndPermitsContainerEvaluation()
    {
        await ImportSpifAsync();

        var policySetId = $"pip-policy-set-{Guid.NewGuid():N}";
        var policyId = $"pip-policy-{Guid.NewGuid():N}";
        var sourceId = $"pip-static-{Guid.NewGuid():N}";

        await CreatePolicySetAsync(policySetId);
        await CreatePolicyAsync(policyId, policySetId);
        await CreatePolicyVersionAsync(policyId);

        var upsertResponse = await _http.PutAsJsonAsync($"/pip/api/sources/{sourceId}", new
        {
            id = sourceId,
            name = "Persisted runtime static source",
            sourceType = "static",
            configJson = JsonSerializer.Serialize(new
            {
                subjects = new Dictionary<string, object>
                {
                    ["user-123"] = new
                    {
                        department = "engineering"
                    }
                }
            }),
            providesAttributes = "department",
            priority = 5,
            isRequired = false,
            cacheEnabled = true,
            cacheTtlSeconds = 300,
            cacheMaxEntries = 100
        });
        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);

        var testResponse = await _http.PostAsync($"/pip/api/sources/{sourceId}/test", content: null);
        Assert.Equal(HttpStatusCode.OK, testResponse.StatusCode);
        var testPayload = await testResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(testPayload.GetProperty("healthy").GetBoolean());
        Assert.Equal(sourceId, testPayload.GetProperty("id").GetString());

        using var grpcChannel = CreateGrpcChannel();
        var pipClient = new PipApi.PipApiClient(grpcChannel);

        var resolved = await pipClient.ResolveAsync(new ResolveAttributesRequestMessage
        {
            SubjectId = "user-123",
            SubjectType = "user",
            RequestedAttributes = { "department" }
        });

        Assert.True(resolved.Success, resolved.Error);
        var department = Assert.Single(resolved.Values);
        Assert.Equal("department", department.Name);
        Assert.Equal("engineering", department.Value.StringValue);
        Assert.Equal(sourceId, department.SourceId);
        Assert.Equal("static", department.SourceType);
        Assert.Empty(resolved.Missing);

        var evaluationResponse = await _http.PostAsJsonAsync("/access/v1/evaluation", new
        {
            requestId = $"pip-eval-{Guid.NewGuid():N}",
            subject = new
            {
                type = "user",
                id = "user-123",
                properties = new
                {
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
                                    new
                                    {
                                        tagOid = "SCI",
                                        tagType = "Restrictive",
                                        bits = new[] { 10 }
                                    }
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
                id = "doc-pip-runtime",
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
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, evaluationResponse.StatusCode);
        var evaluation = await evaluationResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(evaluation.GetProperty("decision").GetBoolean());
        Assert.Contains("Permitted by", evaluation.GetProperty("context").GetProperty("reason_admin").GetString());
    }

    private async Task ImportSpifAsync()
    {
        var response = await _http.PostAsJsonAsync("/pap/api/spifs/import", new
        {
            xml = TestSpifSamples.BasicPolicy,
            activate = true,
            setAsDefault = true,
            importedBy = "docker-pip-runtime"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task CreatePolicySetAsync(string policySetId)
    {
        var response = await _http.PutAsJsonAsync($"/pap/api/policy-sets/{policySetId}", new
        {
            id = policySetId,
            name = "Persisted PIP runtime policy set",
            description = "Verifies persisted sources feed runtime evaluation",
            combiningAlgorithm = "deny-overrides",
            isActive = true,
            policies = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task CreatePolicyAsync(string policyId, string policySetId)
    {
        var response = await _http.PutAsJsonAsync($"/pap/api/policies/{policyId}", new
        {
            id = policyId,
            policySetId,
            name = "Permit engineering readers via PIP enrichment",
            format = "native",
            versions = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task CreatePolicyVersionAsync(string policyId)
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
            }
        });

        var response = await _http.PostAsJsonAsync($"/pap/api/policies/{policyId}/versions", new
        {
            content = policyDocument,
            createdBy = "docker-pip-runtime",
            activate = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
