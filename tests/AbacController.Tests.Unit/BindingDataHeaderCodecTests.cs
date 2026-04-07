using System.Text;
using AbacController.Pep;

namespace AbacController.Tests.Unit;

public sealed class BindingDataHeaderCodecTests
{
    private readonly BindingDataHeaderCodec _codec = new();

    private const string ExpectedBindingType = "urn:nato:stanag:4778:bindinginformation:1:0";

    [Fact]
    public void HeaderName_Is_Binding_Data()
    {
        Assert.Equal("Binding-Data", BindingDataHeaderCodec.HeaderName);
    }

    [Fact]
    public void Encode_Produces_Required_Binding_Type_Parameter()
    {
        var result = _codec.Encode("<mb:BindingInformation/>");

        Assert.Contains($"binding-type=\"{ExpectedBindingType}\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_Produces_Base64_Binding_Data_Object_Parameter()
    {
        var bdoXml = "<mb:BindingInformation/>";
        var result = _codec.Encode(bdoXml);

        // Extract the base64 value and verify it decodes back to the original XML
        var prefix = "binding-data-object=\"";
        var start = result.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var end = result.LastIndexOf('"');
        var base64 = result[start..end];
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

        Assert.Equal(bdoXml, decoded);
    }

    [Fact]
    public void Decode_Returns_BDO_Xml_From_Valid_Header()
    {
        var bdoXml = "<mb:BindingInformation/>";
        var headerValue = _codec.Encode(bdoXml);

        var result = _codec.Decode(headerValue);

        Assert.Equal(bdoXml, result);
    }

    [Fact]
    public void Encode_Decode_RoundTrip_Preserves_Full_BDO()
    {
        var bdoXml = """<mb:BindingInformation xmlns:mb="urn:nato:stanag:4778:bindinginformation:1:0"><mb:MetadataBindingContainer><mb:MetadataBinding xml:id="mb-001"><mb:Metadata xml:id="md-001"><label/></mb:Metadata><mb:DataReference URI="" xmime:contentType="message/http"/></mb:MetadataBinding></mb:MetadataBindingContainer></mb:BindingInformation>""";

        var result = _codec.Decode(_codec.Encode(bdoXml));

        Assert.Equal(bdoXml, result);
    }

    [Fact]
    public void Decode_Is_Case_Insensitive_For_Parameter_Names()
    {
        // RFC-style headers may use mixed case parameter names
        var bdoXml = "<mb:BindingInformation/>";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(bdoXml));
        var headerValue = $"Binding-Type=\"{ExpectedBindingType}\"; Binding-Data-Object=\"{base64}\"";

        var result = _codec.Decode(headerValue);

        Assert.Equal(bdoXml, result);
    }

    [Fact]
    public void Decode_Throws_When_Binding_Type_Is_Wrong()
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<x/>"));
        var wrong = $"binding-type=\"urn:example.com/wrong\"; binding-data-object=\"{base64}\"";

        Assert.Throws<InvalidOperationException>(() => _codec.Decode(wrong));
    }

    [Fact]
    public void Decode_Throws_When_Binding_Type_Is_Missing()
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<x/>"));
        var missing = $"binding-data-object=\"{base64}\"";

        Assert.Throws<InvalidOperationException>(() => _codec.Decode(missing));
    }

    [Fact]
    public void Decode_Throws_When_Binding_Data_Object_Is_Missing()
    {
        var missingParam = $"binding-type=\"{ExpectedBindingType}\"";

        Assert.Throws<InvalidOperationException>(() => _codec.Decode(missingParam));
    }

    [Fact]
    public void Decode_Throws_When_Binding_Data_Object_Is_Not_Valid_Base64()
    {
        var badBase64 = $"binding-type=\"{ExpectedBindingType}\"; binding-data-object=\"!!!not-base64!!!\"";

        Assert.Throws<InvalidOperationException>(() => _codec.Decode(badBase64));
    }

    [Fact]
    public void Encode_Throws_ArgumentException_For_Null_Or_Whitespace_Input()
    {
        Assert.Throws<ArgumentNullException>(() => _codec.Encode(null!));
        Assert.Throws<ArgumentException>(() => _codec.Encode("   "));
    }

    [Fact]
    public void Decode_Throws_ArgumentException_For_Null_Or_Whitespace_Input()
    {
        Assert.Throws<ArgumentNullException>(() => _codec.Decode(null!));
        Assert.Throws<ArgumentException>(() => _codec.Decode("   "));
    }
}
