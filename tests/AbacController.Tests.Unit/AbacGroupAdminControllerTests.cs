using AbacController.Api.Controllers;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Groups;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Repositories;
using AbacController.Pdp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// Direct controller-level tests for <see cref="AbacGroupAdminController"/>.
/// Verifies CRUD round-trips, validation, audit emission, webhook publication
/// on every write path, and that membership writes invalidate the resolver
/// cache for the tenant.
/// </summary>
public sealed class AbacGroupAdminControllerTests : IDisposable
{
    private const string Tenant = "tenant-a";

    private readonly AbacDbContext _db;
    private readonly AbacGroupRepository _repo;
    private readonly StubAuditWriter _audit = new();
    private readonly StubPublisher _publisher = new();
    private readonly GroupMembershipCache _cache = new(TimeSpan.FromMinutes(5));
    private readonly GroupMembershipResolver _resolver;
    private readonly AbacGroupAdminController _controller;

    public AbacGroupAdminControllerTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new AbacGroupRepository(_db);
        _resolver = new GroupMembershipResolver(_repo, _cache);
        _controller = new AbacGroupAdminController(_repo, _resolver, _audit, _publisher)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task Create_Persists_AuditsAndPublishes()
    {
        var result = await _controller.Create(Tenant,
            new AbacGroupAdminController.AbacGroupCreateRequest { Name = "nato" },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var group = Assert.IsType<AbacGroup>(ok.Value);
        Assert.Equal("nato", group.Name);
        Assert.Single(_audit.Events, e => e.ActionName == "create_abac_group");
        Assert.Single(_publisher.Events, e => e.EventType == WebhookEventTypes.AbacGroupChanged);
    }

    [Fact]
    public async Task Create_RejectsEmptyName()
    {
        var result = await _controller.Create(Tenant,
            new AbacGroupAdminController.AbacGroupCreateRequest { Name = "" },
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_audit.Events);
        Assert.Empty(_publisher.Events);
    }

    [Fact]
    public async Task Get_NotFound_WhenMissing()
    {
        var result = await _controller.Get(Tenant, Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_RemovesGroupAndEmitsEvent()
    {
        var created = await _controller.Create(Tenant,
            new AbacGroupAdminController.AbacGroupCreateRequest { Name = "doomed" },
            CancellationToken.None);
        var group = (AbacGroup)((OkObjectResult)created.Result!).Value!;
        _audit.Events.Clear();
        _publisher.Events.Clear();

        var del = await _controller.Delete(Tenant, group.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(del);
        Assert.Single(_audit.Events, e => e.ActionName == "delete_abac_group");
        Assert.Single(_publisher.Events);
    }

    [Fact]
    public async Task Delete_NotFound_WhenMissing()
    {
        var result = await _controller.Delete(Tenant, Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task AddDirectUser_PersistsAndEmitsEvent_AndInvalidatesCache()
    {
        var created = await _controller.Create(Tenant,
            new AbacGroupAdminController.AbacGroupCreateRequest { Name = "ops" },
            CancellationToken.None);
        var group = (AbacGroup)((OkObjectResult)created.Result!).Value!;

        // Warm the cache by resolving once.
        await _resolver.ResolveAsync(Tenant, "alice", Array.Empty<string>());
        _publisher.Events.Clear();
        _audit.Events.Clear();

        var add = await _controller.AddDirectUser(Tenant, group.Id, "alice", CancellationToken.None);

        Assert.IsType<OkObjectResult>(add.Result);
        Assert.Single(_audit.Events, e => e.ActionName == "add_group_user_member");
        Assert.Single(_publisher.Events);

        // After invalidation the next resolve should see Alice's membership.
        var resolved = await _resolver.ResolveAsync(Tenant, "alice", Array.Empty<string>());
        Assert.Contains(group.Id, resolved);
    }

    [Fact]
    public async Task RemoveDirectUser_NotFound_WhenAbsent()
    {
        var created = await _controller.Create(Tenant,
            new AbacGroupAdminController.AbacGroupCreateRequest { Name = "ops" },
            CancellationToken.None);
        var group = (AbacGroup)((OkObjectResult)created.Result!).Value!;

        var result = await _controller.RemoveDirectUser(Tenant, group.Id, "ghost", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task AddKeycloakGroup_PersistsAndEmitsEvent()
    {
        var created = await _controller.Create(Tenant,
            new AbacGroupAdminController.AbacGroupCreateRequest { Name = "nato-readers" },
            CancellationToken.None);
        var group = (AbacGroup)((OkObjectResult)created.Result!).Value!;
        _publisher.Events.Clear();

        var add = await _controller.AddKeycloakGroup(Tenant, group.Id, "kc-nato", CancellationToken.None);

        Assert.IsType<OkObjectResult>(add.Result);
        Assert.Single(_publisher.Events);
        var members = await _controller.ListMembers(Tenant, group.Id, CancellationToken.None);
        Assert.Single(members, m => m.Kind == AbacGroupMemberKind.KeycloakGroup && m.MemberId == "kc-nato");
    }

    [Fact]
    public async Task RemoveKeycloakGroup_NotFound_WhenAbsent()
    {
        var created = await _controller.Create(Tenant,
            new AbacGroupAdminController.AbacGroupCreateRequest { Name = "g" },
            CancellationToken.None);
        var group = (AbacGroup)((OkObjectResult)created.Result!).Value!;

        var result = await _controller.RemoveKeycloakGroup(Tenant, group.Id, "kc-ghost", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task ListMembers_ReturnsBothKinds()
    {
        var created = await _controller.Create(Tenant,
            new AbacGroupAdminController.AbacGroupCreateRequest { Name = "g" },
            CancellationToken.None);
        var group = (AbacGroup)((OkObjectResult)created.Result!).Value!;

        await _controller.AddDirectUser(Tenant, group.Id, "alice", CancellationToken.None);
        await _controller.AddKeycloakGroup(Tenant, group.Id, "kc-nato", CancellationToken.None);

        var members = await _controller.ListMembers(Tenant, group.Id, CancellationToken.None);
        Assert.Equal(2, members.Count);
        Assert.Contains(members, m => m.Kind == AbacGroupMemberKind.User);
        Assert.Contains(members, m => m.Kind == AbacGroupMemberKind.KeycloakGroup);
    }

    // ── Stubs ──

    private sealed class StubAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = new();
        public void Write(AuditEvent auditEvent) => Events.Add(auditEvent);
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubPublisher : IWebhookPublisher
    {
        public List<(string EventType, object Payload, string? TenantId)> Events { get; } = new();
        public Task PublishAsync(string eventType, object payload, string? tenantId, CancellationToken ct = default)
        {
            Events.Add((eventType, payload, tenantId));
            return Task.CompletedTask;
        }
    }
}
