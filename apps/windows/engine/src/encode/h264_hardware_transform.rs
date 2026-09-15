//! The Media Foundation transform behind the hardware encoder, and its event pump.
//!
//! Hardware MFTs do not work like the software one. They will not accept a frame simply because you
//! have one: they raise `METransformNeedInput` when they are ready for a frame and
//! `METransformHaveOutput` when one is finished, and a caller that ignores those and drives
//! `ProcessInput`/`ProcessOutput` directly gets errors that read like the encoder is broken.
//!
//! This module owns finding the transform, unlocking it for asynchronous use, giving it a device to
//! read frames from, and pumping its event queue. The encoder built on top decides what to do with
//! the events; everything here is about getting them at all.
//!
//! # Choosing the adapter
//!
//! On a hybrid laptop the encoder must live on the adapter that *drives the display*, not on the
//! fastest one. Desktop duplication produces its texture on the display adapter — the integrated
//! GPU on nearly every laptop — and a discrete-GPU encoder would need that frame copied across the
//! PCIe bus first, which costs more than the faster encoder saves. This machine is the textbook
//! case: an Intel iGPU driving the panel next to an RTX 4070 with nothing attached to it. Picking
//! by adapter LUID rather than by reputation is what makes this correct rather than merely fast.

use windows::core::Interface;
use windows::Win32::Media::MediaFoundation::{
    IMFActivate, IMFMediaEventGenerator, IMFSample, IMFTransform, MFCreateAttributes,
    MFMediaType_Video, MFTEnum2, MFVideoFormat_H264, MEDIA_EVENT_GENERATOR_GET_EVENT_FLAGS,
    MFT_CATEGORY_VIDEO_ENCODER, MFT_ENUM_ADAPTER_LUID, MFT_ENUM_FLAG_HARDWARE,
    MFT_ENUM_FLAG_SORTANDFILTER, MFT_MESSAGE_SET_D3D_MANAGER, MFT_REGISTER_TYPE_INFO,
    MF_TRANSFORM_ASYNC_UNLOCK,
};

use super::d3d_manager::DeviceManager;
use super::mediafoundation::MediaFoundationPlatform;
use super::video::EncodeError;
use crate::encode::h264::platform;

/// `METransformNeedInput`: the encoder is ready to accept a frame.
pub const TRANSFORM_NEED_INPUT: u32 = 601;
/// `METransformHaveOutput`: the encoder has finished an access unit.
pub const TRANSFORM_HAVE_OUTPUT: u32 = 602;

/// What an event from an asynchronous transform is asking for.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TransformEvent {
    /// Feed a frame now.
    NeedInput,
    /// Collect an access unit now.
    HaveOutput,
    /// Something this pump does not act on.
    Other,
}

impl TransformEvent {
    /// Classifies a raw Media Foundation event type.
    ///
    /// Anything unrecognised is [`TransformEvent::Other`] rather than an error: transforms raise
    /// events this pump has no interest in — format changes, drain markers — and treating one of
    /// those as a failure would end a session that is working perfectly well.
    #[must_use]
    pub fn from_event_type(event_type: u32) -> Self {
        match event_type {
            TRANSFORM_NEED_INPUT => Self::NeedInput,
            TRANSFORM_HAVE_OUTPUT => Self::HaveOutput,
            _ => Self::Other,
        }
    }
}

/// A hardware encoder transform, located on a specific adapter and unlocked for async use.
pub struct HardwareTransform {
    transform: IMFTransform,
    events: IMFMediaEventGenerator,
    // Declared before the transform so it is dropped after it: the transform holds a borrowed
    // reference to this manager and must not outlive the device behind it.
    devices: DeviceManager,
    // Declared last so it outlives both COM objects above: Rust drops fields in declaration order,
    // and releasing a transform after MFShutdown is undefined behaviour.
    _platform: MediaFoundationPlatform,
}

impl HardwareTransform {
    /// Finds a hardware H.264 encoder on the adapter with `adapter_luid`.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the adapter has no hardware H.264 encoder, which is a
    /// perfectly ordinary answer and the reason the caller keeps a software fallback.
    pub fn open(adapter_luid: i64) -> Result<Self, EncodeError> {
        Self::open_inner(adapter_luid, None)
    }

    /// Finds a hardware encoder and runs it on a device the caller already has.
    ///
    /// Used to put the encoder on the capture device, so a captured texture can be converted and
    /// encoded without ever leaving the GPU.
    ///
    /// # Errors
    /// See [`Self::open`].
    pub fn open_on_device(
        adapter_luid: i64,
        device: windows::Win32::Graphics::Direct3D11::ID3D11Device,
        context: windows::Win32::Graphics::Direct3D11::ID3D11DeviceContext,
    ) -> Result<Self, EncodeError> {
        Self::open_inner(adapter_luid, Some((device, context)))
    }

    fn open_inner(
        adapter_luid: i64,
        existing: Option<(
            windows::Win32::Graphics::Direct3D11::ID3D11Device,
            windows::Win32::Graphics::Direct3D11::ID3D11DeviceContext,
        )>,
    ) -> Result<Self, EncodeError> {
        // Media Foundation refuses to enumerate transforms before startup, and reports it as
        // "CoInitialize has not been called" rather than as anything about MFT enumeration.
        let mf_platform = MediaFoundationPlatform::start().map_err(platform)?;

        let output_info = MFT_REGISTER_TYPE_INFO {
            guidMajorType: MFMediaType_Video,
            guidSubtype: MFVideoFormat_H264,
        };

        // The LUID is passed as an enumeration attribute; without it Media Foundation is free to
        // return an encoder on whichever adapter it likes, which on a hybrid laptop is how capture
        // and encode end up on different GPUs.
        let mut attributes = None;
        // SAFETY: the out-parameter is valid and the store is released with this scope.
        unsafe { MFCreateAttributes(&mut attributes, 1) }.map_err(platform)?;
        let attributes =
            attributes.ok_or_else(|| EncodeError::Platform("no attribute store".into()))?;
        // Set as a blob of the LUID's own bytes, which is the shape MFT_ENUM_ADAPTER_LUID is
        // defined to take. Passing it as a UINT64 is accepted by the attribute store and then
        // rejected by enumeration as a bare "catastrophic failure" naming nothing.
        let luid_bytes = adapter_luid.to_ne_bytes();
        // SAFETY: the store is live and copies the bytes it is given.
        unsafe {
            attributes
                .SetBlob(&MFT_ENUM_ADAPTER_LUID, &luid_bytes)
                .map_err(platform)?;
        }

        let mut activates: *mut Option<IMFActivate> = std::ptr::null_mut();
        let mut count = 0u32;
        // MFTEnum2 rather than MFTEnumEx: only the former accepts the attribute store, and the
        // store is the only way to name an adapter. MFTEnumEx would return an encoder from
        // whichever GPU the platform preferred, which is exactly the hybrid-laptop mistake this
        // module exists to avoid.
        // SAFETY: both out-parameters are valid; the returned array is released below.
        unsafe {
            MFTEnum2(
                MFT_CATEGORY_VIDEO_ENCODER,
                MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER,
                None,
                Some(&output_info),
                &attributes,
                &mut activates,
                &mut count,
            )
            .map_err(platform)?;
        }

        if count == 0 || activates.is_null() {
            return Err(EncodeError::Platform(format!(
                "no hardware H.264 encoder on adapter {adapter_luid}"
            )));
        }

        // SAFETY: MFTEnumEx populated `count` entries; the first is the platform's best match.
        let activate = unsafe { (*activates).clone() }.ok_or_else(|| {
            EncodeError::Platform("the hardware encoder activation was empty".into())
        })?;

        // SAFETY: every remaining entry is released exactly once, then the array itself is freed,
        // which is what MFTEnumEx documents for its caller.
        unsafe {
            for index in 1..count as usize {
                let _ = (*activates.add(index)).take();
            }
            windows::Win32::System::Com::CoTaskMemFree(Some(activates.cast()));
        }

        // SAFETY: the activation object is live and describes a transform.
        let transform: IMFTransform = unsafe { activate.ActivateObject() }.map_err(platform)?;

        // A hardware MFT stays locked in synchronous mode until this is set, and reports errors
        // that look like an unsupported media type rather than like a missing unlock.
        // SAFETY: the transform is live; every hardware MFT exposes an attribute store.
        let attributes = unsafe { transform.GetAttributes() }.map_err(platform)?;
        // SAFETY: the store is live and the key takes a 32-bit value.
        unsafe {
            attributes
                .SetUINT32(&MF_TRANSFORM_ASYNC_UNLOCK, 1)
                .map_err(platform)?;
        }

        // The device manager, before any media type is set, and this time with frames to match it.
        //
        // The history is worth keeping. The manager was first added to make the transform produce
        // output at all, which was a misdiagnosis — the real cause was the event pump discarding
        // `METransformNeedInput`. It was then removed, on the grounds that a transform fed
        // system-memory buffers has no business being told to read from the GPU. Removing it
        // changed nothing: the encoder still emitted structurally perfect H.264 that decoded to a
        // field of zeroes, with or without it.
        //
        // What actually matters is that this transform reads its input from the GPU whatever the
        // media type says, so the frames have to be there. The manager comes back *and* every frame
        // is uploaded to an NV12 texture on this device. Either half alone reproduces the green
        // screen; only both together put pixels where the encoder is looking.
        let devices = match existing {
            Some((device, context)) => DeviceManager::for_device(device, context)?,
            None => DeviceManager::for_adapter(adapter_luid)?,
        };
        // SAFETY: the transform is live and the parameter is the manager pointer this message is
        // defined to carry. The transform takes its own reference; `devices` outlives it.
        unsafe {
            transform
                .ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER, devices.as_message_param())
                .map_err(platform)?;
        }

        let events = transform
            .cast::<IMFMediaEventGenerator>()
            .map_err(platform)?;

        Ok(Self {
            transform,
            events,
            devices,
            _platform: mf_platform,
        })
    }

    /// The underlying transform, for configuring media types.
    #[must_use]
    pub fn transform(&self) -> &IMFTransform {
        &self.transform
    }

    /// The Direct3D device this transform reads its frames from.
    #[must_use]
    pub fn devices(&self) -> &DeviceManager {
        &self.devices
    }

    /// Waits up to `timeout` for the transform's next event.
    ///
    /// Bounded rather than blocking. `IMFMediaEventGenerator::GetEvent` with a blocking flag waits
    /// forever, and a hardware transform that never raises `METransformNeedInput` — because a
    /// driver wants a D3D device manager it was not given, say — then hangs the calling thread with
    /// no diagnostic at all. That happened on this project and cost more to find than the encoder
    /// was worth. A timeout turns it into a fallback to software instead of a frozen session.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the event queue itself fails.
    pub fn next_event_within(
        &self,
        timeout: std::time::Duration,
    ) -> Result<Option<TransformEvent>, EncodeError> {
        let deadline = std::time::Instant::now() + timeout;
        loop {
            if let Some(event) = self.poll_event()? {
                return Ok(Some(event));
            }
            if std::time::Instant::now() >= deadline {
                return Ok(None);
            }
            // Short enough that a responsive encoder is not delayed, long enough that this is not
            // a spin: the alternative is burning a core waiting on an empty queue.
            std::thread::sleep(std::time::Duration::from_millis(1));
        }
    }

    /// Waits for the transform's next event and classifies it.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the event queue itself fails.
    pub fn next_event(&self) -> Result<TransformEvent, EncodeError> {
        // SAFETY: the generator is live. A zero flag blocks until an event arrives, which is what
        // an encoder pump wants: the alternative is spinning on an empty queue.
        let event = unsafe { self.events.GetEvent(MF_EVENT_FLAG_BLOCK) }.map_err(platform)?;
        // SAFETY: the event is live and always carries a type.
        let event_type = unsafe { event.GetType() }.map_err(platform)?;
        Ok(TransformEvent::from_event_type(event_type))
    }

    /// Checks for an event without waiting, for use once a frame is already in the encoder.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the event queue itself fails.
    pub fn poll_event(&self) -> Result<Option<TransformEvent>, EncodeError> {
        // SAFETY: the generator is live. An empty queue comes back as an error code rather than a
        // real failure, which is why it maps to None instead of propagating.
        match unsafe { self.events.GetEvent(MF_EVENT_FLAG_NO_WAIT) } {
            Ok(event) => {
                // SAFETY: the event is live and always carries a type.
                let event_type = unsafe { event.GetType() }.map_err(platform)?;
                Ok(Some(TransformEvent::from_event_type(event_type)))
            }
            Err(_) => Ok(None),
        }
    }

    /// Hands a frame to the encoder. Only valid after [`TransformEvent::NeedInput`].
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the transform refuses the sample.
    pub fn submit(&self, sample: &IMFSample) -> Result<(), EncodeError> {
        // SAFETY: the transform is live and stream 0 exists on every encoder.
        unsafe { self.transform.ProcessInput(0, sample, 0) }.map_err(platform)
    }
}

/// Block until an event arrives. Correct while a frame still has to be delivered.
const MF_EVENT_FLAG_BLOCK: MEDIA_EVENT_GENERATOR_GET_EVENT_FLAGS =
    MEDIA_EVENT_GENERATOR_GET_EVENT_FLAGS(0);
/// Return immediately when the queue is empty. Correct once the frame is already in the encoder,
/// because blocking there would wait on output the encoder may not have yet.
const MF_EVENT_FLAG_NO_WAIT: MEDIA_EVENT_GENERATOR_GET_EVENT_FLAGS =
    MEDIA_EVENT_GENERATOR_GET_EVENT_FLAGS(1);

#[cfg(test)]
#[path = "h264_hardware_transform_tests.rs"]
mod tests;
