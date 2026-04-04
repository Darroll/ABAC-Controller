using System.Text.Json;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Pdp;

namespace AbacController.Tests.Unit;

public sealed class XacmlJsonMapperTests
{
    [Fact]
    public void MapFromXacmlJson_ParsesStandardRequest()
    {
        var json = """
        {
            "Request": {
                "AccessSubject": [{
                    "Attribute": [
                        { "AttributeId": "urn:oasis:names:tc:xacml:1.0:subject:subject-id", "Value": "alice" },
                        { "AttributeId": "department", "Value": "ENG" }
                    ]
                }],
                "Resource": [{
                    "Attribute": [
                        { "AttributeId": "urn:oasis:names:tc:xacml:1.0:resource:resource-id", "Value": "doc-123" },
                        { "AttributeId": "resource-type", "Value": "document" }
                    ]
                }],
                "Action": [{
                    "Attribute": [
                        { "AttributeId": "urn:oasis:names:tc:xacml:1.0:action:action-id", "Value": "read" }
                    ]
                }],
                "Environment": [{
                    "Attribute": [
                        { "AttributeId": "current-time", "Value": "2025-01-15T10:00:00Z" }
                    ]
                }]
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var request = XacmlJsonMapper.MapFromXacmlJson(doc.RootElement);

        Assert.NotNull(request);
        Assert.Equal("alice", request.Subject.Id);
        Assert.Equal("ENG", request.Subject.Properties["department"]);
        Assert.Equal("read", request.Action.Name);
        Assert.Equal("doc-123", request.Resource.Id);
        Assert.Equal("document", request.Resource.Type);
        Assert.NotNull(request.Context);
        Assert.Equal("2025-01-15T10:00:00Z", request.Context.Environment["current-time"]);
    }

    [Fact]
    public void MapFromXacmlJson_ReturnsNull_ForInvalidFormat()
    {
        var json = """{ "notARequest": true }""";
        using var doc = JsonDocument.Parse(json);
        var result = XacmlJsonMapper.MapFromXacmlJson(doc.RootElement);
        Assert.Null(result);
    }

    [Fact]
    public void MapToXacmlJsonResponse_ProducesValidXacmlFormat()
    {
        var result = new EvaluationResult
        {
            DecisionId = "d-123",
            Decision = Decision.Permit,
            Status = new StatusInfo { Code = "ok", Message = "Access granted" },
            Obligations =
            [
                new Obligation
                {
                    Id = "log-access",
                    Attributes = new Dictionary<string, object?> { ["logLevel"] = "info" }
                }
            ],
            Advice =
            [
                new Advice
                {
                    Id = "notify-owner",
                    Attributes = new Dictionary<string, object?> { ["method"] = "email" }
                }
            ]
        };

        var response = XacmlJsonMapper.MapToXacmlJsonResponse(result);
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("Response", out var responseArray));
        Assert.Equal(JsonValueKind.Array, responseArray.ValueKind);

        var firstResult = responseArray[0];
        Assert.Equal("Permit", firstResult.GetProperty("Decision").GetString());
        Assert.True(firstResult.TryGetProperty("Obligations", out _));
        Assert.True(firstResult.TryGetProperty("AssociatedAdvice", out _));
        Assert.True(firstResult.TryGetProperty("Status", out var status));
        Assert.Equal("urn:oasis:names:tc:xacml:1.0:status:ok",
            status.GetProperty("StatusCode").GetProperty("Value").GetString());
    }

    [Fact]
    public void MapToXacmlJsonResponse_HandlesAllDecisionTypes()
    {
        var decisions = new[] { Decision.Permit, Decision.Deny, Decision.NotApplicable, Decision.Indeterminate };

        foreach (var decision in decisions)
        {
            var result = new EvaluationResult { DecisionId = "d", Decision = decision };
            var response = XacmlJsonMapper.MapToXacmlJsonResponse(result);
            var json = JsonSerializer.Serialize(response);
            using var doc = JsonDocument.Parse(json);

            var firstResult = doc.RootElement.GetProperty("Response")[0];
            var decisionStr = firstResult.GetProperty("Decision").GetString();
            Assert.NotNull(decisionStr);
            Assert.NotEmpty(decisionStr);
        }
    }

    [Fact]
    public void MapToXacmlJsonBatchResponse_ProducesArrayOfResults()
    {
        var batchResult = new BatchEvaluationResult
        {
            BatchDecisionId = "batch-1",
            Evaluations =
            [
                new EvaluationResult { DecisionId = "d1", Decision = Decision.Permit },
                new EvaluationResult { DecisionId = "d2", Decision = Decision.Deny }
            ]
        };

        var response = XacmlJsonMapper.MapToXacmlJsonBatchResponse(batchResult);
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        var responseArray = doc.RootElement.GetProperty("Response");
        Assert.Equal(2, responseArray.GetArrayLength());
        Assert.Equal("Permit", responseArray[0].GetProperty("Decision").GetString());
        Assert.Equal("Deny", responseArray[1].GetProperty("Decision").GetString());
    }

    [Fact]
    public void MapFromXacmlJson_StripsPrefixes()
    {
        var json = """
        {
            "Request": {
                "AccessSubject": [{
                    "Attribute": [
                        { "AttributeId": "urn:oasis:names:tc:xacml:1.0:subject:subject-id", "Value": "bob" }
                    ]
                }],
                "Action": [{
                    "Attribute": [
                        { "AttributeId": "urn:oasis:names:tc:xacml:1.0:action:action-id", "Value": "write" }
                    ]
                }],
                "Resource": [{
                    "Attribute": [
                        { "AttributeId": "urn:oasis:names:tc:xacml:1.0:resource:resource-id", "Value": "file-42" }
                    ]
                }]
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var request = XacmlJsonMapper.MapFromXacmlJson(doc.RootElement);

        Assert.NotNull(request);
        Assert.Equal("bob", request.Subject.Id);
        Assert.Equal("write", request.Action.Name);
        Assert.Equal("file-42", request.Resource.Id);
    }
}
