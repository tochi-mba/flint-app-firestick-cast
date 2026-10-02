using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flint.Core.Settings;

namespace Flint.Core.Media;

/// <summary>Where a file was stopped, kept so it can be carried on from there.</summary>
/// <param name="Key">The file, as <see cref="ResumePolicy.KeyFor"/> names it.</param>
/// <param name="PositionMs">Where it stopped, in milliseconds.</param>
/// <param name="DurationMs">How long it is, in milliseconds; zero or less when unknown.</param>
/// <param name="LastPlayed">When it was last played.</param>
public sealed record MediaHistoryEntry(string Key, long PositionMs, long DurationMs, DateTimeOffset LastPlayed);

/// <summary>Where played files' positions are kept between runs.</summary>
public interface IMediaHistoryStore
{
    /// <summary>The position kept for <paramref name="key"/>, if any.</summary>
    MediaHistoryEntry? Find(string key);

    /// <summary>Keeps <paramref name="entry"/>, replacing any earlier one for the same file.</summary>
    void Save(MediaHistoryEntry entry);

    /// <summary>Forgets the position kept for <paramref name="key"/>.</summary>
    void Forget(string key);

    /// <summary>Forgets every position.</summary>
    void Clear();
}

/// <summary>What to do with a file that has been played before.</summary>
public enum ResumeDecision
{
    /// <summary>Play it from the start.</summary>
    FromStart = 0,

    /// <summary>Carry on from where it stopped.</summary>
    Resume = 1,

    /// <summary>Ask the person which.</summary>
    Ask = 2,
}

/// <summary>When a position is worth keeping, and what to do with one that was kept.</summary>
/// <remarks>
/// A position in the first half minute is not worth offering back, and neither is one in the last
/// twentieth of a file: that is the credits, and the person has as good as finished it.
/// </remarks>
public static class ResumePolicy
{
    /// <summary>The earliest position worth keeping, in milliseconds.</summary>
    public const long MinimumPositionMs = 30_000;

    /// <summary>How far through a file a position stops being worth keeping.</summary>
    public const double FinishedFraction = 0.95;

    /// <summary>Whether stopping at <paramref name="positionMs"/> is worth offering back later.</summary>
    /// <param name="positionMs">Where the file stopped.</param>
    /// <param name="durationMs">How long it is; zero or less when the TV did not say.</param>
    public static bool IsWorthKeeping(long positionMs, long durationMs) =>
        positionMs >= MinimumPositionMs && (durationMs <= 0 || positionMs < durationMs * FinishedFraction);

    /// <summary>What to do with a file, given what was kept for it and the person's setting.</summary>
    public static ResumeDecision Decide(ResumeMode mode, MediaHistoryEntry? kept) =>
        kept is null || !IsWorthKeeping(kept.PositionMs, kept.DurationMs)
            ? ResumeDecision.FromStart
            : mode switch
            {
                ResumeMode.Resume => ResumeDecision.Resume,
                ResumeMode.StartOver => ResumeDecision.FromStart,
                _ => ResumeDecision.Ask,
            };

    /// <summary>
    /// A name for a file that changes when the file does, and does not say what the file is.
    /// </summary>
    /// <remarks>
    /// The path, size and last-written time together, hashed. A file replaced or edited is a new
    /// file with no position to resume, and the history on disk lists no names of things watched.
    /// </remarks>
    public static string KeyFor(string path, long sizeBytes, DateTimeOffset lastWritten)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var identity = string.Create(
            CultureInfo.InvariantCulture,
            $"{path.ToUpperInvariant()}|{sizeBytes}|{lastWritten.UtcTicks}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
