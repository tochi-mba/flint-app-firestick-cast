using System.Diagnostics;
using System.Text;

namespace Flint.Hardware.E2E;

/// <summary>Reads the live receiver pairing surface through ADB UiAutomator dump + logcat.</summary>
public static class AdbTvSurfaceReader
{
    /// <summary>Launches the debug receiver without installing or clearing data.</summary>
    public static Task<string> LaunchReceiverAsync(string serial, CancellationToken cancellationToken) =>
        RunAdbAsync(serial, ["shell", "am", "start", "-n",
            "com.rextechnologies.flint.receiver.debug/com.rextechnologies.flint.receiver.ReceiverActivity"], cancellationToken);

    /// <summary>Reads the visible TV tree for interleaved assertions.</summary>
    public static Task<string> ReadHierarchyAsync(string serial, CancellationToken cancellationToken) =>
        RunAdbAsync(serial, ["exec-out", "uiautomator", "dump", "/dev/tty"], cancellationToken);

    /// <summary>Sends a bounded remote key code.</summary>
    public static Task<string> PressKeyAsync(string serial, int keyCode, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keyCode);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keyCode, 300);
        return RunAdbAsync(serial, ["shell", "input", "keyevent",
            keyCode.ToString(System.Globalization.CultureInfo.InvariantCulture)], cancellationToken);
    }
    /// <summary>Dumps the current TV UI and parses pairing / browser facts.</summary>
    public static async Task<TvSurfaceFacts> ReadAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        // Dump to stdout so nothing is left on the TV filesystem.
        var dump = await RunAdbAsync(serial, ["exec-out", "uiautomator", "dump", "/dev/tty"], cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(dump) || !dump.Contains("hierarchy", StringComparison.Ordinal))
        {
            // Some Fire OS builds ignore /dev/tty; fall back to a pullable path.
            await RunAdbAsync(
                    serial,
                    ["shell", "uiautomator", "dump", "/sdcard/flint-window-dump.xml"],
                    cancellationToken)
                .ConfigureAwait(false);
            dump = await RunAdbAsync(
                    serial,
                    ["exec-out", "cat", "/sdcard/flint-window-dump.xml"],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var logcat = await RunAdbAsync(
                serial,
                ["logcat", "-d", "-s", "BrowserTlsServer:I"],
                cancellationToken)
            .ConfigureAwait(false);

        return TvSurfaceFactParser.Parse(dump, logcat);
    }

    private static async Task<string> RunAdbAsync(
        string serial,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = "adb",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-s");
        start.ArgumentList.Add(serial);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("adb could not be started. Is platform-tools on PATH?");
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"adb -s {serial} {string.Join(' ', args)} failed ({process.ExitCode}): {stderr}");
        }

        return stdout.ToString();
    }
}
