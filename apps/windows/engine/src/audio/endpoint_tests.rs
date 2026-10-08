use windows::Win32::Media::Audio::{
    DEVICE_STATE, DEVICE_STATE_DISABLED, DEVICE_STATE_NOTPRESENT, DEVICE_STATE_UNPLUGGED,
};
use windows::Win32::System::Com::{CoInitializeEx, COINIT_MULTITHREADED};

use super::*;

/// Windows remembers outputs that are unplugged or turned off; naming one is naming nothing.
/// Runs where this PC remembers such an output, which most do.
#[test]
fn an_output_windows_remembers_but_is_not_plugged_in_is_missing() {
    // SAFETY: joins this test thread to the multithreaded apartment, which it never leaves.
    let _ = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
    let devices = enumerator().expect("every Windows has a device enumerator");
    // SAFETY: the enumerator is live and its collection is read in turn.
    let absent = unsafe {
        devices
            .EnumAudioEndpoints(
                eRender,
                DEVICE_STATE(
                    DEVICE_STATE_DISABLED.0 | DEVICE_STATE_NOTPRESENT.0 | DEVICE_STATE_UNPLUGGED.0,
                ),
            )
            .ok()
            .and_then(|collection| collection.Item(0).ok())
    };
    if let Some(absent) = absent {
        let id = id_of(&absent).unwrap();
        assert!(matches!(open(Some(&id)), Err(AudioError::DeviceMissing)));
    }
}

#[test]
fn a_core_audio_failure_keeps_windows_words() {
    let error = platform(windows::Win32::Foundation::E_FAIL.into());
    assert!(matches!(error, AudioError::Platform(message) if !message.is_empty()));
}
