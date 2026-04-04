using AbacController.Core.Domain.Labels;
using AbacController.Core.Interfaces;
using AbacController.Pep.Codecs;

namespace AbacController.Tests.Unit;

public sealed class LabelCodecRegistryTests
{
    [Fact]
    public void Register_ThenGetCodec_ReturnsRegisteredCodec()
    {
        var registry = new LabelCodecRegistry();
        var codec = new TestCodec("codec-1", "application/test");

        registry.Register(codec);

        Assert.Same(codec, registry.GetCodec("codec-1"));
    }

    [Fact]
    public void Register_ThenGetCodecByContentType_ReturnsRegisteredCodec()
    {
        var registry = new LabelCodecRegistry();
        var codec = new TestCodec("codec-1", "application/test");

        registry.Register(codec);

        Assert.Same(codec, registry.GetCodecByContentType("application/test"));
    }

    [Fact]
    public void Register_SameId_OverwritesPriorCodec()
    {
        var registry = new LabelCodecRegistry();
        var first = new TestCodec("codec-1", "application/first");
        var second = new TestCodec("codec-1", "application/second");

        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.GetCodec("codec-1"));
    }

    [Fact]
    public void GetCodec_Missing_ThrowsKeyNotFoundException()
    {
        var registry = new LabelCodecRegistry();
        Assert.Throws<KeyNotFoundException>(() => registry.GetCodec("missing"));
    }

    [Fact]
    public void GetCodecByContentType_Missing_ThrowsKeyNotFoundException()
    {
        var registry = new LabelCodecRegistry();
        Assert.Throws<KeyNotFoundException>(() => registry.GetCodecByContentType("missing/type"));
    }

    [Fact]
    public void GetRegisteredCodecIds_ReturnsRegisteredIds()
    {
        var registry = new LabelCodecRegistry();
        registry.Register(new TestCodec("codec-1", "application/one"));
        registry.Register(new TestCodec("codec-2", "application/two"));

        var ids = registry.GetRegisteredCodecIds();
        Assert.Contains("codec-1", ids);
        Assert.Contains("codec-2", ids);
    }

    private sealed class TestCodec(string codecId, string contentType) : ILabelCodec
    {
        public string CodecId { get; } = codecId;
        public string ContentType { get; } = contentType;

        public EncodeResult Encode(SecurityLabel label, ISpifIndex spifIndex) => EncodeResult.Success("x");
        public DecodeResult Decode(ReadOnlySpan<byte> encodedLabel) => DecodeResult.Success(new SecurityLabel { ClassificationLacv = 0 });
        public DecodeResult Decode(string encodedLabel) => DecodeResult.Success(new SecurityLabel { ClassificationLacv = 0 });
    }
}
