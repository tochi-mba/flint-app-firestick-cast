//! Bringing a captured frame back from the GPU, for the encoders that need it there.
//!
//! **This is the fallback path.** A hardware encoder sharing the capture device reads the captured
//! texture directly and never comes through here; see [`DesktopFrameSource::enable_gpu_frames`] and
//! `convert::gpu_nv12`. Measured per frame at 1920x1200, staying on the GPU costs about 1.9ms
//! against about 7.4ms for this route, so what follows is what happens when that is unavailable —
//! no hardware encoder on the capture adapter, or a device that will not allocate the textures.
//!
//! It is still needed. The software encoder cannot consume a texture: it converts colour on the
//! processor and needs bytes in system memory to do it. Bridging the two costs a copy per frame,
//! which the engineering notes call the most expensive thing on the host path, and it is confined
//! to this file precisely so the GPU path can bypass it rather than having it threaded through the
//! session.
//!
//! The staging textures are created once and reused. Allocating one per frame is the obvious way to
//! write this and would dominate the frame budget on its own.
//!
//! There are two of them, and that is the single most valuable thing in this file. Copying into a
//! staging texture and immediately mapping it makes the CPU wait for a GPU copy that has only just
//! been queued, and that stall was measured here as roughly 80% of the entire frame cost. Writing
//! into one texture while reading the one filled on the previous frame lets the two overlap, at the
//! price of showing a frame one tick later than the newest one available — which for a mirror is a
//! trade worth taking, because the alternative is every frame arriving late instead.

use windows::Win32::Graphics::Direct3D11::{
    ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D, D3D11_BIND_RENDER_TARGET,
    D3D11_CPU_ACCESS_READ, D3D11_MAPPED_SUBRESOURCE, D3D11_MAP_READ, D3D11_TEXTURE2D_DESC,
    D3D11_USAGE_DEFAULT, D3D11_USAGE_STAGING,
};
use windows::Win32::Graphics::Dxgi::Common::{DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_SAMPLE_DESC};

use super::mipscale::{halvings_to_reach, MipScaler};
use super::{CaptureError, CaptureFormat, FrameOutcome};
use crate::convert::gpu_nv12::GpuNv12Converter;
use crate::encode::video::{FrameData, SourceFrame};

/// Copies captured textures into system memory, reusing one staging texture and one buffer.
pub struct FrameReadback {
    /// Scales by arbitrary ratios in hardware, when the mip chain cannot express one.
    processor: Option<GpuNv12Converter>,
    /// Where the processor writes its reduced frame before the bus copy.
    reduced: Option<ID3D11Texture2D>,
    device: ID3D11Device,
    context: ID3D11DeviceContext,
    staging: [Option<ID3D11Texture2D>; 2],
    /// Which staging texture the next copy writes into; the other holds the previous frame.
    write_index: usize,
    /// Whether the texture opposite `write_index` holds a frame that has not been read yet.
    has_pending: bool,
    size: (u32, u32),
    pixels: Vec<u8>,
    scaler: MipScaler,
}

impl FrameReadback {
    /// Builds a readback bound to the device the frames are produced on.
    ///
    /// The device must be the same one duplication was opened against; copying across devices is
    /// not possible without a shared resource, and on a hybrid laptop the two are easily confused.
    #[must_use]
    pub fn new(device: ID3D11Device, context: ID3D11DeviceContext, format: CaptureFormat) -> Self {
        let pixels = packed_bgra_len(format.width, format.height)
            .map(Vec::with_capacity)
            .unwrap_or_default();

        let scaler = MipScaler::new(device.clone(), context.clone());

        Self {
            device,
            context,
            scaler,
            // Built on first use, and only for ratios the mip chain cannot express.
            processor: None,
            reduced: None,
            staging: [None, None],
            write_index: 0,
            has_pending: false,
            size: (0, 0),
            // Capture geometry is known before the first frame. Reserve here so the readback path
            // only changes the length, then accept the same allocation back from the session.
            pixels,
        }
    }

    /// Copies `texture` into system memory and describes it as a frame the encoder can take.
    ///
    /// # Errors
    /// [`CaptureError::Platform`] when the copy or the map fails.
    pub fn read(
        &mut self,
        texture: &ID3D11Texture2D,
        format: CaptureFormat,
        presentation_time_us: i64,
    ) -> Result<Option<SourceFrame>, CaptureError> {
        self.read_scaled(
            texture,
            format,
            (format.width, format.height),
            presentation_time_us,
        )
    }

    /// Copies `texture` into system memory, reducing it on the GPU first when it can.
    ///
    /// Readback is the most expensive stage on the host path, and its cost is set entirely by how
    /// many pixels cross the bus. When `target` is an exact power-of-two reduction the GPU produces
    /// it before the copy, so a 2560x1600 desktop bound for a 1080p television moves a quarter of
    /// the bytes it otherwise would. Any other ratio is read at full size and left for the caller's
    /// own scaler, which is the honest split rather than quietly changing the aspect ratio.
    ///
    /// # Errors
    /// [`CaptureError::Platform`] when the copy or the map fails.
    pub fn read_scaled(
        &mut self,
        texture: &ID3D11Texture2D,
        format: CaptureFormat,
        target: (u32, u32),
        presentation_time_us: i64,
    ) -> Result<Option<SourceFrame>, CaptureError> {
        let source_size = (format.width, format.height);
        let halvings = halvings_to_reach(source_size, target).filter(|&levels| levels > 0);

        // Three ways to reach the target, in order of preference.
        //
        // A mip chain is cheapest but only expresses exact halvings, so it covers 2560x1600 to
        // 1280x800 and nothing in between. The video processor scales by any ratio in hardware,
        // which is what actually covers the common case: a 16:10 desktop reduced to a 1920 cap is a
        // 0.75 ratio, and before this it fell through to no GPU reduction at all and a full-size
        // readback followed by a resize on the processor. Failing both, the frame is read back
        // whole and the session resizes it.
        let processor_scaled = if halvings.is_none() && target != source_size {
            self.scale_with_processor(texture, source_size, target)
        } else {
            None
        };

        let (width, height) = if halvings.is_some() || processor_scaled.is_some() {
            target
        } else {
            source_size
        };

        // Borrowed in a narrow scope: the scaler owns the texture it returns, and the staging copy
        // below needs `self` mutably again.
        let scaled = match halvings {
            Some(levels) => {
                let (chain, level) = self.scaler.scale(texture, source_size, levels)?;
                Some((chain.clone(), level))
            }
            None => None,
        };

        self.ensure_staging(texture, width, height)?;

        // Queue this frame's copy into one texture...
        let write_index = self.write_index;
        {
            let staging = self.staging[write_index]
                .as_ref()
                .ok_or_else(|| CaptureError::Platform("no staging texture".into()))?;

            match (&scaled, &processor_scaled) {
                // SAFETY: the mip level named here has exactly the staging texture's dimensions,
                // which is what CopySubresourceRegion requires of a whole-subresource copy.
                (Some((chain, level)), _) => unsafe {
                    self.context
                        .CopySubresourceRegion(staging, 0, 0, 0, 0, chain, *level, None);
                },
                // SAFETY: the processor wrote a texture of exactly the staging size.
                (None, Some(reduced)) => unsafe { self.context.CopyResource(staging, reduced) },
                // SAFETY: both textures belong to `self.device` and have identical descriptions.
                (None, None) => unsafe { self.context.CopyResource(staging, texture) },
            }
        }

        // ...and read the one filled last frame, which the GPU has had a whole tick to finish.
        // Mapping the texture just written to is what turns this into a stall.
        let read_index = 1 - write_index;
        self.write_index = read_index;

        if !self.has_pending {
            // Only the copy just queued exists; there is no earlier frame to read. Reported as
            // "nothing this tick" rather than as an interruption, which would make the session
            // count a recovery and force a key frame for what is simply the pipeline filling.
            self.has_pending = true;
            return Ok(None);
        }

        let staging = self.staging[read_index]
            .as_ref()
            .ok_or_else(|| CaptureError::Platform("no staging texture".into()))?;

        let mut mapped = D3D11_MAPPED_SUBRESOURCE::default();
        // SAFETY: the staging texture was created with CPU read access and is not otherwise
        // mapped; the unmap below balances this on every path.
        unsafe {
            self.context
                .Map(staging, 0, D3D11_MAP_READ, 0, Some(&mut mapped))
                .map_err(platform)?;
        }

        let row_bytes = width as usize * 4;
        let pitch = mapped.RowPitch as usize;
        self.pixels.clear();
        self.pixels.resize(row_bytes * height as usize, 0);

        // SAFETY: Map reported a buffer of at least `pitch * height` bytes at `pData`.
        let source = unsafe {
            std::slice::from_raw_parts(mapped.pData.cast::<u8>(), pitch * height as usize)
        };
        // Copied row by row rather than wholesale: the GPU's pitch is almost never the visible row
        // width, and treating it as one shears the picture diagonally.
        for y in 0..height as usize {
            let from = y * pitch;
            let to = y * row_bytes;
            self.pixels[to..to + row_bytes].copy_from_slice(&source[from..from + row_bytes]);
        }

        // SAFETY: balances the successful Map above.
        unsafe { self.context.Unmap(staging, 0) };

        Ok(Some(SourceFrame {
            width,
            height,
            presentation_time_us,
            data: FrameData::Bgra {
                pixels: std::mem::take(&mut self.pixels),
                stride: width * 4,
            },
        }))
    }

    /// Reduces a frame with the video processor, for ratios a mip chain cannot express.
    ///
    /// Returns `None` rather than an error when the processor is unavailable or refuses: the caller
    /// then reads the frame back whole, which is slower and still correct. A capture path that
    /// failed outright because a scaling optimisation was unavailable would be a poor trade.
    fn scale_with_processor(
        &mut self,
        texture: &ID3D11Texture2D,
        source: (u32, u32),
        target: (u32, u32),
    ) -> Option<ID3D11Texture2D> {
        let stale = match self.processor.as_ref() {
            Some(converter) => converter.source() != source || converter.target() != target,
            None => true,
        };
        if stale {
            self.processor =
                GpuNv12Converter::for_scaling(&self.device, &self.context, source, target).ok();
            self.reduced = None;
        }

        if self.reduced.is_none() {
            let description = D3D11_TEXTURE2D_DESC {
                Width: target.0,
                Height: target.1,
                MipLevels: 1,
                ArraySize: 1,
                Format: DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc: DXGI_SAMPLE_DESC {
                    Count: 1,
                    Quality: 0,
                },
                Usage: D3D11_USAGE_DEFAULT,
                // The processor writes through a render target view, exactly as it does for NV12.
                BindFlags: D3D11_BIND_RENDER_TARGET.0 as u32,
                CPUAccessFlags: 0,
                MiscFlags: 0,
            };
            let mut reduced: Option<ID3D11Texture2D> = None;
            // SAFETY: the description is fully initialised and the out-parameter is valid.
            unsafe {
                self.device
                    .CreateTexture2D(&description, None, Some(&mut reduced))
            }
            .ok()?;
            self.reduced = reduced;
        }

        let reduced = self.reduced.clone()?;
        self.processor
            .as_mut()?
            .convert(texture, &reduced)
            .ok()
            .map(|()| reduced)
    }

    /// Takes back the owned pixel allocation after the encoder has finished borrowing it.
    fn recycle(&mut self, frame: SourceFrame) {
        // A texture frame owns nothing on this side of the bus: the GPU path hands back a reference
        // to a pooled texture, and there is no allocation to reclaim.
        let FrameData::Bgra { mut pixels, .. } = frame.data else {
            return;
        };
        pixels.clear();

        // There is normally no competing buffer: a session has exactly one frame in flight. Keep
        // the larger allocation defensively if a direct caller did acquire another frame first.
        if pixels.capacity() >= self.pixels.capacity() {
            self.pixels = pixels;
        }
    }

    /// Creates the staging texture, or re-creates it when the desktop resolution changed.
    fn ensure_staging(
        &mut self,
        texture: &ID3D11Texture2D,
        width: u32,
        height: u32,
    ) -> Result<(), CaptureError> {
        if self.staging[0].is_some() && self.staging[1].is_some() && self.size == (width, height) {
            return Ok(());
        }

        let mut description = D3D11_TEXTURE2D_DESC::default();
        // SAFETY: the texture is live; GetDesc fills the description by pointer.
        unsafe { texture.GetDesc(&mut description) };

        description.Usage = D3D11_USAGE_STAGING;
        description.BindFlags = 0;
        description.CPUAccessFlags = D3D11_CPU_ACCESS_READ.0 as u32;
        description.MiscFlags = 0;
        // Sized to what will actually be read, which after a GPU reduction is smaller than the
        // desktop; a staging texture left at source size would copy the pixels this exists to save.
        description.Width = width;
        description.Height = height;
        description.MipLevels = 1;

        let mut first = None;
        let mut second = None;
        // SAFETY: the description came from a real texture and was only narrowed to a staging
        // texture; the out-parameter is valid.
        unsafe {
            self.device
                .CreateTexture2D(&description, None, Some(&mut first))
                .map_err(platform)?;
            self.device
                .CreateTexture2D(&description, None, Some(&mut second))
                .map_err(platform)?;
        }

        self.staging = [first, second];
        // Neither texture holds anything yet, so the next read must not treat one as a previous
        // frame; a resolution change that skipped this would read a stale, wrongly-sized buffer.
        self.write_index = 0;
        self.has_pending = false;
        self.size = (width, height);
        Ok(())
    }
}

/// Turns a capture outcome plus an optional texture into what a session expects.
///
/// Kept separate from [`FrameReadback::read`] so the mapping from "no texture this tick" to
/// "unchanged" is testable without a device.
#[must_use]
pub fn outcome_needs_readback(outcome: FrameOutcome, has_texture: bool) -> bool {
    matches!(outcome, FrameOutcome::Captured) && has_texture
}

/// The desktop, as a source of encodable frames.
///
/// Owns the duplication and the readback together, re-creating the duplication when Windows takes
/// it away — which it does routinely, on every lock screen and UAC prompt — so the session above
/// only has to decide what a recovery means rather than how to perform one.
pub struct DesktopFrameSource {
    duplication: super::duplication::DesktopDuplication,
    readback: FrameReadback,
    /// What the session will ultimately encode, so the reduction can happen before the bus copy
    /// rather than after it. Defaults to the desktop's own size, which reduces by nothing.
    target: (u32, u32),
    /// Owned copies of captured textures, when frames are handed over on the GPU.
    ///
    /// `None` means the readback path: frames come back as system memory, which every encoder can
    /// take. The GPU path is only usable by a hardware encoder sharing this device.
    gpu_frames: Option<BgraTexturePool>,
    output_index: u32,
    origin: std::time::Instant,
}

impl DesktopFrameSource {
    /// Opens the primary display and prepares it for readback.
    ///
    /// # Errors
    /// Whatever [`super::duplication::DesktopDuplication::open`] reports.
    pub fn open(output_index: u32) -> Result<Self, CaptureError> {
        let duplication = super::duplication::DesktopDuplication::open(output_index)?;
        let readback = FrameReadback::new(
            duplication.device().clone(),
            duplication.context().clone(),
            duplication.format(),
        );
        let format = duplication.format();
        let target = (format.width, format.height);

        Ok(Self {
            duplication,
            readback,
            target,
            gpu_frames: None,
            output_index,
            origin: std::time::Instant::now(),
        })
    }

    /// Asks for frames to arrive already reduced to `target` when the GPU can do it exactly.
    ///
    /// An inexact ratio is ignored rather than approximated: the caller's own scaler handles those,
    /// and silently changing the aspect ratio here would be far worse than doing nothing.
    pub fn set_target_size(&mut self, target: (u32, u32)) {
        self.target = target;
    }

    /// Hands frames over as GPU textures instead of reading them back.
    ///
    /// Only correct when the consumer encodes on this same device: two devices on one adapter
    /// cannot pass textures without shared handles. The caller proves that by construction, since
    /// the encoder is built from [`Self::device`].
    ///
    /// # Errors
    /// [`CaptureError::Platform`] when the pool cannot be allocated, which leaves the source on the
    /// readback path rather than failing the session.
    pub fn enable_gpu_frames(&mut self) -> Result<(), CaptureError> {
        let format = self.duplication.format();
        self.gpu_frames = Some(BgraTexturePool::new(
            self.duplication.device().clone(),
            self.duplication.context().clone(),
            (format.width, format.height),
        )?);
        Ok(())
    }

    /// Whether frames are being handed over on the GPU.
    #[must_use]
    pub fn uses_gpu_frames(&self) -> bool {
        self.gpu_frames.is_some()
    }

    /// The device frames are captured on, for building an encoder that can read them.
    #[must_use]
    pub fn device(&self) -> &ID3D11Device {
        self.duplication.device()
    }

    /// The immediate context frames are captured with.
    #[must_use]
    pub fn context(&self) -> &ID3D11DeviceContext {
        self.duplication.context()
    }

    /// The size frames are currently read back at.
    #[must_use]
    pub fn target_size(&self) -> (u32, u32) {
        self.target
    }

    /// The geometry and adapter the frames come from.
    #[must_use]
    pub fn format(&self) -> CaptureFormat {
        self.duplication.format()
    }

    /// Re-creates the duplication after Windows took it away.
    fn reopen(&mut self) -> Result<(), CaptureError> {
        let duplication = super::duplication::DesktopDuplication::open(self.output_index)?;
        self.readback = FrameReadback::new(
            duplication.device().clone(),
            duplication.context().clone(),
            duplication.format(),
        );
        self.duplication = duplication;
        Ok(())
    }
}

impl crate::session::FrameSource for DesktopFrameSource {
    fn next_frame(
        &mut self,
        timeout_ms: u32,
    ) -> Result<(FrameOutcome, Option<SourceFrame>), CaptureError> {
        let (outcome, texture) = match self.duplication.acquire(timeout_ms) {
            Ok(result) => result,
            Err(CaptureError::Interrupted) => {
                // Re-create eagerly so the next tick can capture again. A failure to reopen is
                // still only an interruption: the desktop may simply not be back yet, and ending
                // the session on a transient lock screen would be worse than waiting.
                let _ = self.reopen();
                return Err(CaptureError::Interrupted);
            }
            Err(error) => return Err(error),
        };

        let Some(texture) = texture else {
            self.duplication.release();
            return Ok((outcome, None));
        };

        let format = self.duplication.format();
        let elapsed = self.origin.elapsed();
        let presentation_time_us = i64::try_from(elapsed.as_micros()).unwrap_or(i64::MAX);

        if let Some(pool) = self.gpu_frames.as_mut() {
            // Copied before the release, because the duplication only lends its texture. The copy
            // stays on the GPU, which is the entire point of this path.
            let owned = pool.copy_of(&texture);
            self.duplication.release();
            return Ok((
                outcome,
                Some(SourceFrame {
                    width: format.width,
                    height: format.height,
                    presentation_time_us,
                    data: FrameData::Texture(owned),
                }),
            ));
        }

        let frame = self
            .readback
            .read_scaled(&texture, format, self.target, presentation_time_us);
        // Released whether or not the readback succeeded: holding the frame would make the next
        // acquire fail, turning one bad frame into a dead session.
        self.duplication.release();
        match frame? {
            Some(frame) => Ok((outcome, Some(frame))),
            // The readback pipeline is still filling; there is genuinely nothing to encode yet.
            None => Ok((FrameOutcome::Unchanged, None)),
        }
    }

    fn recycle_frame(&mut self, frame: SourceFrame) {
        self.readback.recycle(frame);
    }
}

/// Owned copies of captured desktop textures.
///
/// Desktop duplication lends its texture: `ReleaseFrame` invalidates it, and the frame must be
/// released before the next acquire or capture stalls. So a frame that is going to outlive the
/// acquire has to be copied first.
///
/// The copy is GPU to GPU, which is the point. It costs a fraction of a millisecond and keeps the
/// frame on the device, where the colour conversion and the encoder both want it; reading it back
/// to system memory instead costs about three milliseconds a frame at 1080p on this project's
/// development machine.
///
/// Several textures rather than one, because the encoder reads a frame asynchronously after it has
/// been submitted. Overwriting the only copy every tick would rewrite pixels the encoder is still
/// consuming.
pub struct BgraTexturePool {
    device: ID3D11Device,
    context: ID3D11DeviceContext,
    textures: Vec<ID3D11Texture2D>,
    next: usize,
    size: (u32, u32),
}

impl BgraTexturePool {
    /// How many frames may be in flight before one is reused.
    const DEPTH: usize = 6;

    /// Builds a pool of BGRA textures matching the captured desktop.
    fn new(
        device: ID3D11Device,
        context: ID3D11DeviceContext,
        size: (u32, u32),
    ) -> Result<Self, CaptureError> {
        let description = D3D11_TEXTURE2D_DESC {
            Width: size.0,
            Height: size.1,
            MipLevels: 1,
            ArraySize: 1,
            Format: DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc: DXGI_SAMPLE_DESC {
                Count: 1,
                Quality: 0,
            },
            Usage: D3D11_USAGE_DEFAULT,
            // No bind flags at all, which is what a video processor input view wants.
            //
            // Counter-intuitively, `D3D11_BIND_SHADER_RESOURCE` on its own is *refused* here — the
            // input view is rejected with "the parameter is incorrect" and nothing about bind
            // flags. No flags works, and so does shader resource combined with render target;
            // shader resource alone is the one combination that does not.
            // `report_which_input_texture_the_capture_device_will_accept` is the test that
            // establishes this, because none of it is guessable.
            BindFlags: 0,
            CPUAccessFlags: 0,
            MiscFlags: 0,
        };

        let mut textures = Vec::with_capacity(Self::DEPTH);
        for _ in 0..Self::DEPTH {
            let mut texture: Option<ID3D11Texture2D> = None;
            // SAFETY: the description is fully initialised and the out-parameter is valid.
            unsafe { device.CreateTexture2D(&description, None, Some(&mut texture)) }
                .map_err(platform)?;
            textures.push(texture.ok_or_else(|| CaptureError::Platform("no BGRA texture".into()))?);
        }

        Ok(Self {
            device,
            context,
            textures,
            next: 0,
            size,
        })
    }

    /// The frame size this pool holds.
    #[must_use]
    pub fn size(&self) -> (u32, u32) {
        self.size
    }

    /// Copies `source` into the next pooled texture and returns it.
    fn copy_of(&mut self, source: &ID3D11Texture2D) -> ID3D11Texture2D {
        let destination = self.textures[self.next].clone();
        self.next = (self.next + 1) % self.textures.len();
        // SAFETY: both textures share a description; the copy is queued on the immediate context.
        unsafe { self.context.CopyResource(&destination, source) };
        destination
    }

    /// The device these textures live on.
    #[must_use]
    pub fn device(&self) -> &ID3D11Device {
        &self.device
    }
}

/// The packed BGRA byte count when it can be represented by a `Vec` on this target.
fn packed_bgra_len(width: u32, height: u32) -> Option<usize> {
    let len = u64::from(width)
        .checked_mul(u64::from(height))?
        .checked_mul(4)?;
    usize::try_from(len)
        .ok()
        .filter(|&len| len <= isize::MAX as usize)
}

fn platform(error: windows::core::Error) -> CaptureError {
    CaptureError::Platform(error.message())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_a_captured_outcome_carrying_a_texture_needs_a_readback() {
        assert!(outcome_needs_readback(FrameOutcome::Captured, true));
        assert!(!outcome_needs_readback(FrameOutcome::Captured, false));
        assert!(!outcome_needs_readback(FrameOutcome::Unchanged, true));
        assert!(!outcome_needs_readback(FrameOutcome::Unchanged, false));
    }
}
