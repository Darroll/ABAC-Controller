using System.Collections.Immutable;
using AbacController.Api.Controllers;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;
using AbacController.Pap;
using AbacController.Pdp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AbacController.Tests.Unit;

/// <summary>
/// Direct controller-level tests for <see cref="RecipientCheckController"/>. Verifies
/// validation, fail-closed behaviour when PIP returns nothing, hierarchy dominance
/// math, and aggregate Permit/Deny semantics across multiple recipients.
/// </summary>
public sealed class RecipientCheckControllerTests
{
    private readonly SpifParser _parser = new();

    private RecipientCheckController BuildController(
        ISpifIndex? spif,
        Dictionary<string, SecurityClearance> recipientClearances)
    {
        var registry = new StubRegistry(spif);
        var pip = new StubPipResolver(recipientClearances);
        var auditWriter = new StubAuditWriter();
        var tenantContext = new StubTenantContext("tenant-a");

        var controller = new RecipientCheckController(pip, registry, auditWriter, tenantContext);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private ISpifIndex BuildSpifIndex()
    {
        var result = _parser.Parse(TestSpifSamples.BasicPolicy);
        Assert.True(result.Success, string.Join(";", result.Errors.Select(e => e.Message)));
        return new SpifIndex(result.Spif!);
    }

    private static SecurityClearance MakeClearance(string policyOid, params int[] lacvs) => new()
    {
        PolicyOid = policyOid,
        ClassificationLacvs = lacvs.Select(v => (LacvValue)v).ToImmutableHashSet()
    };

    [Fact]
    public async Task Check_BadRequest_WhenPolicyOidEmpty()
    {
        var controller = BuildController(BuildSpifIndex(), new());
        var result = await controller.Check(new RecipientCheckController.RecipientCheckRequest
        {
            PolicyOid = "",
            ClassificationLacv = 2,
            Recipients = [new() { Id = "alice" }]
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Check_BadRequest_WhenRecipientsEmpty()
    {
        var controller = BuildController(BuildSpifIndex(), new());
        var result = await controller.Check(new RecipientCheckController.RecipientCheckRequest
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacv = 2,
            Recipients = []
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Check_NotFound_WhenPolicyOidUnknown()
    {
        var controller = BuildController(spif: null, new());
        var result = await controller.Check(new RecipientCheckController.RecipientCheckRequest
        {
            PolicyOid = "9.9.9.9",
            ClassificationLacv = 2,
            Recipients = [new() { Id = "alice" }]
        }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Check_AggregateDeny_WhenAnyRecipientFailsClearance()
    {
        // SPIF basic policy: CONFIDENTIAL=2, SECRET=3
        var spif = BuildSpifIndex();
        var controller = BuildController(spif, new()
        {
            ["alice"] = MakeClearance("1.2.3.4", 2, 3), // SECRET → can see CONFIDENTIAL
            ["bob"]   = MakeClearance("1.2.3.4", 2)     // CONFIDENTIAL only
        });

        // Target: SECRET (lacv=3). Bob's max hierarchy is 2 < 3 → Deny.
        var result = await controller.Check(new RecipientCheckController.RecipientCheckRequest
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacv = 3,
            Recipients = [new() { Id = "alice" }, new() { Id = "bob" }]
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RecipientCheckController.RecipientCheckResponse>(ok.Value);
        Assert.Equal("Deny", response.Aggregate);
        Assert.Equal(2, response.Results.Count);
        Assert.Equal("Permit", response.Results.Single(r => r.Id == "alice").Decision);
        Assert.Equal("Deny", response.Results.Single(r => r.Id == "bob").Decision);
    }

    [Fact]
    public async Task Check_AggregatePermit_WhenAllCleared()
    {
        var spif = BuildSpifIndex();
        var controller = BuildController(spif, new()
        {
            ["alice"] = MakeClearance("1.2.3.4", 2, 3),
            ["bob"]   = MakeClearance("1.2.3.4", 2, 3)
        });

        var result = await controller.Check(new RecipientCheckController.RecipientCheckRequest
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacv = 2,
            Recipients = [new() { Id = "alice" }, new() { Id = "bob" }]
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RecipientCheckController.RecipientCheckResponse>(ok.Value);
        Assert.Equal("Permit", response.Aggregate);
        Assert.All(response.Results, r => Assert.Equal("Permit", r.Decision));
    }

    [Fact]
    public async Task Check_FailClosed_WhenPipReturnsNoClearance()
    {
        var controller = BuildController(BuildSpifIndex(), recipientClearances: new()); // empty PIP

        var result = await controller.Check(new RecipientCheckController.RecipientCheckRequest
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacv = 2,
            Recipients = [new() { Id = "ghost" }]
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RecipientCheckController.RecipientCheckResponse>(ok.Value);
        Assert.Equal("Deny", response.Aggregate);
        Assert.Equal("Deny", response.Results[0].Decision);
        Assert.Contains("fail-closed", response.Results[0].Reason!);
    }

    [Fact]
    public async Task Check_DenyEmptyRecipientId()
    {
        var controller = BuildController(BuildSpifIndex(), new());

        var result = await controller.Check(new RecipientCheckController.RecipientCheckRequest
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacv = 2,
            Recipients = [new() { Id = "" }]
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RecipientCheckController.RecipientCheckResponse>(ok.Value);
        Assert.Equal("Deny", response.Aggregate);
        Assert.Contains("empty", response.Results[0].Reason!);
    }

    // ── Stubs ──

    private sealed class StubRegistry(ISpifIndex? index) : ISpifRegistry
    {
        private readonly ISpifIndex? _index = index;
        public ISpifIndex? GetByPolicyOid(string policyOid) => _index?.PolicyOid == policyOid ? _index : null;
        public ISpifIndex? GetDefault() => _index;
        public void Register(ISpifIndex spifIndex) { }
        public void SetDefault(string policyOid) { }
        public void Remove(string policyOid) { }
        public bool IsRegistered(string policyOid) => _index?.PolicyOid == policyOid;
        public IReadOnlyList<string> GetRegisteredPolicyOids() => _index is not null ? [_index.PolicyOid] : [];
    }

    private sealed class StubPipResolver(Dictionary<string, SecurityClearance> map) : IPipResolver
    {
        public Task<AttributeResolutionResult> ResolveAsync(AttributeResolutionRequest request, CancellationToken ct = default)
        {
            if (!map.TryGetValue(request.SubjectId, out var clearance))
                return Task.FromResult(AttributeResolutionResult.Succeeded(new()));

            return Task.FromResult(AttributeResolutionResult.Succeeded(new List<AttributeValue>
            {
                new()
                {
                    Name = "securityClearance",
                    Category = AttributeCategory.Subject,
                    Value = clearance,
                    SourceId = "stub",
                    SourceType = "static",
                    FetchedAt = DateTimeOffset.UtcNow
                }
            }));
        }
    }

    private sealed class StubAuditWriter : IAuditWriter
    {
        public void Write(AuditEvent auditEvent) { }
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTenantContext(string? tenantId) : ITenantContext
    {
        public string? TenantId { get; } = tenantId;
    }
}
