using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>One highlight of what has changed, and the id it is remembered by once seen.</summary>
/// <param name="Id">Stable for as long as the highlight exists. Never reused for something else.</param>
/// <param name="Step">What the walkthrough shows for it.</param>
public sealed record WhatsNewHighlight(string Id, OnboardingStep Step);

/// <summary>
/// "What's new": shown once after an update, and only for highlights this person has not seen.
/// </summary>
/// <remarks>
/// <para>
/// It never shows on a first launch, because the introduction is the right start there and every
/// highlight is new to someone who has never used Flint. When the introduction finishes, the shell
/// marks the current highlights seen, so only ones added later ever appear.
/// </para>
/// <para>
/// Finishing or skipping marks every highlight seen, so it is not shown again until there is
/// something newer. It can be opened again from Settings, About, at any time.
/// </para>
/// </remarks>
public sealed class WhatsNewViewModel : WalkthroughViewModel
{
    private readonly IWhatsNewState state;

    /// <summary>Works out whether there is anything new to show, and shows it if there is.</summary>
    /// <param name="state">Which highlights have been seen.</param>
    /// <param name="introductionDone">Whether the first-run introduction has been finished.</param>
    /// <param name="catalogue">Every highlight, oldest first. Defaults to Flint's own.</param>
    public WhatsNewViewModel(IWhatsNewState state, bool introductionDone, IReadOnlyList<WhatsNewHighlight>? catalogue = null)
        : base(StepsOf(catalogue ?? Catalogue))
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        Highlights = catalogue ?? Catalogue;
        var unseen = WhatsNewPolicy.Unseen([.. Highlights.Select(highlight => highlight.Id)], state.Seen, introductionDone);
        if (unseen.Count > 0)
        {
            SetSteps([.. Highlights.Where(highlight => unseen.Contains(highlight.Id)).Select(highlight => highlight.Step)]);
            IsVisible = true;
        }
    }

    /// <summary>Every highlight, oldest first.</summary>
    public IReadOnlyList<WhatsNewHighlight> Highlights { get; }

    /// <inheritdoc />
    protected override string FinishLabel => "GOT IT";

    /// <summary>Records every current highlight as seen without showing them, for a first launch.</summary>
    public void MarkAllSeen() => state.MarkSeen(Highlights.Select(highlight => highlight.Id));

    /// <summary>Shows every highlight again, from the first, whether or not it has been seen.</summary>
    public void ShowAll()
    {
        SetSteps(StepsOf(Highlights));
        IsVisible = true;
    }

    /// <inheritdoc />
    protected override void Finish()
    {
        MarkAllSeen();
        IsVisible = false;
    }

    private static List<OnboardingStep> StepsOf(IReadOnlyList<WhatsNewHighlight> highlights) =>
        [.. highlights.Select(highlight => highlight.Step)];

    /// <summary>
    /// What has changed, oldest first. Add new highlights at the end; never change an existing id.
    /// </summary>
    /// <remarks>
    /// Only changes a person would notice or want to use belong here, said in the words the app
    /// itself uses. Fixes and internal work do not, so an update with nothing to show shows nothing.
    /// </remarks>
    public static IReadOnlyList<WhatsNewHighlight> Catalogue { get; } =
    [
        new(
            "2026-10-ask-before-switching",
            new OnboardingStep(
                "New in Flint",
                "Flint asks before it changes what the TV shows",
                "Opening the browser, sharing your screen or playing a file while the TV shows something "
                    + "else now asks you first, and Keep leaves the TV alone.",
                [
                    "Pages that are not on the TV say what is, with a button to switch.",
                    "Turn the question off in Settings, General, if you would rather it just switched.",
                ])),
        new(
            "2026-10-settings",
            new OnboardingStep(
                "New in Flint",
                "A Settings page you can search",
                "Settings now has sections for General, Media, TVs, Privacy and data, Updates and About, "
                    + "and a search box that finds any setting by name or by what it does.",
                [
                    "Make the whole window larger or smaller, and keep this PC awake while casting.",
                    "Export your settings to a file, or bring them over from another PC.",
                    "Every change applies at once; each section can be reset on its own.",
                ])),
        new(
            "2026-10-playback-controls",
            new OnboardingStep(
                "New in Flint",
                "Control what plays on the TV from here",
                "The Media page has a card for whatever is playing: play and pause, a seek bar, skip "
                    + "back and forward, and the TV's volume.",
                [
                    "On the Media page: Space plays or pauses, Left and Right skip, Up and Down change the volume.",
                    "Choose how far the skip buttons go in Settings, Media.",
                    "If the TV loses its connection, the page says so instead of pretending it is still connected.",
                ])),
        new(
            "2026-10-queue",
            new OnboardingStep(
                "New in Flint",
                "Queue up files, and carry on where you stopped",
                "Drop files or whole folders anywhere on this window, or choose several at once. They "
                    + "play one after another, and a film you stopped part way offers to resume.",
                [
                    "Arrange Up next on the Media page, with shuffle and repeat.",
                    "The queue never replaces something else on the TV; it waits and tells you.",
                    "Choose what happens next, how long pictures show, and whether to remember positions, in Settings, Media.",
                ])),
        new(
            "2026-10-reconnect",
            new OnboardingStep(
                "New in Flint",
                "Flint reaches your TV again by itself",
                "Once you have paired, Flint reconnects without the TV's code: when Flint starts, and "
                    + "when the connection drops.",
                [
                    "If something was playing or being shared, Flint offers to carry on.",
                    "Choose how long it keeps trying, or turn it off, in Settings, General.",
                    "Settings, TVs lists the TVs Flint remembers, and forgets them when you ask.",
                ])),
        new(
            "2026-10-screen-sharing",
            new OnboardingStep(
                "New in Flint",
                "Choose which display to share, and how",
                "The Screen page shows your displays as Windows arranges them, and picture modes for "
                    + "everyday use, films, games, slides and weak Wi-Fi.",
                [
                    "Change the display or the picture while sharing; the TV keeps showing your screen.",
                    "Custom sets the size, frame rate and data rate yourself.",
                    "Turn on live numbers to see what is being sent.",
                ])),
        new(
            "2026-10-sound",
            new OnboardingStep(
                "New in Flint",
                "Your PC's sound plays on the TV with your screen",
                "Sharing your screen now carries what this PC plays, so films, music and calls are heard "
                    + "on the TV too.",
                [
                    "Choose the TV only, and Flint mutes this PC while sharing and puts it back after.",
                    "Pausing the share pauses the sound with it.",
                    "If lips and voices do not line up, add a sound delay in Settings, Screen sharing.",
                ])),
    ];
}
