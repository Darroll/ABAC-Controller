using AbacController.Core.Interfaces;
using AbacController.Pap;
using AbacController.Pdp;
using AbacController.Core.Domain.Spif;

namespace AbacController.Tests.Unit;

/// <summary>
/// Edge case tests for SpifRegistry — default handling, removal, empty state.
/// </summary>
public sealed class SpifRegistryEdgeCaseTests
{
    private static ISpifIndex MakeSpifIndex(string oid, string name = "Test SPIF")
    {
        var spif = new Spif
        {
            SchemaVersion = "2.1",
            PolicyId = new PolicyInfo { Name = name, Oid = oid },
            Classifications = [],
            CategoryTagSets = [],
            EquivalentPolicies = []
        };
        return new SpifIndex(spif);
    }

    [Fact]
    public void GetDefault_EmptyRegistry_ReturnsNull()
    {
        var registry = new SpifRegistry();
        Assert.Null(registry.GetDefault());
    }

    [Fact]
    public void Register_FirstEntry_BecomesDefault()
    {
        var registry = new SpifRegistry();
        var index = MakeSpifIndex("1.2.3.4");
        registry.Register(index);

        var defaultIndex = registry.GetDefault();
        Assert.NotNull(defaultIndex);
        Assert.Equal("1.2.3.4", defaultIndex.PolicyOid);
    }

    [Fact]
    public void Remove_DefaultEntry_PicksNewDefault()
    {
        var registry = new SpifRegistry();
        registry.Register(MakeSpifIndex("1.2.3.4"));
        registry.Register(MakeSpifIndex("5.6.7.8"));
        registry.SetDefault("1.2.3.4");

        registry.Remove("1.2.3.4");

        Assert.False(registry.IsRegistered("1.2.3.4"));
        var newDefault = registry.GetDefault();
        Assert.NotNull(newDefault);
        Assert.Equal("5.6.7.8", newDefault.PolicyOid);
    }

    [Fact]
    public void Remove_NonExistent_DoesNotThrow()
    {
        var registry = new SpifRegistry();
        registry.Remove("nonexistent");
        Assert.Empty(registry.GetRegisteredPolicyOids());
    }

    [Fact]
    public void Remove_AllEntries_DefaultBecomesNull()
    {
        var registry = new SpifRegistry();
        registry.Register(MakeSpifIndex("1.2.3.4"));
        registry.Remove("1.2.3.4");

        Assert.Null(registry.GetDefault());
        Assert.Empty(registry.GetRegisteredPolicyOids());
    }

    [Fact]
    public void Register_SameOidTwice_Updates()
    {
        var registry = new SpifRegistry();
        registry.Register(MakeSpifIndex("1.2.3.4", "Version 1"));
        registry.Register(MakeSpifIndex("1.2.3.4", "Version 2"));

        Assert.Single(registry.GetRegisteredPolicyOids());
        Assert.Equal("Version 2", registry.GetByPolicyOid("1.2.3.4")!.PolicyName);
    }

    [Fact]
    public void SetDefault_UnregisteredOid_Throws()
    {
        var registry = new SpifRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.SetDefault("1.2.3.4"));
    }

    [Fact]
    public void GetRegisteredPolicyOids_ReturnsSorted()
    {
        var registry = new SpifRegistry();
        registry.Register(MakeSpifIndex("9.9.9.9"));
        registry.Register(MakeSpifIndex("1.1.1.1"));
        registry.Register(MakeSpifIndex("5.5.5.5"));

        var oids = registry.GetRegisteredPolicyOids();
        Assert.Equal(3, oids.Count);
        Assert.Equal("1.1.1.1", oids[0]);
        Assert.Equal("5.5.5.5", oids[1]);
        Assert.Equal("9.9.9.9", oids[2]);
    }

    [Fact]
    public void IsRegistered_ReturnsCorrectState()
    {
        var registry = new SpifRegistry();
        Assert.False(registry.IsRegistered("1.2.3.4"));

        registry.Register(MakeSpifIndex("1.2.3.4"));
        Assert.True(registry.IsRegistered("1.2.3.4"));

        registry.Remove("1.2.3.4");
        Assert.False(registry.IsRegistered("1.2.3.4"));
    }

    [Fact]
    public void GetByPolicyOid_NullOrEmpty_Throws()
    {
        var registry = new SpifRegistry();
        Assert.Throws<ArgumentException>(() => registry.GetByPolicyOid(""));
        Assert.Throws<ArgumentException>(() => registry.GetByPolicyOid("  "));
    }
}
