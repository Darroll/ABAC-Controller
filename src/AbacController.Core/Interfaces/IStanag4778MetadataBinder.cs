using AbacController.Core.Domain.Labels;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Minimal metadata binder for wrapping and unwrapping a STANAG 4774 label
/// in a STANAG 4778-style XML envelope.
/// </summary>
public interface IStanag4778MetadataBinder
{
    /// <summary>Bind an encoded label and inline payload into a STANAG 4778 BDO.</summary>
    string Bind(MetadataBindingEnvelope envelope);

    /// <summary>
    /// Bind an encoded label to an externally-located data object (no inline payload).
    /// Produces a BDO with <c>DataReference URI="{dataUri}"</c> and no DataObject element.
    /// For the HTTP entity body binding profile, use <c>dataUri=""</c> and
    /// <c>contentType="message/http"</c> per ADatP-4778.2 Chapter 7.
    /// </summary>
    /// <param name="labelXml">STANAG 4774 label XML.</param>
    /// <param name="dataUri">URI of the data object. Empty string means the HTTP entity body.</param>
    /// <param name="contentType">MIME type of the data object.</param>
    /// <param name="bindingId">Optional caller-supplied binding identifier (valid XML NCName).</param>
    string BindDetached(string labelXml, string dataUri, string? contentType, string? bindingId = null);

    /// <summary>Extract an envelope and decode its embedded security label.</summary>
    MetadataUnbindResult Unbind(string envelopeXml);
}
