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

            // The TV reads as the bytes arrive, as a real one does. Reading only after the send was
            // cancelled deadlocked whenever a chunk did not fit in the socket's buffers: the write
            // waited for a reader that was waiting for the write.
            var received = ReadUntilClearAsync(tv);

            await Should.ThrowAsync<OperationCanceledException>(() => session.PushMediaAndWaitForPlaybackStartAsync(
                file, "clip.mp4", "video/mp4", progress: progress, cancellationToken: cancel.Token));
            await session.SendMediaAsync(new MediaCommandMessage(MediaAction.Clear), Token);

            // Every frame the TV read was whole: the one chunk that went before the cancel, then
            // the clear. A second chunk would mean the cancel was noticed late.
            (await received.WaitAsync(TimeSpan.FromSeconds(30), Token)).ShouldBe(1);
            session.IsConnected.ShouldBeTrue();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ASendWithNoProgressReporter_StillPlays()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();
        var file = Path.Combine(Path.GetTempPath(), $"flint-plain-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(file, [1, 2, 3], Token);
        try
        {
            var playing = session.PushMediaAndWaitForPlaybackStartAsync(file, "clip.mp4", "video/mp4", cancellationToken: Token);

            (await tv.ReadAsync()).Message.ShouldBeOfType<MediaDataMessage>().IsFinal.ShouldBeTrue();
            (await tv.ReadAsync()).Message.ShouldBeOfType<MediaCommandMessage>().Action.ShouldBe(MediaAction.Load);
            await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing));

            (await playing.WaitAsync(TimeSpan.FromSeconds(10), Token)).State.ShouldBe(PlaybackState.Playing);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ATvThatHangsUpBeforePlaying_EndsTheWaitWithTheReason()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        var waiting = session.WaitForPlaybackStartAsync(Token);
        tv.HangUp();

        var failure = await Should.ThrowAsync<IOException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10), Token));
        failure.Message.ShouldBe("The receiver session ended before playback started.");
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

    /// <summary>Reads file chunks until the clear arrives; how many chunks came first.</summary>
    private static async Task<int> ReadUntilClearAsync(FakeTv tv)
    {
        var chunks = 0;
        while (true)
        {
            var message = (await tv.ReadAsync()).Message;
            if (message is MediaCommandMessage clear)
            {
                clear.Action.ShouldBe(MediaAction.Clear);
                return chunks;
            }

            message.ShouldBeOfType<MediaDataMessage>().IsFinal.ShouldBeFalse();
            chunks++;
        }
    }

    /// <summary>Cancels the send the moment the first chunk has gone, on the sending thread.</summary>
    private sealed class CancelOnFirstReport(CancellationTokenSource cancel) : IProgress<double>
    {
        public void Report(double value) => cancel.Cancel();
    }
}
