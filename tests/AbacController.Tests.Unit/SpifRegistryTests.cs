using System.Collections.Immutable;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;
using AbacController.Pap;
using AbacController.Pdp;

namespace AbacController.Tests.Unit;

public sealed class SpifRegistryTests
{
    private static Spif CreateMinimalSpif(string oid, string name) => new()
    {
        SchemaVersion = "3.0",
        PolicyId = new PolicyInfo { Oid = oid, Name = name },
        Classifications = ImmutableList.Create(new SecurityClassification
        {
            Name = "UNCLASSIFIED", Lacv = new LacvValue(0), Hierarchy = 0
        })
    };

    private static ISpifIndex CreateIndex(string oid, string name = "Test")
        => new SpifIndex(CreateMinimalSpif(oid, name));

    [Fact]
    public void Register_ThenGetByPolicyOid_ReturnsIndex()
    {
        var registry = new SpifRegistry();
        var index = CreateIndex("1.2.3.4");
        registry.Register(index);

        var result = registry.GetByPolicyOid("1.2.3.4");
        Assert.NotNull(result);
        Assert.Equal("1.2.3.4", result.PolicyOid);
    }

    [Fact]
    public void GetByPolicyOid_NotRegistered_ReturnsNull()
    {
        var registry = new SpifRegistry();
        Assert.Null(registry.GetByPolicyOid("9.9.9.9"));
    }

    [Fact]
    public void GetDefault_FirstRegistered_IsDefault()
    {
        var registry = new SpifRegistry();
        registry.Register(CreateIndex("1.2.3.4"));
        registry.Register(CreateIndex("5.6.7.8"));

        var defaultIndex = registry.GetDefault();
        Assert.NotNull(defaultIndex);
        Assert.Equal("1.2.3.4", defaultIndex.PolicyOid);
    }

    [Fact]
    public void SetDefault_ChangesDefault()
    {
        var registry = new SpifRegistry();
        registry.Register(CreateIndex("1.2.3.4"));
        registry.Register(CreateIndex("5.6.7.8"));
        registry.SetDefault("5.6.7.8");

        Assert.Equal("5.6.7.8", registry.GetDefault()!.PolicyOid);
    }

    [Fact]
    public void SetDefault_NotRegistered_Throws()
    {
        var registry = new SpifRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.SetDefault("9.9.9.9"));
    }

    [Fact]
    public void Remove_ExistingPolicy_RemovesIt()
    {
        var registry = new SpifRegistry();
        registry.Register(CreateIndex("1.2.3.4"));
        registry.Remove("1.2.3.4");

        Assert.Null(registry.GetByPolicyOid("1.2.3.4"));
        Assert.False(registry.IsRegistered("1.2.3.4"));
    }

    [Fact]
    public void Remove_DefaultPolicy_FallsBackToNextOid()
    {
        var registry = new SpifRegistry();
        registry.Register(CreateIndex("1.2.3.4"));
        registry.Register(CreateIndex("5.6.7.8"));
        registry.Remove("1.2.3.4");

        Assert.Equal("5.6.7.8", registry.GetDefault()!.PolicyOid);
    }

    [Fact]
    public void Remove_NotRegistered_NoOp()
    {
        var registry = new SpifRegistry();
        registry.Remove("9.9.9.9"); // Should not throw
    }

    [Fact]
    public void GetRegisteredPolicyOids_ReturnsSortedList()
    {
        var registry = new SpifRegistry();
        registry.Register(CreateIndex("5.6.7.8"));
        registry.Register(CreateIndex("1.2.3.4"));

        var oids = registry.GetRegisteredPolicyOids();
        Assert.Equal(2, oids.Count);
        Assert.Equal("1.2.3.4", oids[0]);
        Assert.Equal("5.6.7.8", oids[1]);
    }

    [Fact]
    public void IsRegistered_ReturnsTrueForRegistered()
    {
        var registry = new SpifRegistry();
        registry.Register(CreateIndex("1.2.3.4"));

        Assert.True(registry.IsRegistered("1.2.3.4"));
        Assert.False(registry.IsRegistered("9.9.9.9"));
    }

    [Fact]
    public void GetDefault_EmptyRegistry_ReturnsNull()
    {
        var registry = new SpifRegistry();
        Assert.Null(registry.GetDefault());
    }

    [Fact]
    public void Register_OverwritesExisting()
    {
        var registry = new SpifRegistry();
        registry.Register(CreateIndex("1.2.3.4", "First"));
        registry.Register(CreateIndex("1.2.3.4", "Second"));

        var result = registry.GetByPolicyOid("1.2.3.4");
        Assert.Equal("Second", result!.PolicyName);
    }

    [Fact]
    public void Register_NullIndex_ThrowsArgumentNullException()
    {
        var registry = new SpifRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void GetByPolicyOid_NullOrWhitespace_Throws()
    {
        var registry = new SpifRegistry();
        Assert.Throws<ArgumentException>(() => registry.GetByPolicyOid(""));
        Assert.Throws<ArgumentException>(() => registry.GetByPolicyOid("   "));
    }
}
