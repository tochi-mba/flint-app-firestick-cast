//! The C ABI the managed shell calls.
//!
//! Kept deliberately small. Most functions are one-shot queries or session lifecycle calls; the
//! mirror tick copies one already-encoded access unit into a caller-owned reusable buffer.
//!
//! # Safety contract
//!
//! * Every pointer argument must be non-null and correctly aligned, unless documented otherwise.
//! * Returned strings are UTF-8, NUL-terminated, and owned by the caller's buffer; the engine never
//!   hands out an allocation the caller must free.
//! * No panic crosses this boundary. Each entry point catches unwinds, and the release profile
//!   aborts rather than unwinding in the first place.

use std::panic::{catch_unwind, AssertUnwindSafe};

use crate::encode::mediafoundation;

/// Status codes returned across the boundary.
///
/// Values are stable: the managed side matches on them, so an existing code never changes meaning.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(i32)]
pub enum FlintStatus {
    /// The call succeeded.
    Ok = 0,
    /// A required pointer was null.
    NullArgument = -1,
    /// The caller's buffer was too small; nothing was written.
    BufferTooSmall = -2,
    /// The platform refused the request. Diagnostic detail is in the returned text.
    PlatformError = -3,
    /// The engine panicked and recovered. Always a defect in the engine.
    InternalError = -4,
    /// A scalar configuration value was outside its documented range.
    InvalidArgument = -5,
}

/// A hardware encoder, flattened for the ABI.
///
/// `codecs` is a bitmask rather than a list so the struct stays fixed-size and needs no allocation
/// on either side of the boundary.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(C)]
pub struct FlintEncoder {
    /// Vendor, matching `EncoderVendor`.
    pub vendor: u8,
    /// Padding, so the struct's layout is explicit rather than implied.
    pub _reserved: [u8; 7],
    /// The DXGI adapter LUID this encoder belongs to.
    pub adapter_luid: i64,
    /// Bit 0 is H.264, bit 1 is H.265, bit 2 is AV1.
    pub codec_mask: u32,
    /// Padding to an eight-byte boundary.
    pub _reserved2: u32,
}

/// A DXGI graphics adapter, flattened for the ABI.
///
/// The fixed UTF-16 description keeps ownership on the caller's stack and avoids an allocation or
/// a second native call. `description_len` is measured in UTF-16 code units and never exceeds 128.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(C)]
pub struct FlintAdapter {
    /// The adapter's locally unique identifier.
    pub adapter_luid: i64,
    /// One when at least one output is attached to the Windows desktop.
    pub drives_display: u8,
    /// One when this adapter owns the output at virtual-desktop origin `(0, 0)`.
    pub drives_primary_display: u8,
    /// One for WARP or another software renderer.
    pub is_software: u8,
    /// Explicit padding before the 16-bit fields.
    pub _reserved: u8,
    /// Number of meaningful UTF-16 code units in `description`.
    pub description_len: u16,
    /// Explicit padding so `description` starts on an eight-byte boundary.
    pub _reserved2: u16,
    /// DXGI's adapter description, without a required NUL terminator.
    pub description: [u16; 128],
}

/// Bit positions in [`FlintEncoder::codec_mask`].
pub mod codec_bits {
    /// H.264 / AVC.
    pub const H264: u32 = 1 << 0;
    /// H.265 / HEVC.
    pub const H265: u32 = 1 << 1;
    /// AV1.
    pub const AV1: u32 = 1 << 2;
}

/// Writes the engine version into `buffer` as UTF-8 with a NUL terminator.
///
/// # Safety
/// `buffer` must point to at least `capacity` writable bytes.
#[no_mangle]
pub unsafe extern "C" fn flint_version(buffer: *mut u8, capacity: usize) -> i32 {
    if buffer.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let version = crate::VERSION.as_bytes();
    if capacity < version.len() + 1 {
        return FlintStatus::BufferTooSmall as i32;
    }

    // SAFETY: the capacity check above guarantees room for the string and its terminator.
    unsafe {
        std::ptr::copy_nonoverlapping(version.as_ptr(), buffer, version.len());
        buffer.add(version.len()).write(0);
    }

    FlintStatus::Ok as i32
}

/// Probes the host's hardware encoders.
///
/// Writes up to `capacity` entries into `encoders` and stores the number actually found in
/// `found`. When more encoders exist than fit, `found` still reports the true total so the caller
/// can retry with a larger buffer; the entries written are the first `capacity` of them.
///
/// A host with no hardware encoder is a success with `found` set to zero. That is a real answer,
/// not a failure, and the capability report depends on being able to tell it apart from an error.
///
/// # Safety
/// `encoders` must point to at least `capacity` writable [`FlintEncoder`] values, and `found` must
/// point to a writable `u32`.
#[no_mangle]
pub unsafe extern "C" fn flint_probe_encoders(
    encoders: *mut FlintEncoder,
    capacity: usize,
    found: *mut u32,
) -> i32 {
    if found.is_null() || (encoders.is_null() && capacity > 0) {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        let Ok(inventory) = mediafoundation::probe() else {
            return Err(FlintStatus::PlatformError);
        };

        // SAFETY: checked non-null above.
        unsafe { found.write(inventory.encoders.len().min(u32::MAX as usize) as u32) };

        for (index, encoder) in inventory.encoders.iter().take(capacity).enumerate() {
            let mut codec_mask = 0u32;
            for codec in &encoder.codecs {
                codec_mask |= match codec {
                    crate::encode::VideoCodec::H264 => codec_bits::H264,
                    crate::encode::VideoCodec::H265 => codec_bits::H265,
                    crate::encode::VideoCodec::Av1 => codec_bits::AV1,
                };
            }

            // SAFETY: `index` is bounded by `capacity`, and the caller guaranteed that many slots.
            unsafe {
                encoders.add(index).write(FlintEncoder {
                    vendor: encoder.vendor as u8,
                    _reserved: [0; 7],
                    adapter_luid: encoder.adapter_luid,
                    codec_mask,
                    _reserved2: 0,
                });
            }
        }

        Ok(())
    }));

    match result {
        Ok(Ok(())) => FlintStatus::Ok as i32,
        Ok(Err(status)) => status as i32,
        Err(_) => FlintStatus::InternalError as i32,
    }
}

/// Probes the host's DXGI adapters.
///
/// Writes up to `capacity` entries and reports the true inventory size in `found`, following the
/// same truncation contract as [`flint_probe_encoders`]. A zero-capacity call may pass a null
/// `adapters` pointer and can be used to query the required count.
///
/// # Safety
/// `adapters` must point to at least `capacity` writable [`FlintAdapter`] values, and `found` must
/// point to a writable `u32`.
#[no_mangle]
pub unsafe extern "C" fn flint_probe_adapters(
    adapters: *mut FlintAdapter,
    capacity: usize,
    found: *mut u32,
) -> i32 {
    if found.is_null() || (adapters.is_null() && capacity > 0) {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        let Ok(inventory) = mediafoundation::adapters() else {
            return Err(FlintStatus::PlatformError);
        };

        // SAFETY: checked non-null above.
        unsafe { found.write(inventory.len().min(u32::MAX as usize) as u32) };

        for (index, adapter) in inventory.iter().take(capacity).enumerate() {
            let mut description = [0u16; 128];
            let mut description_len = 0usize;
            for (description_index, code_unit) in adapter
                .description
                .encode_utf16()
                .take(description.len())
                .enumerate()
            {
                description[description_index] = code_unit;
                description_len = description_index + 1;
            }

            // SAFETY: `index` is bounded by `capacity`, and the caller guaranteed that many slots.
            unsafe {
                adapters.add(index).write(FlintAdapter {
                    adapter_luid: adapter.luid,
                    drives_display: u8::from(adapter.drives_display),
                    drives_primary_display: u8::from(adapter.drives_primary_display),
                    is_software: u8::from(adapter.is_software),
                    _reserved: 0,
                    description_len: description_len as u16,
                    _reserved2: 0,
                    description,
                });
            }
        }

        Ok(())
    }));

    match result {
        Ok(Ok(())) => FlintStatus::Ok as i32,
        Ok(Err(status)) => status as i32,
        Err(_) => FlintStatus::InternalError as i32,
    }
}

/// Counts the host's graphics adapters.
///
/// # Safety
/// `count` must point to a writable `u32`.
#[no_mangle]
pub unsafe extern "C" fn flint_adapter_count(count: *mut u32) -> i32 {
    if count.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| match mediafoundation::adapters() {
        Ok(adapters) => {
            // SAFETY: checked non-null above.
            unsafe { count.write(adapters.len() as u32) };
            FlintStatus::Ok
        }
        Err(_) => FlintStatus::PlatformError,
    }));

    match result {
        Ok(status) => status as i32,
        Err(_) => FlintStatus::InternalError as i32,
    }
}

#[cfg(test)]
#[path = "ffi_tests.rs"]
mod tests;

/// What the engine can capture on this host.
///
/// A fixed-size struct rather than a status code alone, because the shell wants the geometry for
/// diagnostics and the adapter LUID to decide whether encoding will cross adapters.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(C)]
pub struct FlintCapture {
    /// Non-zero when this host can capture the screen.
    pub available: u8,
    /// The backend in use: 1 for Desktop Duplication. Zero when unavailable.
    pub backend: u8,
    /// Padding, so the layout is explicit rather than implied.
    pub _reserved: [u8; 6],
    /// Frame width in pixels, or zero when unavailable.
    pub width: u32,
    /// Frame height in pixels, or zero when unavailable.
    pub height: u32,
    /// The adapter frames are produced on, or zero when unavailable.
    pub adapter_luid: i64,
}

/// Backend identifiers reported in [`FlintCapture::backend`].
pub mod capture_backend {
    /// DXGI Desktop Duplication.
    pub const DESKTOP_DUPLICATION: u8 = 1;
}

/// Probes whether this host can capture the screen, and how.
///
/// Probes by opening a duplication and closing it, rather than by inspecting adapters. Capture
/// fails for reasons no enumeration reveals — another process already holds the duplication, the
/// session is not an interactive desktop, policy forbids it — and a report that guessed would be
/// wrong in exactly those cases.
///
/// An unavailable host is a success with `available` set to zero. That is an answer, not an error.
///
/// # Safety
/// `capture` must point to a writable [`FlintCapture`].
#[no_mangle]
pub unsafe extern "C" fn flint_probe_capture(capture: *mut FlintCapture) -> i32 {
    if capture.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        let probed = match crate::capture::duplication::DesktopDuplication::open_primary() {
            Ok(duplication) => {
                let format = duplication.format();
                FlintCapture {
                    available: 1,
                    backend: capture_backend::DESKTOP_DUPLICATION,
                    _reserved: [0; 6],
                    width: format.width,
                    height: format.height,
                    adapter_luid: format.adapter_luid,
                }
            }
            Err(_) => FlintCapture {
                available: 0,
                backend: 0,
                _reserved: [0; 6],
                width: 0,
                height: 0,
                adapter_luid: 0,
            },
        };

        // SAFETY: checked non-null above.
        unsafe { capture.write(probed) };
    }));

    match result {
        Ok(()) => FlintStatus::Ok as i32,
        Err(_) => FlintStatus::InternalError as i32,
    }
}

#[cfg(test)]
#[path = "ffi_capture_tests.rs"]
mod capture_ffi_tests;

/// What a mirror session should produce.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(C)]
pub struct FlintMirrorConfig {
    /// Which display to capture, counted in DXGI output order.
    pub output_index: u32,
    /// Target frames per second.
    pub frame_rate: u32,
    /// Target bitrate, bits per second.
    pub bitrate_bits_per_second: u32,
    /// Longest horizontal edge to encode, or zero to encode at the desktop's own size.
    ///
    /// A 4K desktop mirrored to a 1080p television gains nothing from encoding at source
    /// resolution and costs both encode time and bandwidth, so the shell caps it here rather than
    /// discovering the cost per frame.
    pub max_width: u32,
}

/// The result of one mirror tick.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(C)]
pub struct FlintMirrorFrame {
    /// One of the values in [`mirror_tick`].
    pub kind: i32,
    /// One when a decoder can start from this access unit alone.
    pub key_frame: u8,
    /// Explicit padding, so the layout is stated rather than implied.
    pub _reserved: [u8; 3],
    /// Presentation time relative to the start of the session.
    pub presentation_time_us: i64,
    /// Bytes written into the caller's buffer.
    pub byte_count: u32,
    /// Explicit padding to an eight-byte boundary.
    pub _reserved2: u32,
}

impl FlintMirrorFrame {
    const EMPTY: Self = Self {
        kind: mirror_tick::NOTHING,
        key_frame: 0,
        _reserved: [0; 3],
        presentation_time_us: 0,
        byte_count: 0,
        _reserved2: 0,
    };
}

/// Counters for the diagnostics view and for adaptive bitrate.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(C)]
pub struct FlintMirrorStats {
    /// Frames the encoder produced an access unit for.
    pub frames_encoded: u64,
    /// Ticks where the desktop had not changed.
    pub frames_unchanged: u64,
    /// Times capture was lost and re-created.
    pub recoveries: u64,
    /// Total encoded bytes handed to the transport.
    pub bytes_encoded: u64,
    /// Encoded frame width, which may be smaller than the desktop's.
    pub width: u32,
    /// Encoded frame height.
    pub height: u32,
}

/// Values for [`FlintMirrorFrame::kind`].
pub mod mirror_tick {
    /// Nothing to send: the desktop had not changed, or the encoder is still filling.
    pub const NOTHING: i32 = 0;
    /// An access unit was written to the caller's buffer.
    pub const ENCODED: i32 = 1;
    /// Capture was lost and re-created; the next access unit will be a key frame.
    pub const RECOVERED: i32 = 2;
}

/// A live mirror session, opaque to the caller.
pub struct FlintMirrorSession {
    session: crate::session::MirrorSession<
        crate::capture::readback::DesktopFrameSource,
        crate::encode::selected::SelectedEncoder,
    >,
}

/// Starts a mirror session over one display.
///
/// # Safety
/// `config` and `out_handle` must be non-null and correctly aligned. On success `out_handle`
/// receives a session that must be released with [`flint_mirror_stop`] exactly once. Any failure
/// writes a null handle when `out_handle` itself is valid.
#[no_mangle]
pub unsafe extern "C" fn flint_mirror_start(
    config: *const FlintMirrorConfig,
    out_handle: *mut *mut FlintMirrorSession,
) -> i32 {
    if out_handle.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    // A failed start never leaves a caller's stale handle looking live.
    // SAFETY: checked non-null above; the caller guarantees it is aligned and writable.
    unsafe { *out_handle = std::ptr::null_mut() };
    if config.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: checked non-null above; the caller guarantees alignment.
        let config = unsafe { *config };
        if config.frame_rate == 0 || config.bitrate_bits_per_second == 0 || config.max_width == 1 {
            return FlintStatus::InvalidArgument as i32;
        }

        let Ok(mut source) =
            crate::capture::readback::DesktopFrameSource::open(config.output_index)
        else {
            return FlintStatus::PlatformError as i32;
        };

        let format = source.format();
        let (width, height) = crate::convert::scale::encoded_frame_size(
            format.width,
            format.height,
            config.max_width,
        );

        // Asked for up front, and only used by the readback path below: the GPU path scales inside
        // the video processor as part of the colour conversion, so the target it is given here is
        // irrelevant to it.
        //
        // On the readback path this decides whether the frame is reduced before or after the bus
        // copy, which `report_stage_timings` measures at roughly 6.8ms per frame against 12.8ms —
        // the copy carries four times the pixels when the reduction happens afterwards. The source
        // reduces by whatever means it can and silently declines when it cannot, so asking is
        // always safe.
        source.set_target_size((width, height));

        // The fast path: keep the frame on the GPU from capture to encoder.
        //
        // Both halves have to agree or neither works. The encoder is built on the *capture* device,
        // because two devices on one adapter cannot pass textures without shared handles, and the
        // source only hands out textures once that encoder exists. When either step fails the
        // source is left on readback and selection falls back to whatever encoder it can open,
        // which is a slower session rather than a broken one.
        let encoder_config = crate::encode::video::EncoderConfig {
            width,
            height,
            frame_rate: config.frame_rate,
            bitrate_bits_per_second: config.bitrate_bits_per_second,
            codec: crate::encode::VideoCodec::H264,
        };

        let gpu_encoder = crate::encode::selected::SelectedEncoder::open_on_device(
            encoder_config,
            format.adapter_luid,
            source.device().clone(),
            source.context().clone(),
        )
        .ok()
        .filter(|_| source.enable_gpu_frames().is_ok());

        if let Some(encoder) = gpu_encoder {
            let boxed = Box::new(FlintMirrorSession {
                session: crate::session::MirrorSession::new(source, encoder),
            });
            // SAFETY: checked non-null above.
            unsafe { *out_handle = Box::into_raw(boxed) };
            return FlintStatus::Ok as i32;
        }

        // Hardware where the capture adapter has an encode block, software everywhere else. The
        // adapter comes from the capture format rather than from a survey of the machine: the
        // encoder must live where the frames already are, and on a hybrid laptop the fastest GPU is
        // the wrong answer. Selection never fails over to a *different* adapter, only to software.
        let Ok(encoder) =
            crate::encode::selected::SelectedEncoder::open(encoder_config, format.adapter_luid)
        else {
            return FlintStatus::PlatformError as i32;
        };

        let boxed = Box::new(FlintMirrorSession {
            session: crate::session::MirrorSession::new(source, encoder),
        });
        // SAFETY: checked non-null above.
        unsafe { *out_handle = Box::into_raw(boxed) };
        FlintStatus::Ok as i32
    }));

    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Runs one tick, writing any access unit into `buffer`.
///
/// A tick that produced nothing is still a success: a still desktop legitimately produces those
/// continuously, and treating it as an error would end a healthy session.
///
/// # Safety
/// `handle` must come from [`flint_mirror_start`] and not yet have been stopped. `buffer` must
/// point to at least `capacity` writable bytes, and `out_frame` must be non-null.
#[no_mangle]
pub unsafe extern "C" fn flint_mirror_next(
    handle: *mut FlintMirrorSession,
    buffer: *mut u8,
    capacity: u32,
    out_frame: *mut FlintMirrorFrame,
) -> i32 {
    if out_frame.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    // Initialise the result before any fallible work, including the unwind guard.
    // SAFETY: checked non-null above; the caller guarantees it is aligned and writable.
    unsafe { *out_frame = FlintMirrorFrame::EMPTY };
    if handle.is_null() || buffer.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live and not aliased.
        let session = unsafe { &mut (*handle).session };
        let mut frame = FlintMirrorFrame::EMPTY;

        let status = match session.tick() {
            Ok(crate::session::Tick::Unchanged) => FlintStatus::Ok as i32,
            Ok(crate::session::Tick::Recovered) => {
                frame.kind = mirror_tick::RECOVERED;
                FlintStatus::Ok as i32
            }
            Ok(crate::session::Tick::Encoded(encoded)) => {
                let Ok(byte_count) = u32::try_from(encoded.data.len()) else {
                    return FlintStatus::InternalError as i32;
                };
                if encoded.data.len() > capacity as usize {
                    // The access unit is reported as too large rather than truncated: half a frame
                    // decodes to garbage, and a decoder fed garbage stays broken until the next key
                    // frame rather than failing where the mistake was made.
                    frame.byte_count = byte_count;
                    FlintStatus::BufferTooSmall as i32
                } else {
                    // SAFETY: the length was just checked against the caller's capacity.
                    unsafe {
                        std::ptr::copy_nonoverlapping(
                            encoded.data.as_ptr(),
                            buffer,
                            encoded.data.len(),
                        );
                    }
                    frame.kind = mirror_tick::ENCODED;
                    frame.key_frame = u8::from(encoded.key_frame);
                    frame.presentation_time_us = encoded.presentation_time_us;
                    frame.byte_count = byte_count;
                    FlintStatus::Ok as i32
                }
            }
            Err(_) => FlintStatus::PlatformError as i32,
        };

        // SAFETY: checked non-null above.
        unsafe { *out_frame = frame };
        status
    }));

    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Copies one block of codec setup data into `buffer`.
///
/// The receiver cannot configure its decoder without these, so the shell reads them before it
/// sends the first access unit. `index` walks the blocks; `out_len` receives zero once `index` is
/// past the last one, and the block's true size when the buffer was too small.
///
/// A null `buffer` with a `capacity` of zero is a size query rather than a mistake: a caller has
/// to learn a block's length before it can allocate one, and demanding a throwaway buffer to ask
/// would make the obvious call the wrong one.
///
/// # Safety
/// `handle` must be live. `buffer` must point to at least `capacity` writable bytes, unless
/// `capacity` is zero, in which case it may be null. `out_len` must be non-null.
#[no_mangle]
pub unsafe extern "C" fn flint_mirror_codec_data(
    handle: *mut FlintMirrorSession,
    index: u32,
    buffer: *mut u8,
    capacity: u32,
    out_len: *mut u32,
) -> i32 {
    if out_len.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    // SAFETY: checked non-null above; the caller guarantees it is aligned and writable.
    unsafe { *out_len = 0 };
    if handle.is_null() || (buffer.is_null() && capacity > 0) {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live.
        let session = unsafe { &(*handle).session };
        let blocks = session.codec_specific_data();

        let Some(block) = blocks.get(index as usize) else {
            // SAFETY: checked non-null above.
            unsafe { *out_len = 0 };
            return FlintStatus::Ok as i32;
        };

        let Ok(block_len) = u32::try_from(block.len()) else {
            return FlintStatus::InternalError as i32;
        };
        // SAFETY: checked non-null above.
        unsafe { *out_len = block_len };
        if block_len == 0 {
            return FlintStatus::Ok as i32;
        }
        if block.len() > capacity as usize {
            return FlintStatus::BufferTooSmall as i32;
        }

        // SAFETY: the length was just checked against the caller's capacity.
        unsafe { std::ptr::copy_nonoverlapping(block.as_ptr(), buffer, block.len()) };
        FlintStatus::Ok as i32
    }));

    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Reports the codec this session's access units are actually in.
///
/// `out_codec` receives the wire protocol's own codec identifier, so the value can go straight
/// into the decoder configuration the receiver is sent.
///
/// This exists because the alternative — assuming the codec the handshake negotiated — produces a
/// receiver that builds a decoder for one codec and is fed another. That fails on the first access
/// unit as a black screen, nowhere near the mistake.
///
/// # Safety
/// `handle` must be live and `out_codec` non-null.
#[no_mangle]
pub unsafe extern "C" fn flint_mirror_codec(
    handle: *mut FlintMirrorSession,
    out_codec: *mut u32,
) -> i32 {
    if handle.is_null() || out_codec.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live.
        let codec = unsafe { (*handle).session.codec() };
        // SAFETY: checked non-null above.
        unsafe { *out_codec = codec as u32 };
        FlintStatus::Ok as i32
    }));

    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Asks for the next access unit to be a key frame.
///
/// # Safety
/// `handle` must be live.
#[no_mangle]
pub unsafe extern "C" fn flint_mirror_request_key_frame(handle: *mut FlintMirrorSession) -> i32 {
    if handle.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live.
        unsafe { (*handle).session.request_key_frame() };
        FlintStatus::Ok as i32
    }));

    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Reads the session counters and the encoded frame size.
///
/// # Safety
/// `handle` must be live and `out_stats` non-null.
#[no_mangle]
pub unsafe extern "C" fn flint_mirror_stats(
    handle: *mut FlintMirrorSession,
    out_stats: *mut FlintMirrorStats,
) -> i32 {
    if handle.is_null() || out_stats.is_null() {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees the handle is live.
        let session = unsafe { &(*handle).session };
        let stats = session.stats();
        let (width, height) = session.output_size();
        // SAFETY: checked non-null above.
        unsafe {
            *out_stats = FlintMirrorStats {
                frames_encoded: stats.frames_encoded,
                frames_unchanged: stats.frames_unchanged,
                recoveries: stats.recoveries,
                bytes_encoded: stats.bytes_encoded,
                width,
                height,
            };
        }
        FlintStatus::Ok as i32
    }));

    result.unwrap_or(FlintStatus::InternalError as i32)
}

/// Ends a mirror session and releases it.
///
/// A null handle is a no-op rather than an error, so a shell that tears down twice — which a
/// cancelled start followed by a dispose does — is safe.
///
/// # Safety
/// `handle` must come from [`flint_mirror_start`] and must not be used again afterwards.
#[no_mangle]
pub unsafe extern "C" fn flint_mirror_stop(handle: *mut FlintMirrorSession) -> i32 {
    if handle.is_null() {
        return FlintStatus::Ok as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: the caller guarantees this handle came from flint_mirror_start and is released
        // exactly once.
        drop(unsafe { Box::from_raw(handle) });
        FlintStatus::Ok as i32
    }));

    result.unwrap_or(FlintStatus::InternalError as i32)
}

#[cfg(test)]
#[path = "ffi_mirror_tests.rs"]
mod mirror_ffi_tests;
