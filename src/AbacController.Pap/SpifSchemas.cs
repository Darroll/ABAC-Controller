using System.Xml;
using System.Xml.Schema;
using AbacController.Core.Constants;

namespace AbacController.Pap;

/// <summary>
/// Provides compiled XML schema sets for SPIF validation.
/// </summary>
internal static class SpifSchemas
{
    private static readonly Lazy<XmlSchemaSet> Schemas = new(CreateSchemaSet);

    public static XmlSchemaSet CreateValidationSet() => Schemas.Value;

    private static XmlSchemaSet CreateSchemaSet()
    {
        var schemaSet = new XmlSchemaSet();
        schemaSet.Add(SpifNamespaces.Spif, XmlReader.Create(new StringReader(Schema)));
        schemaSet.Compile();
        return schemaSet;
    }

    private const string Schema = """
<?xml version="1.0" encoding="utf-8"?>
<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
           targetNamespace="http://www.xmlspif.org/spif"
           xmlns:spif="http://www.xmlspif.org/spif"
           elementFormDefault="qualified"
           attributeFormDefault="unqualified">

  <xs:simpleType name="schemaVersionType">
    <xs:restriction base="xs:string">
      <xs:enumeration value="2.1"/>
      <xs:enumeration value="3.0"/>
    </xs:restriction>
  </xs:simpleType>

  <xs:simpleType name="oidType">
    <xs:restriction base="xs:string">
      <xs:pattern value="[0-2](\.[0-9]+)+"/>
    </xs:restriction>
  </xs:simpleType>

  <xs:element name="SPIF">
    <xs:complexType>
      <xs:sequence>
        <xs:element name="securityPolicyId" type="spif:policyInfoType"/>
        <xs:element name="securityClassifications" type="spif:classificationsType"/>
        <xs:element name="securityCategoryTagSets" type="spif:tagSetsType" minOccurs="0"/>
        <xs:element name="equivalentPolicies" type="spif:equivalentPoliciesType" minOccurs="0"/>
        <xs:element name="privacyMarks" type="spif:privacyMarksType" minOccurs="0"/>
        <xs:element name="markingData" type="spif:markingDataType" minOccurs="0" maxOccurs="unbounded"/>
        <xs:element name="markingQualifier" type="spif:markingQualifierType" minOccurs="0" maxOccurs="unbounded"/>
      </xs:sequence>
      <xs:attribute name="schemaVersion" type="spif:schemaVersionType" use="required"/>
      <xs:attribute name="version" type="xs:string"/>
      <xs:attribute name="creationDate" type="xs:string"/>
      <xs:attribute name="originatorDN" type="xs:string"/>
      <xs:attribute name="keyIdentifier" type="xs:string"/>
      <xs:attribute name="privilegeId" type="spif:oidType"/>
      <xs:attribute name="rbacId" type="spif:oidType"/>
      <xs:anyAttribute processContents="lax"/>
    </xs:complexType>
  </xs:element>

  <xs:complexType name="policyInfoType">
    <xs:sequence>
      <xs:element name="markingData" type="spif:markingDataType" minOccurs="0" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="name" type="xs:string" use="required"/>
    <xs:attribute name="id" type="spif:oidType" use="required"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="classificationsType">
    <xs:sequence>
      <xs:element name="securityClassification" type="spif:classificationType" maxOccurs="unbounded"/>
    </xs:sequence>
  </xs:complexType>

  <xs:complexType name="classificationType">
    <xs:sequence>
      <xs:element name="markingData" type="spif:markingDataType" minOccurs="0" maxOccurs="unbounded"/>
      <xs:element name="equivalentClassification" type="spif:equivalentClassificationType" minOccurs="0" maxOccurs="unbounded"/>
      <xs:element name="requiredCategory" type="spif:requiredCategoryType" minOccurs="0" maxOccurs="unbounded"/>
      <xs:element name="excludedCategory" type="spif:excludedCategoryType" minOccurs="0" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="name" type="xs:string" use="required"/>
    <xs:attribute name="lacv" type="xs:int" use="required"/>
    <xs:attribute name="hierarchy" type="xs:int" use="required"/>
    <xs:attribute name="obsolete" type="xs:boolean"/>
    <xs:attribute name="color" type="xs:string"/>
    <xs:attribute name="fgcolor" type="xs:string"/>
    <xs:attribute name="bgcolor" type="xs:string"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="tagSetsType">
    <xs:sequence>
      <xs:element name="securityCategoryTagSet" type="spif:tagSetType" maxOccurs="unbounded"/>
    </xs:sequence>
  </xs:complexType>

  <xs:complexType name="tagSetType">
    <xs:sequence>
      <xs:element name="securityCategoryTag" type="spif:tagTypeDef" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="name" type="xs:string" use="required"/>
    <xs:attribute name="id" type="spif:oidType" use="required"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="tagTypeDef">
    <xs:sequence>
      <xs:element name="tagCategory" type="spif:tagCategoryType" maxOccurs="unbounded"/>
      <xs:element name="markingData" type="spif:markingDataType" minOccurs="0" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="name" type="xs:string" use="required"/>
    <xs:attribute name="tagType" type="xs:string" use="required"/>
    <xs:attribute name="enumType" type="xs:string"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="tagCategoryType">
    <xs:sequence>
      <xs:element name="markingData" type="spif:markingDataType" minOccurs="0" maxOccurs="unbounded"/>
      <xs:element name="equivalentSecCategoryTag" type="spif:equivalentSecCategoryTagType" minOccurs="0" maxOccurs="unbounded"/>
      <xs:element name="excludedClass" type="xs:string" minOccurs="0" maxOccurs="unbounded"/>
      <xs:element name="requiredCategory" type="spif:requiredCategoryType" minOccurs="0" maxOccurs="unbounded"/>
      <xs:element name="excludedCategory" type="spif:excludedCategoryType" minOccurs="0" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="name" type="xs:string" use="required"/>
    <xs:attribute name="lacv" type="xs:int" use="required"/>
    <xs:attribute name="requiredClass" type="xs:string"/>
    <xs:attribute name="obsolete" type="xs:boolean"/>
    <xs:attribute name="userInput" type="xs:string"/>
    <xs:attribute name="dateFormat" type="xs:string"/>
    <xs:attribute name="notBefore" type="xs:dateTime"/>
    <xs:attribute name="notAfter" type="xs:dateTime"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="equivalentPoliciesType">
    <xs:sequence>
      <xs:element name="equivalentPolicy" type="spif:equivalentPolicyType" maxOccurs="unbounded"/>
    </xs:sequence>
  </xs:complexType>

  <xs:complexType name="equivalentPolicyType">
    <xs:attribute name="name" type="xs:string" use="required"/>
    <xs:attribute name="id" type="spif:oidType" use="required"/>
    <xs:attribute name="docRefURI" type="xs:string"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="privacyMarksType">
    <xs:sequence>
      <xs:element name="privacyMark" type="spif:privacyMarkType" minOccurs="0" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="maxSelection" type="xs:int"/>
    <xs:attribute name="minSelection" type="xs:int"/>
    <xs:attribute name="singleSelection" type="xs:boolean"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="privacyMarkType">
    <xs:sequence>
      <xs:element name="markingData" type="spif:markingDataType" minOccurs="0" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="name" type="xs:string" use="required"/>
    <xs:attribute name="obsolete" type="xs:boolean"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="markingDataType">
    <xs:sequence>
      <xs:element name="code" type="xs:string" minOccurs="0" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="phrase" type="xs:string" use="required"/>
    <xs:attribute name="shortPhrase" type="xs:string"/>
    <xs:attribute name="simplePhrase" type="xs:string"/>
    <xs:attribute name="inputPhrase" type="xs:string"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="markingQualifierType">
    <xs:sequence>
      <xs:element name="qualifier" type="spif:qualifierType" minOccurs="0" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="markingCode" type="xs:string" use="required"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="qualifierType">
    <xs:attribute name="markingQualifier" type="xs:string" use="required"/>
    <xs:attribute name="qualifierCode" type="xs:string"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="equivalentClassificationType">
    <xs:sequence>
      <xs:element name="requiredCategory" type="spif:requiredCategoryType" minOccurs="0" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="policyRef" type="xs:string" use="required"/>
    <xs:attribute name="lacv" type="xs:int" use="required"/>
    <xs:attribute name="applied" type="xs:string"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="equivalentSecCategoryTagType">
    <xs:attribute name="policyRef" type="xs:string" use="required"/>
    <xs:attribute name="tagSetId" type="spif:oidType" use="required"/>
    <xs:attribute name="tagType" type="xs:string"/>
    <xs:attribute name="lacv" type="xs:int" use="required"/>
    <xs:attribute name="applied" type="xs:string"/>
    <xs:attribute name="action" type="xs:string"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="requiredCategoryType">
    <xs:sequence>
      <xs:element name="categoryGroup" type="spif:categoryGroupType" minOccurs="1" maxOccurs="unbounded"/>
    </xs:sequence>
    <xs:attribute name="operation" type="xs:string"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="categoryGroupType">
    <xs:attribute name="tagSetRef" type="xs:string"/>
    <xs:attribute name="tagSetId" type="spif:oidType"/>
    <xs:attribute name="tagType" type="xs:string"/>
    <xs:attribute name="enumType" type="xs:string"/>
    <xs:attribute name="lacv" type="xs:int" use="required"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

  <xs:complexType name="excludedCategoryType">
    <xs:attribute name="tagSetRef" type="xs:string"/>
    <xs:attribute name="tagSetId" type="spif:oidType"/>
    <xs:attribute name="tagType" type="xs:string"/>
    <xs:attribute name="lacv" type="xs:int" use="required"/>
    <xs:anyAttribute processContents="lax"/>
  </xs:complexType>

</xs:schema>
""";
}
