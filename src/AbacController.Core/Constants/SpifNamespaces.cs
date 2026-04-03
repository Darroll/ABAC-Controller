namespace AbacController.Core.Constants;

/// <summary>
/// XML namespace constants for SPIF parsing.
/// </summary>
public static class SpifNamespaces
{
    /// <summary>Correct xmlspif.org namespace (v2.1 and v3.0).</summary>
    public const string Spif = "http://www.xmlspif.org/spif";

    /// <summary>Common typo found in some SPIFs (xmslpif instead of xmlspif).</summary>
    public const string SpifTypo = "http://www.xmslpif.org/spif";

    /// <summary>XML Schema Instance namespace.</summary>
    public const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>STANAG 4774 Confidentiality Label namespace.</summary>
    public const string Stanag4774 = "urn:nato:stanag:4774:confidentialitymetadatalabel:1:0";

    /// <summary>STANAG 4778 Metadata Binding namespace.</summary>
    public const string Stanag4778 = "urn:nato:stanag:4778:metadatabindingmechanism:1:0";
}
