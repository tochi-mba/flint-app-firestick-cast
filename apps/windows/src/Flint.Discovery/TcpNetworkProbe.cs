using System.Diagnostics;
using System.Net.Sockets;
using Flint.Core;

namespace Flint.Discovery;

/// <summary>
/// Measures the path to a receiver by timing repeated TCP connects.
/// </summary>
/// <remarks>
/// <para>
/// TCP connect timing is used rather than ICMP because Fire TV devices commonly drop pings while
/// happily accepting connections, and because the measurement then exercises the same path a real
/// session would.
/// </para>
/// <para>
/// This measures round-trip time and jitter honestly. It does not measure throughput: doing that
/// requires a receiver willing to sink traffic.
/// <see cref="NetworkPath.ThroughputMbps"/> is reported as unmeasured rather than estimated from
/// link speed, and the report says so.
/// </para>
/// </remarks>
public sealed class TcpNetworkProbe : INetworkProbe
{
    /// <summary>How many connects to time before computing a median.</summary>
    public const int SampleCount = 7;

    /// <summary>Samples discarded from the start, to exclude ARP and route-cache warm-up.</summary>
    public const int WarmupSamples = 2;

    /// <summary>Ceiling on a single connect attempt.</summary>
    public static readonly TimeSpan SampleTimeout = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// Sentinel throughput meaning "not measured".
    /// </summary>
    /// <remarks>
    /// Negative so it can never be mistaken for a real reading, and so any threshold comparison
    /// against it fails closed rather than silently passing.
    /// </remarks>
    public const double UnmeasuredThroughput = -1.0;

    /// <inheritdoc />
    public async Task<NetworkPath?> MeasureAsync(
        FireTvDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.AdbPort is not { } port)
        {
            return null;
        }

        var samples = new List<double>(SampleCount);
        var failures = 0;

        for (var index = 0; index < SampleCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var elapsed = await TimeConnectAsync(device, port, cancellationToken).ConfigureAwait(false);
            if (elapsed is null)
            {
                failures++;
                continue;
            }

            if (index >= WarmupSamples)
            {
                samples.Add(elapsed.Value);
            }
        }

        if (samples.Count == 0)
        {
            return null;
        }

        return new NetworkPath(
            Median(samples),
            Jitter(samples),
            UnmeasuredThroughput,
            100.0 * failures / SampleCount,
            ThroughputMeasured: false);
    }

    private static async Task<double?> TimeConnectAsync(
        FireTvDevice device,
        int port,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient(device.Address.AddressFamily) { NoDelay = true };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SampleTimeout);
            await client.ConnectAsync(device.Address, port, timeout.Token).ConfigureAwait(false);
            return stopwatch.Elapsed.TotalMilliseconds;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>
    /// The median sample.
    /// </summary>
    /// <remarks>
    /// Median rather than mean: a single Wi-Fi retransmission produces an outlier that would drag a
    /// mean well away from what the link actually does most of the time.
    /// </remarks>
    internal static double Median(IReadOnlyList<double> samples)
    {
        var ordered = samples.Order().ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2.0;
    }

    /// <summary>
    /// Mean absolute deviation from the median, which is what sizes a jitter buffer.
    /// </summary>
    internal static double Jitter(IReadOnlyList<double> samples)
    {
        if (samples.Count < 2)
        {
            return 0.0;
        }

        var median = Median(samples);
        return samples.Sum(sample => Math.Abs(sample - median)) / samples.Count;
    }
}
