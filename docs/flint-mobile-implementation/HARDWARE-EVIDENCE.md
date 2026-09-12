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

## Not yet run on hardware

Each of these has a test or a check that exists and has not executed on a device. The list shrinks
as rows are added above.

- The encoder pixel round trip: Settings, "Check this phone's encoder", as it is now. The result
  sentence to record is the one that names the codec and whether the frame "came back from a decoder
  intact".
- Pairing with a television by code, and reconnecting with the stored token.
- Second Screen on a television: the television reads SECOND SCREEN and the dashboard scene.
- Mirror on a television: the television reads SCREEN MIRROR; a rotation followed; sound heard or
  the strip's reason for its absence read.
- The bitrate controller driven from Headroom to Congested and back, with the link word narrating it.
- A thermal step-down, with the sentence it produced.
- A key-frame request honoured, or the fallback to a bounded interval, and which.
- A file pushed and played: the byte count on the phone against the file's size, and the transport
  controls driving playback.
- The receiver installed on a Fire TV from the phone alone, the RSA prompt shown and accepted with
  the remote, and then removed the same way.
- A latency measurement by the camera procedure in `docs/LATENCY_BUDGET.md`. No figure may be quoted
  before one is written here.

## How to add a row

Run the check. Copy the sentence the app showed, verbatim, into the "What the app said" column. Name
the phone with `Settings > About phone > Model` and its Android version, and the television with
`Settings > My Fire TV > About`. If the check failed, the row is still written: a recorded failure is
evidence, and an unrecorded one is a rumour.
