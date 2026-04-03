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
            var confInfoElement = new XElement(SlabNs + "ConfidentialityInformation",
                new XElement(SlabNs + "PolicyIdentifier", spifIndex.PolicyName),
                new XElement(SlabNs + "Classification",
                    spifIndex.GetClassificationName(label.ClassificationLacv)
                    ?? label.ClassificationName
                    ?? label.ClassificationLacv.ToString())
            );

            // Add categories
            foreach (var tagSet in label.CategoryTagSets)
            {
                foreach (var tag in tagSet.Tags)
                {
                    foreach (var cat in tag.Categories)
                    {
                        confInfoElement.Add(new XElement(SlabNs + "Category",
                            new XAttribute("TagName", tag.Name ?? ""),
                            new XAttribute("Type", tag.TagType.ToString().ToLowerInvariant()),
                            new XElement(SlabNs + "CategoryValue", cat.Name)
                        ));
                    }
                }
            }

            var doc = new XDocument(
                new XElement(SlabNs + "originatorConfidentialityLabel",
                    confInfoElement,
                    new XElement(SlabNs + "CreationDateTime",
                        DateTimeOffset.UtcNow.ToString("o"))
                )
            );

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
                return DecodeResult.Failure("Empty document");

            // Find ConfidentialityInformation element (any namespace)
            var confInfo = root.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "ConfidentialityInformation");
            if (confInfo is null)
                return DecodeResult.Failure("Missing ConfidentialityInformation element");

            var policyName = confInfo.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "PolicyIdentifier")?.Value;
            var classificationName = confInfo.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Classification")?.Value;

            // Parse categories
            var tagSets = new Dictionary<string, List<LabelCategoryTag>>();
            var categories = confInfo.Elements()
                .Where(e => e.Name.LocalName == "Category");

            foreach (var catElem in categories)
            {
                var tagName = catElem.Attribute("TagName")?.Value ?? "default";
                var typeStr = catElem.Attribute("Type")?.Value ?? "restrictive";
                var catValue = catElem.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "CategoryValue")?.Value ?? "";

                if (!tagSets.ContainsKey(tagName))
                    tagSets[tagName] = [];

                // Find existing tag or create new one
                var existingTag = tagSets[tagName]
                    .FirstOrDefault(t => t.Name == tagName);

                if (existingTag is null)
                {
                    tagSets[tagName].Add(new LabelCategoryTag
                    {
                        Name = tagName,
                        TagType = ParseTagType(typeStr),
                        Categories = ImmutableList.Create(new LabelCategory
                        {
                            Name = catValue,
                            Lacv = 0 // Will need to be resolved against SPIF
                        })
                    });
                }
                else
                {
                    // Add category to existing tag
                    var idx = tagSets[tagName].IndexOf(existingTag);
                    tagSets[tagName][idx] = existingTag with
                    {
                        Categories = existingTag.Categories.Add(new LabelCategory
                        {
                            Name = catValue,
                            Lacv = 0
                        })
                    };
                }
            }

            // Build label tag sets
            var labelTagSets = ImmutableList.CreateBuilder<LabelCategoryTagSet>();
            if (tagSets.Count > 0)
            {
                labelTagSets.Add(new LabelCategoryTagSet
                {
                    TagSetOid = "decoded", // Will be resolved against SPIF
                    Tags = tagSets.Values.SelectMany(t => t).ToImmutableList()
                });
            }

            var label = new SecurityLabel
            {
                PolicyName = policyName,
                ClassificationName = classificationName,
                CategoryTagSets = labelTagSets.ToImmutable()
            };

            return DecodeResult.Success(label);
        }
        catch (Exception ex)
        {
            return DecodeResult.Failure($"Decode error: {ex.Message}");
        }
    }

    private static TagType ParseTagType(string value) => value.ToLowerInvariant() switch
    {
        "restrictive" => TagType.Restrictive,
        "permissive" => TagType.Permissive,
        "enumerated" => TagType.Enumerated,
        "tagtype7" => TagType.TagType7,
        _ => TagType.NotApplicable
    };
}
