using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Flint.Platform.Windows;

/// <summary>An invisible window that exists only to receive messages, such as shortcut presses.</summary>
/// <remarks>
/// Flint's own window may never have been shown, when it starts in the tray, and has no handle
/// until it is. Shortcuts must work then too, so they are claimed for this window instead. Messages
/// reach it through the thread's ordinary message loop, so it must be created on the UI thread.
/// </remarks>
public sealed unsafe partial class MessageWindow : IDisposable
{
    private const string ClassName = "REX.Flint.MessageWindow";
    private static readonly nint MessageOnlyParent = -3;
    private static readonly ConcurrentDictionary<nint, MessageWindow> Open = new();
    private static readonly Lazy<bool> Registered = new(RegisterClass);

    private MessageWindow(nint handle)
    {
        Handle = handle;
        Open[handle] = this;
    }

    /// <summary>Raised with each message the window receives; return true when it was handled.</summary>
    public event Func<uint, nint, nint, bool>? Received;

    /// <summary>The window's handle.</summary>
    public nint Handle { get; private set; }

    /// <summary>Creates the window on this thread, or returns null when Windows will not.</summary>
    public static MessageWindow? TryCreate() =>
        TryCreate(Registered.Value, () => CreateWindowExW(0, ClassName, string.Empty, 0, 0, 0, 0, 0, MessageOnlyParent, 0, 0, 0));

    /// <summary>Creates the window through <paramref name="create"/>, once its class is registered; for tests.</summary>
    internal static MessageWindow? TryCreate(bool registered, Func<nint> create)
    {
        var handle = registered ? create() : 0;
        return handle == 0 ? null : new MessageWindow(handle);
    }

    /// <summary>Sends a message straight to the window, as Windows would deliver one; for tests.</summary>
    internal nint Send(uint message, nint wParam, nint lParam) => SendMessageW(Handle, message, wParam, lParam);

    /// <inheritdoc />
    public void Dispose()
    {
        var handle = Handle;
        Handle = 0;
        if (handle != 0)
        {
            Open.TryRemove(handle, out _);
            _ = DestroyWindow(handle);
        }
    }

    private static bool RegisterClass()
    {
        var name = Marshal.StringToHGlobalUni(ClassName);
        var windowClass = new WindowClass
        {
            Size = (uint)sizeof(WindowClass),
            Procedure = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&Procedure,
            ClassName = name,
        };

        // The name stays allocated: Windows refers to the class by it for as long as Flint runs.
        return RegisterClassExW(&windowClass) != 0;
    }

    [UnmanagedCallersOnly]
    private static nint Procedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (Open.TryGetValue(window, out var owner) && owner.Received?.Invoke(message, wParam, lParam) == true)
        {
            return 0;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(WindowClass* windowClass);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
}
