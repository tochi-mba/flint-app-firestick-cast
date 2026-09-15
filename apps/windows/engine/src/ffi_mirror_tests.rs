#![allow(
    clippy::undocumented_unsafe_blocks,
    reason = "every unsafe block is the FFI call under test, made with the arguments the test names"
)]

use super::*;

fn config() -> FlintMirrorConfig {
    FlintMirrorConfig {
        output_index: 0,
        frame_rate: 30,
        bitrate_bits_per_second: 8_000_000,
        max_width: 1920,
    }
}

fn frame() -> FlintMirrorFrame {
    FlintMirrorFrame {
        kind: 99,
        key_frame: 9,
        _reserved: [0; 3],
        presentation_time_us: -1,
        byte_count: 7,
        _reserved2: 0,
    }
}

#[test]
fn starting_rejects_null_arguments() {
    let mut handle = std::ptr::dangling_mut::<FlintMirrorSession>();
    assert_eq!(
        unsafe { flint_mirror_start(std::ptr::null(), &raw mut handle) },
        FlintStatus::NullArgument as i32
    );
    assert!(handle.is_null(), "a failed start must clear a stale handle");

    let config = config();
    assert_eq!(
        unsafe { flint_mirror_start(&raw const config, std::ptr::null_mut()) },
        FlintStatus::NullArgument as i32
    );
}

#[test]
fn starting_rejects_invalid_scalar_configuration_before_opening_the_desktop() {
    for invalid in [
        FlintMirrorConfig {
            frame_rate: 0,
            ..config()
        },
        FlintMirrorConfig {
            bitrate_bits_per_second: 0,
            ..config()
        },
        FlintMirrorConfig {
            max_width: 1,
            ..config()
        },
    ] {
        let mut handle = std::ptr::dangling_mut::<FlintMirrorSession>();
        assert_eq!(
            unsafe { flint_mirror_start(&raw const invalid, &raw mut handle) },
            FlintStatus::InvalidArgument as i32
        );
        assert!(handle.is_null());
    }
}

#[test]
fn every_entry_point_rejects_a_null_handle_rather_than_dereferencing_it() {
    let mut frame = frame();
    let mut buffer = [0u8; 8];
    let mut length = 0u32;
    let mut stats = FlintMirrorStats {
        frames_encoded: 0,
        frames_unchanged: 0,
        recoveries: 0,
        bytes_encoded: 0,
        width: 0,
        height: 0,
    };

    assert_eq!(
        unsafe { flint_mirror_next(std::ptr::null_mut(), buffer.as_mut_ptr(), 8, &raw mut frame) },
        FlintStatus::NullArgument as i32
    );
    assert_eq!(frame, FlintMirrorFrame::EMPTY);
    assert_eq!(
        unsafe {
            flint_mirror_codec_data(
                std::ptr::null_mut(),
                0,
                buffer.as_mut_ptr(),
                8,
                &raw mut length,
            )
        },
        FlintStatus::NullArgument as i32
    );
    assert_eq!(length, 0);
    assert_eq!(
        unsafe {
            flint_mirror_next(
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                8,
                &raw mut frame,
            )
        },
        FlintStatus::NullArgument as i32,
        "a null frame buffer is a mistake even though a null codec-data buffer is not"
    );
    assert_eq!(
        unsafe { flint_mirror_request_key_frame(std::ptr::null_mut()) },
        FlintStatus::NullArgument as i32
    );
    let mut codec = 0u32;
    assert_eq!(
        unsafe { flint_mirror_codec(std::ptr::null_mut(), &raw mut codec) },
        FlintStatus::NullArgument as i32
    );
    assert_eq!(
        unsafe { flint_mirror_stats(std::ptr::null_mut(), &raw mut stats) },
        FlintStatus::NullArgument as i32
    );
}

#[test]
fn stopping_a_null_handle_is_a_no_op_so_a_double_teardown_is_safe() {
    assert_eq!(
        unsafe { flint_mirror_stop(std::ptr::null_mut()) },
        FlintStatus::Ok as i32
    );
    assert_eq!(
        unsafe { flint_mirror_stop(std::ptr::null_mut()) },
        FlintStatus::Ok as i32
    );
}

#[test]
fn tick_kinds_are_stable() {
    // The managed side matches on these; changing one silently changes its meaning.
    assert_eq!(mirror_tick::NOTHING, 0);
    assert_eq!(mirror_tick::ENCODED, 1);
    assert_eq!(mirror_tick::RECOVERED, 2);
}

#[test]
fn the_mirror_structs_have_stable_layouts() {
    // The managed structs must match byte for byte.
    assert_eq!(std::mem::size_of::<FlintMirrorConfig>(), 16);
    assert_eq!(std::mem::align_of::<FlintMirrorConfig>(), 4);
    assert_eq!(std::mem::size_of::<FlintMirrorFrame>(), 24);
    assert_eq!(std::mem::align_of::<FlintMirrorFrame>(), 8);
    assert_eq!(std::mem::size_of::<FlintMirrorStats>(), 40);
    assert_eq!(std::mem::align_of::<FlintMirrorStats>(), 8);
}

/// Exercises a real session over this machine's display, when it has one.
///
/// Skips rather than fails on a machine with no interactive desktop, which is what CI is; the
/// null-argument tests above are what run everywhere.
#[test]
fn a_real_session_reports_codec_data_and_a_capped_frame_size() {
    let config = config();
    let mut handle: *mut FlintMirrorSession = std::ptr::null_mut();
    if unsafe { flint_mirror_start(&raw const config, &raw mut handle) } != FlintStatus::Ok as i32 {
        return;
    }
    assert!(!handle.is_null());

    let mut stats = FlintMirrorStats {
        frames_encoded: 0,
        frames_unchanged: 0,
        recoveries: 0,
        bytes_encoded: 0,
        width: 0,
        height: 0,
    };
    assert_eq!(
        unsafe { flint_mirror_stats(handle, &raw mut stats) },
        FlintStatus::Ok as i32
    );
    assert!(stats.width > 0 && stats.width <= config.max_width);
    assert_eq!(stats.width % 2, 0);
    assert_eq!(stats.height % 2, 0);

    // A null buffer with no capacity is how the shell learns each block's size before it
    // allocates one, so that exact call has to work rather than read as a null argument.
    let mut length = 0u32;
    let status =
        unsafe { flint_mirror_codec_data(handle, 0, std::ptr::null_mut(), 0, &raw mut length) };
    assert!(
        status == FlintStatus::Ok as i32 || status == FlintStatus::BufferTooSmall as i32,
        "unexpected status {status}"
    );
    assert!(
        length > 0,
        "an H.264 session with no parameter sets can never be decoded"
    );

    let mut block = vec![0u8; length as usize];
    assert_eq!(
        unsafe { flint_mirror_codec_data(handle, 0, block.as_mut_ptr(), length, &raw mut length) },
        FlintStatus::Ok as i32
    );
    assert_eq!(
        &block[..4],
        &[0, 0, 0, 1],
        "a parameter set must arrive as an Annex B unit the receiver can hand to MediaCodec"
    );

    // Past the last block is "no more", not an error, which is how the walk terminates.
    let mut past_the_end = 0u32;
    assert_eq!(
        unsafe {
            flint_mirror_codec_data(handle, 64, std::ptr::null_mut(), 0, &raw mut past_the_end)
        },
        FlintStatus::Ok as i32
    );
    assert_eq!(past_the_end, 0);

    // The mirror encoder is H.264. Reporting anything else here would have the receiver build
    // a decoder for a codec it is never sent, which is a black screen rather than an error.
    let mut codec = 0u32;
    assert_eq!(
        unsafe { flint_mirror_codec(handle, &raw mut codec) },
        FlintStatus::Ok as i32
    );
    assert_eq!(codec, crate::encode::VideoCodec::H264 as u32);

    assert_eq!(
        unsafe { flint_mirror_request_key_frame(handle) },
        FlintStatus::Ok as i32
    );

    let mut buffer = vec![0u8; 1024 * 1024];
    let mut frame = frame();
    assert_eq!(
        unsafe {
            flint_mirror_next(
                handle,
                buffer.as_mut_ptr(),
                buffer.len() as u32,
                &raw mut frame,
            )
        },
        FlintStatus::Ok as i32
    );
    assert!(
        frame.kind == mirror_tick::NOTHING
            || frame.kind == mirror_tick::ENCODED
            || frame.kind == mirror_tick::RECOVERED,
        "a tick must report one of the documented kinds, got {}",
        frame.kind
    );
    assert!(frame.key_frame <= 1, "the key-frame flag must stay clean");

    assert_eq!(unsafe { flint_mirror_stop(handle) }, FlintStatus::Ok as i32);
}
