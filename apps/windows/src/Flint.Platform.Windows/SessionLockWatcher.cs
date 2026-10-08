using System.Runtime.InteropServices;

namespace Flint.Platform.Windows;

/// <summary>
/// Tells when this PC is locked and unlocked, from the messages Windows sends a window that asked
/// for them.
/// </summary>
/// <remarks>
/// Windows sends <c>WM_WTSSESSION_CHANGE</c> to a window registered with
/// <c>WTSRegisterSessionNotification</c>. Reading it from the window's own message hook avoids a
/// package this solution does not otherwise need, and the hook is the one the global shortcuts use.
/// </remarks>
public sealed partial class SessionLockWatcher : IDisposable
{
    /// <summary>The message Windows sends when the session changes.</summary>
    public const uint SessionChangeMessage = 0x02B1;

    private const int SessionLock = 0x7;
    private const int SessionUnlock = 0x8;
    private const int ThisSessionOnly = 0;

    private readonly IWtsApi api;
    private nint window;

    /// <summary>Starts watching for <paramref name="window"/>, through Windows.</summary>
    public SessionLockWatcher(nint window)
        : this(window, new NativeWtsApi())
    {
    }

    /// <summary>Starts watching for <paramref name="window"/>, through a supplied registration, for tests.</summary>
    internal SessionLockWatcher(nint window, IWtsApi api)
    {
        this.api = api;
        this.window = api.Register(window, ThisSessionOnly) ? window : 0;
    }

    /// <summary>The two calls that sign a window up for session changes, behind an interface for tests.</summary>
    internal interface IWtsApi
    {
        bool Register(nint window, int flags);

        void Unregister(nint window);
    }

    /// <summary>Raised when this PC is locked.</summary>
    public event EventHandler? Locked;

    /// <summary>Raised when this PC is unlocked.</summary>
    public event EventHandler? Unlocked;

    /// <summary>Whether Windows agreed to send the messages.</summary>
    public bool IsWatching => window != 0;

    /// <summary>Reads one window message, raising <see cref="Locked"/> or <see cref="Unlocked"/>.</summary>
    /// <returns>Whether the message was a session change.</returns>
    public bool OnMessage(uint message, nint reason)
    {
        if (message != SessionChangeMessage)
        {
            return false;
        }

        switch ((int)reason)
        {
            case SessionLock:
                Locked?.Invoke(this, EventArgs.Empty);
                break;
            case SessionUnlock:
                Unlocked?.Invoke(this, EventArgs.Empty);
                break;
        }

        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (window != 0)
        {
            api.Unregister(window);
            window = 0;
        }
    }

    private sealed partial class NativeWtsApi : IWtsApi
    {
        public bool Register(nint window, int flags) => WTSRegisterSessionNotification(window, flags);

        public void Unregister(nint window) => _ = WTSUnRegisterSessionNotification(window);

        [LibraryImport("wtsapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool WTSRegisterSessionNotification(nint window, int flags);

        [LibraryImport("wtsapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool WTSUnRegisterSessionNotification(nint window);
    }
}
