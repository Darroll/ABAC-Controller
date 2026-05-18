using System.Xml.Linq;
using AbacController.Core.Constants;

namespace AbacController.Pap;

/// <summary>
/// Converts SPIF documents written in the legacy <c>urn:xmlspif:spif:3.0</c>
/// dialect (used by the 7 bundled sample policies that originated in the
/// Email Classification project and by the Blazor SpifEditor) into the
/// strict xmlspif.org v3.0 shape that <see cref="SpifSchemas"/> validates
/// against.
///
/// <para>
/// The two dialects overlap in intent but disagree on element and attribute
/// names:
/// </para>
/// <list type="bullet">
///   <item><description>The legacy root element has no <c>schemaVersion</c>
///   attribute, uses a <c>version</c> attribute of "3", and carries child
///   elements for the policy id, spif version, creation date, and
///   originator instead of attributes.</description></item>
///   <item><description>The legacy <c>securityCategoryTagSet</c> names its
///   tag set via <c>tagSetName</c>/<c>tagSetId</c> and carries the
///   classification and display metadata as extra attributes.</description></item>
///   <item><description>The legacy <c>securityCategoryTag</c> carries a
///   <c>tagName</c> + <c>lacv</c> directly on the tag itself; the strict
///   schema wraps each value in a <c>tagCategory</c> child.</description></item>
///   <item><description>The legacy <c>markingData</c> uses child elements
///   (<c>markingPhrase</c>, <c>markingCode</c>, colour children) where
///   the strict schema uses a <c>phrase</c> attribute and <c>code</c>
///   children.</description></item>
/// </list>
///
/// This normalizer is a pre-processing step: the parser still runs the
/// full XSD validation pipeline, but it sees the converted document so
/// all existing invariants hold. If the input is not in the legacy
/// dialect the method returns the content unchanged.
/// </summary>
public static class LegacySpifDialectNormalizer
{
    private static readonly XNamespace LegacyNs = SpifNamespaces.SpifUrnV3;
    private static readonly XNamespace SpifNs = SpifNamespaces.Spif;

    /// <summary>
    /// Transforms <paramref name="xmlContent"/> to the strict xmlspif.org
    /// dialect. When the document is already in the strict dialect (or
    /// anything other than <see cref="SpifNamespaces.SpifUrnV3"/>) the
    /// input is returned verbatim.
    /// </summary>
    public static string Normalize(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent)) return xmlContent;
        if (!xmlContent.Contains(SpifNamespaces.SpifUrnV3, StringComparison.Ordinal))
        {
            return xmlContent;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xmlContent, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            // Leave malformed input alone — the parser will surface a clean
            // XmlException further down the pipeline.
            return xmlContent;
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "SPIF" || root.Name.Namespace != LegacyNs)
        {
            return xmlContent;
        }

        var policyIdEl = root.Element(LegacyNs + "securityPolicyId");
        var oid = policyIdEl?.Attribute("oid")?.Value ?? string.Empty;
        var originator = root.Element(LegacyNs + "originator")?.Value?.Trim();
        var creationDate = root.Element(LegacyNs + "creationDate")?.Attribute("value")?.Value;
        var legacyVersion = root.Attribute("version")?.Value ?? "1";

        var newRoot = new XElement(
            SpifNs + "SPIF",
            new XAttribute("schemaVersion", "3.0"),
            new XAttribute("version", legacyVersion));

        if (!string.IsNullOrWhiteSpace(creationDate))
        {
            newRoot.Add(new XAttribute("creationDate", creationDate));
        }

        var policyName = !string.IsNullOrWhiteSpace(originator) ? originator! : oid;
        newRoot.Add(new XElement(
            SpifNs + "securityPolicyId",
            new XAttribute("name", policyName),
            new XAttribute("id", oid)));

        var classifications = root.Element(LegacyNs + "securityClassifications");
        if (classifications is not null)
        {
            var newClassifications = new XElement(SpifNs + "securityClassifications");
            foreach (var c in classifications.Elements(LegacyNs + "securityClassification"))
            {
                newClassifications.Add(ConvertClassification(c));
            }

            if (newClassifications.HasElements)
            {
                newRoot.Add(newClassifications);
            }
        }

        var tagSets = root.Element(LegacyNs + "securityCategoryTagSets");
        if (tagSets is not null)
        {
            var newTagSets = new XElement(SpifNs + "securityCategoryTagSets");
            foreach (var ts in tagSets.Elements(LegacyNs + "securityCategoryTagSet"))
            {
                var converted = ConvertTagSet(ts);
                if (converted is not null)
                {
                    newTagSets.Add(converted);
                }
            }

            if (newTagSets.HasElements)
            {
                newRoot.Add(newTagSets);
            }
        }

        var newDoc = new XDocument(doc.Declaration, newRoot);
        return newDoc.ToString();
    }

    private static XElement ConvertClassification(XElement legacy)
    {
        var name = legacy.Attribute("name")?.Value ?? "UNKNOWN";
        var lacv = legacy.Attribute("lacv")?.Value ?? "0";
        var hierarchy = legacy.Attribute("hierarchy")?.Value ?? lacv;

        var markingData = legacy.Element(LegacyNs + "markingData");
        var fg = markingData?.Element(LegacyNs + "foregroundColor")?.Attribute("value")?.Value;
        var bg = markingData?.Element(LegacyNs + "backgroundColor")?.Attribute("value")?.Value;

        var classification = new XElement(
            SpifNs + "securityClassification",
            new XAttribute("name", name),
            new XAttribute("lacv", lacv),
            new XAttribute("hierarchy", hierarchy));

        if (!string.IsNullOrWhiteSpace(fg)) classification.Add(new XAttribute("fgcolor", fg));
        if (!string.IsNullOrWhiteSpace(bg)) classification.Add(new XAttribute("bgcolor", bg));

        var converted = ConvertMarkingData(markingData, fallbackPhrase: name);
        if (converted is not null)
        {
            classification.Add(converted);
        }

        return classification;
    }

    private static XElement? ConvertTagSet(XElement legacy)
    {
        var name = legacy.Attribute("tagSetName")?.Value ?? legacy.Attribute("name")?.Value;
        var id = legacy.Attribute("tagSetId")?.Value ?? legacy.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var tagSet = new XElement(
            SpifNs + "securityCategoryTagSet",
            new XAttribute("name", name!),
            new XAttribute("id", id!));

        foreach (var tag in legacy.Elements(LegacyNs + "securityCategoryTag"))
        {
            var converted = ConvertTag(tag);
            if (converted is not null)
            {
                tagSet.Add(converted);
            }
        }

        return tagSet.HasElements ? tagSet : null;
    }

    private static XElement? ConvertTag(XElement legacy)
    {
        var tagName = legacy.Attribute("tagName")?.Value ?? legacy.Attribute("name")?.Value;
        var tagType = legacy.Attribute("tagType")?.Value ?? "restrictive";
        var lacvStr = legacy.Attribute("lacv")?.Value ?? "1";
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var tag = new XElement(
            SpifNs + "securityCategoryTag",
            new XAttribute("name", tagName!),
            new XAttribute("tagType", tagType));

        var category = new XElement(
            SpifNs + "tagCategory",
            new XAttribute("name", tagName!),
            new XAttribute("lacv", lacvStr));

        var markingData = legacy.Element(LegacyNs + "markingData");
        var converted = ConvertMarkingData(markingData, fallbackPhrase: tagName!);
        if (converted is not null)
        {
            category.Add(converted);
        }

        tag.Add(category);
        return tag;
    }

    private static XElement? ConvertMarkingData(XElement? legacy, string fallbackPhrase)
    {
        if (legacy is null)
        {
            return new XElement(
                SpifNs + "markingData",
                new XAttribute("phrase", fallbackPhrase));
        }

        var phraseEl = legacy.Element(LegacyNs + "markingPhrase");
        var phrase = phraseEl?.Attribute("phraseText")?.Value ?? fallbackPhrase;
        var abbreviation = phraseEl?.Attribute("portionAbbreviation")?.Value;
        var code = legacy.Element(LegacyNs + "markingCode")?.Attribute("codeValue")?.Value;

        var marking = new XElement(
            SpifNs + "markingData",
            new XAttribute("phrase", phrase));

        if (!string.IsNullOrWhiteSpace(abbreviation))
        {
            marking.Add(new XAttribute("shortPhrase", abbreviation));
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            marking.Add(new XElement(SpifNs + "code", code));
        }

        return marking;
    }
}
