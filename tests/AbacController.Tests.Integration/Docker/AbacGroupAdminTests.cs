using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

/// <summary>
/// End-to-end tests for the Phase 2 ABAC group admin API
/// (<c>/pap/api/groups</c>) against the shipped Docker image.
/// Exercises the full lifecycle (create → list → get → add direct user
/// member → add Keycloak-group inherit → list members → remove
/// members → delete group) plus the tenant-scoped reverse lookup
/// consumed by the visibility endpoint.
/// </summary>
[Collection("Docker")]
public sealed class AbacGroupAdminTests
{
    private readonly AbacContainerFixture _fixture;

    public AbacGroupAdminTests(AbacContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GroupLifecycle_CreateListAddMembersDelete_Succeeds()
    {
        const string tenantId = "tenant-groups-lifecycle";
        var groupName = $"IT-{Guid.NewGuid():N}";

        // Create.
        var createBody = new { name = groupName, description = "end-to-end lifecycle group" };
        var createResponse = await SendAsync(HttpMethod.Post, $"/pap/api/groups/{tenantId}", tenantId, createBody);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var groupId = created.GetProperty("id").GetGuid();
        Assert.Equal(groupName, created.GetProperty("name").GetString());
        Assert.Equal(tenantId, created.GetProperty("tenantId").GetString());

        // List — should contain the new group.
        var listResponse = await SendAsync(HttpMethod.Get, $"/pap/api/groups/{tenantId}", tenantId);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(list);
        Assert.Contains(list!, g => g.GetProperty("id").GetGuid() == groupId);

        // Get by id.
        var getResponse = await SendAsync(HttpMethod.Get, $"/pap/api/groups/{tenantId}/{groupId}", tenantId);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(groupId, fetched.GetProperty("id").GetGuid());

        // Add a direct user member.
        const string userId = "alice@abac-dev.local";
        var addUser = await SendAsync(HttpMethod.Post,
            $"/pap/api/groups/{tenantId}/{groupId}/members/users/{userId}", tenantId);
        Assert.Equal(HttpStatusCode.OK, addUser.StatusCode);

        // Add an inherited Keycloak group reference.
        const string keycloakGroupId = "nato";
        var addKc = await SendAsync(HttpMethod.Post,
            $"/pap/api/groups/{tenantId}/{groupId}/members/keycloak-groups/{keycloakGroupId}", tenantId);
        Assert.Equal(HttpStatusCode.OK, addKc.StatusCode);

        // List members — expect 2 rows (one per kind).
        var membersResponse = await SendAsync(HttpMethod.Get,
            $"/pap/api/groups/{tenantId}/{groupId}/members", tenantId);
        Assert.Equal(HttpStatusCode.OK, membersResponse.StatusCode);
        var members = await membersResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(members);
        Assert.Equal(2, members!.Length);
        Assert.Contains(members, m =>
            m.GetProperty("memberId").GetString() == userId
            && m.GetProperty("kind").GetInt32() == 0);
        Assert.Contains(members, m =>
            m.GetProperty("memberId").GetString() == keycloakGroupId
            && m.GetProperty("kind").GetInt32() == 1);

        // Remove the Keycloak member.
        var removeKc = await SendAsync(HttpMethod.Delete,
            $"/pap/api/groups/{tenantId}/{groupId}/members/keycloak-groups/{keycloakGroupId}", tenantId);
        Assert.Equal(HttpStatusCode.NoContent, removeKc.StatusCode);

        // Remove the user member.
        var removeUser = await SendAsync(HttpMethod.Delete,
            $"/pap/api/groups/{tenantId}/{groupId}/members/users/{userId}", tenantId);
        Assert.Equal(HttpStatusCode.NoContent, removeUser.StatusCode);

        // Delete the group.
        var deleteGroup = await SendAsync(HttpMethod.Delete, $"/pap/api/groups/{tenantId}/{groupId}", tenantId);
        Assert.Equal(HttpStatusCode.NoContent, deleteGroup.StatusCode);

        // Second delete → 404.
        var secondDelete = await SendAsync(HttpMethod.Delete, $"/pap/api/groups/{tenantId}/{groupId}", tenantId);
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    [Fact]
    public async Task CreateGroup_WithoutName_ReturnsBadRequest()
    {
        const string tenantId = "tenant-groups-validation";
        var body = new { name = "", description = "missing name" };
        var response = await SendAsync(HttpMethod.Post, $"/pap/api/groups/{tenantId}", tenantId, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetGroup_Unknown_ReturnsNotFound()
    {
        const string tenantId = "tenant-groups-notfound";
        var response = await SendAsync(HttpMethod.Get, $"/pap/api/groups/{tenantId}/{Guid.NewGuid()}", tenantId);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? tenantId,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            request.Headers.Add("X-Tenant-Id", tenantId);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _fixture.HttpClient.SendAsync(request);
    }
}
