using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>
/// Noticing when the TV goes away, and letting the person end the connection themselves.
/// </summary>
/// <remarks>
/// Before this, the page kept saying "Connected" after the TV had closed or the Wi-Fi had dropped,
/// until the next thing the person tried failed with an error that did not say why.
/// </remarks>
public sealed partial class CastPageViewModel
{
    /// <summary>Whether a disconnect is under way.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDisconnect))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private bool _isDisconnecting;

    /// <summary>Whether the Disconnect button is offered.</summary>
    public bool CanDisconnect => IsConnected && !IsDisconnecting;

    /// <summary>The TV's name for a sentence that starts with it.</summary>
    private string TvAtStart => Report?.Device?.FriendlyName is { Length: > 0 } name ? name : "The TV";

    /// <summary>The TV's name for the middle of a sentence.</summary>
    internal string TvInSentence => Report?.Device?.FriendlyName is { Length: > 0 } name ? name : "the TV";

    /// <summary>What to tell the person when a session ended, by why it ended.</summary>
    internal string DescribeEnd(CastSessionEnd reason) => reason switch
    {
        CastSessionEnd.EndedByTv => $"{TvAtStart} closed the connection.",
        CastSessionEnd.ConnectionLost => $"The connection to {TvInSentence} was lost.",
        CastSessionEnd.ProtocolError => $"{TvAtStart} sent something Flint could not read, so the connection was closed.",
        _ => $"Disconnected from {TvInSentence}.",
    };

    /// <summary>Starts watching a session that has just been adopted.</summary>
    /// <remarks>
    /// Called on the UI thread, so the wait resumes there and the page's state changes where its
    /// bindings read it.
    /// </remarks>
    private async Task WatchAsync(CastSession watched)
    {
        var closed = await watched.WhenClosed.ConfigureAwait(true);
        await OnSessionEndedAsync(watched, closed).ConfigureAwait(true);
    }

    /// <summary>Puts the page back to "TV found" when the session it was using has ended.</summary>
    /// <remarks>
    /// A session this page has already let go of, because it was replaced or disconnected here,
    /// changes nothing: its end was expected.
    /// </remarks>
    internal async Task OnSessionEndedAsync(CastSession ended, CastSessionClosed closed)
    {
        if (!ReferenceEquals(session, ended))
        {
            return;
        }

        FlintDiag.Info("FlintCast", $"session ended reason={closed.Reason}");
        session = null;
        var wasPlaying = IsMediaPlaying;
        NoteWhatWasOnTheTv(IsMirroring, wasPlaying);
        IsConnected = false;
        IsMediaPlaying = false;
        coordinator?.Prompt?.Dismiss();
        await StopMirrorAsync().ConfigureAwait(true);
        await ended.DisposeAsync().ConfigureAwait(true);

        // Said once, in words, where each page shows its state; the error a failing send left
        // behind would only say that something failed, not that the TV went away. The Cast
        // page's line is written last, so whoever sees it can rely on everything else being set.
        var told = DescribeEnd(closed.Reason);
        NowPlaying.ConnectionLost(told);
        Failure = null;
        MirrorStatus = told;
        if (wasPlaying)
        {
            MediaStatus = told;
            OnPropertyChanged(nameof(MediaStatus));
        }

        PairingStatus = told;
        RaiseDerived();
        OnPropertyChanged(nameof(CanDisconnect));
        DisconnectCommand.NotifyCanExecuteChanged();

        // The TV went away by itself: reach it again, as the settings say, without a code. Not
        // waited on here: trying can take minutes, and it says what it is doing in its own banner.
        ReconnectTask = KeepTryingAsync();
    }

    /// <summary>Ends the connection to the TV: stops sharing first, then says goodbye.</summary>
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        if (session is not { } ending)
        {
            return;
        }

        IsDisconnecting = true;
        try
        {
            FlintDiag.Info("FlintCast", "disconnect requested");
            coordinator?.Prompt?.Dismiss();

            // The person ended it: nothing reconnects until they connect again.
            reconnectSuppressed = true;
            CancelReconnect();
            await StopMirrorAsync().ConfigureAwait(true);

            // Let go before saying goodbye, so the end the session reports is not mistaken for
            // the TV leaving.
            session = null;
            IsConnected = false;
            IsMediaPlaying = false;
            await ending.DisconnectAsync().ConfigureAwait(true);
            PairingStatus = DescribeEnd(CastSessionEnd.ClosedByThisPc);
            NowPlaying.ConnectionLost(PairingStatus);
            Failure = null;
        }
        finally
        {
            IsDisconnecting = false;
            RaiseDerived();
            OnPropertyChanged(nameof(CanDisconnect));
        }
    }
}
