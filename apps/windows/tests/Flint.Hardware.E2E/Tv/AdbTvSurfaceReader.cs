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

    /// <summary>
    /// Launches whichever Flint receiver is installed, the debug build first, without installing or
    /// clearing data. A TV set up from a release keeps the release package and nothing else.
    /// </summary>
    public static async Task LaunchInstalledReceiverAsync(string serial, CancellationToken cancellationToken)
    {
        var packages = await RunAdbAsync(serial, ["shell", "pm", "list", "packages", "com.rextechnologies.flint.receiver"], cancellationToken)
            .ConfigureAwait(false);
        var package = packages.Contains("package:com.rextechnologies.flint.receiver.debug", StringComparison.Ordinal)
            ? "com.rextechnologies.flint.receiver.debug"
            : packages.Contains("package:com.rextechnologies.flint.receiver", StringComparison.Ordinal)
                ? "com.rextechnologies.flint.receiver"
                : throw new InvalidOperationException("No Flint receiver is installed on the TV. ./dev.ps1 receiver install puts one there.");
        await RunAdbAsync(serial, ["shell", "am", "start", "-n", $"{package}/com.rextechnologies.flint.receiver.ReceiverActivity"], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Saves what the TV shows now as a PNG at <paramref name="path"/>.</summary>
    public static async Task ScreenshotAsync(string serial, string path, CancellationToken cancellationToken)
    {
        var start = Start(serial, ["exec-out", "screencap", "-p"]);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("adb could not be started. Is platform-tools on PATH?");
        await using (var file = File.Create(path))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

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

        // Newer receivers keep the address and browser port behind Connection details, which has
        // focus on the ready screen. Open it, read it, and close it again.
        if (!TvSurfaceFactParser.HasEndpoint(dump))
        {
            await PressKeyAsync(serial, 23, cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            dump += await RunAdbAsync(serial, ["exec-out", "uiautomator", "dump", "/dev/tty"], cancellationToken)
                .ConfigureAwait(false);
            await PressKeyAsync(serial, 4, cancellationToken).ConfigureAwait(false);
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
        var start = Start(serial, args);
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

    private static ProcessStartInfo Start(string serial, IReadOnlyList<string> args)
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

        return start;
    }
}
