#![allow(
    clippy::undocumented_unsafe_blocks,
    reason = "every unsafe block is the FFI call under test, made with the arguments the test names"
)]

use super::*;

fn config(bitrate_kbps: u32) -> FlintAudioConfig {
    FlintAudioConfig {
        device_id: std::ptr::null(),
        device_id_len: 0,
        bitrate_kbps,
        start_offset_us: 1_000_000,
        delay_ms: 0,
        _reserved: 0,
    }
}

#[test]
fn the_structures_have_stable_layouts() {
    assert_eq!(std::mem::size_of::<FlintAudioDevice>(), 776);
    assert_eq!(std::mem::offset_of!(FlintAudioDevice, name), 8);
    assert_eq!(std::mem::offset_of!(FlintAudioDevice, id), 8 + 256);
    assert_eq!(std::mem::size_of::<FlintAudioConfig>(), 32);
    assert_eq!(std::mem::offset_of!(FlintAudioConfig, bitrate_kbps), 12);
    assert_eq!(std::mem::offset_of!(FlintAudioConfig, start_offset_us), 16);
    assert_eq!(std::mem::size_of::<FlintAudioStats>(), 32);
    assert_eq!(std::mem::offset_of!(FlintAudioStats, state), 20);
    assert_eq!(std::mem::offset_of!(FlintAudioStats, meter), 24);
}

#[test]
fn the_codes_are_stable() {
    assert_eq!(failure_of(&AudioError::NoEncoder), 1);
    assert_eq!(failure_of(&AudioError::UnsupportedBitrate(1)), 2);
    assert_eq!(failure_of(&AudioError::NoOutput), 3);
    assert_eq!(failure_of(&AudioError::DeviceMissing), 4);
    assert_eq!(failure_of(&AudioError::DeviceLost), 5);
    assert_eq!(failure_of(&AudioError::Platform(String::new())), 5);
    assert_eq!(state_of(SoundState::Ready), 1);
    assert_eq!(state_of(SoundState::Sounding), 2);
    assert_eq!(state_of(SoundState::Unavailable), 3);
}

#[test]
fn every_call_refuses_null_rather_than_dereferencing_it() {
    let null = std::ptr::null_mut::<FlintAudioSession>();
    let mut found = 0u32;
    let mut len = 0u32;
    let mut time = 0i64;
    let mut buffer = [0u8; 4];
    let mut stats = FlintAudioStats {
        packets: 0,
        dropped: 0,
        level: 0.0,
        state: 0,
        meter: 0.0,
        reserved: 0,
    };
    let mut handle = std::ptr::null_mut();
    let mut failure = 0u32;
    let null_status = FlintStatus::NullArgument as i32;
    unsafe {
        assert_eq!(
            flint_audio_devices(std::ptr::null_mut(), 0, std::ptr::null_mut()),
            null_status
        );
        assert_eq!(
            flint_audio_devices(std::ptr::null_mut(), 2, &raw mut found),
            null_status
        );
        assert_eq!(
            flint_audio_start(std::ptr::null(), &raw mut handle, &raw mut failure),
            null_status
        );
        let config = config(128);
        assert_eq!(
            flint_audio_start(&raw const config, std::ptr::null_mut(), &raw mut failure),
            null_status
        );
        assert_eq!(
            flint_audio_start(&raw const config, &raw mut handle, std::ptr::null_mut()),
            null_status
        );
        assert_eq!(
            flint_audio_config(null, buffer.as_mut_ptr(), 4, &raw mut len),
            null_status
        );
        assert_eq!(
            flint_audio_next(null, buffer.as_mut_ptr(), 4, 0, &raw mut len, &raw mut time),
            null_status
        );
        assert_eq!(flint_audio_stats(null, &raw mut stats), null_status);
        assert_eq!(
            flint_audio_problem(null, buffer.as_mut_ptr(), 4, &raw mut len),
            null_status
        );
        assert_eq!(flint_audio_set_paused(null, 1), null_status);
        assert_eq!(flint_audio_set_delay(null, 10), null_status);
        assert_eq!(
            flint_audio_muted(std::ptr::null(), 0, std::ptr::null_mut()),
            null_status
        );
        assert_eq!(
            flint_audio_stop(null),
            FlintStatus::Ok as i32,
            "a null stop is a no-op"
        );

        // Rejected before the handle is touched, so a dangling one is safe here.
        let dangling = std::ptr::dangling_mut::<FlintAudioSession>();
        assert_eq!(
            flint_audio_config(dangling, std::ptr::null_mut(), 4, &raw mut len),
            null_status
        );
        assert_eq!(
            flint_audio_config(dangling, buffer.as_mut_ptr(), 4, std::ptr::null_mut()),
            null_status
        );
        assert_eq!(
            flint_audio_next(
                dangling,
                std::ptr::null_mut(),
                4,
                0,
                &raw mut len,
                &raw mut time
            ),
            null_status
        );
        assert_eq!(
            flint_audio_next(
                dangling,
                buffer.as_mut_ptr(),
                4,
                0,
                std::ptr::null_mut(),
                &raw mut time
            ),
            null_status
        );
        assert_eq!(
            flint_audio_next(
                dangling,
                buffer.as_mut_ptr(),
                4,
                0,
                &raw mut len,
                std::ptr::null_mut()
            ),
            null_status
        );
        assert_eq!(
            flint_audio_stats(dangling, std::ptr::null_mut()),
            null_status
        );
        assert_eq!(
            flint_audio_problem(dangling, std::ptr::null_mut(), 4, &raw mut len),
            null_status
        );
        assert_eq!(
            flint_audio_problem(dangling, buffer.as_mut_ptr(), 4, std::ptr::null_mut()),
            null_status
        );
    }
}

#[test]
fn a_start_that_fails_says_why_and_leaves_no_handle() {
    let config = config(100);
    let mut handle = std::ptr::dangling_mut::<FlintAudioSession>();
    let mut failure = 99u32;

    let status = unsafe { flint_audio_start(&raw const config, &raw mut handle, &raw mut failure) };

    assert_eq!(status, FlintStatus::PlatformError as i32);
    assert!(handle.is_null());
    assert_eq!(failure, audio_failure::UNSUPPORTED_BITRATE);

    let missing: Vec<u16> = "not a device".encode_utf16().collect();
    let named = FlintAudioConfig {
        device_id: missing.as_ptr(),
        device_id_len: u32::try_from(missing.len()).unwrap(),
        ..self::config(128)
    };
    let status = unsafe { flint_audio_start(&raw const named, &raw mut handle, &raw mut failure) };
    assert_eq!(status, FlintStatus::PlatformError as i32);
    assert!(failure == audio_failure::DEVICE_MISSING || failure == audio_failure::NO_ENCODER);
}

#[test]
fn this_pcs_outputs_are_listed_with_one_default() {
    let mut found = 0u32;
    if unsafe { flint_audio_devices(std::ptr::null_mut(), 0, &raw mut found) }
        != FlintStatus::Ok as i32
    {
        return;
    }

    let mut devices = vec![
        FlintAudioDevice {
            is_default: 9,
            _reserved: 0,
            name_len: 0,
            id_len: 0,
            _reserved2: 0,
            name: [0; DEVICE_NAME_UNITS],
            id: [0; DEVICE_ID_UNITS],
        };
        found as usize
    ];
    let mut again = 0u32;
    assert_eq!(
        unsafe { flint_audio_devices(devices.as_mut_ptr(), devices.len(), &raw mut again) },
        FlintStatus::Ok as i32
    );
    if again > 0 {
        assert_eq!(
            devices
                .iter()
                .filter(|device| device.is_default == 1)
                .count(),
            1
        );
        assert!(devices
            .iter()
            .all(|device| device.id_len > 0 && device.name_len > 0));
    }
}

#[test]
fn the_default_outputs_mute_can_be_read_and_set_back_as_it_was() {
    let mut muted = 9u8;
    if unsafe { flint_audio_muted(std::ptr::null(), 0, &raw mut muted) } != FlintStatus::Ok as i32 {
        return;
    }
    assert!(muted <= 1);

    // Set to what it already is, so running the tests never changes the PC's sound.
    assert_eq!(
        unsafe { flint_audio_set_muted(std::ptr::null(), 0, muted) },
        FlintStatus::Ok as i32
    );

    let missing: Vec<u16> = "not a device".encode_utf16().collect();
    let length = u32::try_from(missing.len()).unwrap();
    assert_eq!(
        unsafe { flint_audio_muted(missing.as_ptr(), length, &raw mut muted) },
        FlintStatus::PlatformError as i32
    );
    assert_eq!(
        unsafe { flint_audio_set_muted(missing.as_ptr(), length, 0) },
        FlintStatus::PlatformError as i32
    );
}

#[test]
fn a_real_share_runs_through_every_call() {
    let config = config(128);
    let mut handle = std::ptr::null_mut();
    let mut failure = 0u32;
    if unsafe { flint_audio_start(&raw const config, &raw mut handle, &raw mut failure) }
        != FlintStatus::Ok as i32
    {
        return;
    }

    let mut len = 0u32;
    assert_eq!(
        unsafe { flint_audio_config(handle, std::ptr::null_mut(), 0, &raw mut len) },
        FlintStatus::BufferTooSmall as i32
    );
    assert_eq!(len, 2);
    let mut setup = [0u8; 2];
    assert_eq!(
        unsafe { flint_audio_config(handle, setup.as_mut_ptr(), 2, &raw mut len) },
        FlintStatus::Ok as i32
    );
    assert_eq!(setup, [0x11, 0x90]);

    let mut packet = vec![0u8; 4096];
    let mut time = 0i64;
    assert_eq!(
        unsafe {
            flint_audio_next(
                handle,
                packet.as_mut_ptr(),
                4096,
                2_000,
                &raw mut len,
                &raw mut time,
            )
        },
        FlintStatus::Ok as i32
    );
    assert!(len > 0 && time >= 1_000_000);
    let mut tiny = [0u8; 1];
    let status = unsafe {
        flint_audio_next(
            handle,
            tiny.as_mut_ptr(),
            1,
            2_000,
            &raw mut len,
            &raw mut time,
        )
    };
    assert_eq!(
        status,
        FlintStatus::BufferTooSmall as i32,
        "a packet is never cut short"
    );

    let mut stats = FlintAudioStats {
        packets: 0,
        dropped: 0,
        level: -1.0,
        state: 0,
        meter: -1.0,
        reserved: 7,
    };
    assert_eq!(
        unsafe { flint_audio_stats(handle, &raw mut stats) },
        FlintStatus::Ok as i32
    );
    assert!(stats.packets > 0 && stats.level >= 0.0);
    assert!((0.0..=1.0).contains(&stats.meter));
    assert_eq!(stats.reserved, 0);
    assert!(stats.state == audio_state::READY || stats.state == audio_state::SOUNDING);

    assert_eq!(
        unsafe { flint_audio_problem(handle, std::ptr::null_mut(), 0, &raw mut len) },
        FlintStatus::Ok as i32
    );
    assert_eq!(len, 0, "nothing is wrong");

    assert_eq!(
        unsafe { flint_audio_set_paused(handle, 1) },
        FlintStatus::Ok as i32
    );
    let mut buffer = [0u8; 2048];
    loop {
        let status = unsafe {
            flint_audio_next(
                handle,
                buffer.as_mut_ptr(),
                2048,
                0,
                &raw mut len,
                &raw mut time,
            )
        };
        assert_eq!(status, FlintStatus::Ok as i32);
        if len == 0 {
            break;
        }
    }
    assert_eq!(
        unsafe { flint_audio_set_delay(handle, 40) },
        FlintStatus::Ok as i32
    );
    assert_eq!(
        unsafe { flint_audio_set_paused(handle, 0) },
        FlintStatus::Ok as i32
    );
    assert_eq!(unsafe { flint_audio_stop(handle) }, FlintStatus::Ok as i32);
}

#[test]
fn names_longer_than_the_slot_are_cut_rather_than_overflowing() {
    let mut out = [0u16; 4];

    assert_eq!(copy_wide("Speakers", &mut out), 4);
    assert_eq!(String::from_utf16(&out).unwrap(), "Spea");
    assert_eq!(unsafe { identity(std::ptr::null(), 3) }, None);
}

/// A share whose output was unplugged says why through the problem call, never cut short.
#[test]
fn a_lost_output_is_told_through_the_problem_call() {
    let outputs = crate::audio::fake_outputs::FakeOutputs::with(&[("usb", 1)], Some("usb"));
    let options = AudioOptions {
        device_id: Some("usb".into()),
        bitrate_kbps: 128,
        start_offset_us: 0,
        delay_ms: 0,
    };
    let Ok(session) = AudioSession::start_with(outputs.clone(), options) else {
        return;
    };
    let handle = Box::into_raw(Box::new(FlintAudioSession { session }));
    outputs.plug(&[], None);

    let expected = "The chosen sound output was disconnected.";
    let mut len = 0u32;
    let deadline = std::time::Instant::now() + Duration::from_secs(2);
    while len == 0 && std::time::Instant::now() < deadline {
        std::thread::sleep(Duration::from_millis(10));
        let status = unsafe { flint_audio_problem(handle, std::ptr::null_mut(), 0, &raw mut len) };
        assert!(status == FlintStatus::Ok as i32 || status == FlintStatus::BufferTooSmall as i32);
    }
    assert_eq!(len as usize, expected.len());

    let mut short = [0u8; 4];
    assert_eq!(
        unsafe { flint_audio_problem(handle, short.as_mut_ptr(), 4, &raw mut len) },
        FlintStatus::BufferTooSmall as i32
    );
    assert_eq!(short, [0; 4], "nothing is written when it does not fit");

    let mut text = [0u8; 128];
    assert_eq!(
        unsafe { flint_audio_problem(handle, text.as_mut_ptr(), 128, &raw mut len) },
        FlintStatus::Ok as i32
    );
    assert_eq!(&text[..len as usize], expected.as_bytes());
    assert_eq!(unsafe { flint_audio_stop(handle) }, FlintStatus::Ok as i32);
}
