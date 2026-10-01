using System.Runtime.InteropServices;

namespace Flint.Platform.Windows;

/// <summary>How much of this PC must stay awake.</summary>
public enum KeepAwakeLevel
{
    /// <summary>Windows may sleep and turn the display off as it normally would.</summary>
    None = 0,

    /// <summary>The PC stays awake; the display may still turn off.</summary>
    /// <remarks>Enough for a file playing on the TV: the TV needs the connection, not this screen.</remarks>
    System = 1,

    /// <summary>The PC stays awake and the display stays on.</summary>
    /// <remarks>Needed while sharing the screen: a display that has turned off has nothing to capture.</remarks>
    SystemAndDisplay = 2,
}

/// <summary>The one Windows call that keeps a PC awake, behind an interface so it can be tested.</summary>
public interface IExecutionStateApi
{
    /// <summary>Calls <c>SetThreadExecutionState</c> with <paramref name="flags"/>.</summary>
    /// <returns>False when Windows refused.</returns>
    bool TrySet(uint flags);
}

/// <summary>
/// Keeps this PC from sleeping while something from it is on the TV.
/// </summary>
/// <remarks>
/// <para>
/// Windows ties this request to the thread that made it, so every call must come from the same
/// thread. The app makes them all from the UI thread, where the state they follow changes.
/// </para>
/// <para>
/// The request is replaced, never stacked, and only sent when the level actually changes, so
/// nothing is left held when sharing stops however many times the state was reported on the way.
/// </para>
/// </remarks>
public sealed partial class SleepBlocker : IDisposable
{
    /// <summary>Keeps the request in place until it is replaced.</summary>
    internal const uint Continuous = 0x8000_0000;

    /// <summary>Keeps the PC awake.</summary>
    internal const uint SystemRequired = 0x0000_0001;

    /// <summary>Keeps the display on.</summary>
    internal const uint DisplayRequired = 0x0000_0002;

    private readonly IExecutionStateApi api;

    /// <summary>Creates a blocker over the real Windows call.</summary>
    public SleepBlocker()
        : this(new NativeExecutionStateApi())
    {
    }

    /// <summary>Creates a blocker over a supplied call, for tests.</summary>
    /// <param name="api">Where the request is sent.</param>
    public SleepBlocker(IExecutionStateApi api) => this.api = api ?? throw new ArgumentNullException(nameof(api));

    /// <summary>The level last asked for.</summary>
    public KeepAwakeLevel Level { get; private set; }

    /// <summary>Asks Windows to keep <paramref name="level"/> of this PC awake.</summary>
    /// <returns>False when Windows refused; the previous level is then still the one in force.</returns>
    public bool Apply(KeepAwakeLevel level)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Not a keep-awake level.");
        }

        if (level == Level)
        {
            return true;
        }

        if (!api.TrySet(FlagsFor(level)))
        {
            return false;
        }

        Level = level;
        return true;
    }

    /// <summary>Lets Windows sleep normally again.</summary>
    public void Dispose() => Apply(KeepAwakeLevel.None);

    /// <summary>The flags Windows is sent for <paramref name="level"/>.</summary>
    internal static uint FlagsFor(KeepAwakeLevel level) => level switch
    {
        KeepAwakeLevel.SystemAndDisplay => Continuous | SystemRequired | DisplayRequired,
        KeepAwakeLevel.System => Continuous | SystemRequired,
        _ => Continuous,
    };

    private sealed partial class NativeExecutionStateApi : IExecutionStateApi
    {
        public bool TrySet(uint flags) => SetThreadExecutionState(flags) != 0;

        [LibraryImport("kernel32.dll")]
        private static partial uint SetThreadExecutionState(uint flags);
    }
}
