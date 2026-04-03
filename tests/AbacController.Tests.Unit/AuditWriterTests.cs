using AbacController.Audit;
using AbacController.Core.Domain.Audit;

namespace AbacController.Tests.Unit;

public sealed class AuditWriterTests
{
    [Fact]
    public void Write_WhenBufferIsFull_ThrowsInsteadOfDroppingOlderEvents()
    {
        var writer = new AuditWriter(capacity: 1, enqueueTimeout: TimeSpan.FromMilliseconds(50));

        writer.Write(CreateAuditEvent("first"));

        var ex = Assert.Throws<TimeoutException>(() => writer.Write(CreateAuditEvent("second")));
        Assert.Contains("Timed out", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.True(writer.Reader.TryRead(out var retained));
        Assert.Equal("first", retained!.RequestId);
        Assert.False(writer.Reader.TryRead(out _));
    }

    private static AuditEvent CreateAuditEvent(string requestId) => new()
    {
        RequestId = requestId,
        EventType = "evaluation",
        Timestamp = DateTimeOffset.UtcNow
    };
}
