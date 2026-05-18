using AbacController.Api.Controllers;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Classifications;
using AbacController.Core.Domain.Policy;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Repositories;
using AbacController.Pap;
using AbacController.Pdp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// Verifies that the PAP and Application admin controllers actually invoke
/// <see cref="IWebhookPublisher"/> on every state-changing path. These tests would
/// catch a regression where someone adds a new write endpoint and forgets to publish
/// the corresponding event.
/// </summary>
public sealed class PublisherWiringTests : IDisposable
{
    private readonly AbacDbContext _db;
    private readonly StubPublisher _publisher = new();
    private readonly SpifParser _parser = new();

    public PublisherWiringTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private PapAdminController BuildPap()
    {
        var policyRepo = new StubPolicyRepository();
        var spifRegistry = new InMemorySpifRegistry();
        var audit = new NullAuditWriter();
        var tenant = new StubTenantContext("tenant-a");

        return new PapAdminController(policyRepo, _parser, spifRegistry, _db, audit, tenant, _publisher)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private ApplicationAdminController BuildAppAdmin()
    {
        var repo = new ApplicationRepository(_db, new StubTenantContext("tenant-a"));
        var audit = new NullAuditWriter();
        var tenant = new StubTenantContext("tenant-a");
        return new ApplicationAdminController(repo, audit, tenant, _publisher)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task PapAdmin_ImportSpif_PublishesSpifImported()
    {
        var controller = BuildPap();
        var result = await controller.ImportSpif(
            new PapAdminController.SpifImportRequest(TestSpifSamples.BasicPolicy, Activate: true, SetAsDefault: true),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(_publisher.Events, e => e.EventType == WebhookEventTypes.SpifImported);
    }

    [Fact]
    public async Task PapAdmin_DeleteSpif_PublishesSpifDeleted()
    {
        var controller = BuildPap();
        var importResult = await controller.ImportSpif(
            new PapAdminController.SpifImportRequest(TestSpifSamples.BasicPolicy),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(importResult.Result);
        _publisher.Events.Clear();

        var stored = await _db.Spifs.FirstAsync();
        var deleteResult = await controller.DeleteSpif(stored.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(deleteResult);
        Assert.Single(_publisher.Events, e => e.EventType == WebhookEventTypes.SpifDeleted);
    }

    [Fact]
    public async Task ApplicationAdmin_Upsert_PublishesApplicationUpdated()
    {
        var controller = BuildAppAdmin();
        var result = await controller.Upsert("email-classification",
            new ApplicationAdminController.ApplicationRegistrationRequest
            {
                Name = "Email Classification",
                DefaultPolicyOid = "1.2.3.4"
            }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(_publisher.Events, e => e.EventType == WebhookEventTypes.ApplicationUpdated);
    }

    [Fact]
    public async Task ApplicationAdmin_Delete_PublishesApplicationUpdated()
    {
        var controller = BuildAppAdmin();
        await controller.Upsert("email-classification",
            new ApplicationAdminController.ApplicationRegistrationRequest { Name = "Email Classification" },
            CancellationToken.None);
        _publisher.Events.Clear();

        var result = await controller.Delete("email-classification", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Single(_publisher.Events, e => e.EventType == WebhookEventTypes.ApplicationUpdated);
    }

    // ── Stubs ──

    private sealed class StubPublisher : IWebhookPublisher
    {
        public List<(string EventType, object Payload, string? TenantId)> Events { get; } = new();
        public Task PublishAsync(string eventType, object payload, string? tenantId, CancellationToken ct = default)
        {
            Events.Add((eventType, payload, tenantId));
            return Task.CompletedTask;
        }
    }

    private sealed class StubPolicyRepository : IPolicyRepository
    {
        public Task<List<PolicySet>> GetPolicySetsAsync(CancellationToken ct = default) => Task.FromResult(new List<PolicySet>());
        public Task<PolicySet?> GetPolicySetAsync(string id, CancellationToken ct = default) => Task.FromResult<PolicySet?>(null);
        public Task<PolicySet> CreatePolicySetAsync(PolicySet policySet, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolicySet> UpdatePolicySetAsync(PolicySet policySet, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeletePolicySetAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Policy?> GetPolicyAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Policy> CreatePolicyAsync(Policy policy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Policy> UpdatePolicyAsync(Policy policy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeletePolicyAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<PolicyVersion>> GetVersionsAsync(string policyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolicyVersion?> GetVersionAsync(Guid versionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolicyVersion> CreateVersionAsync(PolicyVersion version, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ActivateVersionAsync(string policyId, Guid versionId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class InMemorySpifRegistry : ISpifRegistry
    {
        private readonly Dictionary<string, ISpifIndex> _byOid = new(StringComparer.Ordinal);
        private string? _defaultOid;

        public ISpifIndex? GetByPolicyOid(string policyOid) => _byOid.GetValueOrDefault(policyOid);
        public ISpifIndex? GetDefault() => _defaultOid is not null ? _byOid.GetValueOrDefault(_defaultOid) : null;
        public void Register(ISpifIndex spifIndex) => _byOid[spifIndex.PolicyOid] = spifIndex;
        public void SetDefault(string policyOid) => _defaultOid = policyOid;
        public void Remove(string policyOid) => _byOid.Remove(policyOid);
        public bool IsRegistered(string policyOid) => _byOid.ContainsKey(policyOid);
        public IReadOnlyList<string> GetRegisteredPolicyOids() => _byOid.Keys.ToList();
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public void Write(AuditEvent auditEvent) { }
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTenantContext(string? tenantId) : ITenantContext
    {
        public string? TenantId { get; } = tenantId;
    }
}
