namespace AbacController.Core.Constants;

/// <summary>
/// XML namespace constants for SPIF parsing and standards-conformant metadata binding.
/// </summary>
public static class SpifNamespaces
{
    /// <summary>Correct xmlspif.org namespace (v2.1 and v3.0).</summary>
    public const string Spif = "http://www.xmlspif.org/spif";

    /// <summary>Common typo found in some SPIFs (xmslpif instead of xmlspif).</summary>
    public const string SpifTypo = "http://www.xmslpif.org/spif";

    /// <summary>
    /// URN-form xmlspif namespace emitted by the Blazor SpifEditor and used by the
    /// 7 bundled seed SPIFs that originated in the Email Classification project.
    /// </summary>
    public const string SpifUrnV3 = "urn:xmlspif:spif:3.0";

    /// <summary>XML Schema Instance namespace.</summary>
    public const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>STANAG 4774 Confidentiality Label namespace.</summary>
    public const string Stanag4774 = "urn:nato:stanag:4774:confidentialitymetadatalabel:1:0";

    /// <summary>
    /// STANAG 4778 Binding Information namespace (ADatP-4778.2 Ed A V1).
    /// Used as the root namespace for all Binding Data Objects (BDOs).
    /// </summary>
    public const string Stanag4778 = "urn:nato:stanag:4778:bindinginformation:1:0";

    /// <summary>W3C XML MIME namespace; used for xmime:contentType on DataReference elements.</summary>
    public const string Xmime = "http://www.w3.org/2005/05/xmlmime";
}
