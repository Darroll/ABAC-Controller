using System.Text;

namespace AbacController.Pep;

/// <summary>
/// Encodes and decodes the STANAG 4778 <c>Binding-Data</c> HTTP header field
/// per ADatP-4778.2 Chapter 7 (REST Binding Profile, §7.5).
///
/// Header format:
///   Binding-Data: binding-type="urn:nato:stanag:4778:bindinginformation:1:0";
///                 binding-data-object="{base64-encoded BDO XML}"
///
/// Note: unlike the SMTP profile, the HTTP profile MUST NOT use RFC 2231 continuation
/// for the binding-data-object parameter — the base64 value is a single unbroken string.
/// </summary>
public sealed class BindingDataHeaderCodec
{
    /// <summary>The HTTP header field name.</summary>
    public const string HeaderName = "Binding-Data";

    /// <summary>The binding-type parameter value for STANAG 4778.</summary>
    public const string BindingType = "urn:nato:stanag:4778:bindinginformation:1:0";

    /// <summary>
    /// Encodes a BDO XML string into a <c>Binding-Data</c> header value.
    /// </summary>
    /// <param name="bdoXml">The STANAG 4778 BDO XML document to encode.</param>
    /// <returns>
    /// A complete header value suitable for use as the <c>Binding-Data</c> header field value,
    /// e.g. <c>binding-type="..."; binding-data-object="..."</c>.
    /// </returns>
    public string Encode(string bdoXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bdoXml);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(bdoXml));
        return $"binding-type=\"{BindingType}\"; binding-data-object=\"{base64}\"";
    }

    /// <summary>
    /// Decodes a <c>Binding-Data</c> header value to a BDO XML string.
    /// </summary>
    /// <param name="headerValue">The header field value (everything after the colon).</param>
    /// <returns>The decoded BDO XML string.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if binding-type is missing or incorrect, binding-data-object is missing,
    /// or binding-data-object is not valid base64.
    /// </exception>
    public string Decode(string headerValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerValue);
        var parameters = ParseParameters(headerValue);

        if (!parameters.TryGetValue("binding-type", out var bindingType) ||
            !string.Equals(bindingType, BindingType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Binding-Data header has unsupported or missing binding-type '{bindingType ?? "(missing)"}'. " +
                $"Expected '{BindingType}'.");
        }

        if (!parameters.TryGetValue("binding-data-object", out var base64))
        {
            throw new InvalidOperationException(
                "Binding-Data header is missing the binding-data-object parameter.");
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Binding-Data binding-data-object parameter is not valid base64.", ex);
        }
    }

    // Parses semicolon-delimited key="value" parameter pairs.
    // Parameter names are treated as case-insensitive (RFC 7230 §3.2).
    // The value of the first '=' in each segment is the name/value separator;
    // this handles base64 padding ('=') correctly since base64 cannot contain ';'.
    private static Dictionary<string, string> ParseParameters(string headerValue)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in headerValue.Split(';'))
        {
            var eq = segment.IndexOf('=');
            if (eq < 0) continue;
            var name = segment[..eq].Trim();
            var value = segment[(eq + 1)..].Trim().Trim('"').Trim();
            if (!string.IsNullOrEmpty(name))
                result[name] = value;
        }
        return result;
    }
}
