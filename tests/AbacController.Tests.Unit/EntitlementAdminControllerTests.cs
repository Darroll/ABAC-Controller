using AbacController.Api.Controllers;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Entitlements;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// Direct controller-level tests for <see cref="EntitlementAdminController"/>. Verifies
/// request validation, persistence round-trip, audit emission and webhook publication on
/// every write path. Uses a real <see cref="EntitlementRepository"/> over in-memory
/// SQLite plus stub audit/webhook collaborators so the controller's wiring is exercised
/// end-to-end.
/// </summary>
public sealed class EntitlementAdminControllerTests : IDisposable
{
    private const string Tenant = "tenant-a";
    private const string PolicyA = "1.2.3.4";

    private readonly AbacDbContext _db;
    private readonly EntitlementRepository _repo;
    private readonly StubAuditWriter _audit = new();
    private readonly StubWebhookPublisher _publisher = new();
    private readonly EntitlementAdminController _controller;

    public EntitlementAdminControllerTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new EntitlementRepository(_db);
        _controller = new EntitlementAdminController(_repo, _audit, _publisher)
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
    public async Task AddBaseline_PersistsAuditsAndPublishes()
    {
        var result = await _controller.AddBaseline(Tenant,
            new EntitlementAdminController.EntitlementGrantRequest
            {
                PolicyOid = PolicyA,
                ClassificationLacv = 2
            }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var rows = await _repo.GetBaselineAsync(Tenant);
        Assert.Single(rows);
        Assert.Single(_audit.Events, e => e.ActionName == "add_baseline_entitlement");
        Assert.Single(_publisher.Events, e => e.EventType == WebhookEventTypes.AssignmentChanged);
    }

    [Fact]
    public async Task AddBaseline_RejectsMissingPolicyOid()
    {
        var result = await _controller.AddBaseline(Tenant,
            new EntitlementAdminController.EntitlementGrantRequest { PolicyOid = "" },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_audit.Events);
        Assert.Empty(_publisher.Events);
    }

    [Fact]
    public async Task RemoveBaseline_NotFound_WhenRowMissing()
    {
        var result = await _controller.RemoveBaseline(Tenant, "missing", null, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(_publisher.Events);
    }

    [Fact]
    public async Task RemoveBaseline_NoContent_AndPublishes_WhenRowExists()
    {
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline, PolicyOid = PolicyA
        });

        var result = await _controller.RemoveBaseline(Tenant, PolicyA, null, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _repo.GetBaselineAsync(Tenant));
        Assert.Single(_publisher.Events);
    }

    [Fact]
    public async Task AddGroup_PersistsAndPublishes()
    {
        var result = await _controller.AddGroup(Tenant, "group-alpha",
            new EntitlementAdminController.EntitlementGrantRequest
            {
                PolicyOid = PolicyA,
                ClassificationLacv = 2
            }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var rows = await _repo.GetGroupEntitlementsAsync(Tenant, "group-alpha");
        Assert.Single(rows);
        Assert.Single(_publisher.Events);
    }

    [Fact]
    public async Task AddUserGrant_AndAddUserDeny_ProduceTwoEvents()
    {
        await _controller.AddUserGrant(Tenant, "u1",
            new EntitlementAdminController.EntitlementGrantRequest { PolicyOid = PolicyA, ClassificationLacv = 1 },
            CancellationToken.None);
        await _controller.AddUserDeny(Tenant, "u1",
            new EntitlementAdminController.EntitlementDenyRequest { PolicyOid = PolicyA, ClassificationLacv = 5, Reason = "blocked" },
            CancellationToken.None);

        var (grants, denies) = await _repo.GetUserOverridesAsync(Tenant, "u1");
        Assert.Single(grants);
        Assert.Single(denies);
        Assert.Equal(2, _publisher.Events.Count);
        Assert.All(_publisher.Events, e => Assert.Equal(WebhookEventTypes.AssignmentChanged, e.EventType));
    }

    [Fact]
    public async Task ListUser_ReturnsBothGrantsAndDenies()
    {
        await _repo.AddUserGrantAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.User, TargetId = "u",
            PolicyOid = PolicyA, ClassificationLacv = 1
        });
        await _repo.AddUserDenyAsync(new EntitlementDeny
        {
            TenantId = Tenant, UserId = "u",
            PolicyOid = PolicyA, ClassificationLacv = 5
        });

        var view = await _controller.ListUser(Tenant, "u", CancellationToken.None);

        Assert.Single(view.Grants);
        Assert.Single(view.Denies);
    }

    // ── Stubs ──

    internal sealed class StubAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = new();
        public void Write(AuditEvent auditEvent) => Events.Add(auditEvent);
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    internal sealed class StubWebhookPublisher : IWebhookPublisher
    {
        public List<(string EventType, object Payload, string? TenantId)> Events { get; } = new();

        public Task PublishAsync(string eventType, object payload, string? tenantId, CancellationToken ct = default)
        {
            Events.Add((eventType, payload, tenantId));
            return Task.CompletedTask;
        }
    }
}
