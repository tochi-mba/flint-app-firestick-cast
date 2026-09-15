using System.Diagnostics;

namespace Flint.Core;

/// <summary>
/// Privacy-safe diagnostic breadcrumbs shared by the Windows shell and session transport.
/// </summary>
/// <remarks>
/// <para>
/// Lines are written through <see cref="Trace"/> so the Avalonia host's file listener can capture
/// them without Session taking a dependency on the UI assembly. Never pass pairing codes, VPN
/// configs, cookies, passwords, certificate PEMs, preview pixels, or page text.
/// </para>
/// </remarks>
public static class FlintDiag
{
    /// <summary>Marker prefix recognised by the Windows file log listener.</summary>
    public const string TracePrefix = "FlintDiag";

    /// <summary>Informational breadcrumb.</summary>
    public static void Info(string tag, string message) => Write("INFO", tag, message);

    /// <summary>Warning breadcrumb.</summary>
    public static void Warn(string tag, string message) => Write("WARN", tag, message);

    /// <summary>Error breadcrumb with a UI-safe reason only.</summary>
    public static void Error(string tag, string message) => Write("ERROR", tag, message);

    /// <summary>
    /// Host-only view of a URL for logs. Paths, queries and fragments are dropped so page text
    /// never reaches the file.
    /// </summary>
    public static string SafeHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "(none)";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return "(opaque)";
        }

        return string.IsNullOrWhiteSpace(absolute.Host) ? "(opaque)" : absolute.Host;
    }

    /// <summary>
    /// Host + path + query/fragment lengths — enough to tell two Google searches apart in logs
    /// without recording the search text.
    /// </summary>
    public static string SafeTabRef(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "(none)";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return "(opaque)";
        }

        var host = string.IsNullOrWhiteSpace(absolute.Host) ? "(opaque)" : absolute.Host;
        var path = string.IsNullOrEmpty(absolute.AbsolutePath) ? "/" : absolute.AbsolutePath;
        var queryLen = absolute.Query.Length;
        var fragmentLen = absolute.Fragment.Length;
        return queryLen > 0 || fragmentLen > 0
            ? $"{host}{path} qLen={queryLen} fLen={fragmentLen}"
            : $"{host}{path}";
    }

    private static void Write(string level, string tag, string message)
    {
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Trace.WriteLine(FormatLine(level, tag, message));
    }

    /// <summary>Formats one privacy-safe Trace line (single-line, length-capped message).</summary>
    public static string FormatLine(string level, string tag, string message)
    {
        // Single-line only — multiline payloads are how secrets accidentally leak into log pulls.
        var safeTag = tag.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var safeMessage = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (safeMessage.Length > 480)
        {
            safeMessage = safeMessage[..480];
        }

        return $"{TracePrefix}|{level}|{safeTag}|{safeMessage}";
    }
}
