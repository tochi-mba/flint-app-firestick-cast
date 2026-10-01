using System.ComponentModel;
using Flint.App.ViewModels;
using Flint.Core.Settings;
using Flint.Platform.Windows;

namespace Flint.App.Services;

/// <summary>
/// Keeps this PC awake exactly while something from it is on the TV, when the person wants that.
/// </summary>
/// <remarks>
/// <para>
/// Sharing the screen keeps the display on too, because a display that has turned off gives the
/// capture nothing to send. A file playing on the TV needs only the PC and its connection, so the
/// display is left to turn off as usual.
/// </para>
/// <para>
/// Follows the Cast page's own state rather than being told, so no start or stop path can forget
/// it. Changes arrive on the UI thread, which is the thread Windows ties the request to.
/// </para>
/// </remarks>
public sealed class KeepAwakeCoordinator : IDisposable
{
    private readonly CastPageViewModel cast;
    private readonly ISettingsService settings;
    private readonly Func<KeepAwakeLevel, bool> apply;
    private bool disposed;

    /// <summary>Starts following the Cast page and the settings.</summary>
    /// <param name="cast">Whether a mirror or a file is on the TV.</param>
    /// <param name="settings">Whether the person wants the PC kept awake.</param>
    /// <param name="apply">Asks Windows for a level; false when it refused.</param>
    public KeepAwakeCoordinator(CastPageViewModel cast, ISettingsService settings, Func<KeepAwakeLevel, bool> apply)
    {
        this.cast = cast ?? throw new ArgumentNullException(nameof(cast));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
        cast.PropertyChanged += OnCastChanged;
        settings.Changed += OnSettingsChanged;
        Refresh();
    }

    /// <summary>The level last granted.</summary>
    public KeepAwakeLevel Level { get; private set; }

    /// <summary>The level wanted for a given state.</summary>
    internal static KeepAwakeLevel Decide(bool wanted, bool mirroring, bool mediaPlaying) =>
        !wanted ? KeepAwakeLevel.None
        : mirroring ? KeepAwakeLevel.SystemAndDisplay
        : mediaPlaying ? KeepAwakeLevel.System
        : KeepAwakeLevel.None;

    /// <summary>Stops following and lets the PC sleep normally.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cast.PropertyChanged -= OnCastChanged;
        settings.Changed -= OnSettingsChanged;
        Grant(KeepAwakeLevel.None);
    }

    private void OnCastChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(CastPageViewModel.IsMirroring) or nameof(CastPageViewModel.IsMediaPlaying))
        {
            Refresh();
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args) => Refresh();

    private void Refresh() =>
        Grant(Decide(settings.Current.General.KeepAwake, cast.IsMirroring, cast.IsMediaPlaying));

    private void Grant(KeepAwakeLevel level)
    {
        if (level == Level)
        {
            return;
        }

        if (apply(level))
        {
            Flint.Core.FlintDiag.Info("FlintSession", $"keep awake {Level} -> {level}");
            Level = level;
        }
        else
        {
            Flint.Core.FlintDiag.Warn("FlintSession", $"keep awake refused level={level}");
        }
    }
}
