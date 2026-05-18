using System.Security.Claims;
using AbacController.Api.Controllers;
using AbacController.Core.Domain.Classifications;
using AbacController.Core.Domain.Entitlements;
using AbacController.Core.Domain.Groups;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Entities;
using AbacController.Data.Repositories;
using AbacController.Pdp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for <see cref="SpifVisibilityController"/> exercising the visibility
/// algorithm end-to-end with a real entitlement resolver, group repository,
/// membership resolver, and an in-memory SQLite seeded with SpifEntity rows.
/// Uses a stub <see cref="ClaimsPrincipal"/> to inject the subject id and the
/// Keycloak group claims.
/// </summary>
public sealed class SpifVisibilityControllerTests : IDisposable
{
    private const string Tenant = "tenant-a";
    private const string PolicyA = "1.2.3.4";
    private const string PolicyB = "5.6.7.8";
    private const string PolicyC = "9.10.11.12";

    private readonly AbacDbContext _db;
    private readonly AbacGroupRepository _groupRepo;
    private readonly EntitlementRepository _entitlementRepo;
    private readonly EntitlementResolver _entitlementResolver;
    private readonly GroupMembershipResolver _groupResolver;
    private readonly GroupMembershipCache _cache = new(TimeSpan.FromMinutes(5));
    private readonly StubAppRepo _appRepo = new();
    private readonly StubKeycloakClaims _keycloakClaims = new();
    private readonly SpifVisibilityController _controller;

    public SpifVisibilityControllerTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _groupRepo = new AbacGroupRepository(_db);
        _groupResolver = new GroupMembershipResolver(_groupRepo, _cache);
        _entitlementRepo = new EntitlementRepository(_db);
        _entitlementResolver = new EntitlementResolver(_entitlementRepo);

        SeedSpifs();

        _controller = new SpifVisibilityController(
            _groupResolver,
            _entitlementResolver,
            _appRepo,
            _db,
            new StubTenantContext(Tenant),
            _keycloakClaims);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private void SeedSpifs()
    {
        _db.Spifs.Add(new SpifEntity { Id = Guid.NewGuid(), PolicyOid = PolicyA, Name = "NATO", SchemaVersion = "3.0", Hash = "hashA", TenantId = Tenant, IsActive = true, ClassificationCount = 4 });
        _db.Spifs.Add(new SpifEntity { Id = Guid.NewGuid(), PolicyOid = PolicyB, Name = "US-DOD", SchemaVersion = "3.0", Hash = "hashB", TenantId = Tenant, IsActive = true, ClassificationCount = 3 });
        _db.Spifs.Add(new SpifEntity { Id = Guid.NewGuid(), PolicyOid = PolicyC, Name = "AUS-PSPF", SchemaVersion = "3.0", Hash = "hashC", TenantId = Tenant, IsActive = true, ClassificationCount = 5 });
        _db.SaveChanges();
    }

    private void SetSubject(string userId, params string[] keycloakGroups)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("sub", userId));
        foreach (var g in keycloakGroups)
        {
            identity.AddClaim(new Claim("groups", g));
        }
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        // Mirror the production middleware: when the principal is set, the
        // Keycloak claims provider is populated with the same group claims.
        _keycloakClaims.SetGroups(keycloakGroups);
    }

    private async Task<SpifVisibilityController.VisibleSpifsResponse> Visible(string? appId = null)
    {
        var result = await _controller.Visible(appId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<SpifVisibilityController.VisibleSpifsResponse>(ok.Value);
    }

    [Fact]
    public async Task NoEntitlements_ReturnsEmpty()
    {
        SetSubject("alice");

        var response = await Visible();

        Assert.Empty(response.Spifs);
        Assert.Null(response.DefaultPolicyOid);
    }

    [Fact]
    public async Task BaselineGrant_VisibleToEveryone()
    {
        await _entitlementRepo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline, PolicyOid = PolicyC
        });
        SetSubject("alice");

        var response = await Visible();

        Assert.Single(response.Spifs);
        Assert.Equal(PolicyC, response.Spifs[0].PolicyOid);
    }

    [Fact]
    public async Task DirectUserGrant_VisibleToThatUserOnly()
    {
        await _entitlementRepo.AddUserGrantAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.User, TargetId = "alice", PolicyOid = PolicyA
        });

        SetSubject("alice");
        var aliceResponse = await Visible();
        Assert.Single(aliceResponse.Spifs);
        Assert.Equal(PolicyA, aliceResponse.Spifs[0].PolicyOid);

        SetSubject("bob");
        var bobResponse = await Visible();
        Assert.Empty(bobResponse.Spifs);
    }

    [Fact]
    public async Task GroupGrantViaDirectMembership_Visible()
    {
        var nato = await _groupRepo.CreateGroupAsync(new AbacGroup
        {
            Id = Guid.NewGuid(), TenantId = Tenant, Name = "nato-readers"
        });
        await _groupRepo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = nato.Id, Kind = AbacGroupMemberKind.User, MemberId = "alice", TenantId = Tenant
        });
        await _entitlementRepo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Group, TargetId = nato.Id.ToString(), PolicyOid = PolicyA
        });

        SetSubject("alice");
        var response = await Visible();
        Assert.Single(response.Spifs, s => s.PolicyOid == PolicyA);

        // Bob isn't in the group → no visibility.
        SetSubject("bob");
        Assert.Empty((await Visible()).Spifs);
    }

    [Fact]
    public async Task GroupGrantViaInheritedKeycloakGroup_Visible()
    {
        var nato = await _groupRepo.CreateGroupAsync(new AbacGroup
        {
            Id = Guid.NewGuid(), TenantId = Tenant, Name = "nato-readers"
        });
        await _groupRepo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = nato.Id,
            Kind = AbacGroupMemberKind.KeycloakGroup,
            MemberId = "kc-nato",
            TenantId = Tenant
        });
        await _entitlementRepo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Group, TargetId = nato.Id.ToString(), PolicyOid = PolicyA
        });

        SetSubject("alice", "kc-nato");
        Assert.Single((await Visible()).Spifs, s => s.PolicyOid == PolicyA);

        SetSubject("bob");
        Assert.Empty((await Visible()).Spifs);
    }

    [Fact]
    public async Task ClassificationLevelGrant_StillMakesPolicyVisible()
    {
        // User is granted ONLY a single classification within a policy. The
        // policy as a whole should still appear in the visible list — the
        // classification query is responsible for filtering classifications
        // inside it.
        await _entitlementRepo.AddUserGrantAsync(new EntitlementGrant
        {
            TenantId = Tenant,
            Scope = EntitlementScope.User,
            TargetId = "alice",
            PolicyOid = PolicyA,
            ClassificationLacv = 2
        });

        SetSubject("alice");
        var response = await Visible();

        Assert.Single(response.Spifs, s => s.PolicyOid == PolicyA);
    }

    [Fact]
    public async Task WholePolicyDeny_RemovesPolicyFromVisibleSet()
    {
        await _entitlementRepo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline, PolicyOid = PolicyA
        });
        await _entitlementRepo.AddUserDenyAsync(new EntitlementDeny
        {
            TenantId = Tenant, UserId = "alice", PolicyOid = PolicyA
        });

        SetSubject("alice");
        var response = await Visible();

        Assert.DoesNotContain(response.Spifs, s => s.PolicyOid == PolicyA);
    }

    [Fact]
    public async Task ClassificationLevelDeny_DoesNotRemovePolicy()
    {
        // Critical: a classification-level deny is NOT a policy-level deny.
        // The policy must still be visible so the user can see/use the
        // classifications they DO have access to.
        await _entitlementRepo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline, PolicyOid = PolicyA
        });
        await _entitlementRepo.AddUserDenyAsync(new EntitlementDeny
        {
            TenantId = Tenant, UserId = "alice", PolicyOid = PolicyA, ClassificationLacv = 5
        });

        SetSubject("alice");
        var response = await Visible();

        Assert.Single(response.Spifs, s => s.PolicyOid == PolicyA);
    }

    [Fact]
    public async Task UnionAcrossSources_DefaultPlusGroupPlusUser()
    {
        // Baseline grants C, group grants A (alice in group via direct), user
        // grant on B → expect all three.
        await _entitlementRepo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline, PolicyOid = PolicyC
        });
        var nato = await _groupRepo.CreateGroupAsync(new AbacGroup
        {
            Id = Guid.NewGuid(), TenantId = Tenant, Name = "nato"
        });
        await _groupRepo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = nato.Id, Kind = AbacGroupMemberKind.User, MemberId = "alice", TenantId = Tenant
        });
        await _entitlementRepo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Group, TargetId = nato.Id.ToString(), PolicyOid = PolicyA
        });
        await _entitlementRepo.AddUserGrantAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.User, TargetId = "alice", PolicyOid = PolicyB
        });

        SetSubject("alice");
        var response = await Visible();

        Assert.Equal(3, response.Spifs.Count);
        Assert.Contains(response.Spifs, s => s.PolicyOid == PolicyA);
        Assert.Contains(response.Spifs, s => s.PolicyOid == PolicyB);
        Assert.Contains(response.Spifs, s => s.PolicyOid == PolicyC);
    }

    [Fact]
    public async Task DefaultPolicyOid_PrefersAppRegistration_WhenInVisibleSet()
    {
        await _entitlementRepo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline, PolicyOid = PolicyA
        });
        await _entitlementRepo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline, PolicyOid = PolicyB
        });

        _appRepo.Add(new ApplicationRegistration
        {
            Id = "email-classification", Name = "Email", DefaultPolicyOid = PolicyB
        });

        SetSubject("alice");
        var response = await Visible(appId: "email-classification");

        Assert.Equal(PolicyB, response.DefaultPolicyOid);
    }

    [Fact]
    public async Task DefaultPolicyOid_FallsBackToFirstAlphabetical_WhenAppDefaultMissing()
    {
        await _entitlementRepo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline, PolicyOid = PolicyA
        });
        await _entitlementRepo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline, PolicyOid = PolicyB
        });

        SetSubject("alice");
        var response = await Visible();

        // AUS-PSPF (PolicyC) isn't visible; the alphabetically first remaining
        // SPIF is NATO (PolicyA).
        Assert.Equal(PolicyA, response.DefaultPolicyOid);
    }

    // ── Stubs ──

    private sealed class StubTenantContext(string? tenantId) : ITenantContext
    {
        public string? TenantId { get; } = tenantId;
    }

    private sealed class StubKeycloakClaims : IKeycloakGroupClaimsProvider
    {
        public IReadOnlyCollection<string> KeycloakGroupIds { get; private set; } = Array.Empty<string>();
        public void SetGroups(IReadOnlyCollection<string> groups) => KeycloakGroupIds = groups ?? Array.Empty<string>();
    }

    private sealed class StubAppRepo : IApplicationRepository
    {
        private readonly List<ApplicationRegistration> _apps = new();
        public void Add(ApplicationRegistration app) => _apps.Add(app);

        public Task<List<ApplicationRegistration>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult(_apps.ToList());

        public Task<ApplicationRegistration?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_apps.FirstOrDefault(a => a.Id == id));

        public Task<ApplicationRegistration> UpsertAsync(ApplicationRegistration registration, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
