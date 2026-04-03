using AbacController.Pap;

namespace AbacController.Tests.Unit;

public sealed class SpifParserTests
{
    private readonly SpifParser _parser = new();

    [Fact]
    public void ValidateSchema_Fails_When_RequiredClassificationAttributeMissing()
    {
        const string xml = """
<spif:SPIF xmlns:spif="http://www.xmlspif.org/spif" schemaVersion="2.1">
  <spif:securityPolicyId name="TEST" id="1.2.3.4" />
  <spif:securityClassifications>
    <spif:securityClassification name="SECRET" lacv="3" />
  </spif:securityClassifications>
</spif:SPIF>
""";

        var result = _parser.ValidateSchema(xml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("hierarchy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_Fails_When_EnumeratedTagMissingEnumType()
    {
        const string xml = """
<spif:SPIF xmlns:spif="http://www.xmlspif.org/spif" schemaVersion="2.1">
  <spif:securityPolicyId name="TEST" id="1.2.3.4" />
  <spif:securityClassifications>
    <spif:securityClassification name="SECRET" lacv="3" hierarchy="3" />
  </spif:securityClassifications>
  <spif:securityCategoryTagSets>
    <spif:securityCategoryTagSet name="Compartments" id="1.2.3.4.1">
      <spif:securityCategoryTag name="REL TO" tagType="enumerated">
        <spif:tagCategory name="USA" lacv="20" />
      </spif:securityCategoryTag>
    </spif:securityCategoryTagSet>
  </spif:securityCategoryTagSets>
</spif:SPIF>
""";

        var result = _parser.Parse(xml);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("must declare enumType", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_Succeeds_For_BasicPolicy_And_NormalizesSemanticWarnings()
    {
        var result = _parser.Parse(TestSpifSamples.BasicPolicy);

        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.NotNull(result.Spif);
        Assert.Equal("1.2.3.4", result.Spif!.PolicyId.Oid);
        Assert.Equal(2, result.Spif.Classifications.Count);
        Assert.Single(result.Spif.CategoryTagSets);
    }
}
