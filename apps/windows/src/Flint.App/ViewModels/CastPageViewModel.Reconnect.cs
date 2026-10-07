using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>
/// Reaching a TV again without its code: when Flint starts, and when the connection drops.
/// </summary>
/// <remarks>
/// <para>
/// At pairing the TV grants a login, which it accepts in place of the code until its own app
/// restarts or someone presses New code on it. Flint keeps it, protected for this Windows account,
/// and sends it only to a TV that greets with the name it was granted by.
/// </para>
/// <para>
/// A screen share is never resumed silently: even "carry on" counts down where it can be seen and
/// cancelled first.
/// </para>
/// </remarks>
public sealed partial class CastPageViewModel
{
    /// <summary>How long a share that carries on by itself waits, visibly, before it starts.</summary>
    internal static readonly TimeSpan ShareCountdown = TimeSpan.FromSeconds(3);

    private IKnownTvStore knownTvs = new InMemoryKnownTvStore();
    private ISettingsService? settings;

    /// <summary>Set when the person disconnected, so nothing reconnects until they connect again.</summary>
    private bool reconnectSuppressed;

    private CancellationTokenSource? reconnecting;

    /// <summary>What was on the TV from this PC when the connection dropped, to offer again after.</summary>
    private ResumeAfterDrop? resumeAfterDrop;

    /// <summary>What the reconnect banner says, or null when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReconnectBanner))]
    private string? _reconnectBanner;

    /// <summary>Whether Flint is trying to reach the TV again, which can be cancelled.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelReconnectCommand))]
    private bool _isReconnecting;

    /// <summary>The offer's button, such as SHARE AGAIN or PLAY, or null when nothing is offered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReconnectOffer))]
    private string? _reconnectOfferAction;

    /// <summary>Whether the reconnect banner is showing.</summary>
    public bool HasReconnectBanner => ReconnectBanner is not null;

    /// <summary>Whether the banner offers to carry on with what was on the TV.</summary>
    public bool HasReconnectOffer => ReconnectOfferAction is not null;

    /// <summary>The TVs Flint can reach again, most recently used first.</summary>
    public IReadOnlyList<KnownTv> KnownTvs => knownTvs.Load();

    /// <summary>Gives the page its remembered TVs and the settings that say when to reconnect.</summary>
    internal void UseReconnect(IKnownTvStore store, ISettingsService liveSettings)
    {
        knownTvs = store ?? throw new ArgumentNullException(nameof(store));
        settings = liveSettings ?? throw new ArgumentNullException(nameof(liveSettings));
    }

    /// <summary>Forgets one TV: its login and its address.</summary>
    internal void ForgetTv(string name)
    {
        knownTvs.Forget(name);
        OnPropertyChanged(nameof(KnownTvs));
    }

    /// <summary>Forgets every TV.</summary>
    internal void ForgetAllTvs()
    {
        knownTvs.ForgetAll();
        OnPropertyChanged(nameof(KnownTvs));
    }

    /// <summary>The settings in force; read only once <see cref="CanReconnect"/> has said there are some.</summary>
    private GeneralSettings General => settings!.Current.General;

    /// <summary>
    /// Whether reconnecting is wired up at all: only in the shell, which hands over the settings. A
    /// page built on its own, in a test or a design-time view, never reconnects by itself.
    /// </summary>
    private bool CanReconnect => settings is not null;

    /// <summary>The attempt to reconnect after the last drop, for tests to wait on.</summary>
    internal Task ReconnectTask { get; private set; } = Task.CompletedTask;

    /// <summary>Keeps the TV a session was just made with, and its login when it gave one.</summary>
    private void RememberTv(FireTvDevice device, CastSession paired, int port)
    {
        // A session is only made after the TV's greeting, so its first message always is one. A TV
        // that gives no name cannot be checked before a login is sent, so none is kept for it.
        var name = ((Flint.Protocol.HelloMessage)paired.PeerHello.Message).DeviceName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var kept = knownTvs.Load().FirstOrDefault(tv => tv.Name == name);
        knownTvs.Save(new KnownTv(
            name,
            device.Address.ToString(),
            port,
            paired.GrantedToken ?? kept?.Token,
            time.GetUtcNow()));
        OnPropertyChanged(nameof(KnownTvs));
    }

    /// <summary>
    /// Reaches the most recently used TV that has a login, as Flint starts, when the setting is on.
    /// </summary>
    /// <returns>Whether a session was made.</returns>
    internal async Task<bool> ReconnectOnStartAsync(CancellationToken cancellationToken = default)
    {
        if (!CanReconnect || !General.ReconnectOnStart || knownTvs.Load().FirstOrDefault(tv => tv.HasLogin) is not { } tv)
        {
            return false;
        }

        ReconnectBanner = $"Reconnecting to {tv.Name}...";
        using var attempt = StartReconnecting(cancellationToken);
        try
        {
            // Where it was last; then wherever the network says a TV is, because a router can hand
            // the TV a new address. The greeting's name keeps the login from going anywhere else.
            ManualAddress = tv.Address;
            ManualPort = string.Empty;
            await ProbeAddressCommand.ExecuteAsync(null).ConfigureAwait(true);
            if (await TryLoginAsync(tv, attempt.Token).ConfigureAwait(true) is { } done)
            {
                return done;
            }

            await RunProbeAsync(prober.ProbeAsync, attempt.Token).ConfigureAwait(true);
            if (await TryLoginAsync(tv, attempt.Token).ConfigureAwait(true) is { } found)
            {
                return found;
            }

            ReconnectBanner = null;
            return false;
        }
        catch (OperationCanceledException)
        {
            ClearBannerIfCurrent(attempt);
            return false;
        }
        finally
        {
            DoneReconnecting(attempt);
        }
    }

    /// <summary>
    /// Logs in to the TV the page has found, with <paramref name="tv"/>'s login.
    /// </summary>
    /// <returns>
    /// True when connected, false when the TV refused the login, and null when no TV answered there
    /// as <paramref name="tv"/>, so the caller may look elsewhere.
    /// </returns>
    private async Task<bool?> TryLoginAsync(KnownTv tv, CancellationToken cancellationToken)
    {
        if (Report?.Device is not { } device || tv.Token is not { } token)
        {
            return null;
        }

        try
        {
            var next = await CastSession.ConnectWithTokenAsync(
                device.Address, tv.ReceiverPort, token, tv.Name, cancellationToken: cancellationToken).ConfigureAwait(true);
            ReconnectBanner = null;
            Failure = null;
            await AdoptSessionAsync(device, next, tv.ReceiverPort, $"Connected to {tv.Name} again.", cancellationToken)
                .ConfigureAwait(true);
            FlintDiag.Info("FlintCast", "reconnect ok");
            return true;
        }
        catch (CastAuthenticationRejectedException)
        {
            await NeedNewCodeAsync(tv, device, cancellationToken).ConfigureAwait(true);
            return false;
        }
        catch (Exception exception) when (exception is CastTvMismatchException or IOException
            or System.Net.Sockets.SocketException or TimeoutException or Flint.Protocol.WireFormatException)
        {
            FlintDiag.Info("FlintCast", $"reconnect not here: {exception.GetType().Name}");
            return null;
        }
    }

    /// <summary>The TV no longer takes the login: forget it, and ask for a code once.</summary>
    private async Task NeedNewCodeAsync(KnownTv tv, FireTvDevice device, CancellationToken cancellationToken)
    {
        FlintDiag.Info("FlintCast", "reconnect login refused");
        knownTvs.Save(tv with { Token = null });
        OnPropertyChanged(nameof(KnownTvs));
        ReconnectBanner = $"{tv.Name} needs a new code. Its app restarted, or someone asked it for a new one.";
        PairingStatus = "Enter the six-digit code shown on the TV, then pair.";
        if (General.OpenReceiverForNewCode && device.AdbState is AdbConnectionState.Connected)
        {
            try
            {
                await OpenReceiverCoreAsync(device, cancellationToken).ConfigureAwait(true);
                PairingStatus = "The receiver is open on the TV. Enter the six-digit code shown there, then pair.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The code is still on the TV's own screen once its app is opened by hand.
                PairingStatus = "Open Flint on the TV, enter the six-digit code shown there, then pair.";
                FlintDiag.Warn("FlintCast", $"reconnect could not open receiver: {exception.GetType().Name}");
            }
        }

        RaiseDerived();
    }

    /// <summary>
    /// After the TV went away by itself, tries again on <see cref="ReconnectSchedule"/> until the
    /// setting's time is up, the person cancels, or a session is made.
    /// </summary>
    internal async Task KeepTryingAsync(CancellationToken cancellationToken = default)
    {
        if (!CanReconnect || !General.ReconnectAfterDrop || reconnectSuppressed
            || Report?.Device is not { } device
            || knownTvs.Load().FirstOrDefault(tv => tv.HasLogin && tv.Address == device.Address.ToString()) is not { } tv)
        {
            resumeAfterDrop = null;
            return;
        }

        using var attempt = StartReconnecting(cancellationToken);
        var started = time.GetUtcNow();
        var window = TimeSpan.FromSeconds(General.ReconnectSeconds);
        try
        {
            for (var tryNumber = 1; time.GetUtcNow() - started < window; tryNumber++)
            {
                var wait = ReconnectSchedule.DelayBefore(tryNumber);
                ReconnectBanner = $"Reconnecting to {tv.Name}. Trying again in {wait.TotalSeconds:0} s.";
                await Task.Delay(wait, time, attempt.Token).ConfigureAwait(true);
                ReconnectBanner = $"Reconnecting to {tv.Name}...";
                switch (await TryLoginAsync(tv, attempt.Token).ConfigureAwait(true))
                {
                    case true:
                        DoneReconnecting(attempt);
                        await OfferWhatWasOnTheTvAsync().ConfigureAwait(true);
                        return;
                    case false:
                        resumeAfterDrop = null;
                        return;
                }
            }

            ReconnectBanner = $"Could not reach {tv.Name} again. Connect on the Cast page when it is back.";
            resumeAfterDrop = null;
        }
        catch (OperationCanceledException)
        {
            ClearBannerIfCurrent(attempt);
            resumeAfterDrop = null;
        }
        catch (Exception exception)
        {
            // Nothing waits on this attempt, so it must not end the app: it says it stopped instead.
            FlintDiag.Warn("FlintCast", $"reconnect stopped: {exception.GetType().Name}");
            ReconnectBanner = $"Reconnecting to {tv.Name} stopped. Connect on the Cast page to try again.";
            resumeAfterDrop = null;
        }
        finally
        {
            DoneReconnecting(attempt);
        }
    }

    /// <summary>Stops trying to reconnect, and stops a share that was counting down to start.</summary>
    [RelayCommand(CanExecute = nameof(IsReconnecting))]
    private void CancelReconnect()
    {
        reconnecting?.Cancel();
        ReconnectBanner = null;
        ReconnectOfferAction = null;
    }

    /// <summary>Carries on with what was on the TV before the connection dropped.</summary>
    [RelayCommand]
    private Task AcceptReconnectOfferAsync()
    {
        var resume = resumeAfterDrop;
        ReconnectOfferAction = null;
        ReconnectBanner = null;
        resumeAfterDrop = null;
        return resume is null ? Task.CompletedTask : ResumeAsync(resume, MediaTakeover.Ask);
    }

    /// <summary>Leaves the TV as it is after reconnecting.</summary>
    [RelayCommand]
    private void DismissReconnectOffer()
    {
        ReconnectOfferAction = null;
        ReconnectBanner = null;
        resumeAfterDrop = null;
    }

    /// <summary>Notes what this PC had on the TV as the connection drops, before the page lets go of it.</summary>
    internal void NoteWhatWasOnTheTv(bool wasMirroring, bool wasPlaying)
    {
        resumeAfterDrop = wasMirroring ? new ResumeAfterDrop(null, 0)
            : wasPlaying && lastMediaPath is { } path ? new ResumeAfterDrop(path, NowPlaying.PositionMs)
            : null;
    }

    /// <summary>Once reconnected, does what the setting says with what was on the TV.</summary>
    internal async Task OfferWhatWasOnTheTvAsync()
    {
        if (resumeAfterDrop is not { } resume)
        {
            return;
        }

        switch (General.AfterReconnect)
        {
            case ReconnectOutcome.CarryOn:
                resumeAfterDrop = null;
                await ResumeAsync(resume, MediaTakeover.OnlyIfFree).ConfigureAwait(true);
                break;
            case ReconnectOutcome.Ask:
                ReconnectBanner = resume.Path is { } path
                    ? $"Connected again. Play {System.IO.Path.GetFileName(path)} from {PlaybackTimeText.Format(resume.PositionMs)}?"
                    : "Connected again. Share your screen again?";
                ReconnectOfferAction = resume.Path is null ? "SHARE AGAIN" : "PLAY";
                break;
            default:
                resumeAfterDrop = null;
                break;
        }
    }

    private async Task ResumeAsync(ResumeAfterDrop resume, MediaTakeover takeover)
    {
        if (resume.Path is { } path)
        {
            await PlayFileAsync(path, resume.PositionMs, takeover).ConfigureAwait(true);
            return;
        }

        // A share starts again only after a countdown anyone at the PC can see and stop.
        using var countdown = StartReconnecting(default);
        try
        {
            for (var left = (int)ShareCountdown.TotalSeconds; left > 0; left--)
            {
                ReconnectBanner = $"Sharing your screen again in {left}...";
                await Task.Delay(TimeSpan.FromSeconds(1), time, countdown.Token).ConfigureAwait(true);
            }

            ReconnectBanner = null;
            await StartScreenSessionCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ClearBannerIfCurrent(countdown);
        }
        finally
        {
            DoneReconnecting(countdown);
        }
    }

    private CancellationTokenSource StartReconnecting(CancellationToken cancellationToken)
    {
        reconnecting?.Cancel();
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        reconnecting = source;
        IsReconnecting = true;
        return source;
    }

    /// <summary>
    /// Clears the banner for an attempt that was cancelled, unless a newer attempt has already put
    /// its own words there.
    /// </summary>
    private void ClearBannerIfCurrent(CancellationTokenSource source)
    {
        if (ReferenceEquals(reconnecting, source))
        {
            ReconnectBanner = null;
        }
    }

    /// <summary>Lets go of <paramref name="source"/>, unless a newer attempt has already taken over.</summary>
    private void DoneReconnecting(CancellationTokenSource source)
    {
        if (ReferenceEquals(reconnecting, source))
        {
            reconnecting = null;
            IsReconnecting = false;
        }
    }

    /// <summary>What to offer again after reconnecting: a file and where it was, or the screen when <see cref="Path"/> is null.</summary>
    private sealed record ResumeAfterDrop(string? Path, long PositionMs);
}
