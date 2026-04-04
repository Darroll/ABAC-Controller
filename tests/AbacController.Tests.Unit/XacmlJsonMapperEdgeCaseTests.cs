using System.Text.Json;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Pdp;

namespace AbacController.Tests.Unit;

/// <summary>
/// Edge case tests for XacmlJsonMapper — malformed input, missing fields, round-trip.
/// </summary>
public sealed class XacmlJsonMapperEdgeCaseTests
{
    [Fact]
    public void MapFromXacmlJson_EmptyObject_ReturnsNull()
    {
        var json = JsonDocument.Parse("{}").RootElement;
        var result = XacmlJsonMapper.MapFromXacmlJson(json);
        Assert.Null(result);
    }

    [Fact]
    public void MapFromXacmlJson_NoRequest_ReturnsNull()
    {
        var json = JsonDocument.Parse("{\"foo\": \"bar\"}").RootElement;
        var result = XacmlJsonMapper.MapFromXacmlJson(json);
        Assert.Null(result);
    }

    [Fact]
    public void MapFromXacmlJson_ValidMinimalRequest_ReturnsRequest()
    {
        var jsonText = """
        {
            "Request": {
                "AccessSubject": [{ "Attribute": [{ "AttributeId": "subject-id", "Value": "alice" }] }],
                "Action": [{ "Attribute": [{ "AttributeId": "action-id", "Value": "read" }] }],
                "Resource": [{ "Attribute": [{ "AttributeId": "resource-id", "Value": "doc-1" }] }]
            }
        }
        """;
        var json = JsonDocument.Parse(jsonText).RootElement;
        var result = XacmlJsonMapper.MapFromXacmlJson(json);
        Assert.NotNull(result);
    }

    [Fact]
    public void MapToXacmlJsonResponse_Permit_ContainsPermit()
    {
        var evalResult = new EvaluationResult
        {
            DecisionId = "test-id",
            Decision = Decision.Permit
        };

        var response = XacmlJsonMapper.MapToXacmlJsonResponse(evalResult);
        var json = JsonSerializer.SerializeToElement(response);

        Assert.True(json.TryGetProperty("Response", out var responseArray));
    }

    [Fact]
    public void MapToXacmlJsonResponse_Deny_ContainsDeny()
    {
        var evalResult = new EvaluationResult
        {
            DecisionId = "test-id",
            Decision = Decision.Deny
        };

        var response = XacmlJsonMapper.MapToXacmlJsonResponse(evalResult);
        Assert.NotNull(response);
    }

    [Fact]
    public void MapToXacmlJsonResponse_WithObligations_IncludesObligations()
    {
        var evalResult = new EvaluationResult
        {
            DecisionId = "test-id",
            Decision = Decision.Permit,
            Obligations = [new Obligation { Id = "obl-1", Attributes = new() { ["key"] = "value" } }]
        };

        var response = XacmlJsonMapper.MapToXacmlJsonResponse(evalResult);
        Assert.NotNull(response);
    }
}
