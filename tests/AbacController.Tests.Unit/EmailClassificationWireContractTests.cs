using System.Text.Json;
using AbacController.Api.Controllers;
using AbacController.Core.Domain.Classifications;
using AbacController.Core.Domain.Decisions;

namespace AbacController.Tests.Unit;

/// <summary>
/// Pure wire-contract tests for the Email Classification × ABAC Controller integration.
///
/// These tests exercise the JSON shapes the Email API's <c>AbacClient</c> produces and
/// assert they deserialize cleanly into the ABAC Controller's domain types and DTOs.
/// They catch contract drift between the two services without needing both processes
/// running — if a field is renamed or a type narrowed, these tests fail before any
/// end-to-end test would.
/// </summary>
public sealed class EmailClassificationWireContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ClassificationQueryBody_FromEmailApi_DeserializesIntoAssignmentQuery()
    {
        // This is the exact JSON shape AbacClient.GetActivePoliciesAsync() writes.
        const string body =
            """
            {
              "subject": {
                "type": "user",
                "id": "alice@example.com",
                "properties": {
                  "groups": ["group-alpha", "group-beta"]
                }
              },
              "applicationId": "email-classification",
              "enforceEntitlements": true,
              "includeMarkingData": true,
              "includeCategories": false
            }
            """;

        var query = JsonSerializer.Deserialize<ClassificationAssignmentQuery>(body, JsonOptions);

        Assert.NotNull(query);
        Assert.Equal("email-classification", query!.ApplicationId);
        Assert.True(query.EnforceEntitlements);
        Assert.True(query.IncludeMarkingData);
        Assert.False(query.IncludeCategories);
        Assert.Equal("user", query.Subject.Type);
        Assert.Equal("alice@example.com", query.Subject.Id);

        // The subject groups must survive the round-trip so the engine's
        // ExtractGroups helper can feed them to the EntitlementResolver.
        Assert.True(query.Subject.Properties.ContainsKey("groups"));
    }

    [Fact]
    public void RecipientCheckBody_FromEmailApi_DeserializesIntoRequestRecord()
    {
        // This is the exact JSON shape AbacClient.CheckRecipientsAsync() writes.
        const string body =
            """
            {
              "policyOid": "2.16.840.1.101.2.1.8.2.1",
              "classificationLacv": 3,
              "recipients": [
                { "id": "alice@example.com", "type": "user" },
                { "id": "bob@example.com", "type": "user" }
              ]
            }
            """;

        var request = JsonSerializer.Deserialize<RecipientCheckController.RecipientCheckRequest>(body, JsonOptions);

        Assert.NotNull(request);
        Assert.Equal("2.16.840.1.101.2.1.8.2.1", request!.PolicyOid);
        Assert.Equal(3, request.ClassificationLacv);
        Assert.Equal(2, request.Recipients.Count);
        Assert.Equal("alice@example.com", request.Recipients[0].Id);
        Assert.Equal("user", request.Recipients[0].Type);
    }

    [Fact]
    public void EntitlementBaselineBody_FromEmailAdmin_DeserializesIntoGrantRequest()
    {
        // Shape POSTed by the Blazor Entitlements admin page.
        const string body =
            """
            {
              "policyOid": "2.16.840.1.101.2.1.8.2.1",
              "classificationLacv": 3,
              "tagSetOid": null
            }
            """;

        var request = JsonSerializer.Deserialize<EntitlementAdminController.EntitlementGrantRequest>(body, JsonOptions);

        Assert.NotNull(request);
        Assert.Equal("2.16.840.1.101.2.1.8.2.1", request!.PolicyOid);
        Assert.Equal(3, request.ClassificationLacv);
        Assert.Null(request.TagSetOid);
    }

    [Fact]
    public void WebhookSubscriptionBody_FromEmailApi_DeserializesIntoCreateRequest()
    {
        // Shape POSTed by AbacClient.UpsertWebhookSubscriptionAsync().
        const string body =
            """
            {
              "callbackUrl": "http://email-api:8080/internal/abac-events",
              "secret": "dev-webhook-secret-change-me",
              "eventTypes": "spif.imported,spif.deleted,policy.activated,assignment.changed,application.updated",
              "active": true
            }
            """;

        var request = JsonSerializer.Deserialize<WebhookAdminController.WebhookSubscriptionRequest>(body, JsonOptions);

        Assert.NotNull(request);
        Assert.Equal("http://email-api:8080/internal/abac-events", request!.CallbackUrl);
        Assert.Equal("dev-webhook-secret-change-me", request.Secret);
        Assert.Contains("spif.imported", request.EventTypes);
        Assert.Contains("assignment.changed", request.EventTypes);
        Assert.True(request.Active);
    }

    [Fact]
    public void AllowedClassificationsResponse_SerializesAndRoundTripsIntoEmailApiDto()
    {
        // Build the shape ABAC ClassificationQueryEngine returns and serialize it, then
        // deserialize into the minimal shape AbacClient uses to translate into Email API
        // contracts. This catches drift in either direction.
        var result = new AllowedClassificationsResult
        {
            PolicyOid = "2.16.840.1.101.2.1.8.2.1",
            PolicyName = "Test Policy",
            Classifications =
            [
                new AllowedClassification { Name = "CONFIDENTIAL", Lacv = 2, Hierarchy = 2 },
                new AllowedClassification { Name = "SECRET", Lacv = 3, Hierarchy = 3 }
            ],
            TotalSpifClassifications = 2,
            EvaluationTime = TimeSpan.FromMilliseconds(5)
        };

        var json = JsonSerializer.Serialize(result, JsonOptions);

        // Shape the Email API's AbacClient.AbacAllowedClassificationsResult expects —
        // kept deliberately minimal here.
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2.16.840.1.101.2.1.8.2.1", root.GetProperty("policyOid").GetString());
        Assert.Equal("Test Policy", root.GetProperty("policyName").GetString());

        var classifications = root.GetProperty("classifications");
        Assert.Equal(2, classifications.GetArrayLength());
        Assert.Equal("CONFIDENTIAL", classifications[0].GetProperty("name").GetString());
        Assert.Equal(2, classifications[0].GetProperty("lacv").GetInt32());
        Assert.Equal(3, classifications[1].GetProperty("lacv").GetInt32());
    }
}
