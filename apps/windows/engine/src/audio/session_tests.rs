use super::*;

const fn ms(value: u64) -> Duration {
    Duration::from_millis(value)
}

#[test]
fn the_clock_adds_the_silence_windows_never_delivered() {
    let mut clock = SampleClock::default();
    assert_eq!(clock.missing(ms(10), 480), 0, "on time");
    clock.fed(480);

    // A second later, with nothing captured: a second of silence, less what was already fed.
    assert_eq!(clock.missing(ms(1_010), 0), 48_480 - 480);
}

#[test]
fn the_clock_tolerates_a_little_lateness_without_padding() {
    let mut clock = SampleClock::default();
    clock.fed(48_000);

    assert_eq!(
        clock.missing(ms(1_015), 0),
        0,
        "15 ms behind is within the slack"
    );
    assert_eq!(clock.missing(ms(1_030), 0), 1_440, "30 ms behind is not");
    assert_eq!(
        clock.missing(ms(1_030), 1_440),
        0,
        "unless what was captured covers it"
    );
}

#[test]
fn sounding_holds_for_a_second_after_the_last_sound() {
    let mut liveness = Liveness::default();
    assert_eq!(
        liveness.observe(ms(0), 0.0),
        SoundState::Ready,
        "nothing playing yet"
    );
    assert_eq!(liveness.observe(ms(100), 0.2), SoundState::Sounding);
    assert_eq!(
        liveness.observe(ms(900), 0.0),
        SoundState::Sounding,
        "a quiet moment"
    );
    assert_eq!(
        liveness.observe(ms(1_200), 0.0),
        SoundState::Ready,
        "a second of silence"
    );
}

#[test]
fn the_encoder_gets_the_missing_silence_then_the_sound_or_silence_while_paused() {
    let mut feed = vec![9; 3];

    fill_feed(&mut feed, 2, &[5, 6], false);
    assert_eq!(feed, [0, 0, 0, 0, 5, 6]);

    fill_feed(&mut feed, 1, &[5, 6], true);
    assert_eq!(
        feed,
        [0, 0, 0, 0],
        "nothing captured during a pause is encoded"
    );
}

#[test]
fn the_level_is_the_rms_of_full_scale() {
    assert!(level(&[]).abs() < f32::EPSILON);
    assert!(level(&[0; 8]).abs() < f32::EPSILON);
    assert!((level(&[i16::MAX, i16::MIN, i16::MAX, i16::MIN]) - 1.0).abs() < 0.001);
    let half = level(&[16_384, -16_384]);
    assert!((half - 0.5).abs() < 0.001, "{half}");
}

#[test]
fn a_named_output_that_is_not_there_is_said_so() {
    let options = AudioOptions {
        device_id: Some("not a device".into()),
        bitrate_kbps: 128,
        start_offset_us: 0,
        delay_ms: 0,
    };

    match AudioSession::start(options) {
        Err(AudioError::DeviceMissing | AudioError::NoEncoder) => {}
        Err(other) => panic!("unexpected {other}"),
        Ok(_) => panic!("a missing output was opened"),
    }
}

#[test]
fn a_rate_windows_does_not_offer_is_refused_before_anything_starts() {
    let options = AudioOptions {
        device_id: None,
        bitrate_kbps: 100,
        start_offset_us: 0,
        delay_ms: 0,
    };

    assert!(matches!(
        AudioSession::start(options),
        Err(AudioError::UnsupportedBitrate(100))
    ));
}

/// A real share on this PC's default output, when it has one: packets flow even in silence, and a
/// pause stops them.
#[test]
fn a_real_share_sends_packets_on_the_wall_clock_and_a_pause_stops_them() {
    let options = AudioOptions {
        device_id: None,
        bitrate_kbps: 128,
        start_offset_us: 5_000_000,
        delay_ms: 0,
    };
    let Ok(session) = AudioSession::start(options) else {
        return;
    };
    assert_eq!(session.config(), [0x11, 0x90]);

    let mut packet = Vec::new();
    let first = session.next(&mut packet, Duration::from_secs(2));
    let first = first.expect("silence still produces packets, so the TV's clock keeps running");
    assert!(first >= 5_000_000, "times start at the picture's offset");
    assert!(!packet.is_empty());
    assert_ne!(
        session.state(),
        SoundState::Unavailable,
        "{:?}",
        session.problem()
    );

    session.set_paused(true);
    std::thread::sleep(ms(200));
    while session.next(&mut packet, Duration::ZERO).is_some() {}
    std::thread::sleep(ms(200));
    assert_eq!(
        session.next(&mut packet, ms(100)),
        None,
        "nothing while paused"
    );

    session.set_paused(false);
    session.set_delay(0);
    assert!(
        session.next(&mut packet, Duration::from_secs(2)).is_some(),
        "flowing again"
    );
    assert!(session.stats().packets > 0);
    assert!(session.problem().is_none());
}

/// Waits up to two seconds for `done`, for the capture thread to get there.
fn eventually(session: &AudioSession, done: impl Fn(&AudioSession) -> bool) -> bool {
    let deadline = Instant::now() + Duration::from_secs(2);
    while Instant::now() < deadline {
        if done(session) {
            return true;
        }
        std::thread::sleep(ms(10));
    }
    done(session)
}

fn fake_options(device_id: Option<&str>) -> AudioOptions {
    AudioOptions {
        device_id: device_id.map(str::to_owned),
        bitrate_kbps: 128,
        start_offset_us: 0,
        delay_ms: 0,
    }
}

/// Outputs reading 10 ms of a loud sample at a time, as Windows would while something plays.
fn loud(outputs: &[(&str, i16)], default: Option<&str>) -> fake_outputs::FakeOutputs {
    let fake = fake_outputs::FakeOutputs::with(outputs, default);
    fake.desk().samples_per_read = 960;
    fake
}

use super::super::fake_outputs;

#[test]
fn an_unplugged_output_is_unavailable_until_it_is_plugged_back_in() {
    let outputs = loud(&[("usb", 8_000)], Some("usb"));
    let Ok(session) = AudioSession::start_with(outputs.clone(), fake_options(Some("usb"))) else {
        return;
    };
    assert!(eventually(&session, |session| session.state() == SoundState::Sounding));

    outputs.plug(&[], None);
    assert!(eventually(&session, |session| session.state()
        == SoundState::Unavailable));
    assert_eq!(
        session.problem().as_deref(),
        Some("The chosen sound output was disconnected.")
    );

    outputs.plug(&[("usb", 8_000)], Some("usb"));
    assert!(eventually(&session, |session| session.state() == SoundState::Sounding));
    assert_eq!(session.problem(), None);
}

#[test]
fn the_meter_reaches_the_stats() {
    let outputs = loud(&[("speakers", 0)], Some("speakers"));
    let Ok(session) = AudioSession::start_with(outputs.clone(), fake_options(None)) else {
        return;
    };

    outputs.desk().meter = 0.5;

    assert!(eventually(&session, |session| session.stats().meter == 0.5));
    assert_eq!(session.stats().level, 0.0, "the capture itself is silent");
}

#[test]
fn a_capture_failure_other_than_loss_ends_the_share_with_its_reason() {
    let outputs = loud(&[("speakers", 1)], Some("speakers"));
    let Ok(session) = AudioSession::start_with(outputs.clone(), fake_options(None)) else {
        return;
    };

    outputs.desk().failure = Some(AudioError::Platform("the driver crashed".into()));

    assert!(eventually(&session, |session| session.state()
        == SoundState::Unavailable));
    assert!(session
        .problem()
        .is_some_and(|problem| problem.contains("the driver crashed")));
}

#[test]
fn a_lost_output_is_described_by_whether_one_was_chosen() {
    assert_eq!(
        lost(&fake_options(Some("usb"))),
        "The chosen sound output was disconnected."
    );
    assert_eq!(
        lost(&fake_options(None)),
        "This PC has no sound output connected."
    );
}

#[test]
fn a_sound_thread_that_dies_while_starting_is_a_failure_to_start() {
    let outputs = loud(&[("speakers", 1)], Some("speakers"));
    outputs.desk().panic_on_open = true;

    match AudioSession::start_with(outputs, fake_options(None)) {
        Err(AudioError::Platform(message)) => assert_eq!(message, ENDED_EARLY),
        Err(AudioError::NoEncoder) => {}
        other => panic!("unexpected {:?}", other.map(|_| ())),
    }
}
