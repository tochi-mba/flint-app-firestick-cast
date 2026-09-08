using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using Flint.Core;

namespace Flint.App.Services;

/// <summary>
/// Bounded developer file log for diagnosing Windows ↔ Fire TV sessions.
/// </summary>
/// <remarks>
/// <para>
/// Writes to <c>%LOCALAPPDATA%\Flint\logs\windows-latest.log</c>. Truncates at session start.
/// While the process runs, growth past <see cref="MaximumBytes"/> drops the oldest lines and keeps
/// the newest <see cref="RetainBytes"/> (newline-aligned). Agents copy it into
/// <c>artifacts/logs/</c> via <c>scripts/pull-dev-logs.ps1</c>.
/// </para>
/// <para>
/// Never write VPN config, private keys, cookies, passwords, certificate PEMs, preview pixels, or
/// page text here. Tags and UI-safe reasons only.
/// </para>
/// </remarks>
internal static class DevFileLog
{
    /// <summary>Hard cap so a runaway Trace flood cannot fill the disk.</summary>
    public static long MaximumBytes { get; private set; } = DevLogRetention.DefaultMaximumBytes;

    /// <summary>Bytes kept after a size trim; must stay below <see cref="MaximumBytes"/>.</summary>
    public static long RetainBytes { get; private set; } = DevLogRetention.DefaultRetainBytes;

    /// <summary>UI-thread lag above this is logged as a freeze suspect.</summary>
    private static readonly TimeSpan UiStallWarnAfter = TimeSpan.FromMilliseconds(750);

    private static readonly object Gate = new();
    private static StreamWriter? writer;
    private static string? primaryPath;
    private static DispatcherTimer? watchdog;
    private static int watchdogInFlight;
    private static DevFileTraceListener? listener;

    /// <summary>Primary log path under LocalAppData, once <see cref="Start"/> has run.</summary>
    public static string? PrimaryPath => primaryPath;

    /// <summary>
    /// Opens the session log and attaches a <see cref="Trace"/> listener so Avalonia
    /// <c>LogToTrace</c> and <see cref="FlintDiag"/> breadcrumbs land in the same file.
    /// </summary>
    public static void Start()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flint",
            "logs");
        Directory.CreateDirectory(appDataDir);
        StartCore(
            Path.Combine(appDataDir, "windows-latest.log"),
            DevLogRetention.DefaultMaximumBytes,
            DevLogRetention.DefaultRetainBytes,
            truncateExisting: true);
    }

    /// <summary>Test seam: open a specific path with explicit size bounds.</summary>
    internal static void StartForTests(
        string path,
        long maximumBytes = DevLogRetention.DefaultMaximumBytes,
        long retainBytes = DevLogRetention.DefaultRetainBytes,
        bool truncateExisting = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        StartCore(path, maximumBytes, retainBytes, truncateExisting);
    }

    /// <summary>Test seam: closes the writer and removes the Trace listener.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            if (listener is not null)
            {
                Trace.Listeners.Remove(listener);
                listener = null;
            }

            writer?.Dispose();
            writer = null;
            primaryPath = null;
            MaximumBytes = DevLogRetention.DefaultMaximumBytes;
            RetainBytes = DevLogRetention.DefaultRetainBytes;
            Interlocked.Exchange(ref watchdogInFlight, 0);
        }
    }

    private static void StartCore(string path, long maximumBytes, long retainBytes, bool truncateExisting)
    {
        DevLogRetention.ValidateBounds(maximumBytes, retainBytes);
        lock (Gate)
        {
            if (writer is not null)
            {
                return;
            }

            MaximumBytes = maximumBytes;
            RetainBytes = retainBytes;
            primaryPath = path;
            if (truncateExisting)
            {
                Truncate(primaryPath);
            }
            else
            {
                DevLogRetention.TrimOldestIfNeeded(primaryPath, MaximumBytes, RetainBytes);
            }

            writer = OpenWriter(primaryPath);
            listener = new DevFileTraceListener();
            Trace.Listeners.Add(listener);
            WriteUnlocked("INFO", "DevFileLog", $"session start path={primaryPath} maxBytes={MaximumBytes} retainBytes={RetainBytes}");
        }
    }

    /// <summary>
    /// Starts a periodic UI-thread ping so freezes leave a breadcrumb even when no other log
    /// lines are written. Call after Avalonia's dispatcher exists.
    /// </summary>
    public static void StartUiWatchdog()
    {
        if (watchdog is not null)
        {
            return;
        }

        watchdog = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        watchdog.Tick += OnWatchdogTick;
        watchdog.Start();
        Info("FlintUi", "ui watchdog armed intervalMs=5000 stallWarnMs=750");
    }

    /// <summary>Appends one UI-safe line. Swallows I/O failures so logging never takes down the shell.</summary>
    public static void Info(string tag, string message) => Write("INFO", tag, message);

    /// <summary>Appends one warning line.</summary>
    public static void Warn(string tag, string message) => Write("WARN", tag, message);

    /// <summary>Appends one error line without exception details that might contain secrets.</summary>
    public static void Error(string tag, string message) => Write("ERROR", tag, message);

    /// <summary>Whether Avalonia binding TRACE should be dropped from the file.</summary>
    internal static bool IsAvaloniaBindingNoise(string message) =>
        message.Contains("[Binding]", StringComparison.Ordinal)
        || message.Contains("An error occurred binding", StringComparison.Ordinal)
        || message.Contains("Value is null.", StringComparison.Ordinal);

    /// <summary>
    /// Parses a <see cref="FlintDiag"/> Trace line into level/tag/message when the prefix matches.
    /// </summary>
    internal static bool TryParseDiag(string message, out string level, out string tag, out string body)
    {
        level = string.Empty;
        tag = string.Empty;
        body = string.Empty;
        if (!message.StartsWith(FlintDiag.TracePrefix + "|", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = message.Split('|', 4);
        if (parts.Length < 4)
        {
            return false;
        }

        level = parts[1];
        tag = parts[2];
        body = parts[3];
        return true;
    }

    private static void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref watchdogInFlight, 1) != 0)
        {
            Warn("FlintUi", "ui watchdog previous ping still outstanding — dispatcher may be stalled");
            return;
        }

        var postedAt = DateTimeOffset.UtcNow;
        Dispatcher.UIThread.Post(
            () =>
            {
                var lagMs = (DateTimeOffset.UtcNow - postedAt).TotalMilliseconds;
                Interlocked.Exchange(ref watchdogInFlight, 0);
                if (lagMs >= UiStallWarnAfter.TotalMilliseconds)
                {
                    Warn("FlintUi", $"ui thread lagMs={lagMs:0}");
                }
            },
            DispatcherPriority.Background);
    }

    private static void Write(string level, string tag, string message)
    {
        lock (Gate)
        {
            if (writer is null)
            {
                return;
            }

            try
            {
                MaybeTrimUnlocked();
                WriteUnlocked(level, tag, message);
            }
            catch
            {
                // Logging must never become a failure mode for the product shell.
            }
        }
    }

    private static void WriteUnlocked(string level, string tag, string message)
    {
        writer!.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {tag}: {message}");
    }

    private static void WriteRawUnlocked(string message)
    {
        writer!.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [TRACE] {message}");
    }

    private static void MaybeTrimUnlocked()
    {
        if (primaryPath is null || writer is null)
        {
            return;
        }

        try
        {
            writer.Flush();
            var info = new FileInfo(primaryPath);
            if (!info.Exists || info.Length < MaximumBytes)
            {
                return;
            }

            writer.Dispose();
            writer = null;
            var trimmed = DevLogRetention.TrimOldestIfNeeded(primaryPath, MaximumBytes, RetainBytes);
            writer = OpenWriter(primaryPath);
            if (trimmed)
            {
                WriteUnlocked(
                    "INFO",
                    "DevFileLog",
                    $"trimmed oldest lines maxBytes={MaximumBytes} retainBytes={RetainBytes}");
            }
        }
        catch
        {
            // Leave or reopen the writer if trim fails mid-flight.
            if (writer is null && primaryPath is not null)
            {
                try
                {
                    writer = OpenWriter(primaryPath);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private static StreamWriter OpenWriter(string path) =>
        new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
        {
            AutoFlush = true,
        };

    private static void Truncate(string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
    }

    /// <summary>Routes Avalonia/Trace and <see cref="FlintDiag"/> through the same lock and file.</summary>
    private sealed class DevFileTraceListener : TraceListener
    {
        public DevFileTraceListener() => Name = "FlintDevFileLog";

        public override void Write(string? message)
        {
            // Avalonia sometimes writes partial lines; only keep finished lines via WriteLine.
        }

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            lock (Gate)
            {
                if (writer is null)
                {
                    return;
                }

                try
                {
                    MaybeTrimUnlocked();
                    if (TryParseDiag(message, out var level, out var tag, out var body))
                    {
                        WriteUnlocked(level, tag, body);
                        return;
                    }

                    if (IsAvaloniaBindingNoise(message))
                    {
                        return;
                    }

                    WriteRawUnlocked(message);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}
