//! DXGI Desktop Duplication.
//!
//! Duplication requires the capturing device to be created on the adapter that drives the display
//! being captured. On a hybrid laptop the desktop is usually driven by the integrated GPU while
//! the discrete GPU holds the better encoder, so the adapter is found by asking which one actually
//! has an output attached rather than by picking the fastest-looking card. Getting that backwards
//! is the single most common way this API fails on this class of machine.
//!
//! Frames stay on the GPU. `AcquireNextFrame` hands back a texture that the encoder can consume
//! directly; copying it to system memory would cost more than the entire rest of the host budget.

use std::ops::ControlFlow;
use windows::core::Interface;
use windows::Win32::Graphics::Direct3D::{D3D_DRIVER_TYPE_UNKNOWN, D3D_FEATURE_LEVEL_11_0};
use windows::Win32::Graphics::Direct3D11::{
    D3D11CreateDevice, ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D,
    D3D11_CREATE_DEVICE_BGRA_SUPPORT, D3D11_CREATE_DEVICE_VIDEO_SUPPORT, D3D11_SDK_VERSION,
};
use windows::Win32::Graphics::Dxgi::Common::{
    DXGI_MODE_ROTATION, DXGI_MODE_ROTATION_IDENTITY, DXGI_MODE_ROTATION_UNSPECIFIED,
};
use windows::Win32::Graphics::Dxgi::{
    IDXGIAdapter1, IDXGIOutput1, IDXGIOutputDuplication, IDXGIResource, DXGI_ERROR_ACCESS_LOST,
    DXGI_ERROR_WAIT_TIMEOUT, DXGI_OUTDUPL_FRAME_INFO, DXGI_OUTDUPL_POINTER_SHAPE_INFO,
    DXGI_OUTDUPL_POINTER_SHAPE_TYPE_COLOR, DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MASKED_COLOR,
    DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MONOCHROME,
};

use super::pointer::{PointerKind, PointerShape, PointerTrack};
use super::{CaptureError, CaptureFormat, FrameOutcome};

/// A live duplication of one display.
pub struct DesktopDuplication {
    device: ID3D11Device,
    context: ID3D11DeviceContext,
    duplication: IDXGIOutputDuplication,
    format: CaptureFormat,
    holding_frame: bool,
    /// Whether the display is turned on its side, where the pointer is not drawn: its picture
    /// already reaches the TV sideways, and Windows reports the pointer in the turned desktop's
    /// terms.
    rotated: bool,
    show_pointer: bool,
    pointer: PointerTrack,
}

impl DesktopDuplication {
    /// Opens the primary display for duplication.
    ///
    /// # Errors
    /// Returns [`CaptureError::NoDisplayAdapter`] when nothing on this host drives a display, and
    /// [`CaptureError::Platform`] when Windows refuses - most often because a duplication is
    /// already held by another process, or the session is not an interactive desktop.
    pub fn open_primary() -> Result<Self, CaptureError> {
        Self::open(0)
    }

    /// Opens a specific display, counting outputs across adapters in enumeration order.
    ///
    /// # Errors
    /// See [`Self::open_primary`], plus [`CaptureError::NoSuchOutput`].
    pub fn open(output_index: u32) -> Result<Self, CaptureError> {
        let (adapter, output, adapter_luid) = find_output(output_index)?;

        let mut device: Option<ID3D11Device> = None;
        let mut context: Option<ID3D11DeviceContext> = None;

        // SAFETY: the adapter is a live DXGI interface, and both out-parameters are valid.
        // D3D_DRIVER_TYPE_UNKNOWN is required when passing an explicit adapter.
        unsafe {
            D3D11CreateDevice(
                &adapter,
                D3D_DRIVER_TYPE_UNKNOWN,
                windows::Win32::Foundation::HMODULE::default(),
                // BGRA support is what lets the duplicated surface be shared with the video
                // processor without an intermediate copy. Video support is what lets that
                // processor exist at all: without it the device still exposes ID3D11VideoDevice,
                // and every view it is asked to create is refused with "the parameter is
                // incorrect" - which is how the GPU colour-conversion path fails when this flag is
                // missing, several layers away from the flag itself.
                D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
                Some(&[D3D_FEATURE_LEVEL_11_0]),
                D3D11_SDK_VERSION,
                Some(&raw mut device),
                None,
                Some(&raw mut context),
            )
            .map_err(platform)?;
        }

        let device = device.ok_or_else(|| CaptureError::Platform("no D3D11 device".into()))?;
        let context = context.ok_or_else(|| CaptureError::Platform("no D3D11 context".into()))?;

        // SAFETY: `output` is a live IDXGIOutput1 and `device` was just created on its adapter.
        let duplication = unsafe { output.DuplicateOutput(&device) }.map_err(platform)?;

        // SAFETY: the duplication is live; GetDesc returns its description by value.
        let description = unsafe { duplication.GetDesc() };

        Ok(Self {
            device,
            context,
            duplication,
            format: CaptureFormat {
                width: description.ModeDesc.Width,
                height: description.ModeDesc.Height,
                adapter_luid,
            },
            holding_frame: false,
            rotated: is_turned(description.Rotation),
            show_pointer: false,
            pointer: PointerTrack::default(),
        })
    }

    /// Draws the pointer into frames from now on, or stops.
    ///
    /// While it is drawn, a pointer that moves over a still desktop is a new frame, so the pointer
    /// moves on the TV too.
    pub fn set_show_pointer(&mut self, show: bool) {
        self.show_pointer = show;
    }

    /// The pointer to draw into the frame just taken, or `None` when it is not drawn.
    #[must_use]
    pub fn pointer(&self) -> Option<&PointerTrack> {
        (self.show_pointer && !self.rotated).then_some(&self.pointer)
    }

    /// The frame geometry and the adapter the frames live on.
    #[must_use]
    pub fn format(&self) -> CaptureFormat {
        self.format
    }

    /// The device frames are produced on, for handing to an encoder on the same adapter.
    #[must_use]
    pub fn device(&self) -> &ID3D11Device {
        &self.device
    }

    /// The immediate context, for copies on the capture device.
    #[must_use]
    pub fn context(&self) -> &ID3D11DeviceContext {
        &self.context
    }

    /// Waits up to `timeout_ms` for the desktop to change, and takes the new frame if it does.
    ///
    /// The returned texture is owned by the duplication until [`Self::release`] is called, so the
    /// caller must finish with it - copying or encoding - before asking for the next frame.
    ///
    /// # Errors
    /// [`CaptureError::Interrupted`] means the duplication must be re-created; the caller should do
    /// that and carry on rather than ending the session. A lock screen, a UAC prompt, or a
    /// resolution change all arrive this way.
    pub fn acquire(
        &mut self,
        timeout_ms: u32,
    ) -> Result<(FrameOutcome, Option<ID3D11Texture2D>), CaptureError> {
        // A frame still held would make the next acquire fail; release defensively so a caller
        // that skipped it cannot wedge the stream.
        self.release();

        let mut info = DXGI_OUTDUPL_FRAME_INFO::default();
        let mut resource: Option<IDXGIResource> = None;

        // SAFETY: both out-parameters are valid, and the duplication is live.
        let result = unsafe {
            self.duplication
                .AcquireNextFrame(timeout_ms, &raw mut info, &raw mut resource)
        };

        if let Err(error) = result {
            return match error.code() {
                DXGI_ERROR_WAIT_TIMEOUT => Ok((FrameOutcome::Unchanged, None)),
                DXGI_ERROR_ACCESS_LOST => Err(CaptureError::Interrupted),
                _ => Err(platform(error)),
            };
        }

        self.holding_frame = true;
        let pointer_changed = self.track_pointer(&info);

        // A frame with no accumulated updates carries no new pixels: the desktop reported only a
        // pointer move. Unless the pointer is drawn, treating it as new would send a duplicate
        // frame down the wire.
        if !is_new(
            info.LastPresentTime,
            pointer_changed,
            self.pointer().is_some(),
        ) {
            return Ok((FrameOutcome::Unchanged, None));
        }

        let resource = resource.ok_or_else(|| CaptureError::Platform("no frame surface".into()))?;
        let texture: ID3D11Texture2D = resource.cast().map_err(platform)?;
        Ok((FrameOutcome::Captured, Some(texture)))
    }

    /// Takes what this frame says about the pointer; returns whether the picture would change.
    fn track_pointer(&mut self, info: &DXGI_OUTDUPL_FRAME_INFO) -> bool {
        let mut changed = false;
        if info.LastMouseUpdateTime != 0 {
            let position = info.PointerPosition;
            changed |= self.pointer.moved_to(
                position
                    .Visible
                    .as_bool()
                    .then_some((position.Position.x, position.Position.y)),
            );
        }

        if info.PointerShapeBufferSize > 0 {
            if let Some(shape) = self.read_pointer_shape(info.PointerShapeBufferSize) {
                changed |= self.pointer.reshape(shape);
            }
        }

        changed
    }

    /// Reads the pointer's new shape, which Windows offers only on the frame it changed.
    fn read_pointer_shape(&self, size: u32) -> Option<PointerShape> {
        let mut data = vec![0u8; size as usize];
        let mut required = 0u32;
        let mut shape = DXGI_OUTDUPL_POINTER_SHAPE_INFO::default();
        // SAFETY: a frame is held, and the buffer is as large as the frame said the shape is.
        unsafe {
            self.duplication.GetFramePointerShape(
                size,
                data.as_mut_ptr().cast(),
                &raw mut required,
                &raw mut shape,
            )
        }
        .ok()?;
        pointer_shape(&shape, data)
    }

    /// Returns the current frame to the duplication.
    ///
    /// Safe to call when no frame is held, so callers need not track it themselves.
    pub fn release(&mut self) {
        if !self.holding_frame {
            return;
        }

        // SAFETY: a frame is held, which is exactly when ReleaseFrame is valid. A failure here
        // means the duplication is already lost, which the next acquire reports properly.
        let _ = unsafe { self.duplication.ReleaseFrame() };
        self.holding_frame = false;
    }
}

impl Drop for DesktopDuplication {
    fn drop(&mut self) {
        self.release();
    }
}

/// Whether a frame has something new to send: new desktop pixels, or a pointer that is drawn and
/// moved or changed shape.
/// Whether a display is turned, so a pointer drawn at its reported place would land wrongly.
pub(crate) fn is_turned(rotation: DXGI_MODE_ROTATION) -> bool {
    rotation != DXGI_MODE_ROTATION_IDENTITY && rotation != DXGI_MODE_ROTATION_UNSPECIFIED
}

pub(crate) fn is_new(last_present_time: i64, pointer_changed: bool, drawing_pointer: bool) -> bool {
    last_present_time != 0 || (pointer_changed && drawing_pointer)
}

/// A shape as Windows described it, or `None` for a kind this code does not draw.
pub(crate) fn pointer_shape(
    info: &DXGI_OUTDUPL_POINTER_SHAPE_INFO,
    data: Vec<u8>,
) -> Option<PointerShape> {
    let kind = match info.Type {
        kind if kind == DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MONOCHROME.0 as u32 => {
            PointerKind::Monochrome
        }
        kind if kind == DXGI_OUTDUPL_POINTER_SHAPE_TYPE_COLOR.0 as u32 => PointerKind::Color,
        kind if kind == DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MASKED_COLOR.0 as u32 => {
            PointerKind::MaskedColor
        }
        _ => return None,
    };
    Some(PointerShape {
        kind,
        width: info.Width,
        // A monochrome shape's height counts both of its masks.
        height: if kind == PointerKind::Monochrome {
            info.Height / 2
        } else {
            info.Height
        },
        pitch: info.Pitch,
        hotspot: (info.HotSpot.x, info.HotSpot.y),
        data,
    })
}

/// Finds the requested output and the adapter that drives it.
///
/// Outputs are counted across adapters in enumeration order, so index 0 is the first display on
/// the first adapter that has one - which on a hybrid laptop is the integrated GPU, not the
/// discrete card. The count is [`super::outputs::walk`]'s, the same one the display list shows.
fn find_output(target: u32) -> Result<(IDXGIAdapter1, IDXGIOutput1, i64), CaptureError> {
    let walked = super::outputs::walk_dxgi(|index, adapter, luid, output| {
        if index != target {
            return Ok(ControlFlow::Continue(()));
        }
        let output1: IDXGIOutput1 = output.cast().map_err(platform)?;
        Ok(ControlFlow::Break((adapter.clone(), output1, luid)))
    })?;

    match walked {
        ControlFlow::Break(found) => Ok(found),
        ControlFlow::Continue(0) => Err(CaptureError::NoDisplayAdapter),
        ControlFlow::Continue(_) => Err(CaptureError::NoSuchOutput(target)),
    }
}

fn platform(error: windows::core::Error) -> CaptureError {
    CaptureError::Platform(error.message())
}

/// Whether this host can capture the screen at all.
///
/// Opens a duplication and immediately drops it. Probing by doing rather than by inspecting is
/// deliberate: duplication fails for reasons no amount of enumeration reveals - another process
/// already holds it, the session is not an interactive desktop, or group policy forbids it - and
/// a capability report that guessed would be wrong in exactly those cases.
#[must_use]
pub fn is_available() -> bool {
    DesktopDuplication::open_primary().is_ok()
}

#[cfg(test)]
#[path = "duplication_tests.rs"]
mod tests;

#[cfg(test)]
mod live_probe {
    use super::*;

    /// Reports what capture actually did on this machine.
    ///
    /// Ignored by default because its result depends on the session rather than on the code; run
    /// it deliberately when verifying capture on a new host.
    #[test]
    #[ignore = "depends on an interactive desktop; run deliberately"]
    fn report_capture_state() {
        match DesktopDuplication::open_primary() {
            Ok(mut duplication) => {
                let format = duplication.format();
                println!(
                    "capture OPEN: {}x{} on adapter luid {}",
                    format.width, format.height, format.adapter_luid
                );

                // Poll briefly; a still desktop legitimately yields nothing.
                for attempt in 0..20 {
                    match duplication.acquire(100) {
                        Ok((FrameOutcome::Captured, Some(_))) => {
                            println!("captured a frame on attempt {attempt}");
                            return;
                        }
                        Ok(_) => {}
                        Err(error) => {
                            println!("acquire failed: {error}");
                            return;
                        }
                    }
                }

                println!("no frame in 2s (a completely still desktop does this)");
            }
            Err(error) => println!("capture UNAVAILABLE: {error}"),
        }
    }
}
