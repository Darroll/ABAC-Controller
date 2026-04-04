using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

[Collection("Docker")]
public sealed class AuthZenEvaluationTests
{
    private readonly AbacContainerFixture _fixture;

    public AuthZenEvaluationTests(AbacContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BatchEvaluation_ReturnsOneResultPerRequest()
    {
        var policySetResponse = await _fixture.HttpClient.PutAsJsonAsync("/pap/api/policy-sets/test-batch-ps", new
        {
            id = "test-batch-ps",
            name = "Batch Policy Set",
            combiningAlgorithm = "deny-overrides",
            isActive = true
        });
        policySetResponse.EnsureSuccessStatusCode();

        var policyResponse = await _fixture.HttpClient.PutAsJsonAsync("/pap/api/policies/test-batch-policy", new
        {
            id = "test-batch-policy",
            policySetId = "test-batch-ps",
            name = "Permit Read",
            format = "native"
        });
        policyResponse.EnsureSuccessStatusCode();

        var versionResponse = await _fixture.HttpClient.PostAsJsonAsync("/pap/api/policies/test-batch-policy/versions", new
        {
            content = "allow if subject.id == 'alice' and action.name == 'read'",
            activate = true
        });
        versionResponse.EnsureSuccessStatusCode();

        var response = await _fixture.HttpClient.PostAsJsonAsync("/access/v1/evaluations", new
        {
            evaluations = new object[]
            {
                new
                {
                    subject = new { type = "user", id = "alice", properties = new { } },
                    action = new { name = "read", properties = new { } },
                    resource = new { type = "document", id = "doc-1", properties = new { } }
                },
                new
                {
                    subject = new { type = "user", id = "alice", properties = new { } },
                    action = new { name = "write", properties = new { } },
                    resource = new { type = "document", id = "doc-2", properties = new { } }
                }
            }
        });

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.TryGetProperty("evaluations", out var evaluations));
        Assert.Equal(2, evaluations.GetArrayLength());
        Assert.All(evaluations.EnumerateArray(), item =>
        {
            Assert.True(item.TryGetProperty("decision", out _));
            Assert.True(item.TryGetProperty("context", out _));
        });
    }

    [Fact]
    public async Task BatchEvaluation_EmptyBatch_ReturnsBadRequest()
    {
        var response = await _fixture.HttpClient.PostAsJsonAsync("/access/v1/evaluations", new
        {
            evaluations = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AsyncEvaluation_QueuesAndCompletes()
    {
        var callbackUrl = "http://127.0.0.1:9/callback";
        var enqueueResponse = await _fixture.HttpClient.PostAsJsonAsync("/pdp/api/evaluate/async", new
        {
            callbackUrl,
            subject = new { type = "user", id = "alice", properties = new { } },
            action = new { name = "read", properties = new { } },
            resource = new { type = "document", id = "doc-async", properties = new { } }
        });

        Assert.Equal(HttpStatusCode.Accepted, enqueueResponse.StatusCode);
        var accepted = await enqueueResponse.Content.ReadFromJsonAsync<JsonElement>();
        var evaluationId = accepted.GetProperty("evaluationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(evaluationId));

        JsonElement statusPayload = default;
        var completed = false;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(250);
            var statusResponse = await _fixture.HttpClient.GetAsync($"/pdp/api/evaluate/async/{evaluationId}/status");
            statusResponse.EnsureSuccessStatusCode();
            statusPayload = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
            if (string.Equals(statusPayload.GetProperty("status").GetString(), "completed", StringComparison.Ordinal))
            {
                completed = true;
                break;
            }
        }

        Assert.True(completed, "Async evaluation did not complete within the expected polling window.");
        Assert.True(statusPayload.TryGetProperty("result", out var result));
        Assert.True(result.TryGetProperty("decision", out _));
    }
}
