using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;

namespace AbacController.Pap;

/// <summary>
/// XML SPIF parser supporting v3.0 as the primary schema and v2.1 as the compatibility fallback.
/// Performs namespace normalization, structural validation, and semantic validation.
/// </summary>
public sealed class SpifParser : ISpifParser
{
    private static readonly StringComparer OidComparer = StringComparer.Ordinal;
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> SupportedSchemaVersions =
    [
        "3.0",
        "2.1"
    ];

    private static readonly Regex OidPattern = new("^[0-2](\\.[0-9]+)+$", RegexOptions.Compiled);

    private readonly IXmlSignatureVerifier _signatureVerifier;

    /// <summary>Initializes a new instance of the <see cref="SpifParser"/> class.</summary>
    public SpifParser(IXmlSignatureVerifier? signatureVerifier = null)
    {
        _signatureVerifier = signatureVerifier ?? new RejectingXmlSignatureVerifier();
    }

    /// <inheritdoc />
    public SpifParseResult Parse(string xmlContent)
    {
        ArgumentNullException.ThrowIfNull(xmlContent);

        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return SpifParseResult.Failed([new SpifParseError("SPIF XML content is empty")]);
        }

        try
        {
            var normalized = NormalizeNamespaceTypos(xmlContent);
            var document = XDocument.Parse(normalized, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            return ParseDocument(document);
        }
        catch (XmlException ex)
        {
            return SpifParseResult.Failed(
            [
                new SpifParseError($"XML parsing error: {ex.Message}", ex.LineNumber)
            ]);
        }
        catch (Exception ex)
        {
            return SpifParseResult.Failed(
            [
                new SpifParseError($"Unexpected error: {ex.Message}")
            ]);
        }
    }

    /// <inheritdoc />
    public SpifParseResult Parse(Stream xmlStream)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);

        using var reader = new StreamReader(xmlStream, leaveOpen: true);
        return Parse(reader.ReadToEnd());
    }

    /// <inheritdoc />
    public ValidationResult ValidateSchema(string xmlContent)
    {
        ArgumentNullException.ThrowIfNull(xmlContent);

        try
        {
            var normalized = NormalizeNamespaceTypos(xmlContent);
            var document = XDocument.Parse(normalized, LoadOptions.SetLineInfo);
            var validationErrors = ValidateDocumentShape(document);
            if (validationErrors.Count > 0)
            {
                return ValidationResult.Invalid(validationErrors);
            }

            var schemaErrors = new List<SpifParseError>();
            document.Validate(SpifSchemas.CreateValidationSet(), (sender, args) =>
            {
                var lineInfo = sender as IXmlLineInfo;
                schemaErrors.Add(new SpifParseError(
                    $"Schema validation error: {args.Message}",
                    lineInfo is not null && lineInfo.HasLineInfo() ? lineInfo.LineNumber : null));
            }, true);

            return schemaErrors.Count == 0
                ? ValidationResult.Valid()
                : ValidationResult.Invalid(schemaErrors);
        }
        catch (XmlSchemaValidationException ex)
        {
            return ValidationResult.Invalid(
            [
                new SpifParseError($"Schema validation error: {ex.Message}", ex.LineNumber)
            ]);
        }
        catch (XmlException ex)
        {
            return ValidationResult.Invalid(
            [
                new SpifParseError($"XML validation error: {ex.Message}", ex.LineNumber)
            ]);
        }
    }

    private static string NormalizeNamespaceTypos(string xmlContent)
        => xmlContent.Replace(SpifNamespaces.SpifTypo, SpifNamespaces.Spif, StringComparison.Ordinal);

    private SpifParseResult ParseDocument(XDocument document)
    {
        var root = document.Root;
        if (root is null)
        {
            return SpifParseResult.Failed([new SpifParseError("Empty SPIF document")]);
        }

        var validationErrors = ValidateDocumentShape(document);
        if (validationErrors.Count > 0)
        {
            return SpifParseResult.Failed(validationErrors);
        }

        var warnings = new List<SpifParseWarning>();
        var errors = new List<SpifParseError>();

        var schemaValidation = ValidateSchema(document.ToString(SaveOptions.DisableFormatting));
        if (!schemaValidation.IsValid)
        {
            errors.AddRange(schemaValidation.Errors);
            return SpifParseResult.Failed(errors, warnings);
        }

        var signatureVerification = _signatureVerifier.Verify(document, root.Attribute("keyIdentifier")?.Value?.Trim());
        if (!signatureVerification.IsSuccess)
        {
            errors.Add(new SpifParseError(signatureVerification.Message ?? "XML-DSig verification failed", GetLineNumber(root)));
            return SpifParseResult.Failed(errors, warnings);
        }

        if (!string.IsNullOrWhiteSpace(signatureVerification.Message))
        {
            warnings.Add(new SpifParseWarning(signatureVerification.Message!, GetLineNumber(root)));
        }

        var schemaVersion = root.Attribute("schemaVersion")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            schemaVersion = "2.1";
            warnings.Add(new SpifParseWarning("schemaVersion missing; assuming v2.1", GetLineNumber(root)));
        }
        else if (!SupportedSchemaVersions.Contains(schemaVersion))
        {
            warnings.Add(new SpifParseWarning(
                $"Schema version '{schemaVersion}' is not explicitly targeted; parsing using v2.1/v3.0 compatibility rules",
                GetLineNumber(root)));
        }

        var creationDate = ParseOptionalDateTime(root.Attribute("creationDate")?.Value, root, warnings, "creationDate");

        var policyIdElement = FindRequiredChild(root, "securityPolicyId", errors);
        var classificationsElement = FindRequiredChild(root, "securityClassifications", errors);

        if (errors.Count > 0)
        {
            return SpifParseResult.Failed(errors, warnings);
        }

        var spif = new Spif
        {
            SchemaVersion = schemaVersion!,
            Version = EmptyToNull(root.Attribute("version")?.Value),
            CreationDate = creationDate,
            OriginatorDn = EmptyToNull(root.Attribute("originatorDN")?.Value),
            KeyIdentifier = EmptyToNull(root.Attribute("keyIdentifier")?.Value),
            PrivilegeId = EmptyToNull(root.Attribute("privilegeId")?.Value),
            RbacId = EmptyToNull(root.Attribute("rbacId")?.Value),
            PolicyId = ParsePolicyInfo(policyIdElement!, errors),
            Classifications = ParseClassifications(classificationsElement!, warnings, errors),
            CategoryTagSets = ParseTagSets(FindChild(root, "securityCategoryTagSets"), warnings, errors),
            EquivalentPolicies = ParseEquivalentPolicies(FindChild(root, "equivalentPolicies"), warnings, errors),
            PrivacyMarks = ParsePrivacyMarks(FindChild(root, "privacyMarks"), warnings),
            GlobalMarkingData = ParseMarkingDataList(root, warnings, errors),
            GlobalMarkingQualifiers = ParseMarkingQualifiers(root, warnings)
        };

        ValidateSemantics(spif, warnings, errors);

        return errors.Count > 0
            ? SpifParseResult.Failed(errors, warnings)
            : SpifParseResult.Succeeded(spif, warnings);
    }

    private static List<SpifParseError> ValidateDocumentShape(XDocument document)
    {
        var errors = new List<SpifParseError>();
        var root = document.Root;
        if (root is null)
        {
            errors.Add(new SpifParseError("Empty SPIF document"));
            return errors;
        }

        if (!string.Equals(root.Name.LocalName, "SPIF", StringComparison.Ordinal))
        {
            errors.Add(new SpifParseError("Root element must be SPIF", GetLineNumber(root)));
        }

        var namespaceName = root.Name.NamespaceName;
        if (!string.IsNullOrEmpty(namespaceName) &&
            !string.Equals(namespaceName, SpifNamespaces.Spif, StringComparison.Ordinal))
        {
            errors.Add(new SpifParseError(
                $"Unsupported SPIF namespace '{namespaceName}'",
                GetLineNumber(root)));
        }

        return errors;
    }

    private static PolicyInfo ParsePolicyInfo(XElement policyIdElement, List<SpifParseError> errors)
    {
        var oid = EmptyToNull(policyIdElement.Attribute("id")?.Value);
        if (oid is null)
        {
            errors.Add(new SpifParseError("securityPolicyId missing required 'id' attribute", GetLineNumber(policyIdElement)));
            oid = string.Empty;
        }

        var name = EmptyToNull(policyIdElement.Attribute("name")?.Value);
        if (name is null)
        {
            errors.Add(new SpifParseError("securityPolicyId missing required 'name' attribute", GetLineNumber(policyIdElement)));
            name = "Unknown";
        }

        return new PolicyInfo
        {
            Name = name,
            Oid = oid,
            MarkingData = ParseMarkingDataList(policyIdElement, [], errors)
        };
    }

    private static ImmutableList<SecurityClassification> ParseClassifications(
        XElement classificationsElement,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        var builder = ImmutableList.CreateBuilder<SecurityClassification>();

        foreach (var classificationElement in FindChildren(classificationsElement, "securityClassification"))
        {
            var name = EmptyToNull(classificationElement.Attribute("name")?.Value);
            if (name is null)
            {
                errors.Add(new SpifParseError(
                    "securityClassification missing required 'name' attribute",
                    GetLineNumber(classificationElement)));
                continue;
            }

            if (!TryParseLacv(classificationElement.Attribute("lacv")?.Value, out var lacv))
            {
                errors.Add(new SpifParseError(
                    $"securityClassification '{name}' has missing or invalid lacv",
                    GetLineNumber(classificationElement)));
                continue;
            }

            if (!int.TryParse(classificationElement.Attribute("hierarchy")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hierarchy))
            {
                errors.Add(new SpifParseError(
                    $"securityClassification '{name}' has missing or invalid hierarchy",
                    GetLineNumber(classificationElement)));
                continue;
            }

            builder.Add(new SecurityClassification
            {
                Name = name,
                Lacv = lacv,
                Hierarchy = hierarchy,
                Obsolete = ParseBool(classificationElement.Attribute("obsolete")?.Value),
                Color = EmptyToNull(classificationElement.Attribute("color")?.Value),
                FgColor = EmptyToNull(classificationElement.Attribute("fgcolor")?.Value),
                BgColor = EmptyToNull(classificationElement.Attribute("bgcolor")?.Value),
                MarkingData = ParseMarkingDataList(classificationElement, warnings, errors),
                EquivalentClassifications = ParseEquivalentClassifications(classificationElement, warnings, errors),
                RequiredCategories = ParseRequiredCategories(classificationElement, warnings, errors),
                ExcludedCategories = ParseExcludedCategoryRefs(classificationElement, warnings, errors)
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<SecurityCategoryTagSet> ParseTagSets(
        XElement? tagSetsElement,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        if (tagSetsElement is null)
        {
            return ImmutableList<SecurityCategoryTagSet>.Empty;
        }

        var builder = ImmutableList.CreateBuilder<SecurityCategoryTagSet>();

        foreach (var tagSetElement in FindChildren(tagSetsElement, "securityCategoryTagSet"))
        {
            var oid = EmptyToNull(tagSetElement.Attribute("id")?.Value);
            if (oid is null)
            {
                errors.Add(new SpifParseError(
                    "securityCategoryTagSet missing required 'id' attribute",
                    GetLineNumber(tagSetElement)));
                continue;
            }

            builder.Add(new SecurityCategoryTagSet
            {
                TagSetOid = oid,
                Name = EmptyToNull(tagSetElement.Attribute("name")?.Value) ?? oid,
                Tags = ParseCategoryTags(tagSetElement, warnings, errors)
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<SecurityCategoryTag> ParseCategoryTags(
        XElement tagSetElement,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        var builder = ImmutableList.CreateBuilder<SecurityCategoryTag>();

        foreach (var tagElement in FindChildren(tagSetElement, "securityCategoryTag"))
        {
            var tagType = ParseTagType(tagElement.Attribute("tagType")?.Value, tagElement, warnings);
            var enumType = ParseEnumType(tagElement.Attribute("enumType")?.Value, tagElement, warnings);

            builder.Add(new SecurityCategoryTag
            {
                Name = EmptyToNull(tagElement.Attribute("name")?.Value) ?? "Unknown",
                TagType = tagType,
                EnumType = enumType,
                Categories = ParseTagCategories(tagElement, warnings, errors),
                MarkingData = ParseMarkingDataList(tagElement, warnings, errors)
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<TagCategory> ParseTagCategories(
        XElement tagElement,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        var builder = ImmutableList.CreateBuilder<TagCategory>();

        foreach (var categoryElement in FindChildren(tagElement, "tagCategory"))
        {
            var name = EmptyToNull(categoryElement.Attribute("name")?.Value);
            if (name is null)
            {
                errors.Add(new SpifParseError(
                    "tagCategory missing required 'name' attribute",
                    GetLineNumber(categoryElement)));
                continue;
            }

            if (!TryParseLacv(categoryElement.Attribute("lacv")?.Value, out var lacv))
            {
                errors.Add(new SpifParseError(
                    $"tagCategory '{name}' has missing or invalid lacv",
                    GetLineNumber(categoryElement)));
                continue;
            }

            builder.Add(new TagCategory
            {
                Name = name,
                Lacv = lacv,
                Obsolete = ParseBool(categoryElement.Attribute("obsolete")?.Value),
                RequiredClass = EmptyToNull(categoryElement.Attribute("requiredClass")?.Value),
                UserInput = EmptyToNull(categoryElement.Attribute("userInput")?.Value),
                DateFormat = EmptyToNull(categoryElement.Attribute("dateFormat")?.Value),
                NotBefore = ParseOptionalDateTime(categoryElement.Attribute("notBefore")?.Value, categoryElement, warnings, "notBefore"),
                NotAfter = ParseOptionalDateTime(categoryElement.Attribute("notAfter")?.Value, categoryElement, warnings, "notAfter"),
                MarkingData = ParseMarkingDataList(categoryElement, warnings, errors),
                EquivalentCategories = ParseEquivalentCategoryTags(categoryElement, warnings, errors),
                ExcludedClasses = ParseExcludedClasses(categoryElement),
                RequiredCategories = ParseRequiredCategories(categoryElement, warnings, errors),
                ExcludedCategories = ParseExcludedCategoryRefs(categoryElement, warnings, errors)
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<EquivalentClassification> ParseEquivalentClassifications(
        XElement parent,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        var builder = ImmutableList.CreateBuilder<EquivalentClassification>();

        foreach (var element in FindChildren(parent, "equivalentClassification"))
        {
            var policyRef = EmptyToNull(element.Attribute("policyRef")?.Value);
            if (policyRef is null)
            {
                errors.Add(new SpifParseError("equivalentClassification missing required 'policyRef' attribute", GetLineNumber(element)));
                continue;
            }

            if (!TryParseLacv(element.Attribute("lacv")?.Value, out var lacv))
            {
                errors.Add(new SpifParseError(
                    $"equivalentClassification for policy '{policyRef}' has missing or invalid lacv",
                    GetLineNumber(element)));
                continue;
            }

            builder.Add(new EquivalentClassification
            {
                PolicyRef = policyRef,
                Lacv = lacv,
                Applied = ParseEquivalencyDirection(element.Attribute("applied")?.Value, element, warnings),
                RequiredCategories = ParseRequiredCategories(element, warnings, errors)
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<EquivalentCategoryTag> ParseEquivalentCategoryTags(
        XElement parent,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        var builder = ImmutableList.CreateBuilder<EquivalentCategoryTag>();

        foreach (var element in FindChildren(parent, "equivalentSecCategoryTag"))
        {
            var policyRef = EmptyToNull(element.Attribute("policyRef")?.Value);
            var tagSetId = EmptyToNull(element.Attribute("tagSetId")?.Value);
            if (policyRef is null || tagSetId is null)
            {
                errors.Add(new SpifParseError(
                    "equivalentSecCategoryTag missing required 'policyRef' or 'tagSetId' attribute",
                    GetLineNumber(element)));
                continue;
            }

            if (!TryParseLacv(element.Attribute("lacv")?.Value, out var lacv))
            {
                errors.Add(new SpifParseError(
                    $"equivalentSecCategoryTag for policy '{policyRef}' has missing or invalid lacv",
                    GetLineNumber(element)));
                continue;
            }

            builder.Add(new EquivalentCategoryTag
            {
                PolicyRef = policyRef,
                TagSetId = tagSetId,
                TagType = ParseTagType(element.Attribute("tagType")?.Value, element, warnings),
                Lacv = lacv,
                Applied = ParseEquivalencyDirection(element.Attribute("applied")?.Value, element, warnings),
                Action = EmptyToNull(element.Attribute("action")?.Value)
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<RequiredCategoryConstraint> ParseRequiredCategories(
        XElement parent,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        var builder = ImmutableList.CreateBuilder<RequiredCategoryConstraint>();

        foreach (var element in FindChildren(parent, "requiredCategory"))
        {
            var operation = EmptyToNull(element.Attribute("operation")?.Value) ?? "all";
            if (!IsSupportedRequiredOperation(operation))
            {
                warnings.Add(new SpifParseWarning(
                    $"requiredCategory uses non-standard operation '{operation}'",
                    GetLineNumber(element)));
            }

            builder.Add(new RequiredCategoryConstraint
            {
                Operation = operation,
                CategoryGroups = ParseCategoryGroupRefs(element, warnings, errors)
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<CategoryGroupRef> ParseCategoryGroupRefs(
        XElement parent,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        var builder = ImmutableList.CreateBuilder<CategoryGroupRef>();

        foreach (var element in FindChildren(parent, "categoryGroup"))
        {
            var tagSetRef = EmptyToNull(element.Attribute("tagSetRef")?.Value)
                ?? EmptyToNull(element.Attribute("tagSetId")?.Value);
            if (tagSetRef is null)
            {
                errors.Add(new SpifParseError("categoryGroup missing required 'tagSetRef' or 'tagSetId' attribute", GetLineNumber(element)));
                continue;
            }

            if (!TryParseLacv(element.Attribute("lacv")?.Value, out var lacv))
            {
                errors.Add(new SpifParseError("categoryGroup missing required or valid 'lacv' attribute", GetLineNumber(element)));
                continue;
            }

            builder.Add(new CategoryGroupRef
            {
                TagSetRef = tagSetRef,
                TagType = ParseTagType(element.Attribute("tagType")?.Value, element, warnings),
                Lacv = lacv,
                EnumType = ParseEnumType(element.Attribute("enumType")?.Value, element, warnings)
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<ExcludedCategoryRef> ParseExcludedCategoryRefs(
        XElement parent,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        var builder = ImmutableList.CreateBuilder<ExcludedCategoryRef>();

        foreach (var element in FindChildren(parent, "excludedCategory"))
        {
            var tagSetRef = EmptyToNull(element.Attribute("tagSetRef")?.Value)
                ?? EmptyToNull(element.Attribute("tagSetId")?.Value);
            if (tagSetRef is null)
            {
                errors.Add(new SpifParseError("excludedCategory missing required 'tagSetRef' or 'tagSetId' attribute", GetLineNumber(element)));
                continue;
            }

            if (!TryParseLacv(element.Attribute("lacv")?.Value, out var lacv))
            {
                errors.Add(new SpifParseError("excludedCategory missing required or valid 'lacv' attribute", GetLineNumber(element)));
                continue;
            }

            builder.Add(new ExcludedCategoryRef
            {
                TagSetRef = tagSetRef,
                TagType = ParseTagType(element.Attribute("tagType")?.Value, element, warnings),
                Lacv = lacv
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<string> ParseExcludedClasses(XElement parent)
        => FindChildren(parent, "excludedClass")
            .Select(element => element.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(NameComparer)
            .ToImmutableList();

    private static ImmutableList<EquivalentPolicy> ParseEquivalentPolicies(
        XElement? equivalentPoliciesElement,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        if (equivalentPoliciesElement is null)
        {
            return ImmutableList<EquivalentPolicy>.Empty;
        }

        var builder = ImmutableList.CreateBuilder<EquivalentPolicy>();

        foreach (var element in FindChildren(equivalentPoliciesElement, "equivalentPolicy"))
        {
            var policyOid = EmptyToNull(element.Attribute("id")?.Value);
            if (policyOid is null)
            {
                errors.Add(new SpifParseError("equivalentPolicy missing required 'id' attribute", GetLineNumber(element)));
                continue;
            }

            var name = EmptyToNull(element.Attribute("name")?.Value) ?? policyOid;
            if (string.Equals(name, policyOid, StringComparison.Ordinal))
            {
                warnings.Add(new SpifParseWarning(
                    $"equivalentPolicy '{policyOid}' is missing a display name; using the OID as the name",
                    GetLineNumber(element)));
            }

            builder.Add(new EquivalentPolicy
            {
                Name = name,
                PolicyOid = policyOid,
                DocRefUri = EmptyToNull(element.Attribute("docRefURI")?.Value)
            });
        }

        return builder.ToImmutable();
    }

    private static PrivacyMarks? ParsePrivacyMarks(XElement? privacyMarksElement, List<SpifParseWarning> warnings)
    {
        if (privacyMarksElement is null)
        {
            return null;
        }

        var marks = ImmutableList.CreateBuilder<PrivacyMark>();
        foreach (var element in FindChildren(privacyMarksElement, "privacyMark"))
        {
            marks.Add(new PrivacyMark
            {
                Name = EmptyToNull(element.Attribute("name")?.Value) ?? string.Empty,
                Obsolete = ParseBool(element.Attribute("obsolete")?.Value),
                MarkingData = ParseMarkingDataList(element, warnings, [])
            });
        }

        var maxSelection = ParseNullableInt(privacyMarksElement.Attribute("maxSelection")?.Value);
        var minSelection = ParseNullableInt(privacyMarksElement.Attribute("minSelection")?.Value);

        if (maxSelection is null && ParseBool(privacyMarksElement.Attribute("singleSelection")?.Value))
        {
            maxSelection = 1;
        }

        return new PrivacyMarks
        {
            MaxSelection = maxSelection,
            MinSelection = minSelection,
            Marks = marks.ToImmutable()
        };
    }

    private static ImmutableList<MarkingData> ParseMarkingDataList(
        XElement parent,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        var builder = ImmutableList.CreateBuilder<MarkingData>();

        foreach (var element in FindChildren(parent, "markingData"))
        {
            var phrase = EmptyToNull(element.Attribute("phrase")?.Value);
            if (phrase is null)
            {
                errors.Add(new SpifParseError("markingData missing required 'phrase' attribute", GetLineNumber(element)));
                continue;
            }

            var codes = FindChildren(element, "code")
                .Select(code => code.Value.Trim())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToImmutableList();

            if (codes.Count == 0)
            {
                warnings.Add(new SpifParseWarning("markingData has no code entries", GetLineNumber(element)));
            }

            builder.Add(new MarkingData
            {
                Phrase = phrase,
                ShortPhrase = EmptyToNull(element.Attribute("shortPhrase")?.Value),
                SimplePhrase = EmptyToNull(element.Attribute("simplePhrase")?.Value),
                InputPhrase = EmptyToNull(element.Attribute("inputPhrase")?.Value),
                Language = EmptyToNull(element.Attribute(XNamespace.Xml + "lang")?.Value),
                Codes = codes
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<MarkingQualifier> ParseMarkingQualifiers(
        XElement parent,
        List<SpifParseWarning> warnings)
    {
        var builder = ImmutableList.CreateBuilder<MarkingQualifier>();

        foreach (var element in FindChildren(parent, "markingQualifier"))
        {
            var markingCode = EmptyToNull(element.Attribute("markingCode")?.Value);
            if (markingCode is null)
            {
                warnings.Add(new SpifParseWarning("markingQualifier missing markingCode and was skipped", GetLineNumber(element)));
                continue;
            }

            var qualifiers = ImmutableList.CreateBuilder<QualifierEntry>();
            foreach (var qualifierElement in FindChildren(element, "qualifier"))
            {
                var text = EmptyToNull(qualifierElement.Attribute("markingQualifier")?.Value);
                if (text is null)
                {
                    warnings.Add(new SpifParseWarning("qualifier missing markingQualifier text and was skipped", GetLineNumber(qualifierElement)));
                    continue;
                }

                qualifiers.Add(new QualifierEntry
                {
                    Text = text,
                    Code = ParseQualifierCode(qualifierElement.Attribute("qualifierCode")?.Value, qualifierElement, warnings)
                });
            }

            builder.Add(new MarkingQualifier
            {
                MarkingCode = markingCode,
                Qualifiers = qualifiers.ToImmutable()
            });
        }

        return builder.ToImmutable();
    }

    private static void ValidateSemantics(
        Spif spif,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        if (string.IsNullOrWhiteSpace(spif.PolicyId.Oid))
        {
            errors.Add(new SpifParseError("securityPolicyId must contain a policy OID"));
        }
        else if (!IsValidOid(spif.PolicyId.Oid))
        {
            errors.Add(new SpifParseError($"securityPolicyId '{spif.PolicyId.Oid}' is not a valid OID"));
        }

        ValidateOptionalOid(spif.PrivilegeId, "privilegeId", errors);
        ValidateOptionalOid(spif.RbacId, "rbacId", errors);

        if (spif.Classifications.Count == 0)
        {
            errors.Add(new SpifParseError("SPIF must define at least one securityClassification"));
        }

        if (spif.PrivacyMarks is not null)
        {
            if (spif.PrivacyMarks.MinSelection is not null && spif.PrivacyMarks.MinSelection < 0)
            {
                errors.Add(new SpifParseError("privacyMarks minSelection cannot be negative"));
            }

            if (spif.PrivacyMarks.MaxSelection is not null && spif.PrivacyMarks.MaxSelection < 0)
            {
                errors.Add(new SpifParseError("privacyMarks maxSelection cannot be negative"));
            }

            if (spif.PrivacyMarks.MinSelection is not null &&
                spif.PrivacyMarks.MaxSelection is not null &&
                spif.PrivacyMarks.MinSelection > spif.PrivacyMarks.MaxSelection)
            {
                errors.Add(new SpifParseError("privacyMarks minSelection cannot be greater than maxSelection"));
            }
        }

        var classificationLacvs = new Dictionary<LacvValue, string>();
        var classificationHierarchies = new Dictionary<int, string>();
        var classificationNames = new HashSet<string>(NameComparer);
        foreach (var classification in spif.Classifications)
        {
            if (!classificationNames.Add(classification.Name))
            {
                errors.Add(new SpifParseError($"Duplicate classification name '{classification.Name}'"));
            }

            if (!classificationLacvs.TryAdd(classification.Lacv, classification.Name))
            {
                errors.Add(new SpifParseError(
                    $"Duplicate classification lacv '{classification.Lacv}' for '{classification.Name}' and '{classificationLacvs[classification.Lacv]}'"));
            }

            if (!classificationHierarchies.TryAdd(classification.Hierarchy, classification.Name))
            {
                warnings.Add(new SpifParseWarning(
                    $"Multiple classifications share hierarchy '{classification.Hierarchy}' ('{classificationHierarchies[classification.Hierarchy]}' and '{classification.Name}')"));
            }

            foreach (var requiredCategory in classification.RequiredCategories)
            {
                ValidateRequiredCategoryShape(requiredCategory, $"classification '{classification.Name}'", warnings, errors);
            }
        }

        var tagSetOids = new HashSet<string>(OidComparer);
        foreach (var tagSet in spif.CategoryTagSets)
        {
            if (!IsValidOid(tagSet.TagSetOid))
            {
                errors.Add(new SpifParseError($"securityCategoryTagSet id '{tagSet.TagSetOid}' is not a valid OID"));
            }

            if (!tagSetOids.Add(tagSet.TagSetOid))
            {
                errors.Add(new SpifParseError($"Duplicate securityCategoryTagSet id '{tagSet.TagSetOid}'"));
            }

            var tagNames = new HashSet<string>(NameComparer);
            foreach (var tag in tagSet.Tags)
            {
                if (!tagNames.Add(tag.Name))
                {
                    errors.Add(new SpifParseError($"Duplicate tag name '{tag.Name}' in tag set '{tagSet.Name}'"));
                }

                if (tag.TagType == TagType.Enumerated && tag.EnumType is null)
                {
                    errors.Add(new SpifParseError($"Enumerated tag '{tag.Name}' in tag set '{tagSet.Name}' must declare enumType"));
                }

                if (tag.TagType != TagType.Enumerated && tag.EnumType is not null)
                {
                    warnings.Add(new SpifParseWarning($"Tag '{tag.Name}' in tag set '{tagSet.Name}' declares enumType but is not enumerated"));
                }

                var categoryLacvs = new Dictionary<LacvValue, string>();
                var categoryNames = new HashSet<string>(NameComparer);
                foreach (var category in tag.Categories)
                {
                    if (!categoryNames.Add(category.Name))
                    {
                        errors.Add(new SpifParseError($"Duplicate category name '{category.Name}' in tag '{tag.Name}'"));
                    }

                    if (!categoryLacvs.TryAdd(category.Lacv, category.Name))
                    {
                        errors.Add(new SpifParseError(
                            $"Duplicate category lacv '{category.Lacv}' in tag '{tag.Name}' of tag set '{tagSet.Name}'"));
                    }

                    if (category.NotBefore is not null && category.NotAfter is not null && category.NotBefore > category.NotAfter)
                    {
                        errors.Add(new SpifParseError(
                            $"Category '{category.Name}' has notBefore later than notAfter"));
                    }

                    if (category.RequiredClass is not null && !classificationNames.Contains(category.RequiredClass))
                    {
                        warnings.Add(new SpifParseWarning(
                            $"Category '{category.Name}' references unknown requiredClass '{category.RequiredClass}'"));
                    }

                    foreach (var requiredCategory in category.RequiredCategories)
                    {
                        ValidateRequiredCategoryShape(requiredCategory, $"category '{category.Name}'", warnings, errors);
                    }

                    foreach (var equivalentCategory in category.EquivalentCategories)
                    {
                        if (!IsValidOid(equivalentCategory.TagSetId))
                        {
                            errors.Add(new SpifParseError($"Equivalent category mapping in '{category.Name}' uses invalid tagSetId '{equivalentCategory.TagSetId}'"));
                        }
                    }
                }
            }
        }

        foreach (var classification in spif.Classifications)
        {
            ValidateConstraintReferences(classification.RequiredCategories, classification.ExcludedCategories, spif, $"classification '{classification.Name}'", warnings);
        }

        foreach (var tagSet in spif.CategoryTagSets)
        {
            foreach (var tag in tagSet.Tags)
            {
                foreach (var category in tag.Categories)
                {
                    ValidateConstraintReferences(category.RequiredCategories, category.ExcludedCategories, spif, $"category '{category.Name}'", warnings);
                }
            }
        }

        var policyNames = new HashSet<string>(NameComparer) { spif.PolicyId.Name };
        var equivalentPolicyOids = new HashSet<string>(OidComparer);
        foreach (var equivalentPolicy in spif.EquivalentPolicies)
        {
            if (!IsValidOid(equivalentPolicy.PolicyOid))
            {
                errors.Add(new SpifParseError($"Equivalent policy id '{equivalentPolicy.PolicyOid}' is not a valid OID"));
            }

            if (!equivalentPolicyOids.Add(equivalentPolicy.PolicyOid))
            {
                errors.Add(new SpifParseError($"Equivalent policy id '{equivalentPolicy.PolicyOid}' is duplicated"));
            }

            if (!policyNames.Add(equivalentPolicy.Name))
            {
                warnings.Add(new SpifParseWarning($"Equivalent policy name '{equivalentPolicy.Name}' is duplicated"));
            }
        }
    }

    private static void ValidateConstraintReferences(
        ImmutableList<RequiredCategoryConstraint> requiredCategories,
        ImmutableList<ExcludedCategoryRef> excludedCategories,
        Spif spif,
        string owner,
        List<SpifParseWarning> warnings)
    {
        foreach (var required in requiredCategories)
        {
            foreach (var categoryGroup in required.CategoryGroups)
            {
                ValidateCategoryReference(spif, owner, categoryGroup.TagSetRef, categoryGroup.Lacv, warnings);
            }
        }

        foreach (var excluded in excludedCategories)
        {
            ValidateCategoryReference(spif, owner, excluded.TagSetRef, excluded.Lacv, warnings);
        }
    }

    private static void ValidateCategoryReference(
        Spif spif,
        string owner,
        string tagSetRef,
        LacvValue lacv,
        List<SpifParseWarning> warnings)
    {
        var tagSet = spif.CategoryTagSets.FirstOrDefault(set =>
            string.Equals(set.TagSetOid, tagSetRef, StringComparison.Ordinal) ||
            string.Equals(set.Name, tagSetRef, StringComparison.OrdinalIgnoreCase));

        if (tagSet is null)
        {
            warnings.Add(new SpifParseWarning($"{owner} references unknown tag set '{tagSetRef}'"));
            return;
        }

        if (!tagSet.Tags.SelectMany(tag => tag.Categories).Any(category => category.Lacv.Equals(lacv)))
        {
            warnings.Add(new SpifParseWarning($"{owner} references unknown category lacv '{lacv}' in tag set '{tagSetRef}'"));
        }
    }

    private static XElement? FindRequiredChild(XElement parent, string localName, List<SpifParseError> errors)
    {
        var child = FindChild(parent, localName);
        if (child is null)
        {
            errors.Add(new SpifParseError($"Missing {localName} element", GetLineNumber(parent)));
        }

        return child;
    }

    private static XElement? FindChild(XElement parent, string localName)
        => parent.Elements().FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));

    private static IEnumerable<XElement> FindChildren(XElement parent, string localName)
        => parent.Elements().Where(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));

    private static bool TryParseLacv(string? value, out LacvValue lacv)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            lacv = new LacvValue(parsed);
            return true;
        }

        lacv = default;
        return false;
    }

    private static EnumType? ParseEnumType(string? value, XElement element, List<SpifParseWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value, "restrictive", StringComparison.OrdinalIgnoreCase))
        {
            return EnumType.Restrictive;
        }

        if (string.Equals(value, "permissive", StringComparison.OrdinalIgnoreCase))
        {
            return EnumType.Permissive;
        }

        warnings.Add(new SpifParseWarning($"Unknown enumType '{value}', defaulting to restrictive", GetLineNumber(element)));
        return EnumType.Restrictive;
    }

    private static TagType ParseTagType(string? value, XElement element, List<SpifParseWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TagType.NotApplicable;
        }

        if (string.Equals(value, "restrictive", StringComparison.OrdinalIgnoreCase))
        {
            return TagType.Restrictive;
        }

        if (string.Equals(value, "permissive", StringComparison.OrdinalIgnoreCase))
        {
            return TagType.Permissive;
        }

        if (string.Equals(value, "enumerated", StringComparison.OrdinalIgnoreCase))
        {
            return TagType.Enumerated;
        }

        if (string.Equals(value, "tagType7", StringComparison.OrdinalIgnoreCase))
        {
            return TagType.TagType7;
        }

        if (string.Equals(value, "notApplicable", StringComparison.OrdinalIgnoreCase))
        {
            return TagType.NotApplicable;
        }

        warnings.Add(new SpifParseWarning($"Unknown tagType '{value}', defaulting to notApplicable", GetLineNumber(element)));
        return TagType.NotApplicable;
    }

    private static EquivalencyDirection ParseEquivalencyDirection(string? value, XElement element, List<SpifParseWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "both", StringComparison.OrdinalIgnoreCase))
        {
            return EquivalencyDirection.Both;
        }

        if (string.Equals(value, "encrypt", StringComparison.OrdinalIgnoreCase))
        {
            return EquivalencyDirection.Encrypt;
        }

        if (string.Equals(value, "decrypt", StringComparison.OrdinalIgnoreCase))
        {
            return EquivalencyDirection.Decrypt;
        }

        warnings.Add(new SpifParseWarning($"Unknown equivalency direction '{value}', defaulting to both", GetLineNumber(element)));
        return EquivalencyDirection.Both;
    }

    private static QualifierCode ParseQualifierCode(string? value, XElement element, List<SpifParseWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "prefix", StringComparison.OrdinalIgnoreCase))
        {
            return QualifierCode.Prefix;
        }

        if (string.Equals(value, "suffix", StringComparison.OrdinalIgnoreCase))
        {
            return QualifierCode.Suffix;
        }

        if (string.Equals(value, "separator", StringComparison.OrdinalIgnoreCase))
        {
            return QualifierCode.Separator;
        }

        if (string.Equals(value, "finalSeparator", StringComparison.OrdinalIgnoreCase))
        {
            return QualifierCode.FinalSeparator;
        }

        warnings.Add(new SpifParseWarning($"Unknown qualifierCode '{value}', defaulting to prefix", GetLineNumber(element)));
        return QualifierCode.Prefix;
    }

    private static bool ParseBool(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? ParseOptionalDateTime(
        string? value,
        XElement source,
        List<SpifParseWarning> warnings,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TryParseGenTime(value, out var genTime))
        {
            return genTime;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffset))
        {
            return dateTimeOffset;
        }

        warnings.Add(new SpifParseWarning($"Could not parse {fieldName} value '{value}'", GetLineNumber(source)));
        return null;
    }

    private static bool TryParseGenTime(string value, out DateTimeOffset result)
    {
        string[] formats =
        [
            "yyyyMMddHHmmss'Z'",
            "yyyyMMddHHmm'Z'",
            "yyyyMMddHH'Z'",
            "yyyyMMddHHmmsszzz",
            "yyyyMMddHHmmzzz",
            "yyyyMMddHHzzz",
            "yyyyMMddHHmmssK",
            "yyyyMMddHHmmK",
            "yyyyMMddHHK"
        ];

        if (DateTimeOffset.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result))
        {
            return true;
        }

        var normalized = NormalizeGenTimeOffset(value);
        if (!ReferenceEquals(normalized, value) &&
            DateTimeOffset.TryParseExact(
                normalized,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out result))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeGenTimeOffset(string value)
    {
        if (value.Length < 5)
        {
            return value;
        }

        var signIndex = Math.Max(value.LastIndexOf('+'), value.LastIndexOf('-'));
        if (signIndex <= 0)
        {
            return value;
        }

        var offsetPart = value[signIndex..];
        if (offsetPart.Length == 5 && offsetPart[3] != ':')
        {
            return value.Insert(signIndex + 3, ":");
        }

        return value;
    }

    private static bool IsSupportedRequiredOperation(string operation)
        => string.Equals(operation, "all", StringComparison.OrdinalIgnoreCase)
           || string.Equals(operation, "oneOrMore", StringComparison.OrdinalIgnoreCase)
           || string.Equals(operation, "onlyOne", StringComparison.OrdinalIgnoreCase);

    private static void ValidateRequiredCategoryShape(
        RequiredCategoryConstraint constraint,
        string owner,
        List<SpifParseWarning> warnings,
        List<SpifParseError> errors)
    {
        if (constraint.CategoryGroups.Count == 0)
        {
            errors.Add(new SpifParseError($"{owner} has a requiredCategory with no categoryGroup entries"));
        }

        if (!IsSupportedRequiredOperation(constraint.Operation))
        {
            warnings.Add(new SpifParseWarning($"{owner} uses unsupported requiredCategory operation '{constraint.Operation}'"));
        }
    }

    private static bool IsValidOid(string value)
        => OidPattern.IsMatch(value);

    private static void ValidateOptionalOid(string? value, string fieldName, List<SpifParseError> errors)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsValidOid(value))
        {
            errors.Add(new SpifParseError($"{fieldName} '{value}' is not a valid OID"));
        }
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? GetLineNumber(XObject? node)
    {
        if (node is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
        {
            return lineInfo.LineNumber;
        }

        return null;
    }
}
