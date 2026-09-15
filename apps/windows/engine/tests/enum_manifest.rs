//! A committed manifest of every browser enum value, shared across languages.
//!
//! The golden corpus proves that the *messages* three implementations agree on encode to the same
//! bytes. It cannot prove they agree on the values those messages may carry: a vector only exercises
//! the enum values it happens to use, and 67 of them appear in none. C# in particular decodes enums
//! reflectively, so a value added on one side works there and is rejected by the other two — on a
//! real device, at the moment someone presses the button.
//!
//! This manifest closes that gap without hand-writing a vector per value. Rust generates it, and all
//! three languages assert their own enums against it. Adding, renaming, or renumbering a value in
//! one language now fails the other two.
//!
//! # Regenerating
//!
//! ```text
//! cargo test --test enum_manifest -- --ignored regenerate
//! ```
//!
//! Regenerating is a deliberate act: the diff is the review surface for a protocol change.

use std::collections::BTreeMap;
use std::fs;
use std::path::PathBuf;

use flint_engine::wire::*;

/// Every browser enum, as `name -> sorted wire values`.
///
/// Keyed on the numbers, not the spellings. The three languages deliberately name the same value
/// differently — `SetUa` and `SetUserAgent`, `Tv` and `Television` — and only the number crosses the
/// wire. What must never differ is which numbers are legal and how many there are.
fn manifest() -> BTreeMap<String, Vec<u8>> {
    let mut all = BTreeMap::new();

    macro_rules! record {
        ($enum:ident, $($variant:ident),+ $(,)?) => {{
            let mut values: Vec<u8> = vec![$( $enum::$variant as u8 ),+];
            values.sort_unstable();
            all.insert(stringify!($enum).to_string(), values);
        }};
    }

    record!(
        BrowserCapabilityStatus,
        Available,
        SecureEndpointUnavailable,
        WebViewUnavailable,
        UnsupportedPlatform,
        DistributionRestricted
    );
    record!(
        BrowserCommandAction,
        Open,
        Navigate,
        Back,
        Forward,
        Reload,
        Stop,
        Close,
        SetPreviewEnabled,
        ClearData
    );
    record!(BrowserPointerAction, Down, Move, Up, Cancel);
    record!(
        BrowserSemanticKey,
        Select,
        Up,
        Down,
        Left,
        Right,
        Back,
        Tab,
        ShiftTab,
        Escape,
        PageUp,
        PageDown,
        Home,
        End,
        Refresh
    );
    record!(BrowserLoadState, Idle, Loading, Loaded, Failed, Closed);
    record!(BrowserPreviewState, Disabled, Enabled, Unavailable);
    record!(BrowserDialogType, Alert, Confirm, Prompt, BeforeUnload);
    record!(BrowserTabAction, New, Close, Select, Move, Duplicate);
    record!(
        BrowserViewAction,
        SetZoom,
        SetUa,
        SetDark,
        SetInputMode,
        SetFullscreen,
        FindStart,
        FindNext,
        FindPrev,
        FindClear,
        SetSearchEngine
    );
    record!(BrowserUserAgentMode, Tv, Desktop, Mobile);
    record!(BrowserDarkMode, FollowSystem, Light, Dark);
    record!(BrowserInteractionMode, Cursor, Focus);
    record!(BrowserSearchEngine, DuckDuckGo, Google, Bing, Custom);
    record!(
        BrowserLibraryAction,
        AddBookmark,
        RemoveBookmark,
        ClearHistory,
        ClearBookmarks,
        RequestSnapshot
    );
    record!(BrowserLibraryEntryKind, Bookmark, History);

    record!(BrowserNetworkAction, Set, Clear, RequestSnapshot);
    record!(BrowserVpnProvider, None, WireGuard);
    record!(
        BrowserVpnSessionState,
        Idle,
        NeedsConsent,
        Connecting,
        Connected,
        Failed,
        Unavailable
    );
    record!(
        BrowserProfileAction,
        SelectTvProfile,
        CreateTvProfile,
        RenameTvProfile,
        DeleteTvProfile,
        SelectDevice,
        RequestSnapshot
    );
    record!(BrowserProfileSource, Tv, Device);
    record!(
        BrowserWorkspaceCommandAction,
        Focus,
        OpenPane,
        ClosePane,
        SetLayout,
        Navigate,
        Reload,
        Back,
        Forward,
        SetMute,
        PlayPause,
        SetInteraction,
        EnterTheater,
        ExitTheater,
        RequestSnapshot,
        MovePane
    );
    record!(BrowserWorkspaceInputKind, Key, Text);
    record!(BrowserWorkspaceWireInteractionMode, WorkspaceChrome, Page);
    record!(
        BrowserWorkspaceWireLayout,
        Single,
        TwoColumns,
        TwoRows,
        FourGrid
    );
    record!(
        BrowserWorkspaceWireMuteApplication,
        NotRequested,
        PendingRenderer,
        Requested,
        AppliedToRenderer,
        Unsupported,
        Failed
    );
    record!(
        BrowserWorkspaceWireObservedPlayback,
        Unknown,
        Playing,
        Paused,
        Ended,
        Unavailable
    );
    record!(BrowserWorkspaceWirePaneResidency, Live, Suspended, Failed);

    all
}

fn manifest_path() -> PathBuf {
    let mut current = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    loop {
        let candidate = current.join("protocol").join("golden");
        if candidate.is_dir() {
            return candidate.join("browser-enums.txt");
        }
        assert!(
            current.pop(),
            "could not find protocol/golden above the crate root"
        );
    }
}

/// One line per value: `Enum.VARIANT=value`. A text format so the diff is readable in review.
fn render(all: &BTreeMap<String, Vec<u8>>) -> String {
    let mut out = String::new();
    use std::fmt::Write as _;
    for (name, values) in all {
        let joined: Vec<String> = values.iter().map(u8::to_string).collect();
        writeln!(out, "{name}={}", joined.join(",")).expect("writing to a String cannot fail");
    }
    out
}

#[test]
fn the_committed_enum_manifest_matches_this_build() {
    let path = manifest_path();
    let committed = fs::read_to_string(&path).unwrap_or_else(|_| {
        panic!(
            "missing {}. Generate it with: cargo test --test enum_manifest -- --ignored regenerate",
            path.display()
        )
    });
    assert_eq!(
        committed.replace("\r\n", "\n"),
        render(&manifest()),
        "browser enum values drifted from the committed manifest"
    );
}

#[test]
fn no_enum_declares_a_duplicate_wire_value() {
    // Two variants sharing a number are indistinguishable once encoded: the sender means one, the
    // receiver decodes the other, and nothing on either side reports a problem.
    //
    // Zero is deliberately allowed. `BrowserVpnProvider::None` is a real value meaning "no
    // provider", not a marker for an absent field — nullability is carried by a presence byte
    // beside the value, never by the value itself.
    for (name, values) in manifest() {
        let mut seen = values.clone();
        seen.dedup();
        assert_eq!(
            seen.len(),
            values.len(),
            "{name} declares a duplicate wire value"
        );
    }
}

#[test]
#[ignore = "regenerates the committed manifest"]
fn regenerate() {
    let path = manifest_path();
    fs::write(&path, render(&manifest())).expect("write manifest");
    println!("wrote {}", path.display());
}
