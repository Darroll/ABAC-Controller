using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;

namespace AbacController.Pap;

/// <summary>
/// XML SPIF parser supporting v2.1 and v3.0 schemas.
/// Performs namespace normalization, parsing, and semantic validation.
/// </summary>
public sealed class SpifParser : ISpifParser
{
    private static readonly XNamespace SpifNs = SpifNamespaces.Spif;

    /// <inheritdoc />
    public SpifParseResult Parse(string xmlContent)
    {
        try
        {
            // Normalize namespace typo (xmslpif → xmlspif)
            xmlContent = xmlContent.Replace(
                SpifNamespaces.SpifTypo,
                SpifNamespaces.Spif);

            var doc = XDocument.Parse(xmlContent);
            return ParseDocument(doc);
        }
        catch (XmlException ex)
        {
            return SpifParseResult.Failed(
                [new SpifParseError($"XML parsing error: {ex.Message}", ex.LineNumber)]);
        }
        catch (Exception ex)
        {
            return SpifParseResult.Failed(
                [new SpifParseError($"Unexpected error: {ex.Message}")]);
        }
    }

    /// <inheritdoc />
    public SpifParseResult Parse(Stream xmlStream)
    {
        using var reader = new StreamReader(xmlStream);
        return Parse(reader.ReadToEnd());
    }

    /// <inheritdoc />
    public ValidationResult ValidateSchema(string xmlContent)
    {
        try
        {
            xmlContent = xmlContent.Replace(
                SpifNamespaces.SpifTypo,
                SpifNamespaces.Spif);

            XDocument.Parse(xmlContent);
            return ValidationResult.Valid();
        }
        catch (XmlException ex)
        {
            return ValidationResult.Invalid(
                [new SpifParseError($"XML validation error: {ex.Message}", ex.LineNumber)]);
        }
    }

    private SpifParseResult ParseDocument(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
            return SpifParseResult.Failed([new SpifParseError("Empty document")]);

        // Handle both namespaced and un-namespaced elements
        var spifElement = root.Name.LocalName == "SPIF" ? root : null;
        if (spifElement is null)
            return SpifParseResult.Failed([new SpifParseError("Root element must be SPIF")]);

        var warnings = new List<SpifParseWarning>();
        var errors = new List<SpifParseError>();

        // Parse root attributes
        var schemaVersion = spifElement.Attribute("schemaVersion")?.Value ?? "2.1";
        var version = spifElement.Attribute("version")?.Value;
        var creationDateStr = spifElement.Attribute("creationDate")?.Value;
        var originatorDn = spifElement.Attribute("originatorDN")?.Value;
        var keyIdentifier = spifElement.Attribute("keyIdentifier")?.Value;
        var privilegeId = spifElement.Attribute("privilegeId")?.Value;
        var rbacId = spifElement.Attribute("rbacId")?.Value;

        DateTimeOffset? creationDate = null;
        if (creationDateStr is not null)
        {
            creationDate = ParseGenTime(creationDateStr);
            if (creationDate is null)
                warnings.Add(new SpifParseWarning(
                    $"Could not parse creationDate: {creationDateStr}"));
        }

        // Parse securityPolicyId
        var policyIdElement = FindChild(spifElement, "securityPolicyId");
        if (policyIdElement is null)
            return SpifParseResult.Failed([new SpifParseError("Missing securityPolicyId element")]);

        var policyInfo = new PolicyInfo
        {
            Name = policyIdElement.Attribute("name")?.Value ?? "Unknown",
            Oid = policyIdElement.Attribute("id")?.Value ?? "",
            MarkingData = ParseMarkingDataList(policyIdElement)
        };

        if (string.IsNullOrEmpty(policyInfo.Oid))
            errors.Add(new SpifParseError("securityPolicyId missing 'id' (OID) attribute"));

        // Parse classifications
        var classificationsElement = FindChild(spifElement, "securityClassifications");
        var classifications = ImmutableList<SecurityClassification>.Empty;
        if (classificationsElement is not null)
        {
            classifications = ParseClassifications(classificationsElement, warnings);
        }
        else
        {
            errors.Add(new SpifParseError("Missing securityClassifications element"));
        }

        // Parse category tag sets
        var tagSetsElement = FindChild(spifElement, "securityCategoryTagSets");
        var tagSets = ImmutableList<SecurityCategoryTagSet>.Empty;
        if (tagSetsElement is not null)
        {
            tagSets = ParseTagSets(tagSetsElement, warnings);
        }

        // Parse equivalent policies
        var equivElement = FindChild(spifElement, "equivalentPolicies");
        var equivPolicies = ImmutableList<EquivalentPolicy>.Empty;
        if (equivElement is not null)
        {
            equivPolicies = ParseEquivalentPolicies(equivElement);
        }

        // Parse privacy marks
        PrivacyMarks? privacyMarks = null;
        var privacyElement = FindChild(spifElement, "privacyMarks");
        if (privacyElement is not null)
        {
            privacyMarks = ParsePrivacyMarks(privacyElement);
        }

        // Parse global marking data
        var globalMarkingData = ParseMarkingDataList(spifElement);
        var globalQualifiers = ParseMarkingQualifiers(spifElement);

        if (errors.Count > 0)
            return SpifParseResult.Failed(errors, warnings);

        // Semantic validation
        ValidateSemantics(classifications, tagSets, warnings, errors);

        if (errors.Count > 0)
            return SpifParseResult.Failed(errors, warnings);

        var spif = new Spif
        {
            SchemaVersion = schemaVersion,
            Version = version,
            CreationDate = creationDate,
            OriginatorDn = originatorDn,
            KeyIdentifier = keyIdentifier,
            PrivilegeId = privilegeId,
            RbacId = rbacId,
            PolicyId = policyInfo,
            Classifications = classifications,
            CategoryTagSets = tagSets,
            EquivalentPolicies = equivPolicies,
            PrivacyMarks = privacyMarks,
            GlobalMarkingData = globalMarkingData,
            GlobalMarkingQualifiers = globalQualifiers
        };

        return SpifParseResult.Succeeded(spif, warnings);
    }

    private ImmutableList<SecurityClassification> ParseClassifications(
        XElement parent, List<SpifParseWarning> warnings)
    {
        var builder = ImmutableList.CreateBuilder<SecurityClassification>();

        foreach (var elem in FindChildren(parent, "securityClassification"))
        {
            var name = elem.Attribute("name")?.Value ?? "Unknown";
            if (!int.TryParse(elem.Attribute("lacv")?.Value, out var lacv))
            {
                warnings.Add(new SpifParseWarning($"Classification '{name}' has invalid lacv"));
                continue;
            }
            if (!int.TryParse(elem.Attribute("hierarchy")?.Value, out var hierarchy))
            {
                warnings.Add(new SpifParseWarning($"Classification '{name}' has invalid hierarchy"));
                continue;
            }

            var cls = new SecurityClassification
            {
                Name = name,
                Lacv = lacv,
                Hierarchy = hierarchy,
                Obsolete = ParseBool(elem.Attribute("obsolete")?.Value),
                Color = elem.Attribute("color")?.Value,
                FgColor = elem.Attribute("fgcolor")?.Value,
                BgColor = elem.Attribute("bgcolor")?.Value,
                MarkingData = ParseMarkingDataList(elem),
                EquivalentClassifications = ParseEquivalentClassifications(elem),
                RequiredCategories = ParseRequiredCategories(elem),
                ExcludedCategories = ParseExcludedCategoryRefs(elem)
            };

            builder.Add(cls);
        }

        return builder.ToImmutable();
    }

    private ImmutableList<SecurityCategoryTagSet> ParseTagSets(
        XElement parent, List<SpifParseWarning> warnings)
    {
        var builder = ImmutableList.CreateBuilder<SecurityCategoryTagSet>();

        foreach (var tsElem in FindChildren(parent, "securityCategoryTagSet"))
        {
            var tagSet = new SecurityCategoryTagSet
            {
                TagSetOid = tsElem.Attribute("id")?.Value ?? "",
                Name = tsElem.Attribute("name")?.Value ?? "Unknown",
                Tags = ParseCategoryTags(tsElem, warnings)
            };
            builder.Add(tagSet);
        }

        return builder.ToImmutable();
    }

    private ImmutableList<SecurityCategoryTag> ParseCategoryTags(
        XElement parent, List<SpifParseWarning> warnings)
    {
        var builder = ImmutableList.CreateBuilder<SecurityCategoryTag>();

        foreach (var tagElem in FindChildren(parent, "securityCategoryTag"))
        {
            var tagTypeStr = tagElem.Attribute("tagType")?.Value ?? "notApplicable";
            var enumTypeStr = tagElem.Attribute("enumType")?.Value;

            var tag = new SecurityCategoryTag
            {
                Name = tagElem.Attribute("name")?.Value ?? "Unknown",
                TagType = ParseTagType(tagTypeStr),
                EnumType = enumTypeStr is not null ? ParseEnumType(enumTypeStr) : null,
                Categories = ParseTagCategories(tagElem, warnings),
                MarkingData = ParseMarkingDataList(tagElem)
            };
            builder.Add(tag);
        }

        return builder.ToImmutable();
    }

    private ImmutableList<TagCategory> ParseTagCategories(
        XElement parent, List<SpifParseWarning> warnings)
    {
        var builder = ImmutableList.CreateBuilder<TagCategory>();

        foreach (var catElem in FindChildren(parent, "tagCategory"))
        {
            var name = catElem.Attribute("name")?.Value ?? "Unknown";
            if (!int.TryParse(catElem.Attribute("lacv")?.Value, out var lacv))
            {
                warnings.Add(new SpifParseWarning($"Category '{name}' has invalid lacv"));
                continue;
            }

            var notBeforeStr = catElem.Attribute("notBefore")?.Value;
            var notAfterStr = catElem.Attribute("notAfter")?.Value;

            var cat = new TagCategory
            {
                Name = name,
                Lacv = lacv,
                Obsolete = ParseBool(catElem.Attribute("obsolete")?.Value),
                RequiredClass = catElem.Attribute("requiredClass")?.Value,
                UserInput = catElem.Attribute("userInput")?.Value,
                DateFormat = catElem.Attribute("dateFormat")?.Value,
                NotBefore = notBeforeStr is not null ? ParseIso8601(notBeforeStr) : null,
                NotAfter = notAfterStr is not null ? ParseIso8601(notAfterStr) : null,
                MarkingData = ParseMarkingDataList(catElem),
                EquivalentCategories = ParseEquivalentCategoryTags(catElem),
                ExcludedClasses = ParseExcludedClasses(catElem),
                RequiredCategories = ParseRequiredCategories(catElem),
                ExcludedCategories = ParseExcludedCategoryRefs(catElem)
            };
            builder.Add(cat);
        }

        return builder.ToImmutable();
    }

    private ImmutableList<EquivalentClassification> ParseEquivalentClassifications(XElement parent)
    {
        var builder = ImmutableList.CreateBuilder<EquivalentClassification>();
        foreach (var elem in FindChildren(parent, "equivalentClassification"))
        {
            if (!int.TryParse(elem.Attribute("lacv")?.Value, out var lacv)) continue;
            builder.Add(new EquivalentClassification
            {
                PolicyRef = elem.Attribute("policyRef")?.Value ?? "",
                Lacv = lacv,
                Applied = ParseEquivDirection(elem.Attribute("applied")?.Value),
                RequiredCategories = ParseRequiredCategories(elem)
            });
        }
        return builder.ToImmutable();
    }

    private ImmutableList<EquivalentCategoryTag> ParseEquivalentCategoryTags(XElement parent)
    {
        var builder = ImmutableList.CreateBuilder<EquivalentCategoryTag>();
        foreach (var elem in FindChildren(parent, "equivalentSecCategoryTag"))
        {
            if (!int.TryParse(elem.Attribute("lacv")?.Value, out var lacv)) continue;
            builder.Add(new EquivalentCategoryTag
            {
                PolicyRef = elem.Attribute("policyRef")?.Value ?? "",
                TagSetId = elem.Attribute("tagSetId")?.Value ?? "",
                TagType = ParseTagType(elem.Attribute("tagType")?.Value ?? "notApplicable"),
                Lacv = lacv,
                Applied = ParseEquivDirection(elem.Attribute("applied")?.Value),
                Action = elem.Attribute("action")?.Value
            });
        }
        return builder.ToImmutable();
    }

    private ImmutableList<RequiredCategoryConstraint> ParseRequiredCategories(XElement parent)
    {
        var builder = ImmutableList.CreateBuilder<RequiredCategoryConstraint>();
        foreach (var elem in FindChildren(parent, "requiredCategory"))
        {
            builder.Add(new RequiredCategoryConstraint
            {
                Operation = elem.Attribute("operation")?.Value ?? "all",
                CategoryGroups = ParseCategoryGroupRefs(elem)
            });
        }
        return builder.ToImmutable();
    }

    private ImmutableList<CategoryGroupRef> ParseCategoryGroupRefs(XElement parent)
    {
        var builder = ImmutableList.CreateBuilder<CategoryGroupRef>();
        foreach (var elem in FindChildren(parent, "categoryGroup"))
        {
            if (!int.TryParse(elem.Attribute("lacv")?.Value, out var lacv)) continue;
            var enumTypeStr = elem.Attribute("enumType")?.Value;
            builder.Add(new CategoryGroupRef
            {
                TagSetRef = elem.Attribute("tagSetRef")?.Value ?? "",
                TagType = ParseTagType(elem.Attribute("tagType")?.Value ?? "notApplicable"),
                Lacv = lacv,
                EnumType = enumTypeStr is not null ? ParseEnumType(enumTypeStr) : null
            });
        }
        return builder.ToImmutable();
    }

    private ImmutableList<ExcludedCategoryRef> ParseExcludedCategoryRefs(XElement parent)
    {
        var builder = ImmutableList.CreateBuilder<ExcludedCategoryRef>();
        foreach (var elem in FindChildren(parent, "excludedCategory"))
        {
            if (!int.TryParse(elem.Attribute("lacv")?.Value, out var lacv)) continue;
            builder.Add(new ExcludedCategoryRef
            {
                TagSetRef = elem.Attribute("tagSetRef")?.Value ?? "",
                TagType = ParseTagType(elem.Attribute("tagType")?.Value ?? "notApplicable"),
                Lacv = lacv
            });
        }
        return builder.ToImmutable();
    }

    private ImmutableList<string> ParseExcludedClasses(XElement parent)
    {
        var builder = ImmutableList.CreateBuilder<string>();
        foreach (var elem in FindChildren(parent, "excludedClass"))
        {
            var name = elem.Value.Trim();
            if (!string.IsNullOrEmpty(name))
                builder.Add(name);
        }
        return builder.ToImmutable();
    }

    private ImmutableList<EquivalentPolicy> ParseEquivalentPolicies(XElement parent)
    {
        var builder = ImmutableList.CreateBuilder<EquivalentPolicy>();
        foreach (var elem in FindChildren(parent, "equivalentPolicy"))
        {
            builder.Add(new EquivalentPolicy
            {
                Name = elem.Attribute("name")?.Value ?? "Unknown",
                PolicyOid = elem.Attribute("id")?.Value ?? "",
                DocRefUri = elem.Attribute("docRefURI")?.Value
            });
        }
        return builder.ToImmutable();
    }

    private PrivacyMarks ParsePrivacyMarks(XElement parent)
    {
        var marks = ImmutableList.CreateBuilder<PrivacyMark>();
        foreach (var elem in FindChildren(parent, "privacyMark"))
        {
            marks.Add(new PrivacyMark
            {
                Name = elem.Attribute("name")?.Value ?? "",
                Obsolete = ParseBool(elem.Attribute("obsolete")?.Value),
                MarkingData = ParseMarkingDataList(elem)
            });
        }

        int.TryParse(parent.Attribute("maxSelection")?.Value, out var maxSel);
        int.TryParse(parent.Attribute("minSelection")?.Value, out var minSel);

        return new PrivacyMarks
        {
            MaxSelection = maxSel > 0 ? maxSel : null,
            MinSelection = minSel > 0 ? minSel : null,
            Marks = marks.ToImmutable()
        };
    }

    private ImmutableList<MarkingData> ParseMarkingDataList(XElement parent)
    {
        var builder = ImmutableList.CreateBuilder<MarkingData>();
        foreach (var elem in FindChildren(parent, "markingData"))
        {
            var codes = ImmutableList.CreateBuilder<string>();
            foreach (var codeElem in FindChildren(elem, "code"))
            {
                codes.Add(codeElem.Value.Trim());
            }

            builder.Add(new MarkingData
            {
                Phrase = elem.Attribute("phrase")?.Value ?? "",
                ShortPhrase = elem.Attribute("shortPhrase")?.Value,
                SimplePhrase = elem.Attribute("simplePhrase")?.Value,
                InputPhrase = elem.Attribute("inputPhrase")?.Value,
                Language = elem.Attribute(XNamespace.Xml + "lang")?.Value,
                Codes = codes.ToImmutable()
            });
        }
        return builder.ToImmutable();
    }

    private ImmutableList<MarkingQualifier> ParseMarkingQualifiers(XElement parent)
    {
        var builder = ImmutableList.CreateBuilder<MarkingQualifier>();
        foreach (var elem in FindChildren(parent, "markingQualifier"))
        {
            var qualifiers = ImmutableList.CreateBuilder<QualifierEntry>();
            foreach (var qElem in FindChildren(elem, "qualifier"))
            {
                qualifiers.Add(new QualifierEntry
                {
                    Text = qElem.Attribute("markingQualifier")?.Value ?? "",
                    Code = ParseQualifierCode(qElem.Attribute("qualifierCode")?.Value)
                });
            }

            builder.Add(new MarkingQualifier
            {
                MarkingCode = elem.Attribute("markingCode")?.Value ?? "",
                Qualifiers = qualifiers.ToImmutable()
            });
        }
        return builder.ToImmutable();
    }

    private void ValidateSemantics(
        ImmutableList<SecurityClassification> classifications,
        ImmutableList<SecurityCategoryTagSet> tagSets,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        // Check classification lacv uniqueness
        var seenLacvs = new HashSet<int>();
        foreach (var cls in classifications)
        {
            if (!seenLacvs.Add(cls.Lacv.Value))
                errors.Add(new SpifParseError(
                    $"Duplicate classification lacv: {cls.Lacv} (name: {cls.Name})"));
        }

        // Check category lacv uniqueness within each tag
        foreach (var tagSet in tagSets)
        {
            foreach (var tag in tagSet.Tags)
            {
                var seenCatLacvs = new HashSet<int>();
                foreach (var cat in tag.Categories)
                {
                    if (!seenCatLacvs.Add(cat.Lacv.Value))
                        errors.Add(new SpifParseError(
                            $"Duplicate category lacv {cat.Lacv} in tag '{tag.Name}' " +
                            $"of tag set '{tagSet.Name}'"));
                }
            }
        }
    }

    // ── Helper methods ──

    /// <summary>Find a child element by local name, ignoring namespace.</summary>
    private static XElement? FindChild(XElement parent, string localName)
        => parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    /// <summary>Find all child elements by local name, ignoring namespace.</summary>
    private static IEnumerable<XElement> FindChildren(XElement parent, string localName)
        => parent.Elements().Where(e => e.Name.LocalName == localName);

    private static TagType ParseTagType(string value) => value.ToLowerInvariant() switch
    {
        "restrictive" => TagType.Restrictive,
        "permissive" => TagType.Permissive,
        "enumerated" => TagType.Enumerated,
        "tagtype7" => TagType.TagType7,
        _ => TagType.NotApplicable
    };

    private static EnumType ParseEnumType(string value) => value.ToLowerInvariant() switch
    {
        "restrictive" => EnumType.Restrictive,
        "permissive" => EnumType.Permissive,
        _ => EnumType.Restrictive
    };

    private static EquivalencyDirection ParseEquivDirection(string? value) => value?.ToLowerInvariant() switch
    {
        "encrypt" => EquivalencyDirection.Encrypt,
        "decrypt" => EquivalencyDirection.Decrypt,
        "both" => EquivalencyDirection.Both,
        _ => EquivalencyDirection.Both
    };

    private static QualifierCode ParseQualifierCode(string? value) => value?.ToLowerInvariant() switch
    {
        "prefix" => QualifierCode.Prefix,
        "suffix" => QualifierCode.Suffix,
        "separator" => QualifierCode.Separator,
        "finalseparator" => QualifierCode.FinalSeparator,
        _ => QualifierCode.Prefix
    };

    private static bool ParseBool(string? value)
        => value is not null && (value == "true" || value == "1");

    private static DateTimeOffset? ParseGenTime(string value)
    {
        // genTime format: YYYYMMDDHHMMSSZ or variations
        if (DateTimeOffset.TryParse(value, out var dto))
            return dto;

        // Try YYYYMMDDHHMMSSZ format
        if (value.Length >= 14 && value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            var cleaned = value[..14];
            if (DateTime.TryParseExact(cleaned, "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            {
                return new DateTimeOffset(dt, TimeSpan.Zero);
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseIso8601(string value)
    {
        if (DateTimeOffset.TryParse(value, out var dto))
            return dto;
        return null;
    }
}
