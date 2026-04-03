using System.Collections.Concurrent;
using AbacController.Core.Interfaces;

namespace AbacController.Pep.Codecs;

/// <summary>
/// Registry for label codecs. Manages codec registration and lookup.
/// Populated at DI setup time; read-only thereafter.
/// </summary>
public sealed class LabelCodecRegistry : ILabelCodecRegistry
{
    private readonly ConcurrentDictionary<string, ILabelCodec> _codecs = new();
    private readonly ConcurrentDictionary<string, ILabelCodec> _codecsByContentType = new();

    /// <inheritdoc />
    public void Register(ILabelCodec codec)
    {
        _codecs[codec.CodecId] = codec;
        _codecsByContentType[codec.ContentType] = codec;
    }

    /// <inheritdoc />
    public ILabelCodec GetCodec(string codecId)
    {
        if (_codecs.TryGetValue(codecId, out var codec))
            return codec;
        throw new KeyNotFoundException($"No codec registered with ID '{codecId}'");
    }

    /// <inheritdoc />
    public ILabelCodec GetCodecByContentType(string contentType)
    {
        if (_codecsByContentType.TryGetValue(contentType, out var codec))
            return codec;
        throw new KeyNotFoundException($"No codec registered for content type '{contentType}'");
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredCodecIds()
        => _codecs.Keys.ToList().AsReadOnly();
}
