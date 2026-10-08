//! Sound across the C ABI: the outputs, their mute, and a running sound share.
//!
//! Follows the rest of the boundary: every pointer is checked, nothing panics across it, and a
//! buffer of zero capacity asks for a size. A missing sound export never stops the picture: the
//! shell treats it as "sound unavailable".

use std::panic::{catch_unwind, AssertUnwindSafe};
use std::time::Duration;

use super::FlintStatus;
use crate::audio::session::{AudioOptions, AudioSession, SoundState};
use crate::audio::{endpoint, AudioError};

/// The longest output name a [`FlintAudioDevice`] holds, in UTF-16 code units.
pub const DEVICE_NAME_UNITS: usize = 128;

/// The longest output identity a [`FlintAudioDevice`] holds, in UTF-16 code units.
pub const DEVICE_ID_UNITS: usize = 256;

/// One sound output, flattened for the ABI.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(C)]
pub struct FlintAudioDevice {
    /// One when Windows plays through this output by default.
    pub is_default: u8,
    /// Explicit padding before the 16-bit fields.
    pub _reserved: u8,
    /// Meaningful UTF-16 code units in `name`.
    pub name_len: u16,
    /// Meaningful UTF-16 code units in `id`.
    pub id_len: u16,
    /// Explicit padding.
    pub _reserved2: u16,
    /// The name Windows shows for it.
    pub name: [u16; DEVICE_NAME_UNITS],
    /// Windows' lasting identity for it.
    pub id: [u16; DEVICE_ID_UNITS],
}

/// What a sound share is asked to do.
#[derive(Debug, Clone, Copy)]
#[repr(C)]
pub struct FlintAudioConfig {
    /// The output's identity as UTF-16, or null for the default output.
    pub device_id: *const u16,
    /// Code units in `device_id`.
    pub device_id_len: u32,
    /// The data rate: 96, 128, 160 or 192.
    pub bitrate_kbps: u32,
    /// Added to every packet's time, so sound shares the picture's clock.
    pub start_offset_us: i64,
    /// How long every packet is held back.
    pub delay_ms: u32,
    /// Explicit padding to an eight-byte boundary.
    pub _reserved: u32,
}

/// A running sound share's counters and state.
#[derive(Debug, Clone, Copy, PartialEq)]
#[repr(C)]
pub struct FlintAudioStats {
    /// Packets encoded.
    pub packets: u64,
    /// Packets dropped because the queue was full.
    pub dropped: u64,
    /// The captured level, from 0 to 1.
    pub level: f32,
    /// One of [`audio_state`].
    pub state: u32,
    /// The level Windows' meter shows for the output, from 0 to 1, measured before its mute.
    pub meter: f32,
    /// Always zero.
    pub reserved: u32,
}

/// Values for [`FlintAudioStats::state`].
pub mod audio_state {
    /// Running, and nothing is playing on this PC yet.
    pub const READY: u32 = 1;
    /// Running, and sound is reaching the TV.
    pub const SOUNDING: u32 = 2;
    /// Stopped by a problem; `flint_audio_problem` says which.
    pub const UNAVAILABLE: u32 = 3;
}

/// Why a sound share could not start, written by [`flint_audio_start`].
pub mod audio_failure {
    /// It started.
    pub const NONE: u32 = 0;
    /// This edition of Windows has no AAC encoder.
    pub const NO_ENCODER: u32 = 1;
    /// The data rate is not one Windows offers.
    pub const UNSUPPORTED_BITRATE: u32 = 2;
    /// This PC has no sound output.
    pub const NO_OUTPUT: u32 = 3;
    /// The named output is not connected.
    pub const DEVICE_MISSING: u32 = 4;
    /// Windows refused for another reason.
    pub const PLATFORM: u32 = 5;
}

/// A running sound share, opaque to the caller.
pub struct FlintAudioSession {
    session: AudioSession,
}

/// The failure code for `error`.
#[must_use]
pub fn failure_of(error: &AudioError) -> u32 {
    match error {
        AudioError::NoEncoder => audio_failure::NO_ENCODER,
        AudioError::UnsupportedBitrate(_) => audio_failure::UNSUPPORTED_BITRATE,
        AudioError::NoOutput => audio_failure::NO_OUTPUT,
        AudioError::DeviceMissing => audio_failure::DEVICE_MISSING,
        AudioError::DeviceLost | AudioError::Platform(_) => audio_failure::PLATFORM,
    }
}

/// The state code for `state`.
#[must_use]
pub fn state_of(state: SoundState) -> u32 {
    match state {
        SoundState::Ready => audio_state::READY,
        SoundState::Sounding => audio_state::SOUNDING,
        SoundState::Unavailable => audio_state::UNAVAILABLE,
    }
}

/// Lists the sound outputs, writing up to `capacity` and the true count to `found`.
///
/// A zero-capacity call may pass a null `devices` to ask for the count. Outputs Windows will not
/// list are reported as none.
///
/// # Safety
/// `devices` must point to `capacity` writable values and `found` to a writable `u32`.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_devices(
    devices: *mut FlintAudioDevice,
    capacity: usize,
    found: *mut u32,
) -> i32 {
    if found.is_null() || (devices.is_null() && capacity > 0) {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        let outputs = with_com(endpoint::list).unwrap_or_default();
        // SAFETY: checked non-null above.
        unsafe { found.write(u32::try_from(outputs.len()).unwrap_or(u32::MAX)) };
        for (slot, output) in outputs.iter().take(capacity).enumerate() {
            let mut name = [0u16; DEVICE_NAME_UNITS];
            let name_len = copy_wide(&output.name, &mut name);
            let mut id = [0u16; DEVICE_ID_UNITS];
            let id_len = copy_wide(&output.id, &mut id);
            // SAFETY: `slot` is below `capacity`, which the caller guaranteed.
            unsafe {
                devices.add(slot).write(FlintAudioDevice {
                    is_default: u8::from(output.is_default),
                    _reserved: 0,
                    name_len,
                    id_len,
                    _reserved2: 0,
                    name,
                    id,
                });
            }
        }
        FlintStatus::Ok as i32
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Starts a sound share; on failure, `out_failure` says why in [`audio_failure`] terms.
///
/// # Safety
/// `config`, `out_handle` and `out_failure` must be non-null, and `config.device_id`, when not
/// null, must point to `config.device_id_len` code units. A session returned must be released with
/// [`flint_audio_stop`] exactly once.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_start(
    config: *const FlintAudioConfig,
    out_handle: *mut *mut FlintAudioSession,
    out_failure: *mut u32,
) -> i32 {
    if config.is_null() || out_handle.is_null() || out_failure.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    // SAFETY: checked non-null above; written before anything can fail.
    unsafe {
        *out_handle = std::ptr::null_mut();
        *out_failure = audio_failure::NONE;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: checked non-null above.
        let config = unsafe { *config };
        let device_id = if config.device_id.is_null() || config.device_id_len == 0 {
            None
        } else {
            // SAFETY: the caller guarantees `device_id_len` readable code units.
            let units = unsafe {
                std::slice::from_raw_parts(config.device_id, config.device_id_len as usize)
            };
            Some(String::from_utf16_lossy(units))
        };

        let options = AudioOptions {
            device_id,
            bitrate_kbps: config.bitrate_kbps,
            start_offset_us: config.start_offset_us,
            delay_ms: config.delay_ms,
        };
        match AudioSession::start(options) {
            Ok(session) => {
                // SAFETY: checked non-null above.
                unsafe { *out_handle = Box::into_raw(Box::new(FlintAudioSession { session })) };
                FlintStatus::Ok as i32
            }
            Err(error) => {
                // SAFETY: checked non-null above.
                unsafe { *out_failure = failure_of(&error) };
                FlintStatus::PlatformError as i32
            }
        }
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Copies the two-byte setup data for the TV's decoder; a null buffer of zero capacity asks for
/// its size.
///
/// # Safety
/// `handle` must be live, `buffer` must hold `capacity` bytes unless `capacity` is zero, and
/// `out_len` must be non-null.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_config(
    handle: *mut FlintAudioSession,
    buffer: *mut u8,
    capacity: u32,
    out_len: *mut u32,
) -> i32 {
    if handle.is_null() || out_len.is_null() || (buffer.is_null() && capacity > 0) {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live.
        let config = unsafe { (*handle).session.config() };
        // SAFETY: checked non-null above.
        unsafe { *out_len = u32::try_from(config.len()).unwrap_or(u32::MAX) };
        if config.len() > capacity as usize {
            return FlintStatus::BufferTooSmall as i32;
        }
        // SAFETY: the length was checked against the caller's capacity.
        unsafe { std::ptr::copy_nonoverlapping(config.as_ptr(), buffer, config.len()) };
        FlintStatus::Ok as i32
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Waits up to `timeout_ms` for the next packet. `out_len` is zero when none was ready.
///
/// # Safety
/// `handle` must be live, `buffer` must hold `capacity` bytes, and both out-pointers non-null.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_next(
    handle: *mut FlintAudioSession,
    buffer: *mut u8,
    capacity: u32,
    timeout_ms: u32,
    out_len: *mut u32,
    out_time_us: *mut i64,
) -> i32 {
    if handle.is_null() || buffer.is_null() || out_len.is_null() || out_time_us.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    // SAFETY: checked non-null above.
    unsafe {
        *out_len = 0;
        *out_time_us = 0;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        let mut packet = Vec::new();
        // SAFETY: the caller guarantees the handle is live.
        let next = unsafe {
            (*handle)
                .session
                .next(&mut packet, Duration::from_millis(u64::from(timeout_ms)))
        };
        let Some(time) = next else {
            return FlintStatus::Ok as i32;
        };
        // SAFETY: checked non-null above.
        unsafe { *out_len = u32::try_from(packet.len()).unwrap_or(u32::MAX) };
        if packet.len() > capacity as usize {
            return FlintStatus::BufferTooSmall as i32;
        }
        // SAFETY: the length was checked against the caller's capacity.
        unsafe {
            std::ptr::copy_nonoverlapping(packet.as_ptr(), buffer, packet.len());
            *out_time_us = time;
        }
        FlintStatus::Ok as i32
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Reads the share's counters and state.
///
/// # Safety
/// `handle` must be live and `out_stats` non-null.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_stats(
    handle: *mut FlintAudioSession,
    out_stats: *mut FlintAudioStats,
) -> i32 {
    if handle.is_null() || out_stats.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live.
        let session = unsafe { &(*handle).session };
        let stats = session.stats();
        // SAFETY: checked non-null above.
        unsafe {
            *out_stats = FlintAudioStats {
                packets: stats.packets,
                dropped: stats.dropped,
                level: stats.level,
                state: state_of(session.state()),
                meter: stats.meter,
                reserved: 0,
            };
        }
        FlintStatus::Ok as i32
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Copies why the share is unavailable, as UTF-8; `out_len` is zero when it is not.
///
/// # Safety
/// `handle` must be live, `buffer` must hold `capacity` bytes unless `capacity` is zero, and
/// `out_len` must be non-null.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_problem(
    handle: *mut FlintAudioSession,
    buffer: *mut u8,
    capacity: u32,
    out_len: *mut u32,
) -> i32 {
    if handle.is_null() || out_len.is_null() || (buffer.is_null() && capacity > 0) {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live.
        let problem = unsafe { (*handle).session.problem() }.unwrap_or_default();
        // SAFETY: checked non-null above.
        unsafe { *out_len = u32::try_from(problem.len()).unwrap_or(u32::MAX) };
        if problem.len() > capacity as usize {
            return FlintStatus::BufferTooSmall as i32;
        }
        // SAFETY: the length was checked against the caller's capacity.
        unsafe { std::ptr::copy_nonoverlapping(problem.as_ptr(), buffer, problem.len()) };
        FlintStatus::Ok as i32
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Pauses (non-zero) or resumes (zero) the share.
///
/// # Safety
/// `handle` must be live.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_set_paused(handle: *mut FlintAudioSession, paused: u8) -> i32 {
    if handle.is_null() {
        return FlintStatus::NullArgument as i32;
    }
    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live.
        unsafe { (*handle).session.set_paused(paused != 0) };
        FlintStatus::Ok as i32
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Holds packets queued from now on back by `delay_ms`.
///
/// # Safety
/// `handle` must be live.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_set_delay(
    handle: *mut FlintAudioSession,
    delay_ms: u32,
) -> i32 {
    if handle.is_null() {
        return FlintStatus::NullArgument as i32;
    }
    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live.
        unsafe { (*handle).session.set_delay(delay_ms) };
        FlintStatus::Ok as i32
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Stops and releases a share. A null handle is a no-op, so a double teardown is safe.
///
/// # Safety
/// `handle` must come from [`flint_audio_start`] and not be used again.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_stop(handle: *mut FlintAudioSession) -> i32 {
    if handle.is_null() {
        return FlintStatus::Ok as i32;
    }
    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees this handle came from flint_audio_start and is released once.
        drop(unsafe { Box::from_raw(handle) });
        FlintStatus::Ok as i32
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Reads whether an output is muted: `device_id` names it, or null for the default output.
///
/// # Safety
/// `device_id`, when not null, must point to `device_id_len` code units; `out_muted` non-null.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_muted(
    device_id: *const u16,
    device_id_len: u32,
    out_muted: *mut u8,
) -> i32 {
    if out_muted.is_null() {
        return FlintStatus::NullArgument as i32;
    }
    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the identity's length.
        let id = unsafe { identity(device_id, device_id_len) };
        match with_com(|| {
            endpoint::open(id.as_deref()).and_then(|device| endpoint::is_muted(&device))
        }) {
            Ok(muted) => {
                // SAFETY: checked non-null above.
                unsafe { *out_muted = u8::from(muted) };
                FlintStatus::Ok as i32
            }
            Err(_) => FlintStatus::PlatformError as i32,
        }
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Mutes (non-zero) or unmutes (zero) an output: `device_id` names it, or null for the default.
///
/// # Safety
/// `device_id`, when not null, must point to `device_id_len` code units.
#[no_mangle]
pub unsafe extern "C" fn flint_audio_set_muted(
    device_id: *const u16,
    device_id_len: u32,
    muted: u8,
) -> i32 {
    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the identity's length.
        let id = unsafe { identity(device_id, device_id_len) };
        match with_com(|| {
            endpoint::open(id.as_deref())
                .and_then(|device| endpoint::set_muted(&device, muted != 0))
        }) {
            Ok(()) => FlintStatus::Ok as i32,
            Err(_) => FlintStatus::PlatformError as i32,
        }
    }));
    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Reads an output identity passed across the boundary; null or empty means the default.
unsafe fn identity(device_id: *const u16, device_id_len: u32) -> Option<String> {
    if device_id.is_null() || device_id_len == 0 {
        return None;
    }
    // SAFETY: the caller guarantees `device_id_len` readable code units.
    let units = unsafe { std::slice::from_raw_parts(device_id, device_id_len as usize) };
    Some(String::from_utf16_lossy(units))
}

/// Copies `text` into `out` as UTF-16, cutting it at the buffer's end; returns the units written.
fn copy_wide(text: &str, out: &mut [u16]) -> u16 {
    let mut written = 0u16;
    for (slot, unit) in out.iter_mut().zip(text.encode_utf16()) {
        *slot = unit;
        written += 1;
    }
    written
}

/// Runs `work` with COM initialised on this thread, as Core Audio needs.
fn with_com<T>(work: impl FnOnce() -> Result<T, AudioError>) -> Result<T, AudioError> {
    // SAFETY: balanced below when it succeeded; an existing apartment is used as it is.
    let initialised = unsafe {
        windows::Win32::System::Com::CoInitializeEx(
            None,
            windows::Win32::System::Com::COINIT_MULTITHREADED,
        )
    }
    .is_ok();
    let result = work();
    if initialised {
        // SAFETY: balances the successful CoInitializeEx above, on this thread.
        unsafe { windows::Win32::System::Com::CoUninitialize() };
    }
    result
}

#[cfg(test)]
#[path = "ffi_audio_tests.rs"]
mod tests;
