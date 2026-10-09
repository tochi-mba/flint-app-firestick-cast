using System.Runtime.InteropServices;
using Flint.Core.Shortcuts;

namespace Flint.Platform.Windows;

/// <summary>Shortcuts that work anywhere in Windows, claimed with <c>RegisterHotKey</c> for Flint's window.</summary>
/// <remarks>
/// Windows posts <c>WM_HOTKEY</c> to the window, so presses are read in the window's own message
/// hook, the same one the lock watcher uses. A combination another program already holds is
/// refused by Windows, which is the only reliable way to learn that it is taken.
/// </remarks>
public sealed partial class GlobalHotKeys : IHotKeyRegistrar, IDisposable
{
    /// <summary>The message Windows sends when a claimed combination is pressed.</summary>
    public const uint HotKeyMessage = 0x0312;

    /// <summary>Held down, a combination fires once rather than repeating.</summary>
    private const uint NoRepeat = 0x4000;

    private readonly nint window;
    private readonly IHotKeyApi api;
    private readonly HashSet<int> held = [];

    /// <summary>Claims combinations for <paramref name="window"/>, through Windows.</summary>
    public GlobalHotKeys(nint window)
        : this(window, new NativeHotKeyApi())
    {
    }

    /// <summary>Claims combinations through a supplied API, for tests.</summary>
    internal GlobalHotKeys(nint window, IHotKeyApi api)
    {
        this.window = window;
        this.api = api;
    }

    /// <summary>The two calls that claim and release a combination, behind an interface for tests.</summary>
    internal interface IHotKeyApi
    {
        bool Register(nint window, int id, uint modifiers, uint virtualKey);

        void Unregister(nint window, int id);
    }

    /// <inheritdoc />
    public event Action<int>? Pressed;

    /// <inheritdoc />
    public bool Register(int id, HotKeyModifiers modifiers, int virtualKey)
    {
        Unregister(id);
        if (!api.Register(window, id, (uint)modifiers | NoRepeat, (uint)virtualKey))
        {
            return false;
        }

        held.Add(id);
        return true;
    }

    /// <inheritdoc />
    public void Unregister(int id)
    {
        if (held.Remove(id))
        {
            api.Unregister(window, id);
        }
    }

    /// <summary>Reads one window message, raising <see cref="Pressed"/> for a claimed combination.</summary>
    /// <returns>Whether the message was a shortcut press.</returns>
    public bool OnMessage(uint message, nint id)
    {
        if (message != HotKeyMessage)
        {
            return false;
        }

        Pressed?.Invoke((int)id);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var id in held.ToArray())
        {
            Unregister(id);
        }
    }

    private sealed partial class NativeHotKeyApi : IHotKeyApi
    {
        public bool Register(nint window, int id, uint modifiers, uint virtualKey) =>
            RegisterHotKey(window, id, modifiers, virtualKey);

        public void Unregister(nint window, int id) => _ = UnregisterHotKey(window, id);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnregisterHotKey(nint window, int id);
    }
}
