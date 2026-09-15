//! Windows implementation of encoder discovery.
//!
//! Asks Media Foundation which hardware transforms can encode each codec on each DXGI adapter.
//! Querying per adapter is essential on hybrid laptops: guessing from a friendly name can silently
//! send capture and encode to different GPUs.

use std::ffi::OsString;
use std::os::windows::ffi::OsStringExt;

use windows::core::{GUID, PWSTR};
use windows::Win32::Foundation::RPC_E_CHANGED_MODE;
use windows::Win32::Graphics::Dxgi::{
    CreateDXGIFactory1, IDXGIAdapter1, IDXGIFactory1, DXGI_ADAPTER_FLAG,
    DXGI_ADAPTER_FLAG_SOFTWARE, DXGI_ERROR_NOT_FOUND,
};
use windows::Win32::Media::MediaFoundation::{
    IMFActivate, MFCreateAttributes, MFMediaType_Video, MFStartup, MFTEnum2,
    MFT_FRIENDLY_NAME_Attribute, MFVideoFormat_AV1, MFVideoFormat_H264, MFVideoFormat_HEVC,
    MFSTARTUP_FULL, MFT_CATEGORY_VIDEO_ENCODER, MFT_ENUM_ADAPTER_LUID, MFT_ENUM_FLAG_HARDWARE,
    MFT_ENUM_FLAG_SORTANDFILTER, MFT_REGISTER_TYPE_INFO, MF_VERSION,
};
use windows::Win32::System::Com::{CoInitializeEx, CoUninitialize, COINIT_MULTITHREADED};

use crate::encode::caps::{vendor_from_name, EncoderInventory};
use crate::encode::{HostEncoder, VideoCodec};

/// A graphics adapter present on this host.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Adapter {
    /// The adapter's locally unique identifier, as DXGI reports it.
    pub luid: i64,
    /// The adapter description string.
    pub description: String,
    /// Whether this is a software renderer rather than real hardware.
    pub is_software: bool,
    /// Whether at least one output on this adapter is attached to the desktop.
    pub drives_display: bool,
    /// Whether this adapter owns the output at desktop origin `(0, 0)`.
    ///
    /// Windows places the primary monitor at the virtual desktop origin.
    pub drives_primary_display: bool,
}

/// Enumerates the physical graphics adapters.
///
/// Software adapters (WARP, the Basic Render Driver) are reported but flagged. They exist, so
/// hiding them would make the diagnostics lie, but nothing may select one for a session.
///
/// # Errors
/// Returns an error when DXGI cannot be initialised at all.
pub fn adapters() -> windows::core::Result<Vec<Adapter>> {
    // SAFETY: CreateDXGIFactory1 is safe to call on any thread and returns a checked HRESULT.
    let factory: IDXGIFactory1 = unsafe { CreateDXGIFactory1()? };
    let mut found = Vec::new();

    for index in 0.. {
        // SAFETY: `index` is bounded by the DXGI_ERROR_NOT_FOUND break below.
        let adapter: IDXGIAdapter1 = match unsafe { factory.EnumAdapters1(index) } {
            Ok(adapter) => adapter,
            Err(error) if error.code() == DXGI_ERROR_NOT_FOUND => break,
            Err(error) => return Err(error),
        };

        // SAFETY: `adapter` is a live DXGI adapter object.
        let desc = unsafe { adapter.GetDesc1()? };

        let luid =
            (i64::from(desc.AdapterLuid.HighPart) << 32) | i64::from(desc.AdapterLuid.LowPart);
        let (drives_display, drives_primary_display) = output_roles(&adapter)?;
        found.push(Adapter {
            luid,
            description: wide_to_string(&desc.Description),
            is_software: DXGI_ADAPTER_FLAG(desc.Flags as i32).contains(DXGI_ADAPTER_FLAG_SOFTWARE),
            drives_display,
            drives_primary_display,
        });
    }

    Ok(found)
}

/// Discovers the hardware encoders this host has.
///
/// Media Foundation is queried once per DXGI adapter using `MFT_ENUM_ADAPTER_LUID`. The transform's
/// friendly name is used only as a diagnostic vendor label, not to choose the adapter.
///
/// # Errors
/// Returns an error when DXGI enumeration fails. A Media Foundation failure for one codec is not
/// an error: it means that codec has no hardware encoder, which is a valid answer.
pub fn probe() -> windows::core::Result<EncoderInventory> {
    let _platform = MediaFoundationPlatform::start()?;
    let adapters = adapters()?;
    let mut encoders: Vec<HostEncoder> = Vec::new();

    for adapter in adapters.iter().filter(|adapter| !adapter.is_software) {
        for (codec, subtype) in [
            (VideoCodec::H264, MFVideoFormat_H264),
            (VideoCodec::H265, MFVideoFormat_HEVC),
            (VideoCodec::Av1, MFVideoFormat_AV1),
        ] {
            for name in hardware_encoder_names(subtype, adapter.luid) {
                let vendor = vendor_from_name(&name);

                match encoders.iter_mut().find(|existing| {
                    existing.vendor == vendor && existing.adapter_luid == adapter.luid
                }) {
                    Some(existing) => {
                        if !existing.codecs.contains(&codec) {
                            existing.codecs.push(codec);
                            existing.codecs.sort_unstable();
                        }
                    }
                    None => encoders.push(HostEncoder::new(vendor, adapter.luid, vec![codec])),
                }
            }
        }
    }

    Ok(EncoderInventory { encoders })
}

/// The friendly names of every hardware transform that can output `subtype`.
fn hardware_encoder_names(subtype: GUID, adapter_luid: i64) -> Vec<String> {
    let output = MFT_REGISTER_TYPE_INFO {
        guidMajorType: MFMediaType_Video,
        guidSubtype: subtype,
    };

    let mut attributes = None;
    // SAFETY: `attributes` is a valid out parameter and is released by its COM wrapper.
    if unsafe { MFCreateAttributes(&raw mut attributes, 1) }.is_err() {
        return Vec::new();
    }
    let Some(attributes) = attributes else {
        return Vec::new();
    };
    let luid_bytes = adapter_luid.to_ne_bytes();
    // SAFETY: the attribute object is valid and copies the supplied bytes.
    if unsafe { attributes.SetBlob(&MFT_ENUM_ADAPTER_LUID, &luid_bytes) }.is_err() {
        return Vec::new();
    }

    let mut activates: *mut Option<IMFActivate> = std::ptr::null_mut();
    let mut count = 0u32;

    // SAFETY: MFTEnum2 writes a COM-allocated array of `count` activation objects. A failure
    // leaves both outputs untouched, which the early return below respects.
    let result = unsafe {
        MFTEnum2(
            MFT_CATEGORY_VIDEO_ENCODER,
            MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER,
            None,
            Some(&raw const output),
            &attributes,
            &raw mut activates,
            &raw mut count,
        )
    };

    if result.is_err() || activates.is_null() {
        return Vec::new();
    }

    // SAFETY: MFTEnumEx guarantees `activates` points to `count` initialised entries.
    let slice = unsafe { std::slice::from_raw_parts(activates, count as usize) };
    let names = slice
        .iter()
        .filter_map(|activate| activate.as_ref())
        .filter_map(friendly_name)
        .collect::<Vec<_>>();

    // Drop each wrapper to release its COM reference before freeing the separately allocated
    // pointer array.
    for index in 0..count as usize {
        // SAFETY: every element below `count` is an initialised `Option<IMFActivate>`, dropped once.
        unsafe { std::ptr::drop_in_place(activates.add(index)) };
    }

    // SAFETY: the array itself is a COM allocation and must be released once read.
    unsafe { windows::Win32::System::Com::CoTaskMemFree(Some(activates.cast())) };

    names
}

/// Reads and owns one activation object's friendly name.
fn friendly_name(activate: &IMFActivate) -> Option<String> {
    let mut pointer = PWSTR::null();
    let mut length = 0u32;
    // SAFETY: both out parameters are valid and the activation object remains live for the call.
    if unsafe {
        activate.GetAllocatedString(
            &MFT_FRIENDLY_NAME_Attribute,
            &raw mut pointer,
            &raw mut length,
        )
    }
    .is_err()
        || pointer.is_null()
    {
        return None;
    }

    // SAFETY: the successful call above returned `length` readable UTF-16 code units.
    let text = unsafe { std::slice::from_raw_parts(pointer.0, length as usize) };
    let owned = OsString::from_wide(text).to_string_lossy().into_owned();
    // SAFETY: Media Foundation allocated the string and transfers ownership to the caller.
    unsafe { windows::Win32::System::Com::CoTaskMemFree(Some(pointer.0.cast())) };
    Some(owned)
}

/// Determines whether an adapter drives the desktop and whether it owns the primary output.
fn output_roles(adapter: &IDXGIAdapter1) -> windows::core::Result<(bool, bool)> {
    let mut drives_display = false;
    let mut drives_primary = false;

    for index in 0.. {
        // SAFETY: `index` is bounded by the DXGI_ERROR_NOT_FOUND break below.
        let output = match unsafe { adapter.EnumOutputs(index) } {
            Ok(output) => output,
            Err(error) if error.code() == DXGI_ERROR_NOT_FOUND => break,
            Err(error) => return Err(error),
        };
        // SAFETY: `output` is a live DXGI output object.
        let description = unsafe { output.GetDesc()? };
        if description.AttachedToDesktop.as_bool() {
            drives_display = true;
            if description.DesktopCoordinates.left == 0 && description.DesktopCoordinates.top == 0 {
                drives_primary = true;
            }
        }
    }

    Ok((drives_display, drives_primary))
}

/// Initialises COM on this thread and Media Foundation for the process.
///
/// # Why this no longer shuts Media Foundation down
///
/// It used to pair every `MFStartup` with an `MFShutdown` on drop, on the reasoning that the
/// platform reference counts them. It does — but the count is not the whole story. `MFShutdown`
/// tears down platform work queues that other threads may still be using, and a library has no
/// coordination point at which it can know it is the last user. Two encoders and a decoder living
/// on different threads is enough: the first to finish calls `MFShutdown`, and the others fault
/// inside the platform. That was an access violation partway through this crate's own test suite,
/// with no failing assertion and no indication of which component was at fault.
///
/// So Media Foundation is started once and left running for the life of the process. This is what
/// the platform is designed for and what every long-lived host does; the cost is that a process
/// which used Media Foundation once keeps it loaded, which is not a cost worth a crash.
///
/// COM is still balanced, because `CoInitializeEx` and `CoUninitialize` are per thread and each
/// guard genuinely is the last user of its own thread's initialisation.
pub(crate) struct MediaFoundationPlatform {
    uninitialize_com: bool,
}

/// Runs `MFStartup` exactly once for the process.
static MEDIA_FOUNDATION_STARTUP: std::sync::Once = std::sync::Once::new();

/// Whether that one startup succeeded.
static MEDIA_FOUNDATION_READY: std::sync::atomic::AtomicBool =
    std::sync::atomic::AtomicBool::new(false);

impl MediaFoundationPlatform {
    pub(crate) fn start() -> windows::core::Result<Self> {
        // SAFETY: no COM values cross this boundary. A thread that was already initialised as STA
        // can still use Media Foundation, so RPC_E_CHANGED_MODE is not a failure here.
        let com_result = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
        let uninitialize_com = if com_result.is_ok() {
            true
        } else if com_result == RPC_E_CHANGED_MODE {
            false
        } else {
            return Err(windows::core::Error::from(com_result));
        };

        MEDIA_FOUNDATION_STARTUP.call_once(|| {
            // SAFETY: called exactly once for the process, and deliberately never balanced. See the
            // type's own documentation for why shutting the platform down from a library crashes.
            let started = unsafe { MFStartup(MF_VERSION, MFSTARTUP_FULL) }.is_ok();
            MEDIA_FOUNDATION_READY.store(started, std::sync::atomic::Ordering::Release);
        });

        if !MEDIA_FOUNDATION_READY.load(std::sync::atomic::Ordering::Acquire) {
            if uninitialize_com {
                // SAFETY: this call balances the successful CoInitializeEx above.
                unsafe { CoUninitialize() };
            }
            return Err(windows::core::Error::from(
                windows::Win32::Foundation::E_FAIL,
            ));
        }

        Ok(Self { uninitialize_com })
    }
}

impl Drop for MediaFoundationPlatform {
    fn drop(&mut self) {
        // No MFShutdown. See the type's documentation: the platform outlives every guard, because
        // no guard can know whether another thread still holds a transform.
        if self.uninitialize_com {
            // SAFETY: this call balances this guard's successful CoInitializeEx, on this thread.
            unsafe { CoUninitialize() };
        }
    }
}

/// Converts a fixed-size wide character buffer to a string, stopping at the first NUL.
fn wide_to_string(buffer: &[u16]) -> String {
    let end = buffer.iter().position(|&c| c == 0).unwrap_or(buffer.len());
    OsString::from_wide(&buffer[..end])
        .to_string_lossy()
        .into_owned()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::encode::EncoderVendor;

    #[test]
    fn wide_buffer_stops_at_the_first_nul() {
        let buffer = [
            u16::from(b'R'),
            u16::from(b'E'),
            u16::from(b'X'),
            0,
            u16::from(b'!'),
        ];
        assert_eq!(wide_to_string(&buffer), "REX");
    }

    #[test]
    fn wide_buffer_without_a_nul_reads_to_the_end() {
        let buffer = [u16::from(b'R'), u16::from(b'E'), u16::from(b'X')];
        assert_eq!(wide_to_string(&buffer), "REX");
    }

    #[test]
    fn vendor_labels_survive_the_adapter_round_trip() {
        assert_eq!(
            vendor_from_name("NVIDIA GeForce RTX 4070 Laptop GPU"),
            EncoderVendor::Nvenc
        );
    }
}

#[cfg(test)]
mod hardware_report {
    use super::*;

    /// Reports the hardware encoders this machine actually has.
    #[test]
    #[ignore = "reports rather than asserts; run deliberately"]
    fn report_hardware_encoders() {
        match probe() {
            Ok(inventory) => {
                println!("adapters:");
                for adapter in adapters().unwrap_or_default() {
                    println!(
                        "  luid {:>8}  software={:<5} display={:<5} primary={:<5}  {}",
                        adapter.luid,
                        adapter.is_software,
                        adapter.drives_display,
                        adapter.drives_primary_display,
                        adapter.description
                    );
                }
                println!("encoders: {}", inventory.encoders.len());
                for encoder in inventory.encoders {
                    println!(
                        "  vendor {:?}  luid {:>8}  codecs {:?}",
                        encoder.vendor, encoder.adapter_luid, encoder.codecs
                    );
                }
            }
            Err(error) => println!("probe failed: {error}"),
        }
    }
}
