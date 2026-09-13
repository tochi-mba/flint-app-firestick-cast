# Hardware evidence

Every claim in this repository about what a physical phone or television did is made here and
nowhere else. A row is written by the person who ran the check, in the words the app showed them,
with the devices named. A step in `FINISHING-PLAN.md` gets a hardware mark only from a row on this
page. Nothing on this page is inferred from a compiler, an emulator or a unit test.

Fill in the device columns for the rows that already exist: the results were reported from a phone
whose model and Android version were not recorded at the time.

## Runs

| Date | Phone (model, Android) | Fire TV (model, Fire OS) | What ran | What the app said | Outcome |
|---|---|---|---|---|---|
| 2026-09-12 | _not recorded_ | none involved | Settings, second-screen check | "This phone gave Flint a display only this app can see, and the frame drawn into it came back intact." | Passed |
| 2026-09-12 | _not recorded_ | none involved | Settings, encoder check (the listing-only version that preceded the pixel round trip) | "This phone can encode H.264, H.265 in hardware." | Passed; proves the encoders are listed, not that they produce pixels |

## The bring-up session

Everything below is written, gated by tests that run on every change, and has never touched a
television. One session with a phone and a Fire TV closes all of it. Do the steps in order: each one
is the prerequisite for the next, and stopping early still leaves every row above it recorded.

Take the phone's model and Android version from **Settings → About phone**, and the television's from
**Settings → My Fire TV → About**, before you start. Every row wants both.

**1. The encoder pixel round trip.** On the phone alone, no television needed. Settings → *Check this
phone's encoder*. Record the whole sentence. It names the codec and says whether the frame "came back
from a decoder intact". This is the one check that looks at pixels, and it is the gate for everything
after it: if it fails, record the failure and stop, because the rest cannot work.

**2. The television, identified.** Turn on the phone's hotspot and join the TV to it. On the TV,
enable **Settings → My Fire TV → Developer Options → ADB debugging**. In Flint, choose the TV on the
Cast screen, then Settings → *Identify this TV*. The television shows its own authorisation prompt:
accept it with the remote. Record what the card then says, including the Fire OS generation it names,
and whether the prompt appeared at all.

**3. The receiver, installed.** On the same card, press Install. Record the disclosure it showed
first (package, version, size), how long the install took, and what it said afterwards. Then confirm
the TV shows the Flint receiver with a pairing code.

**4. Pairing.** Type the six-digit code into the phone. Record whether it connected, and then close
and reopen the app: it should reconnect with the stored token and show no code. Record both.

**5. Second Screen.** Start it. The television should read SECOND SCREEN and show the dashboard.
Record what is on the TV, and the live strip's codec and link word on the phone.

**6. Mirror, with sound.** Stop the second screen, start Mirror, accept the capture dialog, and allow
the recording permission when asked. Record: whether the TV reads SCREEN MIRROR; whether the picture
follows the phone when you rotate it; whether sound is heard when you play something, or what the
strip's Sound row says instead.

**7. The link under load.** While mirroring, walk away from the TV until the link word changes, then
come back. Record the words it passed through and whether the picture recovered. If the phone gets
warm, record the thermal sentence it shows.

**8. Media handoff.** Stop the mirror. Pick a video with the phone's picker. Record the byte count
against the file's real size, whether it played at original quality, and whether play, pause and seek
drove it.

**9. Removal.** Settings → Remove, then confirm. Record that the television went back to its own
screen, and that the card then offers to install again.

**10. Latency, only if you have a 240 fps camera.** Follow `docs/LATENCY_BUDGET.md` exactly. Until a
figure is written here by that procedure, no figure may be quoted anywhere.

If any step fails, write the row anyway with what the app said. A recorded failure is evidence and is
worth more than an untested claim; an unrecorded one is a rumour.

## How to add a row

Run the check. Copy the sentence the app showed, verbatim, into the "What the app said" column. Name
the phone with `Settings > About phone > Model` and its Android version, and the television with
`Settings > My Fire TV > About`. If the check failed, the row is still written: a recorded failure is
evidence, and an unrecorded one is a rumour.
