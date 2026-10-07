using Flint.Protocol;

namespace Flint.Session;

/// <summary>Playing a file on the TV: sending it, waiting for it to start, and controlling the player.</summary>
public sealed partial class CastSession
{
    /// <summary>
    /// Bytes read per <see cref="MediaDataMessage"/> chunk when pushing a file. Comfortably under
    /// <see cref="WireCodec.MaxFrameLength"/> so a chunk is never rejected as oversized, and large
    /// enough that a multi-megabyte file does not need thousands of round trips to send.
    /// </summary>
    private const int PushChunkBytes = 512 * 1024;

    /// <summary>Sends a local-media command for the receiver to fetch and play.</summary>
    public Task SendMediaAsync(MediaCommandMessage command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return SendAsync(command, cancellationToken);
    }

    /// <summary>Sends media and waits for the receiver to confirm playback or report an error.</summary>
    public async Task<PlaybackStateMessage> SendMediaAndWaitForPlaybackStartAsync(
        MediaCommandMessage command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await LoadAndWaitForStartAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a local file's bytes over this connection in order, then asks the receiver to play it.
    /// </summary>
    /// <remarks>
    /// Some receivers cannot reach the host at all: several Fire OS builds silently drop an outbound
    /// connection the receiver app itself opens to a private LAN address, even though the exact same
    /// address works perfectly for this connection (host to receiver - which is how pairing already
    /// succeeded). Pushing the bytes over the connection that is proven to work sidesteps that
    /// restriction entirely, at the cost of buffering the whole file into memory on the receiver
    /// rather than streaming it - acceptable for the files this path is meant for.
    /// <para>
    /// Cancelling stops the send at the next chunk, leaving the connection usable; the caller then
    /// tells the receiver to drop what it has with <see cref="MediaAction.Clear"/>.
    /// </para>
    /// </remarks>
    public async Task<PlaybackStateMessage> PushMediaAndWaitForPlaybackStartAsync(
        string filePath,
        string title,
        string mimeType,
        long durationMs = -1,
        long startPositionMs = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        await using (var file = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            PushChunkBytes,
            useAsync: true))
        {
            var totalBytes = file.Length;
            var sentBytes = 0L;
            var buffer = new byte[PushChunkBytes];
            int bytesRead;
            while ((bytesRead = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                sentBytes += bytesRead;
                var isFinal = sentBytes >= totalBytes;

                // Cancelled between chunks, never during one: a chunk cut off halfway would leave
                // the TV reading the next frame from the middle of it, and the person who pressed
                // Cancel would lose the connection along with the file.
                cancellationToken.ThrowIfCancellationRequested();
                await SendAsync(
                    new MediaDataMessage(BinaryData.From(buffer.AsSpan(0, bytesRead)), isFinal),
                    CancellationToken.None).ConfigureAwait(false);
                // Inside the loop a chunk has been read, so the file is not empty.
                progress?.Report((double)sentBytes / totalBytes);
            }

            if (totalBytes == 0)
            {
                await SendAsync(new MediaDataMessage(BinaryData.Empty, IsFinal: true), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return await LoadAndWaitForStartAsync(
            new MediaCommandMessage(MediaAction.Load, Url: "", title, mimeType, durationMs, startPositionMs),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tells the TV's player to play, pause, stop, seek or move through its queue.</summary>
    /// <param name="action">What to do.</param>
    /// <param name="positionMs">
    /// Where to seek to, for <see cref="TransportAction.SeekTo"/>; ignored otherwise, and sent as -1.
    /// </param>
    /// <param name="cancellationToken">Abandons the send.</param>
    /// <exception cref="ArgumentOutOfRangeException">A seek to a negative position.</exception>
    public Task SendTransportAsync(
        TransportAction action,
        long positionMs = -1,
        CancellationToken cancellationToken = default)
    {
        if (action is TransportAction.SeekTo)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(positionMs);
        }
        else
        {
            positionMs = -1;
        }

        return SendAsync(
            new ControlMessage(NextControlSequence(), new TransportControl(action, positionMs)),
            cancellationToken);
    }

    /// <summary>Sets the TV's volume, from 0 (silent) to 1 (loudest).</summary>
    /// <remarks>A level outside that range is clamped to it rather than refused.</remarks>
    /// <exception cref="ArgumentException">A level that is not a number.</exception>
    public Task SetVolumeAsync(float level, CancellationToken cancellationToken = default)
    {
        if (float.IsNaN(level))
        {
            throw new ArgumentException("A volume level must be a number.", nameof(level));
        }

        return SendAsync(
            new ControlMessage(NextControlSequence(), new VolumeControl(Math.Clamp(level, 0f, 1f))),
            cancellationToken);
    }

    /// <summary>
    /// Sends <paramref name="load"/> and waits until the TV starts, ends or refuses the item it asks for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The TV reports its player twice a second whatever it is doing, so until it takes the new item
    /// it goes on reporting the old one: a file that ended says Ended, one still playing says
    /// Playing. Taken at its word, that answered for the new file before it had even arrived; on a
    /// real Fire TV the queue's next file was "refused" because the one before it had ended.
    /// </para>
    /// <para>
    /// So it listens only from the load on, never during an upload, and Playing and Ended count only
    /// once the TV has said Buffering, which it does the moment it takes a new item, even a picture or
    /// a file too short to report Playing. A refusal counts at once: the TV refuses some loads without
    /// buffering. Listening starts before the load is sent, not after: a TV close by can answer
    /// before the send has returned, and a wait that started after would never hear it.
    /// </para>
    /// </remarks>
    private async Task<PlaybackStateMessage> LoadAndWaitForStartAsync(
        MediaCommandMessage load,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<PlaybackStateMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var buffering = false;
        void OnPlaybackState(PlaybackStateMessage message)
        {
            // Reports arrive on the receive loop, one at a time, so the flag needs no lock.
            if (message.State is PlaybackState.Buffering)
            {
                buffering = true;
            }
            else if (message.State is PlaybackState.Error
                || (buffering && message.State is PlaybackState.Playing or PlaybackState.Ended))
            {
                completion.TrySetResult(message);
            }
        }

        PlaybackStateReceived += OnPlaybackState;
        try
        {
            await SendMediaAsync(load, cancellationToken).ConfigureAwait(false);
            var closedTask = closed.Task.WaitAsync(cancellationToken);
            await Task.WhenAny(completion.Task, closedTask).ConfigureAwait(false);

            // A final playback state and EOF can arrive in the same receive-loop turn. Prefer the
            // state that the receiver deliberately sent over the transport closing immediately
            // afterward; choosing EOF here made short and empty media fail nondeterministically.
            // One look is enough: the receive loop reports a state before it marks the session
            // closed, on the same thread, so a close seen here already has any final state behind it.
            if (completion.Task.IsCompleted)
            {
                return await completion.Task.ConfigureAwait(false);
            }

            var exception = await closedTask.ConfigureAwait(false);
            throw new IOException("The receiver session ended before playback started.", exception);
        }
        finally
        {
            PlaybackStateReceived -= OnPlaybackState;
        }
    }

    /// <summary>The next control's sequence number: one higher than the last, whoever sent it.</summary>
    private long NextControlSequence() => Interlocked.Increment(ref controlSequence);
}
