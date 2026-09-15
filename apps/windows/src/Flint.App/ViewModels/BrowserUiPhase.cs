namespace Flint.App.ViewModels;

/// <summary>Windows browser page secure-session phases.</summary>
public enum BrowserUiPhase
{
    /// <summary>No secure session has been started.</summary>
    Idle = 0,

    /// <summary>TLS and fingerprint comparison are in progress.</summary>
    Verifying = 1,

    /// <summary>Pinned session is authenticated and ready for navigation.</summary>
    SecureReady = 2,

    /// <summary>Pin mismatch, rejected code, or transport failure.</summary>
    Mismatch = 3,
}
