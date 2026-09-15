using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;

namespace Flint.App.ViewModels;

/// <summary>Explicit, replayable browser help. It never opens automatically or captures input.</summary>
public sealed partial class BrowserHelpViewModel : ObservableObject
{
    private readonly FileOnboardingState? persistence;
    private readonly Action remember;
    [ObservableProperty] private bool isExpanded;
    private int index;
    [ObservableProperty] private bool dismissed;

    /// <summary>Creates per-PC help; tests can supply isolated completion callbacks.</summary>
    public BrowserHelpViewModel(bool? alreadyDismissed = null, Action? rememberDismissal = null)
    {
        if (alreadyDismissed is null)
            persistence = new FileOnboardingState(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                FileOnboardingState.VendorFolder, FileOnboardingState.ProductFolder, "help", "browser-v1"));
        dismissed = alreadyDismissed ?? persistence!.HasCompleted;
        remember = rememberDismissal ?? (() => persistence?.MarkCompleted());
    }

    /// <summary>Short invitation, without an interrupting popup.</summary>
    public string Label => Dismissed ? "WEB HELP" : "NEW HERE? WEB CONTROLS";
    /// <summary>Current topic; invalid external assignments cannot corrupt the help sequence.</summary>
    public int Index
    {
        get => index;
        set
        {
            if (SetProperty(ref index, Math.Clamp(value, 0, Topics.Length - 1)))
                OnIndexChanged();
        }
    }
    /// <summary>Current topic heading.</summary>
    public string Title => Topics[Index].Title;
    /// <summary>Current topic instructions.</summary>
    public string Body => Topics[Index].Body;
    /// <summary>Position in the explicit help sequence.</summary>
    public string Progress => $"{Index + 1} / {Topics.Length}";
    /// <summary>Whether previous topic exists.</summary>
    public bool CanGoBack => Index > 0;
    /// <summary>Next-step label.</summary>
    public string NextLabel => Index == Topics.Length - 1 ? "GOT IT" : "NEXT";

    /// <summary>Opens or collapses help without changing stored completion.</summary>
    [RelayCommand] private void Toggle() => IsExpanded = !IsExpanded;
    /// <summary>Returns to the previous topic without wrapping.</summary>
    [RelayCommand] private void Back() { if (CanGoBack) Index--; }
    /// <summary>Advances or explicitly completes help.</summary>
    [RelayCommand] private void Next() { if (Index < Topics.Length - 1) Index++; else Dismiss(); }
    /// <summary>Remembers an explicit dismissal; help remains replayable.</summary>
    [RelayCommand] private void Dismiss()
    {
        if (!Dismissed) remember();
        Dismissed = true;
        IsExpanded = false;
        Index = 0;
    }
    partial void OnDismissedChanged(bool value) => OnPropertyChanged(nameof(Label));
    private void OnIndexChanged()
    {
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(CanGoBack)); OnPropertyChanged(nameof(NextLabel));
    }

    private static readonly (string Title, string Body)[] Topics =
    [
        ("One TV, two controls", "Pair in Cast, then open Web. A previously trusted TV reconnects securely when its browser endpoint and pairing code are available. If its security identity changes, stop and verify it on the TV before trusting it again."),
        ("Choose where your profile lives", "TV profiles save bookmarks, history and workspace pages on the television and work without Windows. A connected-device profile is temporary on the TV. TV site sign-ins are currently shared; profile names do not create separate cookie jars."),
        ("Tabs vs split view", "Tabs switch one page at a time in a strip. Split View opens a second independent page beside the first — that mosaic is not the same as tabs. Stack puts one page above the other. Advanced layout chips stay disabled until the right number of pages are open."),
        ("Independent pages", "Each workspace pane is its own webpage. Click a pane in the Windows preview to select it and send the click there — you do not have to focus it first. Page fullscreen fills its pane. Available layouts and live-page limits depend on the receiver; suspended pages may reload when restored."),
        ("Input and media", "Enter page interaction to control the focused page; return to workspace controls to select another pane. Play/Pause sends a request to the page, not proof that video is playing. Mute is confirmed only when the renderer supports and applies it."),
        ("VPN and recovery", "VPN settings belong to a TV profile. Auto-connect is on by default when you enable VPN; Android may ask for permission on the TV. A profile requiring VPN must wait before browsing. Never assume a connection failure protects traffic; check the displayed network state."),
        ("Free WireGuard config", "Flint does not run a free VPN service. For $0 egress, create a free-tier VPS (for example Oracle Cloud Always Free), install WireGuard, open UDP 51820, and export a peer .conf with AllowedIPs = 0.0.0.0/0. Paste it under Web → MORE → TV NETWORK / VPN, enable, save, then approve the TV VPN prompt. Help can be opened again at any time."),
    ];
}
