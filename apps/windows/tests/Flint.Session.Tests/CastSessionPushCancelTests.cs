using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>Cancelling a file send, and closing a session whose TV has stopped reading.</summary>
public sealed class CastSessionPushCancelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CancellingASend_StopsBetweenChunks_AndLeavesTheConnectionUsable()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();
        var file = Path.Combine(Path.GetTempPath(), $"flint-cancel-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(file, new byte[4 * 1024 * 1024], Token);
        try
        {
            using var cancel = new CancellationTokenSource();
            var progress = new CancelOnFirstReport(cancel);

            await Should.ThrowAsync<OperationCanceledException>(() => session.PushMediaAndWaitForPlaybackStartAsync(
                file, "clip.mp4", "video/mp4", progress: progress, cancellationToken: cancel.Token));
            await session.SendMediaAsync(new MediaCommandMessage(MediaAction.Clear), Token);

            // Every frame the TV reads is whole: the chunks that went, then the clear.
            var chunks = 0;
            while (true)
            {
                var message = (await tv.ReadAsync()).Message;
                if (message is MediaCommandMessage clear)
                {
                    clear.Action.ShouldBe(MediaAction.Clear);
                    break;
                }

                message.ShouldBeOfType<MediaDataMessage>().IsFinal.ShouldBeFalse();
                chunks++;
            }

            chunks.ShouldBeInRange(1, 7);
            session.IsConnected.ShouldBeTrue();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Closing_BehindAWriteTheTvNeverReads_StillFinishes()
    {
        await using var tv = await FakeTv.StartAsync();
        var session = await tv.PairAsync();
        var frame = BinaryData.From(new byte[1024 * 1024]);

        // Fill the socket until a write cannot finish because the TV is not reading.
        var stuck = Task.CompletedTask;
        for (var attempt = 0; attempt < 512 && stuck.IsCompleted; attempt++)
        {
            stuck = session.SendVideoAsync(attempt, keyFrame: false, frame, Token);
            await Task.WhenAny(stuck, Task.Delay(200, Token));
        }

        stuck.IsCompleted.ShouldBeFalse("the TV stopped reading, so a write is held open");

        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15), Token);

        var failure = await Should.ThrowAsync<Exception>(() => stuck.WaitAsync(TimeSpan.FromSeconds(15), Token));
        failure.ShouldBeAssignableTo<IOException>();
        (await session.WhenClosed.WaitAsync(Token)).Reason.ShouldBe(CastSessionEnd.ClosedByThisPc);
    }

    /// <summary>Cancels the send the moment the first chunk has gone, on the sending thread.</summary>
    private sealed class CancelOnFirstReport(CancellationTokenSource cancel) : IProgress<double>
    {
        public void Report(double value) => cancel.Cancel();
    }
}
