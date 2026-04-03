using System.Collections.Immutable;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;

namespace AbacController.Pdp;

/// <summary>
/// Pre-compiled, immutable index of a SPIF for fast ACDF evaluation.
/// Built at SPIF load time; shared read-only across all evaluation threads.
/// </summary>
public sealed class SpifIndex : ISpifIndex
{
    private readonly ImmutableDictionary<LacvValue, int> _hierarchyMap;
    private readonly ImmutableDictionary<LacvValue, SecurityClassification> _classificationMap;
    private readonly ImmutableDictionary<string, TagSetIndex> _tagSetMap;
    private readonly ImmutableDictionary<string, EquivalentPolicy> _equivalentPolicies;
    private readonly ImmutableList<LacvValue> _classificationsByHierarchy;

    public string PolicyOid { get; }
    public string PolicyName { get; }
    public string SchemaVersion { get; }
    public Spif Spif { get; }

    public SpifIndex(Spif spif)
    {
        Spif = spif ?? throw new ArgumentNullException(nameof(spif));
        PolicyOid = spif.PolicyId.Oid;
        PolicyName = spif.PolicyId.Name;
        SchemaVersion = spif.SchemaVersion;

        // Build hierarchy map: lacv → hierarchy value
        var hierarchyBuilder = ImmutableDictionary.CreateBuilder<LacvValue, int>();
        var classificationBuilder = ImmutableDictionary.CreateBuilder<LacvValue, SecurityClassification>();

        foreach (var cls in spif.Classifications)
        {
            hierarchyBuilder[cls.Lacv] = cls.Hierarchy;
            classificationBuilder[cls.Lacv] = cls;
        }

        _hierarchyMap = hierarchyBuilder.ToImmutable();
        _classificationMap = classificationBuilder.ToImmutable();

        // Pre-sort classifications by hierarchy
        _classificationsByHierarchy = spif.Classifications
            .OrderBy(c => c.Hierarchy)
            .Select(c => c.Lacv)
            .ToImmutableList();

        // Build tag set indexes
        var tagSetBuilder = ImmutableDictionary.CreateBuilder<string, TagSetIndex>();
        foreach (var tagSet in spif.CategoryTagSets)
        {
            tagSetBuilder[tagSet.TagSetOid] = new TagSetIndex(tagSet);
        }
        _tagSetMap = tagSetBuilder.ToImmutable();

        // Build equivalent policy map
        var equivBuilder = ImmutableDictionary.CreateBuilder<string, EquivalentPolicy>();
        foreach (var ep in spif.EquivalentPolicies)
        {
            equivBuilder[ep.PolicyOid] = ep;
        }
        _equivalentPolicies = equivBuilder.ToImmutable();
    }

    public bool TryGetHierarchy(LacvValue lacv, out int hierarchy)
        => _hierarchyMap.TryGetValue(lacv, out hierarchy);

    public SecurityClassification? GetClassification(LacvValue lacv)
        => _classificationMap.GetValueOrDefault(lacv);

    public string? GetClassificationName(LacvValue lacv)
        => _classificationMap.TryGetValue(lacv, out var cls) ? cls.Name : null;

    public ITagSetIndex? GetTagSet(string tagSetOid)
        => _tagSetMap.GetValueOrDefault(tagSetOid);

    public EquivalentPolicy? GetEquivalentPolicy(string targetPolicyOid)
        => _equivalentPolicies.GetValueOrDefault(targetPolicyOid);

    public IReadOnlyList<LacvValue> GetClassificationsByHierarchy()
        => _classificationsByHierarchy;

    public IReadOnlyList<string> GetTagSetOids()
        => _tagSetMap.Keys.ToImmutableList();
}

/// <summary>
/// Index for a single tag set within a SPIF.
/// </summary>
public sealed class TagSetIndex : ITagSetIndex
{
    private readonly ImmutableDictionary<(string tagName, LacvValue lacv), TagCategory> _categoryLookup;
    private readonly ImmutableDictionary<string, ImmutableHashSet<LacvValue>> _tagCategoryLacvs;

    public string TagSetOid { get; }
    public string Name { get; }
    public IReadOnlyList<SecurityCategoryTag> Tags { get; }

    public TagSetIndex(SecurityCategoryTagSet tagSet)
    {
        TagSetOid = tagSet.TagSetOid;
        Name = tagSet.Name;
        Tags = tagSet.Tags;

        var catBuilder = ImmutableDictionary.CreateBuilder<(string, LacvValue), TagCategory>();
        var lacvBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<LacvValue>>();

        foreach (var tag in tagSet.Tags)
        {
            var lacvs = ImmutableHashSet.CreateBuilder<LacvValue>();
            foreach (var cat in tag.Categories)
            {
                catBuilder[(tag.Name, cat.Lacv)] = cat;
                lacvs.Add(cat.Lacv);
            }
            lacvBuilder[tag.Name] = lacvs.ToImmutable();
        }

        _categoryLookup = catBuilder.ToImmutable();
        _tagCategoryLacvs = lacvBuilder.ToImmutable();
    }

    public TagCategory? GetCategory(string tagName, LacvValue lacv)
        => _categoryLookup.GetValueOrDefault((tagName, lacv));

    public ImmutableHashSet<LacvValue> GetCategoryLacvs(string tagName)
        => _tagCategoryLacvs.GetValueOrDefault(tagName, ImmutableHashSet<LacvValue>.Empty);
}
