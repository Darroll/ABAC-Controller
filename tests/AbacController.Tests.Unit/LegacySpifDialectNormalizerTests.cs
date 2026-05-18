using System.IO;
using AbacController.Pap;
using Xunit;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for <see cref="LegacySpifDialectNormalizer"/> plus an end-to-end
/// parser round-trip covering the seven bundled sample SPIF files that ship
/// in <c>src/AbacController.Api/data/seed-spifs/</c>. These fixtures use the
/// legacy <c>urn:xmlspif:spif:3.0</c> dialect and must parse cleanly once
/// the dialect normalizer rewrites them to the strict schema.
/// </summary>
public sealed class LegacySpifDialectNormalizerTests
{
    private static readonly string[] BundledFixtures =
    [
        "nato.spif.xml",
        "us-dod.spif.xml",
        "cui.spif.xml",
        "aus-pspf.spif.xml",
        "test-corp.spif.xml",
        "test-gov.spif.xml",
        "rfc3114-acme.spif.xml",
    ];

    [Fact]
    public void Normalize_PassthroughNonLegacyInput_ReturnsInputUnchanged()
    {
        const string canonical = """
            <?xml version="1.0" encoding="UTF-8"?>
            <SPIF xmlns="http://www.xmlspif.org/spif" schemaVersion="3.0">
              <securityPolicyId name="TEST" id="1.2.3.4"/>
              <securityClassifications>
                <securityClassification name="U" lacv="1" hierarchy="1">
                  <markingData phrase="UNCLASSIFIED"/>
                </securityClassification>
              </securityClassifications>
            </SPIF>
            """;

        var result = LegacySpifDialectNormalizer.Normalize(canonical);
        Assert.Equal(canonical, result);
    }

    [Fact]
    public void Normalize_EmptyInput_ReturnsInputUnchanged()
    {
        Assert.Equal(string.Empty, LegacySpifDialectNormalizer.Normalize(string.Empty));
    }

    [Fact]
    public void Normalize_SmallLegacyDocument_ProducesStrictSchemaShape()
    {
        const string legacy = """
            <?xml version="1.0" encoding="UTF-8"?>
            <SPIF xmlns="urn:xmlspif:spif:3.0" version="3">
              <securityPolicyId oid="1.2.3.4.5"/>
              <spifVersionNumber value="1"/>
              <creationDate value="2026-01-01T00:00:00Z"/>
              <originator>Acme Corp</originator>
              <securityClassifications>
                <securityClassification name="INTERNAL" lacv="1" hierarchy="1">
                  <markingData>
                    <markingPhrase phraseText="INTERNAL" portionAbbreviation="INT"/>
                    <markingCode codeValue="INT"/>
                    <foregroundColor value="#000000"/>
                    <backgroundColor value="#cccccc"/>
                  </markingData>
                </securityClassification>
              </securityClassifications>
              <securityCategoryTagSets>
                <securityCategoryTagSet tagSetName="Projects" tagSetId="1.2.3.4.5.1" displayOrder="1">
                  <securityCategoryTag tagName="PROJECT-A" lacv="1" tagType="restrictive">
                    <markingData>
                      <markingPhrase phraseText="PROJECT A" portionAbbreviation="PA"/>
                      <markingCode codeValue="PA"/>
                    </markingData>
                  </securityCategoryTag>
                </securityCategoryTagSet>
              </securityCategoryTagSets>
            </SPIF>
            """;

        var normalized = LegacySpifDialectNormalizer.Normalize(legacy);

        Assert.Contains("http://www.xmlspif.org/spif", normalized);
        Assert.DoesNotContain("urn:xmlspif:spif:3.0", normalized);
        Assert.Contains("schemaVersion=\"3.0\"", normalized);
        Assert.Contains("name=\"Acme Corp\"", normalized);
        Assert.Contains("id=\"1.2.3.4.5\"", normalized);
        Assert.Contains("<tagCategory", normalized);
        Assert.Contains("phrase=\"INTERNAL\"", normalized);

        // Feed the normalized output back through the full parser and
        // assert it round-trips cleanly.
        var parser = new SpifParser();
        var result = parser.Parse(normalized);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(result.Spif);
        Assert.Equal("1.2.3.4.5", result.Spif!.PolicyId.Oid);
        Assert.Equal("Acme Corp", result.Spif.PolicyId.Name);
    }

    [Theory]
    [MemberData(nameof(BundledFixtureNames))]
    public void Parse_BundledFixture_SucceedsAfterNormalization(string fileName)
    {
        var path = FindSeedFile(fileName);
        var xml = File.ReadAllText(path);
        var parser = new SpifParser();

        var result = parser.Parse(xml);

        Assert.True(result.Success, $"{fileName}: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Spif);
        Assert.False(string.IsNullOrWhiteSpace(result.Spif!.PolicyId.Oid));
        Assert.False(string.IsNullOrWhiteSpace(result.Spif.PolicyId.Name));
        Assert.NotEmpty(result.Spif.Classifications);
    }

    /// <summary>xUnit MemberData supplier for every bundled SPIF fixture.</summary>
    public static IEnumerable<object[]> BundledFixtureNames =>
        BundledFixtures.Select(f => new object[] { f });

    private static string FindSeedFile(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "AbacController.Api",
                "data",
                "seed-spifs",
                fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate seed SPIF '{fileName}' starting from '{AppContext.BaseDirectory}'.");
    }
}
