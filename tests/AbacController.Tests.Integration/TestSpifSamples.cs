namespace AbacController.Tests.Integration;

internal static class TestSpifSamples
{
    public const string BasicPolicy = """
<spif:SPIF xmlns:spif="http://www.xmlspif.org/spif" schemaVersion="2.1" creationDate="20260101000000Z">
  <spif:securityPolicyId name="TEST" id="1.2.3.4" />
  <spif:securityClassifications>
    <spif:securityClassification name="CONFIDENTIAL" lacv="2" hierarchy="2" />
    <spif:securityClassification name="SECRET" lacv="3" hierarchy="3">
      <spif:requiredCategory operation="onlyOne">
        <spif:categoryGroup tagSetRef="1.2.3.4.1" tagType="restrictive" lacv="10" />
        <spif:categoryGroup tagSetRef="1.2.3.4.1" tagType="restrictive" lacv="11" />
      </spif:requiredCategory>
    </spif:securityClassification>
  </spif:securityClassifications>
  <spif:securityCategoryTagSets>
    <spif:securityCategoryTagSet name="Compartments" id="1.2.3.4.1">
      <spif:securityCategoryTag name="SCI" tagType="restrictive">
        <spif:tagCategory name="ALPHA" lacv="10" />
        <spif:tagCategory name="BRAVO" lacv="11" />
      </spif:securityCategoryTag>
      <spif:securityCategoryTag name="REL TO" tagType="enumerated" enumType="permissive">
        <spif:tagCategory name="USA" lacv="20" />
        <spif:tagCategory name="GBR" lacv="21" />
      </spif:securityCategoryTag>
    </spif:securityCategoryTagSet>
  </spif:securityCategoryTagSets>
</spif:SPIF>
""";
}
