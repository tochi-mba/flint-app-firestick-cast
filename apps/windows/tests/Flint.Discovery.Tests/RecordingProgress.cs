namespace Flint.Discovery.Tests;

/// <summary>Every fraction the ADB client reported, in the order it reported them.</summary>
/// <remarks>
/// Not <see cref="Progress{T}"/>: that posts each report to the thread pool, where concurrent adds
/// corrupted a list and took the whole test host down. The client reports synchronously from its
/// one read loop, so a plain list is safe here.
/// </remarks>
internal sealed class RecordingProgress : IProgress<double>
{
    public List<double> Values { get; } = [];

    public void Report(double value) => Values.Add(value);
}
