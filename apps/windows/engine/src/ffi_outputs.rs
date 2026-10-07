//! The displays this host can share, across the C ABI.

use std::panic::{catch_unwind, AssertUnwindSafe};

use super::FlintStatus;
use crate::capture::outputs::DisplayOutput;

/// One display, flattened for the ABI.
///
/// `device_name` is Windows' GDI device name, such as `\\.\DISPLAY1`, which the shell uses to
/// find the monitor's own name. It is fixed-size, as Windows' own is, so the struct needs no
/// allocation on either side.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(C)]
pub struct FlintOutput {
    /// The index capture opens this display by, as passed in `FlintMirrorConfig::output_index`.
    pub index: u32,
    /// How the display is turned: 1 upright, 2 a quarter clockwise, 3 upside down, 4 a quarter
    /// anticlockwise.
    pub rotation: u32,
    /// The adapter that drives it.
    pub adapter_luid: i64,
    /// Its left edge on the Windows desktop.
    pub left: i32,
    /// Its top edge on the Windows desktop.
    pub top: i32,
    /// Its right edge on the Windows desktop, exclusive.
    pub right: i32,
    /// Its bottom edge on the Windows desktop, exclusive.
    pub bottom: i32,
    /// One when the display is part of the desktop.
    pub attached: u8,
    /// One when it is the display Windows calls the main one.
    pub main: u8,
    /// Explicit padding before the 16-bit fields.
    pub _reserved: [u8; 2],
    /// Number of meaningful UTF-16 code units in `device_name`.
    pub device_name_len: u16,
    /// Explicit padding so `device_name` starts on a four-byte boundary.
    pub _reserved2: u16,
    /// The device name, without a required NUL terminator.
    pub device_name: [u16; 32],
}

impl FlintOutput {
    fn from_display(display: &DisplayOutput) -> Self {
        let mut device_name = [0u16; 32];
        let mut device_name_len = 0u16;
        for (slot, unit) in device_name
            .iter_mut()
            .zip(display.device_name.encode_utf16())
        {
            *slot = unit;
            device_name_len += 1;
        }

        Self {
            index: display.index,
            rotation: display.rotation as u32,
            adapter_luid: display.adapter_luid,
            left: display.desktop.0,
            top: display.desktop.1,
            right: display.desktop.2,
            bottom: display.desktop.3,
            attached: u8::from(display.attached),
            main: u8::from(display.is_main()),
            _reserved: [0; 2],
            device_name_len,
            _reserved2: 0,
            device_name,
        }
    }
}

/// Lists the displays capture can open, in the order and with the indices capture uses.
///
/// Writes up to `capacity` entries and the true count to `found`. A zero-capacity call may pass a
/// null `outputs` pointer to ask for the count. A host with no display is a success with a count
/// of zero: that is an answer, not an error.
///
/// # Safety
/// `outputs` must point to at least `capacity` writable [`FlintOutput`] values, and `found` must
/// point to a writable `u32`.
#[no_mangle]
pub unsafe extern "C" fn flint_probe_outputs(
    outputs: *mut FlintOutput,
    capacity: usize,
    found: *mut u32,
) -> i32 {
    if found.is_null() || (outputs.is_null() && capacity > 0) {
        return FlintStatus::NullArgument as i32;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        let displays = crate::capture::outputs::list().map_err(|_| FlintStatus::PlatformError)?;
        write_outputs(&displays, outputs, capacity, found);
        Ok::<(), FlintStatus>(())
    }));

    match result {
        Ok(Ok(())) => FlintStatus::Ok as i32,
        Ok(Err(status)) => status as i32,
        Err(_) => FlintStatus::InternalError as i32,
    }
}

/// Copies `displays` into the caller's slots, truncating to `capacity`.
///
/// Split from the export so the copy can be tested with displays a build agent does not have.
fn write_outputs(
    displays: &[DisplayOutput],
    outputs: *mut FlintOutput,
    capacity: usize,
    found: *mut u32,
) {
    // SAFETY: the export checked `found` is non-null; the caller guarantees it is writable.
    unsafe { found.write(u32::try_from(displays.len()).unwrap_or(u32::MAX)) };

    for (slot, display) in displays.iter().take(capacity).enumerate() {
        // SAFETY: `slot` is below `capacity`, and the caller guaranteed that many slots.
        unsafe { outputs.add(slot).write(FlintOutput::from_display(display)) };
    }
}

#[cfg(test)]
#[path = "ffi_outputs_tests.rs"]
mod tests;
