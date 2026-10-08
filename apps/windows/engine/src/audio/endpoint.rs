//! This PC's sound outputs: which there are, what each is called, its mute and its level.

use windows::core::{HSTRING, PWSTR};
use windows::Win32::Devices::FunctionDiscovery::PKEY_Device_FriendlyName;
use windows::Win32::Media::Audio::Endpoints::IAudioEndpointVolume;
use windows::Win32::Media::Audio::{
    eConsole, eRender, IMMDevice, IMMDeviceEnumerator, MMDeviceEnumerator, DEVICE_STATE_ACTIVE,
};
use windows::Win32::System::Com::{CoCreateInstance, CoTaskMemFree, CLSCTX_ALL, STGM_READ};

use super::AudioError;

/// One sound output.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct OutputDevice {
    /// Windows' identity for it, which stays the same across restarts.
    pub id: String,
    /// The name Windows shows for it, such as "Speakers (Realtek(R) Audio)".
    pub name: String,
    /// Whether it is the output Windows plays through by default.
    pub is_default: bool,
}

/// Lists the outputs that are plugged in and working.
///
/// # Errors
/// [`AudioError::Platform`] when Windows will not list them.
pub fn list() -> Result<Vec<OutputDevice>, AudioError> {
    let devices = enumerator()?;
    // SAFETY: the enumerator is live; the collection and each device it holds are read in turn.
    unsafe {
        let default_id = devices
            .GetDefaultAudioEndpoint(eRender, eConsole)
            .ok()
            .and_then(|device| id_of(&device).ok());
        let collection = devices
            .EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)
            .map_err(platform)?;
        let count = collection.GetCount().map_err(platform)?;
        let mut outputs = Vec::with_capacity(count as usize);
        for index in 0..count {
            let device = collection.Item(index).map_err(platform)?;
            let id = id_of(&device)?;
            outputs.push(OutputDevice {
                is_default: default_id.as_deref() == Some(id.as_str()),
                name: name_of(&device).unwrap_or_else(|| "Sound output".to_owned()),
                id,
            });
        }
        Ok(outputs)
    }
}

/// Opens the output with `id`, or the default output when `id` is `None`.
///
/// # Errors
/// [`AudioError::DeviceMissing`] when a named output is not there or not plugged in, and
/// [`AudioError::NoOutput`] when there is no default output at all.
pub fn open(id: Option<&str>) -> Result<IMMDevice, AudioError> {
    let devices = enumerator()?;
    // SAFETY: the enumerator is live and the identity is passed as a wide string Windows copies.
    unsafe {
        match id {
            // Windows remembers outputs that are unplugged, so being found is not being there.
            Some(id) => devices
                .GetDevice(&HSTRING::from(id))
                .ok()
                .filter(|device| {
                    device
                        .GetState()
                        .is_ok_and(|state| state == DEVICE_STATE_ACTIVE)
                })
                .ok_or(AudioError::DeviceMissing),
            None => devices
                .GetDefaultAudioEndpoint(eRender, eConsole)
                .map_err(|_| AudioError::NoOutput),
        }
    }
}

/// Whether `device` is muted.
///
/// # Errors
/// [`AudioError::Platform`] when Windows will not say.
pub fn is_muted(device: &IMMDevice) -> Result<bool, AudioError> {
    // SAFETY: the device is live; its volume control is read once.
    unsafe {
        let volume: IAudioEndpointVolume = device.Activate(CLSCTX_ALL, None).map_err(platform)?;
        Ok(volume.GetMute().map_err(platform)?.as_bool())
    }
}

/// Mutes or unmutes `device`, leaving its level where it is.
///
/// # Errors
/// [`AudioError::Platform`] when Windows refuses.
pub fn set_muted(device: &IMMDevice, muted: bool) -> Result<(), AudioError> {
    // SAFETY: the device is live; its volume control is set once, with no event context.
    unsafe {
        let volume: IAudioEndpointVolume = device.Activate(CLSCTX_ALL, None).map_err(platform)?;
        volume.SetMute(muted, std::ptr::null()).map_err(platform)
    }
}

/// Windows' identity for `device`.
pub(crate) fn id_of(device: &IMMDevice) -> Result<String, AudioError> {
    // SAFETY: GetId hands back a wide string the caller frees, which happens once it is copied.
    unsafe {
        let id: PWSTR = device.GetId().map_err(platform)?;
        let text = id.to_string();
        CoTaskMemFree(Some(id.0.cast()));
        text.map_err(|_| AudioError::Platform("the output's identity is not readable text".into()))
    }
}

fn name_of(device: &IMMDevice) -> Option<String> {
    // SAFETY: the property store is opened for reading and the name read as a PROPVARIANT, which
    // frees itself when dropped.
    unsafe {
        let store = device.OpenPropertyStore(STGM_READ).ok()?;
        let value = store.GetValue(&PKEY_Device_FriendlyName).ok()?;
        let name = value.to_string();
        (!name.trim().is_empty()).then_some(name)
    }
}

fn enumerator() -> Result<IMMDeviceEnumerator, AudioError> {
    // SAFETY: a COM object created in-process; the calling thread has COM initialised.
    unsafe { CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL) }.map_err(platform)
}

fn platform(error: windows::core::Error) -> AudioError {
    AudioError::Platform(error.message())
}

#[cfg(test)]
#[path = "endpoint_tests.rs"]
mod tests;
