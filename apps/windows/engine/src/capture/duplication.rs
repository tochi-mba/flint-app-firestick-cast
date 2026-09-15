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

use windows::core::Interface;
use windows::Win32::Graphics::Direct3D::{D3D_DRIVER_TYPE_UNKNOWN, D3D_FEATURE_LEVEL_11_0};
use windows::Win32::Graphics::Direct3D11::{
    D3D11CreateDevice, ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D,
    D3D11_CREATE_DEVICE_BGRA_SUPPORT, D3D11_CREATE_DEVICE_VIDEO_SUPPORT, D3D11_SDK_VERSION,
};
use windows::Win32::Graphics::Dxgi::{
    CreateDXGIFactory1, IDXGIAdapter1, IDXGIFactory1, IDXGIOutput1, IDXGIOutputDuplication,
    IDXGIResource, DXGI_ERROR_ACCESS_LOST, DXGI_ERROR_NOT_FOUND, DXGI_ERROR_WAIT_TIMEOUT,
    DXGI_OUTDUPL_FRAME_INFO,
};

use super::{CaptureError, CaptureFormat, FrameOutcome};

/// A live duplication of one display.
pub struct DesktopDuplication {
    device: ID3D11Device,
    context: ID3D11DeviceContext,
    duplication: IDXGIOutputDuplication,
    format: CaptureFormat,
    holding_frame: bool,
}

impl DesktopDuplication {
    /// Opens the primary display for duplication.
    ///
    /// # Errors
    /// Returns [`CaptureError::NoDisplayAdapter`] when nothing on this host drives a display, and
    /// [`CaptureError::Platform`] when Windows refuses — most often because a duplication is
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
                None,
                // BGRA support is what lets the duplicated surface be shared with the video
                // processor without an intermediate copy. Video support is what lets that
                // processor exist at all: without it the device still exposes ID3D11VideoDevice,
                // and every view it is asked to create is refused with "the parameter is
                // incorrect" — which is how the GPU colour-conversion path fails when this flag is
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
        })
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
    /// caller must finish with it — copying or encoding — before asking for the next frame.
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

        // A frame with no accumulated updates carries no new pixels: the desktop reported only a
        // pointer move. Treating it as new would send a duplicate frame down the wire.
        if info.LastPresentTime == 0 {
            return Ok((FrameOutcome::Unchanged, None));
        }

        let resource = resource.ok_or_else(|| CaptureError::Platform("no frame surface".into()))?;
        let texture: ID3D11Texture2D = resource.cast().map_err(platform)?;
        Ok((FrameOutcome::Captured, Some(texture)))
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

/// Finds the requested output and the adapter that drives it.
///
/// Outputs are counted across adapters in enumeration order, so index 0 is the first display on
/// the first adapter that has one — which on a hybrid laptop is the integrated GPU, not the
/// discrete card.
fn find_output(target: u32) -> Result<(IDXGIAdapter1, IDXGIOutput1, i64), CaptureError> {
    // SAFETY: CreateDXGIFactory1 is callable on any thread and returns a checked HRESULT.
    let factory: IDXGIFactory1 = unsafe { CreateDXGIFactory1() }.map_err(platform)?;
    let mut seen = 0u32;
    let mut any_output = false;

    for adapter_index in 0.. {
        // SAFETY: bounded by the DXGI_ERROR_NOT_FOUND break below.
        let adapter: IDXGIAdapter1 = match unsafe { factory.EnumAdapters1(adapter_index) } {
            Ok(adapter) => adapter,
            Err(error) if error.code() == DXGI_ERROR_NOT_FOUND => break,
            Err(error) => return Err(platform(error)),
        };

        // SAFETY: the adapter is live; GetDesc1 returns its description.
        let description = unsafe { adapter.GetDesc1() }.map_err(platform)?;
        let luid = (i64::from(description.AdapterLuid.HighPart) << 32)
            | i64::from(description.AdapterLuid.LowPart);

        for output_index in 0.. {
            // SAFETY: bounded by the DXGI_ERROR_NOT_FOUND break below.
            let output = match unsafe { adapter.EnumOutputs(output_index) } {
                Ok(output) => output,
                Err(error) if error.code() == DXGI_ERROR_NOT_FOUND => break,
                Err(error) => return Err(platform(error)),
            };

            any_output = true;
            if seen == target {
                let output1: IDXGIOutput1 = output.cast().map_err(platform)?;
                return Ok((adapter, output1, luid));
            }

            seen += 1;
        }
    }

    if any_output {
        Err(CaptureError::NoSuchOutput(target))
    } else {
        Err(CaptureError::NoDisplayAdapter)
    }
}

fn platform(error: windows::core::Error) -> CaptureError {
    CaptureError::Platform(error.message())
}

/// Whether this host can capture the screen at all.
///
/// Opens a duplication and immediately drops it. Probing by doing rather than by inspecting is
/// deliberate: duplication fails for reasons no amount of enumeration reveals — another process
/// already holds it, the session is not an interactive desktop, or group policy forbids it — and
/// a capability report that guessed would be wrong in exactly those cases.
#[must_use]
pub fn is_available() -> bool {
    DesktopDuplication::open_primary().is_ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    // These run against the real desktop. On a build agent with no interactive session they
    // correctly report no capture, which is the same answer the capability report would give.

    #[test]
    fn probing_never_panics_whatever_the_session() {
        let _ = is_available();
    }

    #[test]
    fn opening_an_absurd_output_index_is_refused() {
        let result = DesktopDuplication::open(9_999);
        assert!(matches!(
            result,
            Err(CaptureError::NoSuchOutput(9_999) | CaptureError::NoDisplayAdapter)
        ));
    }

    #[test]
    fn a_capture_reports_a_plausible_geometry_when_one_is_available() {
        let Ok(duplication) = DesktopDuplication::open_primary() else {
            // No interactive desktop here; nothing to assert.
            return;
        };

        let format = duplication.format();
        assert!(format.width > 0 && format.height > 0);
        assert!(format.adapter_luid != 0);
    }

    #[test]
    fn releasing_without_a_held_frame_is_harmless() {
        let Ok(mut duplication) = DesktopDuplication::open_primary() else {
            return;
        };

        duplication.release();
        duplication.release();
    }

    #[test]
    fn an_idle_desktop_reports_unchanged_rather_than_failing() {
        // A still desktop produces no frame, and that must not read as an error.
        let Ok(mut duplication) = DesktopDuplication::open_primary() else {
            return;
        };

        let outcome = duplication.acquire(50);
        assert!(outcome.is_ok(), "a timeout must not surface as an error");
    }
}

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
