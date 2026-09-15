#![allow(
    clippy::undocumented_unsafe_blocks,
    reason = "a report run by hand against this machine's encoder; nothing here ships"
)]

use super::*;
use crate::capture::duplication::DesktopDuplication;
use windows::Win32::Media::MediaFoundation::MF_TRANSFORM_ASYNC_UNLOCK;

/// Reports whether a hardware encoder can be opened on the capture adapter.
#[test]
#[ignore = "reports rather than asserts; run deliberately"]
fn report_hardware_encoder_on_the_capture_adapter() {
    let Ok(duplication) = DesktopDuplication::open(0) else {
        println!("NO CAPTURE");
        return;
    };
    let luid = duplication.format().adapter_luid;
    println!("capture adapter luid: {luid}");

    match HardwareTransform::open(luid) {
        Ok(transform) => {
            println!("hardware encoder opened on the capture adapter");
            // Confirms the async unlock took, which is what separates a usable hardware MFT
            // from one that rejects everything with a misleading media-type error.
            let attributes = unsafe { transform.transform().GetAttributes() };
            match attributes {
                Ok(attributes) => {
                    let unlocked = unsafe { attributes.GetUINT32(&MF_TRANSFORM_ASYNC_UNLOCK) };
                    println!("  async unlock: {unlocked:?}");
                }
                Err(error) => println!("  no attributes: {error}"),
            }
        }
        Err(error) => println!("NO HARDWARE ENCODER: {error}"),
    }
}
