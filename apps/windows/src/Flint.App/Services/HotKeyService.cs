using Flint.Core;
using Flint.Core.Settings;
using Flint.Core.Shortcuts;

namespace Flint.App.Services;

/// <summary>What a shortcut does.</summary>
public enum ShortcutAction
{
    /// <summary>Starts or stops sharing the screen.</summary>
    StartStopSharing = 1,

    /// <summary>Pauses or resumes sharing.</summary>
    PauseResumeSharing = 2,

    /// <summary>Plays or pauses what is on the TV.</summary>
    PlayPauseTv = 3,

    /// <summary>Plays the next item in the queue.</summary>
    NextItem = 4,

    /// <summary>Plays the previous item in the queue.</summary>
    PreviousItem = 5,

    /// <summary>Turns the TV up.</summary>
    VolumeUp = 6,

    /// <summary>Turns the TV down.</summary>
    VolumeDown = 7,

    /// <summary>Brings Flint's window forward.</summary>
    ShowWindow = 8,

    /// <summary>Disconnects from the TV.</summary>
    Disconnect = 9,
}

/// <summary>Claims the shortcuts the settings name, and runs their actions when they are pressed.</summary>
/// <remarks>
/// <para>
/// Only what changed is claimed or released, so changing one shortcut never disturbs the others.
/// A combination another program holds is reported against its action and left unclaimed; the
/// setting is not changed behind the person's back.
/// </para>
/// <para>
/// The keyboard's media keys are claimed only when the setting says so, because another player on
/// this PC may be relying on them.
/// </para>
/// </remarks>
public sealed class HotKeyService : IDisposable
{
    /// <summary>The identity media keys are claimed under, past every action's own.</summary>
    internal const int MediaKeyBase = 100;

    /// <summary>The media keys, as virtual-key codes, and what each does.</summary>
    internal static readonly IReadOnlyList<(int VirtualKey, ShortcutAction Action)> MediaKeys =
    [
        (0xB3, ShortcutAction.PlayPauseTv),
        (0xB0, ShortcutAction.NextItem),
        (0xB1, ShortcutAction.PreviousItem),
    ];

    private readonly IHotKeyRegistrar registrar;
    private readonly ISettingsService settings;
    private readonly Func<ShortcutAction, bool> run;
    private readonly Dictionary<ShortcutAction, HotKeyGesture> claimed = [];
    private readonly Dictionary<ShortcutAction, string> problems = [];
    private bool mediaKeysClaimed;

    /// <summary>Claims what the settings name, and follows them as they change.</summary>
    /// <param name="registrar">Claims combinations from Windows.</param>
    /// <param name="settings">The live settings.</param>
    /// <param name="run">Runs an action; false when it could not do anything just now.</param>
    public HotKeyService(IHotKeyRegistrar registrar, ISettingsService settings, Func<ShortcutAction, bool> run)
    {
        this.registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.run = run ?? throw new ArgumentNullException(nameof(run));
        registrar.Pressed += OnPressed;
        settings.Changed += OnSettingsChanged;
        Apply(settings.Current.Shortcuts);
    }

    /// <summary>Raised when an action's problem appears or goes away.</summary>
    public event EventHandler? ProblemsChanged;

    /// <summary>Why an action's shortcut is not working, by action; empty when they all work.</summary>
    public IReadOnlyDictionary<ShortcutAction, string> Problems => problems;

    /// <summary>The shortcut text the settings hold for <paramref name="action"/>, or null when it has none.</summary>
    public static string? TextFor(ShortcutSettings shortcuts, ShortcutAction action)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        return action switch
        {
            ShortcutAction.StartStopSharing => shortcuts.StartStopSharing,
            ShortcutAction.PauseResumeSharing => shortcuts.PauseResumeSharing,
            ShortcutAction.PlayPauseTv => shortcuts.PlayPauseTv,
            ShortcutAction.NextItem => shortcuts.NextItem,
            ShortcutAction.PreviousItem => shortcuts.PreviousItem,
            ShortcutAction.VolumeUp => shortcuts.VolumeUp,
            ShortcutAction.VolumeDown => shortcuts.VolumeDown,
            ShortcutAction.ShowWindow => shortcuts.ShowWindow,
            ShortcutAction.Disconnect => shortcuts.Disconnect,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Not a shortcut action."),
        };
    }

    /// <summary>The settings with <paramref name="action"/>'s shortcut set to <paramref name="text"/>, or cleared with null.</summary>
    public static ShortcutSettings With(ShortcutSettings shortcuts, ShortcutAction action, string? text)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        return action switch
        {
            ShortcutAction.StartStopSharing => shortcuts with { StartStopSharing = text },
            ShortcutAction.PauseResumeSharing => shortcuts with { PauseResumeSharing = text },
            ShortcutAction.PlayPauseTv => shortcuts with { PlayPauseTv = text },
            ShortcutAction.NextItem => shortcuts with { NextItem = text },
            ShortcutAction.PreviousItem => shortcuts with { PreviousItem = text },
            ShortcutAction.VolumeUp => shortcuts with { VolumeUp = text },
            ShortcutAction.VolumeDown => shortcuts with { VolumeDown = text },
            ShortcutAction.ShowWindow => shortcuts with { ShowWindow = text },
            ShortcutAction.Disconnect => shortcuts with { Disconnect = text },
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Not a shortcut action."),
        };
    }

    /// <summary>
    /// Gives <paramref name="action"/> the shortcut <paramref name="gesture"/>, when Windows lets Flint
    /// have it.
    /// </summary>
    /// <returns>Why it could not, or null when it is now the action's shortcut.</returns>
    public string? TryAssign(ShortcutAction action, HotKeyGesture gesture)
    {
        var shortcuts = settings.Current.Shortcuts;
        if (shortcuts.WorkInBackground && !claimed.ContainsValue(gesture))
        {
            // Tried first under a spare identity, so the action keeps its old shortcut if this fails.
            const int Trial = 99;
            if (!registrar.Register(Trial, gesture.Modifiers, gesture.VirtualKey))
            {
                return $"Another program already uses {gesture}.";
            }

            registrar.Unregister(Trial);
        }

        settings.Update(current => current with { Shortcuts = With(current.Shortcuts, action, gesture.ToString()) });
        return null;
    }

    /// <summary>The action that already has <paramref name="gesture"/>, other than <paramref name="except"/>, if any.</summary>
    public ShortcutAction? OwnerOf(HotKeyGesture gesture, ShortcutAction except) =>
        Enum.GetValues<ShortcutAction>()
            .Where(action => action != except)
            .Select(action => (ShortcutAction?)action)
            .FirstOrDefault(action => HotKeyGesture.TryParse(TextFor(settings.Current.Shortcuts, action!.Value), out var held, out _) && held == gesture);

    /// <summary>Runs the action bound to a gesture pressed inside Flint's own window, when shortcuts work only there.</summary>
    /// <returns>Whether a shortcut was pressed.</returns>
    public bool OnWindowKey(HotKeyGesture gesture)
    {
        var shortcuts = settings.Current.Shortcuts;
        if (shortcuts.WorkInBackground)
        {
            return false;
        }

        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            if (HotKeyGesture.TryParse(TextFor(shortcuts, action), out var bound, out _) && bound == gesture)
            {
                Run(action);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        registrar.Pressed -= OnPressed;
        settings.Changed -= OnSettingsChanged;
        foreach (var action in claimed.Keys.ToArray())
        {
            Release(action);
        }

        ClaimMediaKeys(false);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs change)
    {
        if (change.Previous.Shortcuts != change.Current.Shortcuts)
        {
            Apply(change.Current.Shortcuts);
        }
    }

    private void Apply(ShortcutSettings shortcuts)
    {
        var before = problems.ToDictionary();
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            var wanted = shortcuts.WorkInBackground && HotKeyGesture.TryParse(TextFor(shortcuts, action), out var gesture, out _)
                ? gesture
                : (HotKeyGesture?)null;
            if (claimed.TryGetValue(action, out var held) && held == wanted)
            {
                continue;
            }

            Release(action);
            problems.Remove(action);
            if (wanted is not { } next)
            {
                continue;
            }

            if (registrar.Register((int)action, next.Modifiers, next.VirtualKey))
            {
                claimed[action] = next;
            }
            else
            {
                problems[action] = $"Another program already uses {next}.";
                FlintDiag.Warn("FlintShortcuts", $"refused {action}");
            }
        }

        ClaimMediaKeys(shortcuts.UseMediaKeys);
        if (!before.OrderBy(pair => pair.Key).SequenceEqual(problems.OrderBy(pair => pair.Key)))
        {
            ProblemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Release(ShortcutAction action)
    {
        if (claimed.Remove(action))
        {
            registrar.Unregister((int)action);
        }
    }

    private void ClaimMediaKeys(bool wanted)
    {
        if (wanted == mediaKeysClaimed)
        {
            return;
        }

        mediaKeysClaimed = wanted;
        for (var index = 0; index < MediaKeys.Count; index++)
        {
            if (wanted)
            {
                _ = registrar.Register(MediaKeyBase + index, HotKeyModifiers.None, MediaKeys[index].VirtualKey);
            }
            else
            {
                registrar.Unregister(MediaKeyBase + index);
            }
        }
    }

    private void OnPressed(int id)
    {
        // Only what Flint holds: anything else is not Flint's to act on.
        if (mediaKeysClaimed && id >= MediaKeyBase && id - MediaKeyBase < MediaKeys.Count)
        {
            Run(MediaKeys[id - MediaKeyBase].Action);
        }
        else if (claimed.ContainsKey((ShortcutAction)id))
        {
            Run((ShortcutAction)id);
        }
    }

    private void Run(ShortcutAction action)
    {
        // A shortcut that cannot do anything now does nothing, and says nothing: no beep, no window.
        var done = run(action);
        FlintDiag.Info("FlintShortcuts", $"{action} {(done ? "ran" : "had nothing to do")}");
    }
}
