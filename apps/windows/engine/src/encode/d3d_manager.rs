//! A Direct3D device manager, which is what makes a hardware encoder actually encode.
//!
//! A hardware MFT in asynchronous mode will accept `SetInputType`, `SetOutputType`, begin
//! streaming, and raise `METransformNeedInput` exactly as a healthy encoder does — and then never
//! produce a single access unit, because it has no device to encode *on*. Intel's Quick Sync
//! encoder behaves this way, and the failure is silent: no error code, no event, just an input
//! queue that swallows frames. This project spent an afternoon on it, watching every frame cost
//! precisely the pump's timeout and nothing else, which is the tell.
//!
//! The device manager fixes that by handing the transform a D3D11 device on the adapter the frames
//! were captured on, so the encoder reads them where they already live rather than needing them
//! copied down to system memory and back up again.
//!
//! Two details are not optional:
//!
//! * **Multithread protection.** The transform encodes on its own worker threads while the capture
//!   loop is still using the device. A D3D11 device is not thread-safe by default, and without
//!   `ID3D11Multithread::SetMultithreadProtected` the result is corruption or a device-removed
//!   error under load rather than a clean failure at startup.
//! * **The reset token.** `MFCreateDXGIDeviceManager` hands back a token that must be passed to the
//!   matching `ResetDevice`. It is not a handle to keep for later; it is proof to the manager that
//!   the caller is the one who created it, and any other value is rejected.

use windows::core::Interface;
use windows::Win32::Graphics::Direct3D::{D3D_DRIVER_TYPE_UNKNOWN, D3D_FEATURE_LEVEL_11_0};
use windows::Win32::Graphics::Direct3D11::{
    D3D11CreateDevice, ID3D11Device, ID3D11DeviceContext, ID3D11Multithread,
    D3D11_CREATE_DEVICE_BGRA_SUPPORT, D3D11_CREATE_DEVICE_VIDEO_SUPPORT, D3D11_SDK_VERSION,
};
use windows::Win32::Graphics::Dxgi::{
    CreateDXGIFactory1, IDXGIAdapter1, IDXGIFactory1, DXGI_ADAPTER_DESC1,
};
use windows::Win32::Media::MediaFoundation::{IMFDXGIDeviceManager, MFCreateDXGIDeviceManager};

use super::video::EncodeError;
use crate::encode::h264::platform;

/// A D3D11 device on a chosen adapter, wrapped in the manager a hardware MFT expects.
pub struct DeviceManager {
    manager: IMFDXGIDeviceManager,
    // Held so the device outlives the manager that points at it. Dropping the device first would
    // leave the transform encoding against freed memory.
    device: ID3D11Device,
    context: ID3D11DeviceContext,
}

impl DeviceManager {
    /// Builds a device manager on the adapter with `adapter_luid`.
    ///
    /// The LUID matters as much here as it does for choosing the encoder: a manager built on the
    /// discrete GPU while frames are captured on the integrated one forces every frame across the
    /// PCIe bus, which costs more than the faster encoder saves.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the adapter is gone, or when it has no video support — an
    /// ordinary answer on a machine with no hardware encoder, and the caller's cue to use software.
    pub fn for_adapter(adapter_luid: i64) -> Result<Self, EncodeError> {
        let adapter = find_adapter(adapter_luid)?;
        let (device, context) = create_video_device(&adapter)?;
        Self::wrap(device, context)
    }

    /// Builds a device manager around a device that already exists.
    ///
    /// This is how the encoder ends up on the *capture* device rather than one of its own. Two
    /// devices on the same adapter cannot pass textures to each other without shared handles, so a
    /// pipeline that captures on one and encodes on another has to go through system memory —
    /// which is the readback this whole path exists to remove.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the device cannot be shared with Media Foundation.
    pub fn for_device(
        device: ID3D11Device,
        context: ID3D11DeviceContext,
    ) -> Result<Self, EncodeError> {
        Self::wrap(device, context)
    }

    /// Enables multithread protection and wraps a device in a Media Foundation manager.
    fn wrap(device: ID3D11Device, context: ID3D11DeviceContext) -> Result<Self, EncodeError> {
        enable_multithread_protection(&device)?;

        let mut token = 0u32;
        let mut manager: Option<IMFDXGIDeviceManager> = None;
        // SAFETY: both out-parameters are valid for the duration of the call.
        unsafe { MFCreateDXGIDeviceManager(&raw mut token, &raw mut manager) }.map_err(platform)?;
        let manager =
            manager.ok_or_else(|| EncodeError::Platform("no DXGI device manager".into()))?;

        // SAFETY: the manager was just created with this exact token, which is the only value it
        // will accept, and the device is live.
        unsafe { manager.ResetDevice(&device, token) }.map_err(platform)?;

        Ok(Self {
            manager,
            device,
            context,
        })
    }

    /// The device the encoder reads its frames from.
    ///
    /// Shared deliberately: frames must be uploaded to *this* device, not to another one on the
    /// same adapter. A texture the encoder's device cannot see is exactly as useful to it as the
    /// system memory it was ignoring before.
    #[must_use]
    pub fn device(&self) -> &ID3D11Device {
        &self.device
    }

    /// The immediate context, for mapping and uploading frames.
    #[must_use]
    pub fn context(&self) -> &ID3D11DeviceContext {
        &self.context
    }

    /// The manager as the pointer-sized value `MFT_MESSAGE_SET_D3D_MANAGER` carries.
    ///
    /// Media Foundation passes interfaces through `ProcessMessage` as a raw pointer in the
    /// message parameter, so this is a borrow rather than a transfer: the transform takes its own
    /// reference, and this manager must outlive it either way.
    #[must_use]
    pub fn as_message_param(&self) -> usize {
        self.manager.as_raw() as usize
    }
}

/// Finds the DXGI adapter with a given LUID.
fn find_adapter(adapter_luid: i64) -> Result<IDXGIAdapter1, EncodeError> {
    // SAFETY: the out-parameter is valid and the factory is released with this scope.
    let factory: IDXGIFactory1 = unsafe { CreateDXGIFactory1() }.map_err(platform)?;

    for index in 0.. {
        // SAFETY: enumeration ends by returning an error, which breaks the loop.
        let Ok(adapter) = (unsafe { factory.EnumAdapters1(index) }) else {
            break;
        };
        // SAFETY: the adapter is live; GetDesc1 returns its description by value.
        let Ok(description) = (unsafe { adapter.GetDesc1() }) else {
            continue;
        };
        if luid_of(&description) == adapter_luid {
            return Ok(adapter);
        }
    }

    Err(EncodeError::Platform(format!(
        "no display adapter with LUID {adapter_luid}"
    )))
}

/// Recombines a split DXGI LUID into the single signed value the rest of Flint uses.
///
/// DXGI reports a LUID as a signed high word and an unsigned low word; treating the low word as
/// signed — the obvious-looking cast — corrupts every LUID with the high bit set, and those compare
/// unequal to the same adapter's LUID obtained anywhere else.
#[must_use]
fn luid_of(description: &DXGI_ADAPTER_DESC1) -> i64 {
    (i64::from(description.AdapterLuid.HighPart) << 32) | i64::from(description.AdapterLuid.LowPart)
}

/// Creates a D3D11 device with the video support a hardware encoder needs.
fn create_video_device(
    adapter: &IDXGIAdapter1,
) -> Result<(ID3D11Device, ID3D11DeviceContext), EncodeError> {
    let mut device: Option<ID3D11Device> = None;
    let mut context: Option<ID3D11DeviceContext> = None;
    // SAFETY: the adapter is live and the out-parameter is valid. D3D_DRIVER_TYPE_UNKNOWN is
    // required whenever an explicit adapter is passed.
    unsafe {
        D3D11CreateDevice(
            adapter,
            D3D_DRIVER_TYPE_UNKNOWN,
            None,
            // VIDEO_SUPPORT is the flag that matters: without it the device is created happily and
            // the encoder then refuses it, reporting nothing about which flag was missing. BGRA
            // support keeps this device interchangeable with the capture device.
            D3D11_CREATE_DEVICE_VIDEO_SUPPORT | D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            Some(&[D3D_FEATURE_LEVEL_11_0]),
            D3D11_SDK_VERSION,
            Some(&raw mut device),
            None,
            Some(&raw mut context),
        )
        .map_err(platform)?;
    }
    let device =
        device.ok_or_else(|| EncodeError::Platform("no D3D11 device for the encoder".into()))?;
    let context =
        context.ok_or_else(|| EncodeError::Platform("no D3D11 context for the encoder".into()))?;
    Ok((device, context))
}

/// Turns on the device's internal locking, which sharing it with a transform requires.
fn enable_multithread_protection(device: &ID3D11Device) -> Result<(), EncodeError> {
    let multithread = device.cast::<ID3D11Multithread>().map_err(platform)?;
    // SAFETY: the interface is live. The return value is the setting's *previous* state, not a
    // success code, so discarding it loses nothing.
    let _previously_protected = unsafe { multithread.SetMultithreadProtected(true) };
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use windows::Win32::Foundation::LUID;

    fn description_with(high: i32, low: u32) -> DXGI_ADAPTER_DESC1 {
        DXGI_ADAPTER_DESC1 {
            AdapterLuid: LUID {
                HighPart: high,
                LowPart: low,
            },
            ..Default::default()
        }
    }

    #[test]
    fn a_luid_recombines_its_two_halves() {
        assert_eq!(luid_of(&description_with(0, 0x0009_0002)), 0x0009_0002);
    }

    #[test]
    fn a_luid_keeps_its_high_word() {
        assert_eq!(luid_of(&description_with(3, 1)), (3_i64 << 32) | 1);
    }

    #[test]
    fn a_low_word_with_the_high_bit_set_stays_positive() {
        // The bug this guards: casting the low word through i32 makes 0x8000_0000 negative, and the
        // adapter then never matches the LUID capture reported for the very same GPU.
        assert_eq!(luid_of(&description_with(0, 0x8000_0000)), 0x8000_0000);
    }

    #[test]
    fn a_zero_luid_is_zero() {
        assert_eq!(luid_of(&description_with(0, 0)), 0);
    }

    #[test]
    fn asking_for_an_adapter_that_does_not_exist_fails_rather_than_picking_another() {
        // Silently substituting an adapter is the hybrid-laptop bug this module exists to avoid, so
        // a miss must be an error and never a fallback.
        assert!(
            find_adapter(i64::MAX).is_err(),
            "a nonexistent LUID must not resolve to some other adapter"
        );
    }

    #[test]
    fn the_error_for_a_missing_adapter_names_the_luid_that_was_asked_for() {
        let Err(EncodeError::Platform(message)) = find_adapter(i64::MAX) else {
            panic!("expected a platform error naming the adapter");
        };
        assert!(
            message.contains(&i64::MAX.to_string()),
            "the error should say which adapter was wanted, got: {message}"
        );
    }
}
