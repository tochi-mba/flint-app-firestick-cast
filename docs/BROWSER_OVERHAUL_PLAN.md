# Browser overhaul: two hosts, one live workspace

Status: target design for review, not shipped capabilities. Updated 29 September 2026. Receiver lifecycle fixes shipped in PR #26.

## Host and display flows

Required behavior: a connected laptop sees and controls the actual live TV browser workspace, including all visible panes and their playback.

### Run on and show on

Run on: This laptop or [named TV]. With laptop hosting, Show on offers Laptop or Laptop + TV; minimizing the laptop viewer does not stop the TV output. With TV hosting, the TV is the primary display and the laptop live view opens automatically after authorized attachment. The laptop can minimize its viewer; reopening it resumes the same session. TV-hosted laptop-only headless browsing is outside this release: do not imply the TV can be switched off while serving pages.

### Laptop-hosted journey

Start offline → choose local profile → browse → add pages/layout/audio → select TV → verify trust and capabilities → inspect shared workspace → present. Both screens show the same pages. Stop presenting leaves laptop pages alive. Switch TV changes output only, preserves live forms/playback, clears the old output and verifies the new receiver. A TV connection is never required to start local browsing.

### TV-hosted journey

Start Browser on TV with remote, or choose Run on TV from Windows → connect/authorize → choose TV profile → attach to existing workspace or explicitly open saved workspace. Windows immediately shows decoded live workspace pixels with active-page address, compact controls and input. Mouse clicks, scroll, keys and text operate the actual TV page. TV remote actions reflect back on Windows. Attach never recreates already-open pages or silently overwrites the TV session.

### Live means live

Stream the complete browser composition from TV to laptop using a bounded, latest-frame video pipeline; the current periodic JPEG preview is insufficient. Negotiate resolution/rate/codec and measure capture-to-display latency, frame age, dropped frames, A/V drift, CPU, memory and thermals. No performance figure is promised before hardware measurement. Protected/uncapturable surfaces show an explicit explanation rather than a black pane that looks broken. If smooth video capture and local TV playback cannot coexist on a device, that device fails this mode’s release gate; do not quietly downgrade it to screenshots.

### Stale view and input correctness

The host owns page identity, layout revision, cursor and keyboard focus. Every displayed frame is associated with session/layout generation. Drop superseded frames, request a keyframe after loss and scale input through the actual content rectangle. On stale/missing view, disable coordinate-based clicks and typing, release held input, show Reconnecting / Retry / Disconnect, and restore input only after fresh geometry and focus acknowledgement. Local Stop/Disconnect remains usable. No retry may replay committed text or clicks.

### Switch execution host

Run on changes open a concise review: destination profile, pages/layout to reopen, destination network/VPN, logins that stay behind, unsupported capabilities and unsaved-state warning. Save checkpoint → validate destination → create destination pages muted → confirm readiness → show destination → offer resume → park source pages paused. Cancellation or destination failure leaves the source usable. Before-unload rejection cancels closing the source. This is a reopen operation, never advertised as seamless transfer of DOM, passwords or playback. Prevent both sources becoming audible; discard late callbacks using a transition ID.

### Profile ownership on TV

Clearly label Personal · This laptop versus Family · Living-room TV. Local profile metadata may save URLs/layout/gains from a TV session on user request and reopen them on another TV; cookies, logins and permissions remain at the host. Named TV profiles require proven isolated storage contexts or processes on each supported OS. Android WebView isolation limitations are an engineering gate: do not ship profile labels over one shared cookie jar. Guest sessions clear host storage after exit and revoke remote access. Switching profile blanks remote views, clears queued frames/input, and requires consent before exposing the destination.

### Audio while controlling TV

TV sound remains the default; the laptop live view is silent to avoid echo. Offer TV / Laptop / Both only when the active host can actually capture, route and suppress local output as requested. Per-pane gain/mute/solo must affect the same source on both displays and report acknowledged state. Prove TV per-pane isolation with cross-origin players and mixed sources; DOM volume hacks are not acceptance. Minimized viewer does not mute the TV. One master output choice and one set of pane levels prevent competing mixers.

### Disconnect, lock and new code

For TV hosting, Stop viewing closes only the laptop viewer; Disconnect remote releases control and all reverse media/input channels while normal TV pages continue by default. Offer End TV browser as a separate explicit action with unsaved-state handling. Laptop lock/sleep/disconnect releases input and stops remote viewing, without claiming TV-hosted pages stopped. Private/guest mode defaults to ending its TV session on controller loss, with this consequence visible before entry. New code disconnects the controller, revokes its current authorization and clears its viewer; the TV locally chooses Keep browsing or End browser. For laptop hosting, receiver disconnect clears TV presentation and preserves laptop pages.

### TV interruption and reconnect

If the TV restarts, Windows marks the remote workspace unavailable and offers reconnect or Reopen on laptop; no automatic host switch. Cold TV restore uses that TV’s checkpoint and requires audio resume. A temporary control-link loss can leave nonprivate TV browsing playing; the laptop drops stale pixels/audio and reconnects only within bounded policy. Changed TV identity requires verification. TV Home/background behavior and decoder/encoder surface recreation must be tested; a suspended browser is reported honestly.

### Dialogs, focus and accessibility

Mirror browser content and necessary browser dialogs, while keeping pairing codes, receiver admin UI and unrelated OS surfaces out of the stream. Route permission/dialog state as structured messages so Windows presents accessible controls naming the requesting origin and host. Exactly one response wins. Never automatically relay clipboard, camera or microphone. Screen-reader support needs a remote semantic/accessibility bridge for TV pages; video pixels alone are insufficient. If that bridge cannot be supported, document the limitation and offer local hosting; shell accessibility remains mandatory.

### Architecture for both directions

One workspace reducer and shared command semantics feed LaptopBrowserHost and TvBrowserHost adapters. The authoritative host owns runtime, storage, composition, input and media; the other device is a viewer/controller. Laptop → TV uses native composition and receiver decode; TV → laptop needs a browser-only capture/encoder on TV and hardware decoder/native display on Windows. Neither path sends frames through C# ViewModels. Negotiate capabilities per direction and session; authenticate/encrypt both media and control, use bounded queues, and test session revocation across every channel. The TV capture, engine isolation and audio mechanisms must be selected by prototype evidence, not assumed equivalent to CEF.

## Product contract

Choose where pages run; see and control the same workspace on both screens.

### Decision already agreed

Support both laptop-hosted and TV-hosted browsing as first-class choices. Laptop-hosted pages work locally and can also be presented on a TV. TV-hosted pages run independently on the TV and MUST have a live interactive view on a connected laptop. The laptop view shows the same running pages, not a second browser session. Host selection and display selection are distinct. Changing display keeps live state; changing host reopens a workspace with the destination host’s website storage.

### The experience we are building

Open Flint → Browser → choose a profile → continue browsing immediately. Select a TV and present the current workspace. Add a second or third page and the layout adjusts. Play several videos, set each pane’s volume, enlarge one with the others still visible as live miniatures, then return to the exact previous layout. Disconnect the TV and keep the laptop session. Reconnect elsewhere without copying cookies or signing in because the television changed.

### Scope

Redesign the entire Windows browser surface, TV presentation and D-pad controls, profiles and persistence, tabs/panes/layouts, pointer and text input, per-pane sound, presentation lifecycle, receiver installation/update/removal, pairing and access management, errors, recovery, permissions, and browser runtime boundaries. Phone receiver-management flows are included. A phone-hosted browser engine and automatic profile synchronization between different laptops/phones are separate future work.

### Success is behavior, not a screenshot

The overhaul is complete only when a representative user can perform the full journey without learning internal modes, finding a hidden capture switch, typing into a second Send box, restarting a TV to disconnect, or losing playback when resizing. A redesigned screen with unreliable input or fake volume controls does not qualify.

### Limits to make explicit

Profiles follow the same laptop to another TV; this does not mean portable authentication between unrelated computers. Websites can expire sessions or require reauthentication. Protected video, DRM and some embedded-browser logins may be unsupported; establish compatibility with actual sites before promising them. A laptop-hosted browser uses the laptop’s internet connection/VPN, not the TV’s VPN. Existing TV browser cookies cannot simply be migrated into Chromium on Windows.

## Windows browser UX

The main window is a browser workspace, not a remote-control dashboard. Local browsing is always available.

### Screen structure

Use one compact top bar: profile switcher, Back/Forward/Reload, address/search for the active page, Add page, Layout, Audio and the TV connection chip. Below it, a compact page strip and the workspace consume the remaining window. Downloads, history, bookmarks, permissions and diagnostics live in contextual panels or the menu. Remove the permanent D-pad, capture toggle, duplicate address bars, manual Send field and explanatory cards from the working browser.

### Navigation is normal browsing

The address bar always identifies the active page, preserves an unsubmitted draft until the user changes pages, and clearly distinguishes a URL from a search. Back/Forward/Reload affect that page only. Support new page, close/reopen closed page, history, bookmarks, page zoom, find-in-page, downloads, copy link and context menus. Ctrl+L focuses the address bar; Ctrl+T adds a page; Ctrl+W closes the active page; Ctrl+Shift+T reopens it. Tab and Shift+Tab belong to the webpage when webpage focus is active. App-level shortcuts must not duplicate page key events.

### Pages and panes use one model

A PageSession is a persistent browser instance. A pane is where that page is shown. There is no separate Tabs mode and Workspace mode with duplicate pages and transfer buttons. One visible page is normal browsing; Split shows an existing or new page beside it. Drag a page onto an edge to split, or onto another pane header to swap positions. Repositioning never navigates or recreates the page. One page has one interactive placement; thumbnails reuse its existing texture.

### Hide is different from close

The page-strip close button closes the page after handling before-unload state. Remove from layout keeps it in the page strip and is a separate overflow action. Hidden pages may keep playing; the audio mixer must list them. Explicit Pause or Suspend is available. Reopen closed page restores saved navigation/checkpoints where supported, not a promise to resurrect unsaved forms. Closing the final page opens the start surface, never a dead blank workspace.

### Pane controls

Each active/hovered pane gets a thin contextual header: title, audible state, Focus, Close and More. Volume opens a small slider/popover; resizing uses draggable dividers. Inactive panes have minimal labels, not a row of large action buttons. Keyboard users can focus the header and operate every action. Important controls keep usable hit targets even when their visible icons are compact.

### Profile and workspace selection

Profile selection is always visible and uses human names. Each profile has isolated website storage and named saved workspaces, with a current autosaved session. Switch profile → checkpoint current workspace → pause its media by default → activate the destination profile. If presenting, briefly blank the TV while switching, then explicitly choose to present the destination; never flash the other profile’s login page. Create, rename, duplicate layout, export workspace and delete have distinct semantics. Duplicating a workspace copies structure, not browser credentials.

### Presentation controls

The connection chip reads Not presenting, Connecting, Presenting to [TV], Reconnecting or Update required. Its menu exposes Stop presenting, Disconnect TV, Switch TV and Manage TV. Presenting does not disable local browsing. A Share preview step shows exactly the workspace to be sent; desktop, other applications, local dialogs and downloads are outside the capture source. Stop presenting is always one visible action.

### Responsive and accessible

At smaller window sizes, move secondary actions into More, keep address and active page visible, and allow page-strip scrolling. No stacked permanent toolbars covering the page. Support high DPI, keyboard navigation, screen readers, contrast, visible focus and reduced motion. When the laptop window is resized, scale the local viewport of the presentation canvas rather than unexpectedly reflowing the TV workspace; an explicit Fit to TV/layout change controls the shared geometry.

### Ordinary browser interruptions

Downloads, uploads, camera/mic and external applications belong to the browser host. Laptop mode uses laptop pickers/devices. TV mode explicitly identifies TV storage/devices; sending a laptop file requires user selection, authenticated bounded transfer and temporary-file cleanup, never arbitrary remote paths. Offer Save to laptop only through an implemented transfer flow. Unsupported TV print/camera/upload actions explain why and offer switching host. Popups stay in the same profile. Certificate errors, login windows, permissions and before-unload dialogs always have cancel/back routes. Never disable browser security to make a site work.

## TV, layout and media

The TV is primarily content. Normal browsing and a multi-video wall are the same workspace.

### Content fills the display

The video canvas fills the TV surface. Do not apply the 5% overscan margin to all page content. Keep interactive TV overlays inside the 5% safe area, with an optional content-fit calibration for TVs that really crop edges. In viewing mode, no permanent large buttons, setup text, address panel or remote-control card consumes space.

### One discoverable TV menu

Menu opens a compact overlay for Pages, Layout, Audio, Connection and End presentation. It is fully D-pad operable and focus is visible. The overlay stays open while a person is using it and hides after dismissal; a brief first-use hint explains how to reopen it. Back dismisses the current overlay rather than exiting Flint unexpectedly. The receiver always retains a local way to stop presentation even if the laptop is unresponsive.

### Automatic layout

Start with one full pane; two split equally; three use one large pane plus two smaller panes with no empty fourth cell; four use a 2×2 grid. Offer Equal grid, Columns, Rows and Main + side. Add chooses an unused page or creates one, puts it in the layout and recalculates geometry. Remove reflows remaining placements. Save ratios/order per workspace. Four simultaneously active videos is an initial validation target, not a claim about every laptop; higher capacity follows measured CPU/GPU/memory/encode headroom.

### Custom layout without fiddling

Drag a divider with minimum pane sizes and snap points. Resize previews locally during drag and commits at a bounded rate; remote input uses a layout generation so stale clicks cannot land in another pane. Reorder by dragging headers; offer Move/Swap and size presets for keyboard and TV remote users. Provide Undo layout change and Reset layout. Automatic reflow cannot silently overwrite a saved custom layout; show that it has become custom.

### Focus view and live miniatures

Focus enlarges a chosen page while the other visible pages remain in a compact live filmstrip. Selecting a miniature switches the main page without recreation. Return to grid restores the exact old layout, ratios, page identities, playback and audio choices. Miniatures are recompositions of the same browser textures, not new browser instances or extra TV decoders. The user can hide the filmstrip for complete immersion and restore it via Menu.

### Three different kinds of fullscreen

A website’s fullscreen request fills its own pane by default. Focus enlarges a pane within Flint while retaining other-page access. Present fullscreen fills the TV display. Label these distinctly. On TV, Back first dismisses keyboard/dialog/menu, then exits website fullscreen, then exits Focus view; at the workspace level it reveals browser/connection controls. Leaving the presentation is explicit, not a final invisible Back step. Windows Esc follows the same local hierarchy; an unsaved form warning is respected.

### Real independent volume

In laptop-hosted mode, each browser instance supplies an audio stream to a native mixer. TV-hosted mode must prove equivalent per-page gain on its engine; Android WebView does not automatically provide this contract. Each page has persistent gain 0–100%, mute and temporary solo; a master workspace level sits after the mix. Gain changes must not change any other pane or the OS master volume. Solo temporarily overrides audible routing and restores prior gains/mutes when released. Avoid clipping with a documented mixer/limiter. Audio state is confirmed by the mixer, not an optimistic icon.

### Playback and focus are independent

Clicking, typing, reordering, resizing and focusing a pane must not pause or mute the others. New pages are muted until deliberately enabled; restored pages retain preferences but require Resume after a cold restart. Expose Play/Pause only when the engine/site media session can actually acknowledge it; otherwise the site’s player controls remain the reliable route. Muting is a browser-output operation and must work for cross-origin players without ad-hoc JavaScript scraping.

### Output routing and resource pressure

Choose TV, Laptop or Both; while presenting, default to TV to prevent doubled audio. Both is explicit and needs delay handling. TV hardware volume remains distinct from per-pane gain. On congestion, lower presentation resolution/rate before disrupting page playback. Never silently suspend an audible pane. If the laptop cannot sustain the requested mix, show the measured limitation and offer reduced quality, fewer live pages or explicit suspend. A preview that is stale must say so.

### Independent videos, not synchronized sources

Several videos can play simultaneously, but unrelated websites have different buffers and live delays. Keep the composed video and mixed audio synchronized; do not promise that separate live events are synchronized with one another. Test supported codecs, live streams, embedded cross-origin players, advertisements and changes between inline/fullscreen playback. Protected content may be blocked by the provider and must have an honest unsupported-state UI.

## Pointer and typing

One logical cursor and one focused browser target. Laptop typing targets the selected host through local input or authenticated remote input.

### Pointer authority

InputCoordinator owns cursor position, active page, hover target, held buttons, keyboard focus and input owner. Mouse and TV remote update that same state. Deliberate mouse movement/click or remote navigation takes ownership; background jitter does not. During a drag or text composition, finish/cancel the current operation before transferring ownership and release held keys/buttons. A small temporary indicator can say Laptop or TV remote when control changes.

### Exactly one cursor per surface

Do not draw a host cursor, a remote cursor and the OS cursor on top of one another. Render the canonical cursor once in the TV composition. Inside the Windows workspace use a single cursor presentation, hiding the OS cursor only if Flint draws the authoritative cursor there. Outside the viewport restore normal Windows behavior immediately. Cursor shape follows the active webpage. When controls are hidden after inactivity, hide the cursor too; never hide focused D-pad controls.

### Native laptop typing

Click a webpage field and type normally. Send physical keys, committed text and IME composition through the runtime’s supported input APIs; do not convert all text into virtual-key events. Cover Unicode, emoji, CJK composition, dead keys, keyboard layouts, selection, Ctrl+A/C/V/Z, Backspace/Delete, multiline Enter and password fields. No Capture on switch or Send button is needed for ordinary typing.

### Focus routing

Input targets are ShellControl, BrowserPage(pageId), TVOverlay or None. Address bar focus stays local to the address bar. Clicking the page selects and focuses it in one operation; an acknowledgement/generation protects against delayed remote events. Losing window focus, lock, disconnect or renderer crash releases every pressed key and mouse button. A closed/replaced page never receives buffered text. Clipboard transfer occurs only through the user’s paste action, with no automatic clipboard polling/sync.

### TV text entry

In laptop-hosted mode, fields receive local browser input. In TV-hosted mode, an active laptop typing lease suppresses automatic TV IME opening and forwards key events, committed text and composition to the focused TV page. Suppression must be verified on Fire OS 6. When the TV remote takes over, explicit Type on TV restores the TV keyboard route. A remote-only user can explicitly choose Type on TV to open a compact keyboard overlay, whose composition is sent to the host input model; dismiss returns focus to the page. A native TV keyboard fallback, if used, is explicitly selected, not auto-opened. Password entry is masked and never logged.

### Remote navigation

Remote directions navigate menu focus when a Flint overlay is open. In page cursor mode they move the same canonical pointer; Select clicks, held Select permits a documented drag action, and scroll is an explicit mode/action. Provide a visible way to switch pointer/scroll rather than guessing based on content. Media keys apply to the explicitly selected page, with unsupported commands reported. Home remains the OS exit behavior; returning must recover the presentation safely.

### Protocol discipline

Remote input includes session generation, page ID, layout revision, sequence and source. Coalesce motion/scroll where safe; never drop key-up/button-up or duplicate committed text. Validate and reject obsolete geometry rather than clicking the wrong page. TV-local emergency Disconnect bypasses page focus and does not rely on a webpage responding.

## Profiles and persistence

Website storage and workspace layout are different kinds of state. Workspace metadata can travel; website storage belongs to the selected host.

### Stable profile identity

For laptop hosting, use stable random profile IDs and isolated Chromium storage contexts/directories. Names can change without changing identity. Cookies, site storage, permissions and cache remain in the engine-managed profile. Tabs/pages in the same profile intentionally share login state; different profiles must not share it. A workspace uses one profile initially. Switching profiles does not rename a shared cookie jar.

### Durable workspace model

Store versioned Profile, Workspace, Page and Placement records: page IDs, URLs/navigation entries where supported, active page, layout tree/ratios, visible/hidden pages, Focus state, page zoom, gains/mutes, bookmarks and supported media/scroll checkpoints. Use atomic transactions and a journal/checkpoint policy, not a single JSON file overwritten from multiple ViewModels. Keep engine storage separate from app metadata; never hand-edit the browser’s cookie database.

### Warm continuation

For laptop hosting, disconnecting or switching TVs leaves browser instances on the laptop intact. Reconnection attaches the existing compositor and input session; it must not recreate or renavigate every page. Focus, a live form and current playback continue as long as their page process remains alive. Switching from Browser to Mirror/Media pauses presentation, but preserves the workspace and clearly identifies the selected output mode.

### Cold restore

After restarting Flint/laptop, restore profile, page addresses, layout, gains/mutes and active page. Reopen background pages progressively so restore does not overload the machine. Restore scroll/video positions where the site permits and label unavailable/expired state. Never promise arbitrary DOM state, unsaved forms, payment flows, expiring links, live-stream exact positions or DRM sessions will survive restart. Offer Resume workspace instead of blasting sound on launch.

### Guest/private and deletion

On the selected host, guest/private sessions use ephemeral browser storage and do not autosave history, page thumbnails or workspace URLs into a normal profile. Closing them ends their session; explain downloads remain on the host where saved. Delete profile requires confirmation naming the profile, signs out/terminates its browsers, then safely removes its storage after processes exit. Clear browsing data, forget TV and delete workspace remain separate actions.

### Save and export honesty

Save locally on meaningful layout/navigation changes with a proposed short debounce, checkpoint on close/disconnect/update, and retain a recoverable prior revision. A write failure produces Saved failed / Retry rather than a success toast. Workspace export contains structure/bookmarks only by default and warns if URLs may be sensitive; it does not export credentials. Cross-computer credential migration/sync is not in this release.

### Migrate existing users

Import old Windows profile names, bookmarks and page metadata without deleting the original store. Show which pages/layouts were restored and which website logins require a one-time login on the laptop. Old TV cookies remain on that TV until an explicit Clear old TV browser data action; no claim that this migration moved them. Preserve a schema-versioned backup and validate upgrade/recovery before deleting old implementation paths.

## Connect, disconnect and updates

Installed, current, paired, trusted and presenting are separate facts. Every state must have a concrete next action.

### Persistent TV management

Windows and phone show the selected TV’s actual receiver version/build, channel, reachability, compatibility and current session. Action is Install, Update, Open on TV, Reconnect or Check again according to evidence. Unknown version stays unknown. A newer receiver is not automatically downgraded. Debug/release packages and incompatible signers are identified separately. Link Update required errors straight to this management panel.

### Receiver release foundations first

Publish stable receiver package/channel identities, durable signing keys and monotonically increasing receiver versionCode values. Bind build number, signer, hash, OS support, protocol range and release notes in a verifiable artifact manifest. Audit installed debug packages; a clean CI runner’s debug key is not a reliable update identity. Never uninstall the user’s receiver just to work around a signer mismatch.

### Update from the app

Check installed evidence → resolve compatible bundled/downloaded artifact → show Update [TV] from X to Y and interruption/data implications → choose Save and update now, After this session or Later → obtain existing ADB authorization / honor the TV RSA prompt → transfer → install in place → relaunch → verify exact installed build, identity and capability handshake → offer Resume. Download/transfer can show measured progress; package installation remains honestly indeterminate. Cancel before installation commit where possible, not a promise to interrupt package-manager commit.

### Update recovery

Distinguish offline TV, denied authorization, no storage, interrupted download, hash/signature failure, incompatible signer, rejected downgrade, install timeout and boot/health failure. Retry must be idempotent and recheck actual installed state. Preserve local workspace and stable receiver trust during a normal update. Prefer a known-good forward repair build for rollback; older APK installation and schema downgrade are offered only when actually supported. Removal is available beside installation with explicit consequences.

### Stop presenting versus disconnect

Stop presenting removes the workspace from the TV but retains the trusted connection and laptop browser. Disconnect TV terminates that controller’s media/control/input channels, clears the TV surface and suppresses automatic reconnect; local profiles/pages remain. Reconnect is an explicit action that clears suppression. Closing the browser and changing app mode do not secretly create or destroy pairing.

### New code without restarting

A Connection menu is reachable on the TV from every surface. Disconnect and show new code ends the active controller session, invalidates its active authorization and the old pairing code, then opens a time-limited new pairing window. Remembered controllers are listed separately; the UI states whether they retain future access and requires a TV-approved reconnection/takeover so an old controller cannot immediately retake the screen. New code alone is never labelled Revoke all access.

### Trust and access management

Forget this TV disconnects and removes local credentials/pin for that TV, preserving browser profiles. The TV can Remove controller or Revoke all access and invalidate affected credentials/sessions. If offline, local forgetting does not claim remote revocation succeeded. Receiver identity is stable and cryptographic, not an IP address or generic name. A changed identity stops connection and offers an explicit verify-again flow; do not silently accept a changed certificate.

### One logical session

Unify cast/browser presentation ownership behind a lifecycle coordinator while retaining separate transport capabilities internally. One controller owns the presentation; other controllers request takeover and TV approval. A disconnect/revoke transaction terminates all associated channels and increments generation. Old asynchronous callbacks cannot kill a new session. ADB authorization remains separate from Flint pairing, and removing a Flint controller does not claim to remove its Android ADB key.

## Recovery and boundaries

The rules below describe laptop hosting; the Host and display flows chapter specifies TV-hosted continuation and failure behavior.

### Network loss

The TV quickly replaces stale private content with a reconnecting/blank safe surface, rejects queued input and clears audio. The laptop retains pages and layout; playback pauses/mutes by default while output is unavailable rather than suddenly moving sound to its speakers. Offer Continue on laptop. On reconnection, verify the receiver/session, request a keyframe, restore output and require Resume audio when interruption was significant. Do not run an infinite invisible reconnect loop.

### Intentional stop, revocation and takeover

An intentional Disconnect never auto-reconnects on discovery refresh, app navigation or old retry completion. TV-side revocation immediately clears presentation and rejects the previous credentials. On Switch TV, clear the old TV before starting the new one; if the old TV is unreachable, show that the clear acknowledgement was not received and rely on its stale-session timeout, not a false Cleared claim.

### Laptop close, sleep and lock

While presenting, closing the window asks whether to keep Flint running in the tray or end presentation; tray mode is visible and has Stop. OS lock and sleep end/blank the TV output and release input by default. Pages are checkpointed where possible; no promise of saving after abrupt power loss. On unlock/wake, the user explicitly resumes. Prevent unintended idle sleep during an active presentation only with an exposed setting/policy.

### Page/runtime/GPU failure

A crashed renderer leaves the other pages operating and displays Reload this page. A runtime crash restores a checkpoint without corrupting profile storage. A GPU reset rebuilds compositor/encoder resources and resumes with fresh generations/keyframes; no stale texture pointers. Memory pressure offers clear quality/suspend choices. A failed restore names affected pages, preserves the rest and permits retry.

### Privacy and permissions

Only workspace content is streamed; never capture the desktop, file picker, notification toasts or unrelated windows. Browser chrome can reveal URLs, so the TV presentation layer excludes local-only UI by design. Password characters stay masked by the browser; users may optionally blank TV while signing in. Do not log text, page content, cookies, tokens, full sensitive URLs or preview pixels. Permission prompts identify the origin and identify which host owns camera/microphone; no automatic cross-device forwarding.

### Security boundary

Signed-in browser pixels, mixed audio and control require authenticated encrypted transport tied to the verified TV identity. Existing plaintext cast transport is not sufficient. No fallback to plaintext for browser presentation. Prioritize input/control over media, bound queues, expire stale sessions and authenticate reconnects. Bind sockets to a validated interface as required by the repository. Site traffic remains subject to Chromium security; do not bypass TLS errors or DRM.

### Unsupported devices and sites

Advertise actual receiver/codec capabilities; do not infer multi-video capacity from a TV model name. Test the user’s Fire OS 6 device rather than assuming newer WebView/OS behavior. Vega support is outside this Android receiver plan. If a provider refuses embedded browsing or protected capture, explain the supported alternative without claiming playback is fixed. The user’s priority streaming sites are still to be supplied.

## Architecture and migration

Implement two host adapters with one workspace contract, and prove both media paths before committing to support claims.

### Browser engine decision gate

Prototype CEF first: its browser-keyed PCM audio and offscreen GPU texture callbacks match independent volume and composition. Do not call it production-ready before verifying codec/site compatibility, persistent profiles, IME, accessibility, hidden/minimized rendering and safe distribution/security updates. WebView2 has good profile support, but mute alone is not independent gain/mixing; it is an alternative only if equivalent audio/capture behavior is proven. Never substitute system volume or fragile page-script injection for per-pane sound.

### Process boundaries

Laptop-hosted path: Avalonia/C# owns screens, commands and low-rate state. A separate Chromium browser worker owns page instances, storage, navigation, permission callbacks and input; use CEF’s native interface with a minimal bridge. A native compositor/mixer owns pooled GPU textures and audio buffers. Rust owns conversion, encode, pacing, packetization and presentation transport. The receiver decodes one composed video stream plus mixed audio and sends authenticated remote input. Chromium itself allocates; the no-allocation rule applies to Flint’s steady-state frame/audio pipeline, not a claim about third-party browser internals.

### Stable domain contracts

ProfileStore manages metadata and schema migrations. BrowserRuntime manages engine instances and isolated contexts. WorkspaceController is a pure reducer for pages, layout and Focus state. InputCoordinator owns focus/cursor/leases. AudioMixer owns gain/mute/solo and routing. PresentationCoordinator owns TV/session transitions. ReceiverManager owns installed-build evidence and update operations. UI ViewModels adapt these contracts; they do not own sockets, cookies or render loops.

### Compositor and media

CEF callback texture handles cannot outlive the callback; copy into owned preallocated textures and compose asynchronously. For laptop hosting, both the local view and TV stream use the same page textures and layout model. Render one fixed-resolution composition, encode once for the selected TV, and downscale/adapt according to negotiated capabilities. No per-frame copying through C# or JPEG preview loop. Add browser-only PCM capture, resampling, per-page gain, mix/limiter, encoding and timestamped A/V synchronization; the current Windows video-only mirror path is not a complete audio implementation.

### What is reusable

Rust FrameSource and SourceFrame already support a useful injection seam and D3D11 source textures. Reuse proven color conversion, encoder selection, keyframe policy, network interface validation, decoder components and protocol golden-vector tooling where tests support it. FFI currently hardwires desktop-output capture; add a workspace compositor source, never capture the whole desktop as a shortcut. Keep the managed shell out of the new frame loop even though the existing mirror runner currently participates per frame.

### Protocol evolution

Add a negotiated browser-presentation capability distinct from legacy TV browsing. Define authenticated presentation lifecycle, TV→host input, layout revisions, session generation, audio configuration/timestamps, acknowledgements, limits and explicit errors. Old receivers get an actionable Update required state, not partially working controls. Rust/C#/Kotlin codecs and regenerated golden vectors must agree byte for byte. Reuse decoder formats only after end-to-end video/audio round-trip tests.

### Transition and deletion

Ship behind an explicit feature flag/internal channel. Supersede the old TV-resident ADRs so they no longer prohibit the chosen host browser. Migrate metadata once with backup; retain and overhaul TV-hosted browsing as a first-class host adapter; never silently switch engines with different profiles. Remove duplicated tab/workspace models, the obsolete JPEG input preview, independent cursor overlays and old browser keyboard capture only after replacement paths for both hosts pass acceptance. Update help/install/network/VPN copy and diagnostics with the actual ownership model.

### Runtime maintenance is product work

Pin a supported runtime build for reproducibility, retain its sandbox, and establish timely security updates, signed packaging and rollback/repair policy. Measure installer size, startup time and memory, including multiple profiles. Engine choice is a tradeoff between richer native media control and ongoing distribution burden; a green prototype alone is not a release strategy.

## Delivery gates

### 0 · Agree interaction model

Review this plan, the Windows/TV wireframe, action names and failure behavior. Record dual-host architecture, host/display transitions and selected site/device test matrix.

Exit gate: Every requested flow has an owner, visible action, exit and recovery path. No implementation of the full redesign yet.

### 1 · Prove the hard path

Prove laptop CEF composition/PCM and TV browser capture/encoding concurrently with local playback. Validate 2 and 4 video workloads separately per host, profile isolation, IME and both encrypted streaming directions on Fire OS 6.

Exit gate: Real decoded TV pixels and recorded audio demonstrate independent gain and A/V sync. Supported sites work; unsupported sites are documented. No fabricated latency or four-pane claim.

### 2 · Fix receiver lifecycle

Stable signing/build metadata; actual installed version; Disconnect, new code, forget/revoke; Windows and phone management surface; in-place update and health check.

Exit gate: Two successive receiver releases update on real Fire OS 6 hardware without unintended data loss. Disconnect cannot reconnect itself; stale credentials/code behave as documented.

### 3 · Ship the host adapters

Build the local Windows browser and refactor TV browsing behind the same workspace commands. Add per-host profile storage, normal input, navigation/popups/downloads/permissions, crash recovery and autosave.

Exit gate: Laptop works with no TV; TV works without laptop. Login separation, restart/restore, IME and ordinary browser flows pass independently for each host. Unsupported capabilities are visible, never fake controls.

### 4 · Add the workspace

Unify page/pane model, auto/custom layouts, live Focus filmstrip, native mixer, state persistence and resource governor.

Exit gate: Resize/reorder/focus without reload or changing other pages’ audio; exact layout return; independent volume proven from output.

### 5 · Complete both display directions

Implement laptop → TV presentation and TV → laptop live viewing, native decode surfaces, host selector, explicit transfer transaction, one pointer, keyboard/IME routing, compact D-pad overlays and reconnect.

Exit gate: Both hosting journeys and all transitions pass on supported Fire TVs. Same-instance live video is demonstrated in both directions, with no duplicated cursor, ghost input, stale frame or unrelated-screen leakage.

### 6 · Harden and migrate

Failure injection, performance/soak tests, accessibility, metadata migration, old-store backup, runtime update policy and staged release.

Exit gate: Acceptance matrix below passes; CI includes meaningful protocol/media/UI checks; new receiver and Windows artifacts verified together.

### 7 · Remove legacy paths

Delete only replaced code, duplicated models and outdated docs after both host adapters pass migration/rollback checks. Keep TV-hosted browsing.

Exit gate: Two explicit host adapters, one workspace contract, no duplicate legacy paths; regression tests prove existing Mirror/Media still work.

## Acceptance scenarios

### Host/display matrix

Scenarios: Laptop alone; laptop → TV with local view; TV standalone; TV → laptop live view; viewer minimize/restore; all host/display transitions

Pass: Each route uses one authoritative page instance. Display changes never reload; host changes disclose reopening and destination login state. No unexpected audible duplicate.

### TV reverse video

Scenarios: 1/2/3/4 moving video panes, GPU/video overlays, inline/fullscreen, popup, protected surface, bandwidth loss, decoder restart

Pass: Actual decoded Windows pixels match TV content. Measure frame age/latency and resources on Fire OS 6 and newer supported hardware; no blank moving video or silent screenshot fallback.

### TV-hosted typing

Scenarios: Amazon keyboard suppression, remote takeover, CJK composition, emoji, dead keys, key repeats, password/paste, dropped acknowledgements

Pass: Exactly-once committed text at intended TV field; no double keyboard or stuck key. TV keyboard returns when explicitly selected. Sensitive text absent from logs.

### Host-switch transaction

Scenarios: Cancel at each step; unsaved form; destination offline/full/crashed; profile unavailable; capability mismatch; old callback arrives late

Pass: Original workspace stays recoverable; no credential copying; no lost checkpoint or double audio; success only when destination acknowledges readiness.

### TV profile and continuation

Scenarios: Same-site different profiles; reconnect same TV; another TV; controller loss; guest expiry; TV restart; laptop lock

Pass: Cookie isolation proven on device. Metadata portability is distinct from authentication. Normal TV session continues on remote detach; guest session follows declared cleanup policy.

### Test layers and release evidence

Scenarios: Pure reducer/property tests for every legal transition; persistence crash injection; fake transport reorder/loss/replay; cross-language vectors; headless UI; real engine fixtures; physical two-device tests

Pass: Every visible action has happy path, cancel, failure and recovery tests. Use real video/audio round trips, not structure-only assertions. CI hardware skips never count as device passes; record build, runtime, device, OS, workload and measured results.

### Receiver update

Scenarios: Old/current/newer/unknown build, signer mismatch, offline, full storage, denied ADB, interrupted transfer, restart failure

Pass: Correct action/version shown; never silently uninstall; local workspace retained; completion only after a verified handshake.

### Access lifecycle

Scenarios: Disconnect during browsing/reconnect/update; new code while connected; revoke one/all; changed certificate; DHCP change; competing controller

Pass: Old session/input rejected; no surprise auto-reconnect; fresh pairing works; other TV trust and profiles preserved.

### Normal browsing

Scenarios: No-TV start, URL/search, back/forward, popup login, download/cancel, upload, permissions, find, zoom, before-unload

Pass: Feels like a browser; correct page affected; every prompt has an exit; OS dialogs stay on the actual host; remote dialogs name the host and origin.

### Profiles

Scenarios: Two logins on same site, rename/switch/restart, TV A→B, guest close, disk-write failure, delete and old-store import

Pass: No cookie leakage; TV change preserves live laptop state; saved state truthful; secrets never copied to TV.

### Text input

Scenarios: Password, CJK, emoji, dead keys, selection/paste/undo, multiline, address bar versus page, fast focus changes

Pass: Exactly once into intended field; no surprise Amazon keyboard; no global keyboard capture outside the workspace.

### Pointer

Scenarios: Mouse/remote alternation, mid-drag takeover, resize during input, letterboxing/DPI, disconnect with held key/button

Pass: One visible cursor per surface; coordinates agree; no stuck key/click; stale events cannot target a new page.

### Layout

Scenarios: 1→2→3→4, add/remove/swap, hidden page, custom ratios, Focus/filmstrip/site fullscreen, undo/reset

Pass: Pages keep identity/playback; exact return; no empty fourth cell at three; controls do not permanently shrink TV content.

### Audio/video

Scenarios: Four reference videos, independent gain/mute/solo, cross-origin audio, output TV/Laptop/Both, live streams, profile change

Pass: Measured independent outputs, no clipping/doubled audio, bounded drift; focus does not pause others; unsupported providers identified.

### Resilience

Scenarios: 30–60 minute soak (proposed gate), bandwidth loss, renderer/GPU crash, sleep/lock, TV restart, hotplug and mode switch

Pass: No accumulating leak/queue; safe blanking; recovery preserves workspace; actual glass-to-glass latency and A/V drift recorded.

### Accessibility

Scenarios: Keyboard only, TV D-pad only, high DPI/contrast, screen reader, small window, overlay timeout

Pass: Visible focus and reachable controls; compact icons keep adequate targets; no disappearing focused menu.

### Security/release

Scenarios: Invalid manifest/signature, protocol mismatch, replay/stale generation, untrusted TV, source leak, runtime update

Pass: Fail safely with an action; authenticated encrypted browser presentation; golden vectors and hardware round trips pass.
