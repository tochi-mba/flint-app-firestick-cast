using Shouldly;

namespace Flint.Platform.Windows.Tests;

/// <summary>Locking and unlocking, read from the messages Windows sends the window.</summary>
public sealed partial class SessionLockWatcherTests
{
    [Fact]
    public void LockAndUnlock_AreToldApart_AndOtherMessagesIgnored()
    {
        var api = new FakeWts();
        using var watcher = new SessionLockWatcher(42, api);
        var heard = new List<string>();
        watcher.Locked += (_, _) => heard.Add("locked");
        watcher.Unlocked += (_, _) => heard.Add("unlocked");

        watcher.OnMessage(SessionLockWatcher.SessionChangeMessage, 0x7).ShouldBeTrue();
        watcher.OnMessage(SessionLockWatcher.SessionChangeMessage, 0x8).ShouldBeTrue();
        watcher.OnMessage(SessionLockWatcher.SessionChangeMessage, 0x5).ShouldBeTrue("a session change, just not one that matters here");
        watcher.OnMessage(0x0010, 0x7).ShouldBeFalse();

        heard.ShouldBe(["locked", "unlocked"]);
        watcher.IsWatching.ShouldBeTrue();
        api.Registered.ShouldBe([(42, 0)]);
    }

    [Fact]
    public void Nobody_ListeningIsFine()
    {
        using var watcher = new SessionLockWatcher(42, new FakeWts());

        Should.NotThrow(() => watcher.OnMessage(SessionLockWatcher.SessionChangeMessage, 0x7));
        Should.NotThrow(() => watcher.OnMessage(SessionLockWatcher.SessionChangeMessage, 0x8));
    }

    [Fact]
    public void TheWindow_IsSignedOutOnce_AndOnlyIfItWasSignedUp()
    {
        var api = new FakeWts();
        var watcher = new SessionLockWatcher(42, api);
        watcher.Dispose();
        watcher.Dispose();
        api.Unregistered.ShouldBe([42]);

        var refused = new FakeWts { Refuse = true };
        using var notWatching = new SessionLockWatcher(42, refused);
        notWatching.IsWatching.ShouldBeFalse();
        notWatching.Dispose();
        refused.Unregistered.ShouldBeEmpty();
    }

    [Fact]
    public void ARealWindowlessRegistration_IsRefusedQuietly()
    {
        using var watcher = new SessionLockWatcher(0);

        watcher.IsWatching.ShouldBeFalse("Windows will not register no window");
    }

    [Fact]
    public void ARealWindow_IsSignedUpAndOut()
    {
        // A message-only window: real enough for Windows to send it session changes, never shown.
        var window = NativeWindow.CreateMessageOnly();
        try
        {
            var watcher = new SessionLockWatcher(window);
            watcher.IsWatching.ShouldBeTrue();
            watcher.Dispose();
            watcher.IsWatching.ShouldBeFalse();
        }
        finally
        {
            NativeWindow.Destroy(window);
        }
    }

    private static partial class NativeWindow
    {
        private static readonly nint MessageOnlyParent = -3;

        public static nint CreateMessageOnly() =>
            CreateWindowExW(0, "STATIC", string.Empty, 0, 0, 0, 0, 0, MessageOnlyParent, 0, 0, 0);

        public static void Destroy(nint window) => _ = DestroyWindow(window);

        [System.Runtime.InteropServices.LibraryImport("user32.dll", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
        private static partial nint CreateWindowExW(
            int exStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            nint parent,
            nint menu,
            nint instance,
            nint param);

        [System.Runtime.InteropServices.LibraryImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static partial bool DestroyWindow(nint window);
    }

    private sealed class FakeWts : SessionLockWatcher.IWtsApi
    {
        public bool Refuse { get; init; }

        public List<(nint Window, int Flags)> Registered { get; } = [];

        public List<nint> Unregistered { get; } = [];

        public bool Register(nint window, int flags)
        {
            Registered.Add((window, flags));
            return !Refuse;
        }

        public void Unregister(nint window) => Unregistered.Add(window);
    }
}
