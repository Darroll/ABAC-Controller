using AbacController.Core.Domain.Labels;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Minimal metadata binder for wrapping and unwrapping a STANAG 4774 label
/// in a STANAG 4778-style XML envelope.
/// </summary>
public interface IStanag4778MetadataBinder
{
    /// <summary>Bind an encoded label and payload into an XML envelope.</summary>
    string Bind(MetadataBindingEnvelope envelope);

    /// <summary>Extract an envelope and decode its embedded security label.</summary>
    MetadataUnbindResult Unbind(string envelopeXml);
}
