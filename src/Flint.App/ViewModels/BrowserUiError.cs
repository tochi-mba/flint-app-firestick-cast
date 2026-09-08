using Flint.App.Controls;
using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>Actionable, presentation-safe browser failure.</summary>
public sealed record BrowserUiError(BrowserUiErrorKind Kind, string Title, string Message)
{
    /// <summary>Error colour for a failure; neutral for a capability limitation.</summary>
    public Tone Tone => Kind == BrowserUiErrorKind.FeatureUnavailable ? Tone.Neutral : Tone.Live;

    /// <summary>Classifies receiver and transport details without exposing exception types in XAML.</summary>
    public static BrowserUiError FromReceiver(BrowserStateMessage state)
    {
        var detail = Bounded(state.ErrorDetail, "The TV could not open that page.");
        var lower = detail.ToLowerInvariant();
        var kind = lower switch
        {
            _ when lower.Contains("certificate", StringComparison.Ordinal) => BrowserUiErrorKind.Certificate,
            _ when lower.Contains("find", StringComparison.Ordinal) => BrowserUiErrorKind.SiteNotFound,
            _ when lower.Contains("network", StringComparison.Ordinal)
                || lower.Contains("reach", StringComparison.Ordinal)
                || lower.Contains("connect", StringComparison.Ordinal) => BrowserUiErrorKind.Network,
            _ when lower.Contains("blocked", StringComparison.Ordinal)
                || lower.Contains("https", StringComparison.Ordinal) => BrowserUiErrorKind.Blocked,
            _ when lower.Contains("renderer", StringComparison.Ordinal)
                || lower.Contains("stopped", StringComparison.Ordinal) => BrowserUiErrorKind.Renderer,
            _ => BrowserUiErrorKind.Site,
        };
        return new BrowserUiError(kind, TitleFor(kind), detail);
    }

    /// <summary>Classifies a local send/verification failure.</summary>
    public static BrowserUiError FromException(Exception exception, BrowserUiErrorKind fallback)
    {
        var message = Bounded(exception.Message, "The secure TV connection stopped responding.");
        return new BrowserUiError(fallback, TitleFor(fallback), message);
    }

    public static BrowserUiError Unavailable(string message) =>
        new(BrowserUiErrorKind.FeatureUnavailable, "Receiver update required", message);

    private static string TitleFor(BrowserUiErrorKind kind) => kind switch
    {
        BrowserUiErrorKind.Verification => "Secure verification failed",
        BrowserUiErrorKind.Connection => "TV connection lost",
        BrowserUiErrorKind.Network => "Site unreachable",
        BrowserUiErrorKind.SiteNotFound => "Site not found",
        BrowserUiErrorKind.Certificate => "Certificate rejected",
        BrowserUiErrorKind.Blocked => "Address blocked",
        BrowserUiErrorKind.Renderer => "Page stopped",
        BrowserUiErrorKind.Preview => "Preview unavailable",
        BrowserUiErrorKind.FeatureUnavailable => "Receiver update required",
        _ => "Page error",
    };

    private static string Bounded(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length <= 240 ? text : text[..240];
    }
}

/// <summary>Stable browser failure categories used by UI and accessibility tests.</summary>
public enum BrowserUiErrorKind
{
    Verification,
    Connection,
    Network,
    SiteNotFound,
    Site,
    Certificate,
    Blocked,
    Renderer,
    Preview,
    FeatureUnavailable,
}
