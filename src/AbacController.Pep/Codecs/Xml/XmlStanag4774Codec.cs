using System.Collections.Immutable;
using System.Xml.Linq;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;

namespace AbacController.Pep.Codecs.Xml;

/// <summary>
/// XML STANAG 4774 label codec. Encodes/decodes security labels
/// to/from the STANAG 4774 XML format.
///
/// Note: STANAG 4778 metadata binding is not implemented here; this codec handles
/// label syntax only. To preserve controller-relevant semantics across round trips,
/// the codec emits and consumes extension attributes for canonical identifiers.
/// </summary>
public sealed class XmlStanag4774Codec : ILabelCodec
{
    private static readonly XNamespace SlabNs = SpifNamespaces.Stanag4774;

    /// <inheritdoc />
    public string CodecId => "stanag4774-xml";

    /// <inheritdoc />
    public string ContentType => "application/xml";

    /// <inheritdoc />
    public EncodeResult Encode(SecurityLabel label, ISpifIndex spifIndex)
    {
        try
        {
            var policyIdentifier = new XElement(
                SlabNs + "PolicyIdentifier",
                new XAttribute("oid", label.PolicyOid ?? spifIndex.PolicyOid),
                label.PolicyName ?? spifIndex.PolicyName);

            var classificationName = spifIndex.GetClassificationName(label.ClassificationLacv)
                ?? label.ClassificationName
                ?? label.ClassificationLacv.ToString();

            var classification = new XElement(
                SlabNs + "Classification",
                new XAttribute("lacv", label.ClassificationLacv.Value),
                classificationName);

            var confInfoElement = new XElement(SlabNs + "ConfidentialityInformation", policyIdentifier, classification);

            foreach (var tagSet in label.CategoryTagSets)
            {
                var spifTagSet = spifIndex.GetTagSet(tagSet.TagSetOid);
                foreach (var tag in tagSet.Tags)
                {
                    var selectedCategories = ResolveSelectedCategories(tag, spifTagSet);
                    foreach (var cat in selectedCategories)
                    {
                        confInfoElement.Add(new XElement(
                            SlabNs + "Category",
                            new XAttribute("TagSetOid", tagSet.TagSetOid),
                            new XAttribute("TagName", tag.Name ?? tag.TagOid ?? string.Empty),
                            new XAttribute("Type", ToWireTagType(tag.TagType)),
                            tag.EnumType is not null
                                ? new XAttribute("EnumType", tag.EnumType == EnumType.Restrictive ? "restrictive" : "permissive")
                                : null,
                            new XElement(
                                SlabNs + "CategoryValue",
                                new XAttribute("lacv", cat.Lacv.Value),
                                cat.Name)));
                    }
                }
            }

            var doc = new XDocument(
                new XElement(SlabNs + "originatorConfidentialityLabel",
                    confInfoElement,
                    new XElement(SlabNs + "CreationDateTime",
                        (label.CreatedAt ?? DateTimeOffset.UtcNow).ToString("o"))));

            return EncodeResult.Success(doc.ToString());
        }
        catch (Exception ex)
        {
            return EncodeResult.Failure($"Encoding error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public DecodeResult Decode(ReadOnlySpan<byte> encodedLabel)
    {
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(encodedLabel);
            return Decode(text);
        }
        catch (Exception ex)
        {
            return DecodeResult.Failure($"Decode error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public DecodeResult Decode(string encodedLabel)
    {
        try
        {
            var doc = XDocument.Parse(encodedLabel);
            var root = doc.Root;
            if (root is null)
            {
                return DecodeResult.Failure("Empty document");
            }

            var confInfo = root.Name.LocalName == "ConfidentialityInformation"
                ? root
                : root.Descendants().FirstOrDefault(e => e.Name.LocalName == "ConfidentialityInformation");
            if (confInfo is null)
            {
                return DecodeResult.Failure("Missing ConfidentialityInformation element");
            }

            var policyElement = confInfo.Elements().FirstOrDefault(e => e.Name.LocalName == "PolicyIdentifier");
            var classificationElement = confInfo.Elements().FirstOrDefault(e => e.Name.LocalName == "Classification");
            var createdAtElement = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "CreationDateTime");

            var policyName = policyElement?.Value?.Trim();
            var policyOid = policyElement?.Attribute("oid")?.Value?.Trim();
            var classificationName = classificationElement?.Value?.Trim();
            var classificationLacv = ParseLacv(classificationElement?.Attribute("lacv")?.Value, classificationName);

            var tagMap = new Dictionary<(string TagSetOid, string TagKey), MutableTag>();
            var categories = confInfo.Elements().Where(e => e.Name.LocalName == "Category");

            foreach (var catElem in categories)
            {
                var tagSetOid = catElem.Attribute("TagSetOid")?.Value?.Trim() ?? "decoded";
                var tagName = catElem.Attribute("TagName")?.Value?.Trim() ?? "default";
                var type = ParseTagType(catElem.Attribute("Type")?.Value ?? "restrictive");
                var enumType = ParseEnumType(catElem.Attribute("EnumType")?.Value);
                var categoryValueElement = catElem.Elements().FirstOrDefault(e => e.Name.LocalName == "CategoryValue");
                var categoryName = categoryValueElement?.Value?.Trim() ?? string.Empty;
                var categoryLacv = ParseLacv(categoryValueElement?.Attribute("lacv")?.Value, categoryName);

                var key = (tagSetOid, tagName);
                if (!tagMap.TryGetValue(key, out var mutableTag))
                {
                    mutableTag = new MutableTag(tagName, type, enumType);
                    tagMap[key] = mutableTag;
                }

                mutableTag.AddCategory(new LabelCategory
                {
                    Name = categoryName,
                    Lacv = categoryLacv
                });
            }

            var labelTagSets = tagMap
                .GroupBy(kvp => kvp.Key.TagSetOid)
                .Select(group => new LabelCategoryTagSet
                {
                    TagSetOid = group.Key,
                    Tags = group.Select(kvp => kvp.Value.Build()).ToImmutableList()
                })
                .ToImmutableList();

            DateTimeOffset? createdAt = null;
            if (DateTimeOffset.TryParse(createdAtElement?.Value, out var parsedCreatedAt))
            {
                createdAt = parsedCreatedAt;
            }

            var label = new SecurityLabel
            {
                PolicyOid = policyOid,
                PolicyName = policyName,
                ClassificationLacv = classificationLacv,
                ClassificationName = classificationName,
                CategoryTagSets = labelTagSets,
                CreatedAt = createdAt
            };

            return DecodeResult.Success(label);
        }
        catch (Exception ex)
        {
            return DecodeResult.Failure($"Decode error: {ex.Message}");
        }
    }

    private static ImmutableList<LabelCategory> ResolveSelectedCategories(LabelCategoryTag tag, ITagSetIndex? spifTagSet)
    {
        if (tag.Categories.Count > 0)
        {
            return tag.Categories;
        }

        var lacvs = tag.TagType == TagType.Enumerated ? tag.EnumeratedValues : tag.Bits;
        if (lacvs.Count == 0)
        {
            return ImmutableList<LabelCategory>.Empty;
        }

        return lacvs.Select(lacv => new LabelCategory
        {
            Name = ResolveCategoryName(spifTagSet, tag, lacv),
            Lacv = lacv
        }).ToImmutableList();
    }

    private static string ResolveCategoryName(ITagSetIndex? spifTagSet, LabelCategoryTag tag, LacvValue lacv)
        => spifTagSet?.GetCategory(tag.Name ?? string.Empty, lacv)?.Name
           ?? lacv.ToString();

    private static LacvValue ParseLacv(string? attributeValue, string? fallbackText)
    {
        if (int.TryParse(attributeValue, out var parsedAttribute))
        {
            return parsedAttribute;
        }

        if (int.TryParse(fallbackText, out var parsedText))
        {
            return parsedText;
        }

        return 0;
    }

    private static string ToWireTagType(TagType tagType) => tagType switch
    {
        TagType.Restrictive => "restrictive",
        TagType.Permissive => "permissive",
        TagType.Enumerated => "enumerated",
        TagType.TagType7 => "tagType7",
        _ => "notApplicable"
    };

    private static TagType ParseTagType(string value) => value.ToLowerInvariant() switch
    {
        "restrictive" => TagType.Restrictive,
        "permissive" => TagType.Permissive,
        "enumerated" => TagType.Enumerated,
        "tagtype7" => TagType.TagType7,
        _ => TagType.NotApplicable
    };

    private static EnumType? ParseEnumType(string? value) => value?.ToLowerInvariant() switch
    {
        "restrictive" => EnumType.Restrictive,
        "permissive" => EnumType.Permissive,
        _ => null
    };

    private sealed class MutableTag(string name, TagType tagType, EnumType? enumType)
    {
        private readonly List<LabelCategory> _categories = [];
        private readonly HashSet<LacvValue> _bits = [];
        private readonly HashSet<LacvValue> _enumeratedValues = [];

        public void AddCategory(LabelCategory category)
        {
            _categories.Add(category);
            if (tagType == TagType.Enumerated)
            {
                _enumeratedValues.Add(category.Lacv);
            }
            else
            {
                _bits.Add(category.Lacv);
            }
        }

        public LabelCategoryTag Build() => new()
        {
            Name = name,
            TagType = tagType,
            EnumType = enumType,
            Bits = _bits.ToImmutableHashSet(),
            EnumeratedValues = _enumeratedValues.ToImmutableHashSet(),
            Categories = _categories.ToImmutableList()
        };
    }
}
