using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;
using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>Starting a share: asking which display when the settings say to, then counting down.</summary>
public sealed partial class ScreenPageViewModel
{
    private CancellationTokenSource? countdown;

    /// <summary>Whether the small display chooser is open, as "ask each time" opens it.</summary>
    [ObservableProperty]
    private bool _isChoosingDisplay;

    /// <summary>Whether sharing is counting down to its start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShare))]
    [NotifyCanExecuteChangedFor(nameof(ShareCommand))]
    private bool _isCountingDown;

    /// <summary>The countdown, such as "SHARING IN 3…".</summary>
    [ObservableProperty]
    private string? _countdownText;

    /// <summary>Whether Share is offered: this PC can mirror to this TV and nothing is starting or running.</summary>
    /// <remarks>
    /// Offered before a TV is paired, as it always was: pressing it then says to pair first, which
    /// tells the person more than a button that does nothing.
    /// </remarks>
    public bool CanShare => Cast.MirrorVerdict is { IsOfferable: true } && !Cast.IsMirroring && !IsCountingDown;

    /// <summary>Shares the chosen display, asking first or counting down as the settings say.</summary>
    [RelayCommand(CanExecute = nameof(CanShare))]
    private Task ShareAsync()
    {
        if (Screen.DisplayPrompt is ShareDisplayPrompt.AskEveryTime && HasSeveralDisplays)
        {
            IsChoosingDisplay = true;
            return Task.CompletedTask;
        }

        return CountDownThenShareAsync();
    }

    /// <summary>Shares the display picked in the chooser.</summary>
    [RelayCommand]
    private Task ShareDisplayAsync(DisplayInfo? display)
    {
        IsChoosingDisplay = false;
        if (display is null)
        {
            return Task.CompletedTask;
        }

        Choose(display);
        return CountDownThenShareAsync();
    }

    /// <summary>Closes the chooser without sharing.</summary>
    [RelayCommand]
    private void CancelChoosing() => IsChoosingDisplay = false;

    /// <summary>Stops the countdown; nothing is shared.</summary>
    [RelayCommand]
    private void CancelCountdown() => countdown?.Cancel();

    private async Task CountDownThenShareAsync()
    {
        var seconds = Screen.CountdownSeconds;
        if (seconds > 0)
        {
            using var counting = new CancellationTokenSource();
            countdown = counting;
            IsCountingDown = true;
            try
            {
                for (var left = seconds; left > 0; left--)
                {
                    CountdownText = $"SHARING IN {left.ToString(CultureInfo.InvariantCulture)}…";
                    await Task.Delay(TimeSpan.FromSeconds(1), time, counting.Token).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                countdown = null;
                CountdownText = null;
                IsCountingDown = false;
            }
        }

        await Cast.StartMirrorAsync(ShareOptions()).ConfigureAwait(true);
    }
}
