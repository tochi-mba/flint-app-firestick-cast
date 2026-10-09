//! Controlling a running mirror session across the C ABI.

use std::panic::{catch_unwind, AssertUnwindSafe};

use super::{FlintMirrorSession, FlintStatus};
use crate::session::Pause;

/// Draws the mouse pointer into the shared picture, or stops, from the next frame.
///
/// Off until asked: Windows hands the pointer over apart from the picture, so a share shows none
/// unless this turns it on.
///
/// # Safety
/// `handle` must come from `flint_mirror_start` and not yet have been stopped.
#[no_mangle]
pub unsafe extern "C" fn flint_mirror_set_pointer(
    handle: *mut FlintMirrorSession,
    show: u8,
) -> i32 {
    if handle.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live and not aliased.
        unsafe { (*handle).session.set_show_pointer(show != 0) };
        FlintStatus::Ok as i32
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Values for [`flint_mirror_set_pause`]'s `mode`.
pub mod pause_mode {
    /// Sending the screen.
    pub const RUNNING: u32 = 0;
    /// Paused; the TV keeps the last picture.
    pub const HOLDING: u32 = 1;
    /// Paused; the TV is sent one black frame.
    pub const BLANKING: u32 = 2;
}

/// The pause a `mode` value stands for, or `None` for a value this engine does not know.
#[must_use]
pub fn pause_from(mode: u32) -> Option<Pause> {
    match mode {
        pause_mode::RUNNING => Some(Pause::Running),
        pause_mode::HOLDING => Some(Pause::Holding),
        pause_mode::BLANKING => Some(Pause::Blanking),
        _ => None,
    }
}

/// Pauses or resumes a session; it takes effect on the next tick.
///
/// Setting the mode the session is already in changes nothing, so a caller may repeat it.
///
/// # Safety
/// `handle` must come from `flint_mirror_start` and not yet have been stopped.
#[no_mangle]
pub unsafe extern "C" fn flint_mirror_set_pause(handle: *mut FlintMirrorSession, mode: u32) -> i32 {
    if handle.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let Some(pause) = pause_from(mode) else {
        return FlintStatus::InvalidArgument as i32;
    };

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live and not aliased.
        unsafe { (*handle).session.set_pause(pause) };
        FlintStatus::Ok as i32
    }));

    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Reads how long the session has been capturing, on the clock its frames are timed by.
///
/// A sound share started now times its packets from this point, so picture and sound share one
/// clock on the TV.
///
/// # Safety
/// `handle` must be live and `out_elapsed_us` non-null.
#[no_mangle]
pub unsafe extern "C" fn flint_mirror_elapsed_us(
    handle: *mut FlintMirrorSession,
    out_elapsed_us: *mut i64,
) -> i32 {
    if handle.is_null() || out_elapsed_us.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live.
        let elapsed = unsafe { (*handle).session.elapsed_us() };
        // SAFETY: checked non-null above.
        unsafe { *out_elapsed_us = elapsed };
        FlintStatus::Ok as i32
    }));

    result.unwrap_or(FlintStatus::InternalError as i32)
}

#[cfg(test)]
mod tests {
    #![allow(
        clippy::undocumented_unsafe_blocks,
        reason = "every unsafe block is the FFI call under test, made with the arguments the test names"
    )]

    use super::*;

    #[test]
    fn a_null_handle_is_refused() {
        assert_eq!(
            unsafe { flint_mirror_set_pause(std::ptr::null_mut(), pause_mode::HOLDING) },
            FlintStatus::NullArgument as i32
        );
    }

    #[test]
    fn an_unknown_mode_is_refused_before_the_handle_is_touched() {
        let handle = std::ptr::dangling_mut::<FlintMirrorSession>();
        assert_eq!(
            unsafe { flint_mirror_set_pause(handle, 3) },
            FlintStatus::InvalidArgument as i32
        );
    }

    #[test]
    fn showing_the_pointer_refuses_a_null_handle() {
        assert_eq!(
            unsafe { flint_mirror_set_pointer(std::ptr::null_mut(), 1) },
            FlintStatus::NullArgument as i32
        );
    }

    #[test]
    fn the_elapsed_time_refuses_null_arguments() {
        let mut elapsed = 0i64;
        assert_eq!(
            unsafe { flint_mirror_elapsed_us(std::ptr::null_mut(), &raw mut elapsed) },
            FlintStatus::NullArgument as i32
        );
        assert_eq!(
            unsafe {
                flint_mirror_elapsed_us(
                    std::ptr::dangling_mut::<FlintMirrorSession>(),
                    std::ptr::null_mut(),
                )
            },
            FlintStatus::NullArgument as i32
        );
    }

    #[test]
    fn the_mode_values_are_stable() {
        assert_eq!(pause_from(0), Some(Pause::Running));
        assert_eq!(pause_from(1), Some(Pause::Holding));
        assert_eq!(pause_from(2), Some(Pause::Blanking));
        assert_eq!(pause_from(7), None);
    }

    #[test]
    fn a_real_session_pauses_and_resumes_and_repeating_is_harmless() {
        let config = super::super::FlintMirrorConfig {
            output_index: 0,
            frame_rate: 30,
            bitrate_bits_per_second: 8_000_000,
            max_width: 1280,
        };
        let mut handle: *mut FlintMirrorSession = std::ptr::null_mut();
        if unsafe { super::super::flint_mirror_start(&raw const config, &raw mut handle) }
            != FlintStatus::Ok as i32
        {
            return;
        }

        // The black frame has to come out of this machine's real encoder, which may finish a
        // frame only after it is given the next; a paused share drains it instead.
        assert_eq!(
            unsafe { flint_mirror_set_pause(handle, pause_mode::BLANKING) },
            FlintStatus::Ok as i32
        );
        let mut buffer = vec![0u8; 1024 * 1024];
        let mut sent = 0;
        for _ in 0..20 {
            let mut frame = super::super::FlintMirrorFrame::EMPTY;
            let status = unsafe {
                super::super::flint_mirror_next(
                    handle,
                    buffer.as_mut_ptr(),
                    u32::try_from(buffer.len()).unwrap(),
                    &raw mut frame,
                )
            };
            assert_eq!(status, FlintStatus::Ok as i32);
            if frame.kind == super::super::mirror_tick::ENCODED {
                sent += 1;
            }
        }
        assert!(sent >= 1, "the black frame never reached the wire");

        let mut elapsed = -1i64;
        assert_eq!(
            unsafe { flint_mirror_elapsed_us(handle, &raw mut elapsed) },
            FlintStatus::Ok as i32
        );
        assert!(
            elapsed > 0,
            "the session has been capturing since it started"
        );

        for mode in [
            pause_mode::BLANKING,
            pause_mode::HOLDING,
            pause_mode::RUNNING,
        ] {
            assert_eq!(
                unsafe { flint_mirror_set_pause(handle, mode) },
                FlintStatus::Ok as i32
            );
        }

        for show in [1, 0] {
            assert_eq!(
                unsafe { flint_mirror_set_pointer(handle, show) },
                FlintStatus::Ok as i32
            );
        }

        assert_eq!(
            unsafe { super::super::flint_mirror_stop(handle) },
            FlintStatus::Ok as i32
        );
    }
}
