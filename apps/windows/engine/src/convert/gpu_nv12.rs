//! Converting a captured desktop texture to NV12 on the GPU.
//!
//! The frame starts on the GPU — desktop duplication hands back a texture — and the encoder wants
//! it on the GPU. Everything in between was going the long way round: read the texture back to
//! system memory, convert BGRA to NV12 with the processor, then upload the result to a texture
//! again. That is two bus crossings and a colour conversion on the CPU, and on this machine it costs
//! about three milliseconds a frame against a sixteen millisecond budget — comfortably more than
//! the encoder itself.
//!
//! `ID3D11VideoProcessor` is the hardware built for exactly this. It does the colour conversion and
//! the downscale in a single pass, on the same silicon that will encode the result, and the frame
//! never leaves the GPU.
//!
//! # Why the colour space has to be stated
//!
//! The video processor will convert BGRA to NV12 without being told anything, and it will pick a
//! colour space when it does. Left to itself it tends to assume full-range BT.601, while Flint's
//! receiver decodes limited-range BT.709 — the difference is washed-out, slightly green-shifted
//! output that looks like a bad capture rather than like a bug. Both ends are stated explicitly
//! here so the conversion is defined rather than inherited.

use windows::core::Interface;
use windows::Win32::Foundation::RECT;
use windows::Win32::Graphics::Direct3D11::{
    ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D, ID3D11VideoContext, ID3D11VideoDevice,
    ID3D11VideoProcessor, ID3D11VideoProcessorEnumerator, ID3D11VideoProcessorInputView,
    ID3D11VideoProcessorOutputView, D3D11_TEX2D_VPIV, D3D11_TEX2D_VPOV,
    D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE, D3D11_VIDEO_PROCESSOR_COLOR_SPACE,
    D3D11_VIDEO_PROCESSOR_CONTENT_DESC, D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC,
    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC_0, D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC,
    D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC_0, D3D11_VIDEO_PROCESSOR_STREAM,
    D3D11_VIDEO_USAGE_PLAYBACK_NORMAL, D3D11_VPIV_DIMENSION_TEXTURE2D,
    D3D11_VPOV_DIMENSION_TEXTURE2D,
};

use crate::encode::h264::platform;
use crate::encode::video::EncodeError;

/// Names which call failed, because every one of them answers "the parameter is incorrect".
///
/// Direct3D's video APIs report almost every rejection with the same message and no indication of
/// which argument it objected to, so the step has to be carried in the error or a failure means
/// bisecting the function by hand.
fn step(what: &str, error: windows::core::Error) -> EncodeError {
    EncodeError::Platform(format!("{what} failed: {error}"))
}

/// `Nominal_Range` for full-range 0-255 content.
const NOMINAL_RANGE_0_255: u32 = 2;

/// `Nominal_Range` for studio-swing 16-235 content, which is what the receiver decodes.
const NOMINAL_RANGE_16_235: u32 = 1;

/// Packs a `D3D11_VIDEO_PROCESSOR_COLOR_SPACE` bitfield for BT.709 playback.
///
/// The `windows` crate exposes this structure as a bare `u32`, so the layout is written out here
/// rather than inherited from generated accessors. From the least significant bit: `Usage` (1 bit,
/// 0 for playback), `RGB_Range` (1 bit, 0 for full range), `YCbCr_Matrix` (1 bit, 1 for BT.709),
/// `YCbCr_xvYCC` (1 bit, 0), then `Nominal_Range` (2 bits).
///
/// Getting this wrong does not fail; it produces a picture in the wrong range, which reads as a
/// washed-out or crushed capture rather than as a bug.
#[must_use]
const fn colour_space(nominal_range: u32) -> u32 {
    const USAGE_PLAYBACK: u32 = 0;
    const RGB_RANGE_FULL: u32 = 0;
    const YCBCR_MATRIX_BT709: u32 = 1;
    const XVYCC_OFF: u32 = 0;

    USAGE_PLAYBACK
        | (RGB_RANGE_FULL << 1)
        | (YCBCR_MATRIX_BT709 << 2)
        | (XVYCC_OFF << 3)
        | (nominal_range << 4)
}

/// What a converter is producing, which decides the output colour space.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OutputKind {
    /// NV12 for an encoder: studio-swing BT.709, which is what the receiver decodes.
    Nv12,
    /// BGRA for readback: full-range RGB, because the pixels are going back to a colour converter
    /// that expects the same range the desktop arrived in.
    Bgra,
}

/// Converts and scales desktop textures with the GPU's video processor.
///
/// Used two ways. The fast path converts BGRA straight to NV12 for the encoder. The fallback path
/// converts BGRA to BGRA purely to *scale* before readback, which is worth doing on its own: a
/// 2560x1600 desktop reduced to 1920x1200 first crosses the bus with 44% fewer pixels.
pub struct GpuNv12Converter {
    video_device: ID3D11VideoDevice,
    video_context: ID3D11VideoContext,
    processor: ID3D11VideoProcessor,
    enumerator: ID3D11VideoProcessorEnumerator,
    source: (u32, u32),
    target: (u32, u32),
    kind: OutputKind,
}

impl GpuNv12Converter {
    /// Builds a converter from `source` size to `target` size on `device`.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the device has no video processor, which is the signal to
    /// fall back to the readback path rather than to fail the session.
    pub fn new(
        device: &ID3D11Device,
        context: &ID3D11DeviceContext,
        source: (u32, u32),
        target: (u32, u32),
    ) -> Result<Self, EncodeError> {
        Self::for_output(device, context, source, target, OutputKind::Nv12)
    }

    /// Builds a converter that scales without changing colour space, for readback.
    ///
    /// # Errors
    /// See [`Self::new`].
    pub fn for_scaling(
        device: &ID3D11Device,
        context: &ID3D11DeviceContext,
        source: (u32, u32),
        target: (u32, u32),
    ) -> Result<Self, EncodeError> {
        Self::for_output(device, context, source, target, OutputKind::Bgra)
    }

    /// Builds a converter producing `kind`.
    ///
    /// # Errors
    /// See [`Self::new`].
    pub fn for_output(
        device: &ID3D11Device,
        context: &ID3D11DeviceContext,
        source: (u32, u32),
        target: (u32, u32),
        kind: OutputKind,
    ) -> Result<Self, EncodeError> {
        let video_device = device.cast::<ID3D11VideoDevice>().map_err(platform)?;
        let video_context = context.cast::<ID3D11VideoContext>().map_err(platform)?;

        let description = D3D11_VIDEO_PROCESSOR_CONTENT_DESC {
            InputFrameFormat: D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE,
            InputWidth: source.0,
            InputHeight: source.1,
            OutputWidth: target.0,
            OutputHeight: target.1,
            // Normal playback rather than one of the quality-biased usages: this is a live mirror,
            // and a processor that spends longer to look better is spending the latency budget.
            Usage: D3D11_VIDEO_USAGE_PLAYBACK_NORMAL,
            ..Default::default()
        };

        // SAFETY: the description is fully initialised; the enumerator is released with this scope.
        let enumerator =
            unsafe { video_device.CreateVideoProcessorEnumerator(&raw const description) }
                .map_err(platform)?;
        // SAFETY: rate conversion index 0 is the processor's default capability set.
        let processor =
            unsafe { video_device.CreateVideoProcessor(&enumerator, 0) }.map_err(platform)?;

        let converter = Self {
            video_device,
            video_context,
            processor,
            enumerator,
            source,
            target,
            kind,
        };
        converter.set_colour_spaces();
        Ok(converter)
    }

    /// The size this converter produces.
    #[must_use]
    pub fn target(&self) -> (u32, u32) {
        self.target
    }

    /// The size this converter expects.
    #[must_use]
    pub fn source(&self) -> (u32, u32) {
        self.source
    }

    /// What this converter produces.
    #[must_use]
    pub fn kind(&self) -> OutputKind {
        self.kind
    }

    /// Declares the input as full-range RGB and the output as limited-range BT.709.
    ///
    /// Not optional. The desktop arrives as full-range RGB and the receiver decodes limited-range
    /// BT.709; a processor left to guess produces a picture that is merely wrong rather than
    /// visibly broken, which is the hardest kind of wrong to notice.
    fn set_colour_spaces(&self) {
        // The desktop always arrives as full-range RGB.
        let input = D3D11_VIDEO_PROCESSOR_COLOR_SPACE {
            _bitfield: colour_space(NOMINAL_RANGE_0_255),
        };
        // The output range depends on where the pixels are going. An encoder wants studio swing,
        // because that is what the receiver decodes; a readback that is only being scaled must come
        // back in the range it went in, or the colour converter downstream shifts it twice.
        let output = D3D11_VIDEO_PROCESSOR_COLOR_SPACE {
            _bitfield: colour_space(match self.kind {
                OutputKind::Nv12 => NOMINAL_RANGE_16_235,
                OutputKind::Bgra => NOMINAL_RANGE_0_255,
            }),
        };

        // SAFETY: the processor is live; both calls take a colour space by reference and cannot
        // fail in a way worth acting on — a driver that ignores them produces a defined picture in
        // the wrong range, which the round-trip test catches.
        unsafe {
            self.video_context.VideoProcessorSetStreamColorSpace(
                &self.processor,
                0,
                &raw const input,
            );
            self.video_context
                .VideoProcessorSetOutputColorSpace(&self.processor, &raw const output);
        }
    }

    /// Converts one BGRA texture into `destination`, which must be NV12 at the target size.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when a view cannot be created or the blit fails.
    pub fn convert(
        &mut self,
        source: &ID3D11Texture2D,
        destination: &ID3D11Texture2D,
    ) -> Result<(), EncodeError> {
        let input_description = D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC {
            FourCC: 0,
            ViewDimension: D3D11_VPIV_DIMENSION_TEXTURE2D,
            Anonymous: D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC_0 {
                Texture2D: D3D11_TEX2D_VPIV {
                    MipSlice: 0,
                    ArraySlice: 0,
                },
            },
        };
        let mut input_view: Option<ID3D11VideoProcessorInputView> = None;
        // SAFETY: the texture is live, the description matches it, and the out-parameter is valid.
        unsafe {
            self.video_device.CreateVideoProcessorInputView(
                source,
                &self.enumerator,
                &raw const input_description,
                Some(&raw mut input_view),
            )
        }
        .map_err(|error| step("creating the input view", error))?;
        let input_view =
            input_view.ok_or_else(|| EncodeError::Platform("no video input view".into()))?;

        let output_description = D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC {
            ViewDimension: D3D11_VPOV_DIMENSION_TEXTURE2D,
            Anonymous: D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC_0 {
                Texture2D: D3D11_TEX2D_VPOV { MipSlice: 0 },
            },
        };
        let mut output_view: Option<ID3D11VideoProcessorOutputView> = None;
        // SAFETY: as above, for the destination.
        unsafe {
            self.video_device.CreateVideoProcessorOutputView(
                destination,
                &self.enumerator,
                &raw const output_description,
                Some(&raw mut output_view),
            )
        }
        .map_err(|error| step("creating the output view", error))?;
        let output_view =
            output_view.ok_or_else(|| EncodeError::Platform("no video output view".into()))?;

        // Whole frame in, whole frame out. Stated explicitly because a processor left to default
        // its rectangles has been observed to letterbox rather than scale.
        let source_rect = RECT {
            left: 0,
            top: 0,
            right: self.source.0 as i32,
            bottom: self.source.1 as i32,
        };
        let target_rect = RECT {
            left: 0,
            top: 0,
            right: self.target.0 as i32,
            bottom: self.target.1 as i32,
        };
        // SAFETY: the processor and both rectangles are live for these calls.
        unsafe {
            self.video_context.VideoProcessorSetStreamSourceRect(
                &self.processor,
                0,
                true,
                Some(&raw const source_rect),
            );
            self.video_context.VideoProcessorSetStreamDestRect(
                &self.processor,
                0,
                true,
                Some(&raw const target_rect),
            );
            self.video_context.VideoProcessorSetOutputTargetRect(
                &self.processor,
                true,
                Some(&raw const target_rect),
            );
        }

        let stream = D3D11_VIDEO_PROCESSOR_STREAM {
            Enable: true.into(),
            OutputIndex: 0,
            InputFrameOrField: 0,
            PastFrames: 0,
            FutureFrames: 0,
            ppPastSurfaces: std::ptr::null_mut(),
            pInputSurface: std::mem::ManuallyDrop::new(Some(input_view)),
            ppFutureSurfaces: std::ptr::null_mut(),
            ..Default::default()
        };

        // SAFETY: one correctly-initialised stream describing live views.
        unsafe {
            self.video_context
                .VideoProcessorBlt(&self.processor, &output_view, 0, &[stream])
        }
        .map_err(|error| step("the video processor blit", error))?;

        Ok(())
    }
}

#[cfg(test)]
#[path = "gpu_nv12_tests.rs"]
mod tests;
