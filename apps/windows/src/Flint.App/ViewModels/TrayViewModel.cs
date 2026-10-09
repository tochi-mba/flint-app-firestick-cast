using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels;

/// <summary>How the tray icon looks.</summary>
public enum TrayState
{
    /// <summary>Not connected to a TV.</summary>
    Idle = 0,

    /// <summary>Connected, and not sharing.</summary>
    Connected = 1,

    /// <summary>Sharing this screen.</summary>
    Sharing = 2,

    /// <summary>Sharing, paused.</summary>
    Paused = 3,
}

/// <summary>One line of the tray menu.</summary>
/// <param name="Label">What it says.</param>
/// <param name="Command">What choosing it does, or null for a line that only says something.</param>
public sealed record TrayMenuItem(string Label, ICommand? Command)
{
    /// <summary>Whether the line can be chosen.</summary>
    public bool IsEnabled => Command?.CanExecute(null) ?? false;
}

/// <summary>The tray icon's look, its tooltip and its menu, worked out from what Flint is doing.</summary>
/// <remarks>
/// All the deciding is here, so it is tested without a real notification area; the host only
/// shows what this says.
/// </remarks>
public sealed partial class TrayViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel shell;
    private readonly TimeProvider time;
    private DateTimeOffset? lastClick;

    /// <summary>Builds the tray over the shell.</summary>
    public TrayViewModel(MainWindowViewModel shell, TimeProvider? time = null)
    {
        this.shell = shell ?? throw new ArgumentNullException(nameof(shell));
        this.time = time ?? TimeProvider.System;
        shell.Cast.PropertyChanged += OnChanged;
        shell.Media.NowPlaying.PropertyChanged += OnChanged;
        Refresh();
    }

    /// <summary>How long two clicks can be apart and still be a double click.</summary>
    internal static TimeSpan DoubleClick { get; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How the icon looks.</summary>
    [ObservableProperty]
    private TrayState _state;

    /// <summary>What the icon says when pointed at.</summary>
    [ObservableProperty]
    private string _tooltip = string.Empty;

    /// <summary>The menu, top to bottom.</summary>
    [ObservableProperty]
    private IReadOnlyList<TrayMenuItem> _menu = [];

    /// <summary>A click on the icon: opens Flint on one click or two, as the settings say.</summary>
    public void OnClicked()
    {
        var now = time.GetUtcNow();
        var opens = shell.SettingsService.Current.Tray.SingleClickOpens
            || (lastClick is { } before && now - before <= DoubleClick);
        lastClick = opens ? null : now;
        if (opens)
        {
            shell.ShowWindow();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        shell.Cast.PropertyChanged -= OnChanged;
        shell.Media.NowPlaying.PropertyChanged -= OnChanged;
    }

    [RelayCommand]
    private void Open() => shell.ShowWindow();

    [RelayCommand]
    private void OpenSettings()
    {
        shell.Selected = shell.Destinations.Single(destination => destination.Label == "Settings");
        shell.ShowWindow();
    }

    [RelayCommand]
    private Task QuitAsync() => shell.QuitAsync();

    /// <summary>What the tray shows depends on these alone; the rest change too often to follow.</summary>
    private static readonly HashSet<string> Followed =
    [
        nameof(CastPageViewModel.IsMirroring),
        nameof(CastPageViewModel.MirrorPause),
        nameof(CastPageViewModel.IsSessionConnected),
        nameof(CastPageViewModel.IsConnected),
        nameof(CastPageViewModel.IsReconnecting),
        nameof(CastPageViewModel.Report),
        nameof(NowPlayingViewModel.IsActive),
        nameof(NowPlayingViewModel.PlayPauseName),
    ];

    private void OnChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is null or "" || Followed.Contains(change.PropertyName))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var cast = shell.Cast;
        var playing = shell.Media.NowPlaying;
        var tv = cast.TvInSentence;
        State = cast switch
        {
            { IsMirrorPaused: true } => TrayState.Paused,
            { IsMirroring: true } => TrayState.Sharing,
            { IsSessionConnected: true } => TrayState.Connected,
            _ => TrayState.Idle,
        };
        var status = cast switch
        {
            { IsMirrorPaused: true } => $"Sharing to {tv}, paused",
            { IsMirroring: true } => $"Sharing your screen to {tv}",
            { IsReconnecting: true } => $"Reconnecting to {tv}",
            { IsSessionConnected: true } when playing.IsActive => $"Playing on {tv}",
            { IsSessionConnected: true } => $"Connected to {tv}",
            _ => "Not connected to a TV",
        };
        Tooltip = $"Flint: {char.ToLowerInvariant(status[0])}{status[1..]}";

        var items = new List<TrayMenuItem>
        {
            new(status, null),
            new("Open Flint", OpenCommand),
            cast.IsMirroring
                ? new("Stop sharing", cast.StopScreenSessionCommand)
                : new("Share screen", shell.Screen.ShareCommand),
        };
        if (cast.IsMirroring)
        {
            items.Add(cast.IsMirrorPaused
                ? new("Resume sharing", shell.Screen.ResumeCommand)
                : new("Pause sharing", shell.Screen.PauseCommand));
        }

        if (playing.IsActive)
        {
            items.Add(new(playing.PlayPauseName, playing.PlayPauseCommand));
            items.Add(new("Next", shell.Media.Queue.NextCommand));
        }

        if (cast.IsSessionConnected)
        {
            items.Add(new("Disconnect", cast.DisconnectCommand));
        }

        items.Add(new("Settings", OpenSettingsCommand));
        items.Add(new("Quit Flint", QuitCommand));
        if (!items.SequenceEqual(Menu))
        {
            Menu = items;
        }
    }
}
