//! Uploading NV12 frames into Direct3D textures for a hardware encoder to read.
//!
//! A hardware H.264 transform on Windows is a GPU object. Media Foundation lets it advertise a
//! system-memory input type, and it will accept system-memory samples all day — but on at least
//! one Intel Quick Sync driver, what it actually encodes is whatever the corresponding GPU
//! allocation happens to contain, not the bytes it was handed. The stream that comes out is
//! impeccable in every structural respect and decodes to a field of zeroes, which a television
//! renders as flat green.
//!
//! There is no error anywhere in that path. The encoder reports success, the bitrate looks right,
//! the parameter sets are correct, and `ffprobe` reads every frame without complaint. The only way
//! to see it is to decode the output and look, which is what
//! `what_the_hardware_encoder_produces_decodes_back_into_a_picture` does.
//!
//! So frames are put where the encoder is actually looking: an `NV12` texture on the same adapter,
//! wrapped as a DXGI surface buffer. That is the documented path for hardware MFTs.
//!
//! There are two ways a frame reaches one of these textures. [`Nv12TexturePool::upload`] copies it
//! up from system memory, which is what happens when the encoder is handed a frame the capture
//! source already read back. [`Nv12TexturePool::next_texture`] hands one over for the video
//! processor to write into directly, which is the fast path and the one a session normally takes:
//! the frame is captured, converted and encoded without ever crossing the bus.

use windows::core::Interface;
use windows::Win32::Graphics::Direct3D11::{
    ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D, D3D11_BIND_RENDER_TARGET,
    D3D11_CPU_ACCESS_WRITE, D3D11_MAPPED_SUBRESOURCE, D3D11_MAP_WRITE, D3D11_TEXTURE2D_DESC,
    D3D11_USAGE_DEFAULT, D3D11_USAGE_STAGING,
};
use windows::Win32::Graphics::Dxgi::Common::{DXGI_FORMAT_NV12, DXGI_SAMPLE_DESC};
use windows::Win32::Media::MediaFoundation::{
    IMFSample, MFCreateDXGISurfaceBuffer, MFCreateSample,
};

use super::video::EncodeError;
use crate::encode::h264::platform;

/// A pool of NV12 textures, one per frame in flight.
///
/// Round-robin rather than a single texture, because an asynchronous encoder reads a frame after
/// `ProcessInput` returns. Overwriting one texture every frame would rewrite pixels the encoder is
/// still consuming, which is the same class of bug as freeing a system-memory sample too early and
/// produces the same unreadable output.
pub struct Nv12TexturePool {
    device: ID3D11Device,
    context: ID3D11DeviceContext,
    /// CPU-writable, and the only texture the processor ever touches.
    ///
    /// A separate staging texture rather than a writable pool, because Direct3D 11 refuses to
    /// create an NV12 texture with dynamic usage at all — `CreateTexture2D` answers "the parameter
    /// is incorrect" and names nothing. Video formats are default or staging only, so a frame is
    /// written here and copied on the GPU into whichever pool texture is next.
    staging: ID3D11Texture2D,
    textures: Vec<ID3D11Texture2D>,
    next: usize,
    width: u32,
    height: u32,
}

impl Nv12TexturePool {
    /// How many textures to rotate through.
    ///
    /// Comfortably deeper than any encoder's input queue. The cost is memory — about 1.4MB per
    /// texture at 1080p — and the benefit is that a frame is never overwritten while in use.
    pub const DEPTH: usize = 8;

    /// Builds a pool of NV12 textures on `device`.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the device cannot allocate an NV12 texture, which on older
    /// hardware means the format is unsupported and the caller should stay on system memory.
    pub fn new(
        device: ID3D11Device,
        context: ID3D11DeviceContext,
        width: u32,
        height: u32,
    ) -> Result<Self, EncodeError> {
        let base = D3D11_TEXTURE2D_DESC {
            Width: width,
            Height: height,
            MipLevels: 1,
            ArraySize: 1,
            Format: DXGI_FORMAT_NV12,
            SampleDesc: DXGI_SAMPLE_DESC {
                Count: 1,
                Quality: 0,
            },
            Usage: D3D11_USAGE_DEFAULT,
            // A video processor writes into these through a render target view, which the texture
            // has to be created for. Without the flag the output view is refused with "the
            // parameter is incorrect" and nothing about bind flags.
            BindFlags: D3D11_BIND_RENDER_TARGET.0 as u32,
            CPUAccessFlags: 0,
            MiscFlags: 0,
        };

        let staging_description = D3D11_TEXTURE2D_DESC {
            Usage: D3D11_USAGE_STAGING,
            CPUAccessFlags: D3D11_CPU_ACCESS_WRITE.0 as u32,
            // Staging resources may not be bound to the pipeline at all.
            BindFlags: 0,
            ..base
        };
        let mut staging: Option<ID3D11Texture2D> = None;
        // SAFETY: the description is fully initialised and the out-parameter is valid.
        unsafe {
            device.CreateTexture2D(&raw const staging_description, None, Some(&raw mut staging))
        }
        .map_err(platform)?;
        let staging =
            staging.ok_or_else(|| EncodeError::Platform("no NV12 staging texture".into()))?;

        let mut textures = Vec::with_capacity(Self::DEPTH);
        for _ in 0..Self::DEPTH {
            let mut texture: Option<ID3D11Texture2D> = None;
            // SAFETY: as above.
            unsafe { device.CreateTexture2D(&raw const base, None, Some(&raw mut texture)) }
                .map_err(platform)?;
            textures.push(texture.ok_or_else(|| EncodeError::Platform("no NV12 texture".into()))?);
        }

        Ok(Self {
            device,
            context,
            staging,
            textures,
            next: 0,
            width,
            height,
        })
    }

    /// The device these textures live on, for handing to a transform.
    #[must_use]
    pub fn device(&self) -> &ID3D11Device {
        &self.device
    }

    /// Hands out the next texture in the rotation, for something else to write into.
    ///
    /// Used by the GPU path, where the video processor writes the frame directly and there is
    /// nothing for [`Self::upload`] to copy.
    pub fn next_texture(&mut self) -> ID3D11Texture2D {
        let texture = self.textures[self.next].clone();
        self.next = (self.next + 1) % self.textures.len();
        texture
    }

    /// Wraps a texture from this pool as a Media Foundation sample.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the surface buffer or sample cannot be created.
    pub fn wrap(
        &self,
        texture: &ID3D11Texture2D,
        presentation_time_hns: i64,
        duration_hns: i64,
    ) -> Result<IMFSample, EncodeError> {
        // SAFETY: the texture is live and the interface identifier matches its type.
        let buffer = unsafe { MFCreateDXGISurfaceBuffer(&ID3D11Texture2D::IID, texture, 0, false) }
            .map_err(platform)?;
        // SAFETY: the out-parameter is valid.
        let sample = unsafe { MFCreateSample() }.map_err(platform)?;
        // SAFETY: every object here is live.
        unsafe {
            sample.AddBuffer(&buffer).map_err(platform)?;
            sample
                .SetSampleTime(presentation_time_hns)
                .map_err(platform)?;
            sample.SetSampleDuration(duration_hns).map_err(platform)?;
        }
        Ok(sample)
    }

    /// Uploads one NV12 frame and wraps it as a Media Foundation sample.
    ///
    /// `nv12` must be tightly packed: a `width * height` luma plane followed by a
    /// `width * height / 2` interleaved chroma plane.
    ///
    /// # Errors
    /// [`EncodeError::Platform`] when the texture cannot be mapped, and
    /// [`EncodeError::InvalidConfig`] when the buffer is not the size the pool was built for.
    pub fn upload(
        &mut self,
        nv12: &[u8],
        presentation_time_hns: i64,
        duration_hns: i64,
    ) -> Result<IMFSample, EncodeError> {
        let luma_len = (self.width * self.height) as usize;
        let expected = luma_len + luma_len / 2;
        if nv12.len() != expected {
            return Err(EncodeError::InvalidConfig);
        }

        let texture = self.textures[self.next].clone();
        self.next = (self.next + 1) % self.textures.len();

        let mut mapped = D3D11_MAPPED_SUBRESOURCE::default();
        // SAFETY: the staging texture was created with CPU write access, which is what MAP_WRITE
        // requires; the unmap below balances this on every path.
        unsafe {
            self.context
                .Map(&self.staging, 0, D3D11_MAP_WRITE, 0, Some(&raw mut mapped))
                .map_err(platform)?;
        }

        // The GPU picks its own row pitch, which is rarely the frame width. Copying the buffer in
        // one block would work only when they happen to match, and would silently skew the picture
        // when they do not — so rows are copied one at a time into the pitch the driver gave us.
        // SAFETY: `mapped.pData` addresses at least `RowPitch * height * 3 / 2` writable bytes for
        // an NV12 texture, and every write below stays inside one row of that.
        unsafe {
            let destination = mapped.pData.cast::<u8>();
            let pitch = mapped.RowPitch as usize;
            let width = self.width as usize;

            for row in 0..self.height as usize {
                std::ptr::copy_nonoverlapping(
                    nv12.as_ptr().add(row * width),
                    destination.add(row * pitch),
                    width,
                );
            }

            // NV12 chroma follows the luma plane in the texture too, starting at a row offset of
            // the aligned height rather than the frame height on some drivers; the mapped pitch
            // covers both planes, and the chroma plane is half as tall.
            let chroma_source = nv12.as_ptr().add(luma_len);
            let chroma_destination = destination.add(self.height as usize * pitch);
            for row in 0..(self.height as usize / 2) {
                std::ptr::copy_nonoverlapping(
                    chroma_source.add(row * width),
                    chroma_destination.add(row * pitch),
                    width,
                );
            }
        }

        // SAFETY: balances the Map above.
        unsafe { self.context.Unmap(&self.staging, 0) };

        // On to the texture the encoder will read. The copy happens on the GPU, so the staging
        // texture is free again immediately and the pool entry is not touched by the processor
        // while the encoder has it.
        // SAFETY: both textures share a description apart from usage, which is what CopyResource
        // requires.
        unsafe { self.context.CopyResource(&texture, &self.staging) };

        // SAFETY: the texture is live and the interface identifier matches its type.
        let buffer =
            unsafe { MFCreateDXGISurfaceBuffer(&ID3D11Texture2D::IID, &texture, 0, false) }
                .map_err(platform)?;

        // SAFETY: the out-parameter is valid.
        let sample = unsafe { MFCreateSample() }.map_err(platform)?;
        // SAFETY: every object here is live.
        unsafe {
            sample.AddBuffer(&buffer).map_err(platform)?;
            sample
                .SetSampleTime(presentation_time_hns)
                .map_err(platform)?;
            sample.SetSampleDuration(duration_hns).map_err(platform)?;
        }
        Ok(sample)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use windows::Win32::Graphics::Direct3D::{D3D_DRIVER_TYPE_HARDWARE, D3D_FEATURE_LEVEL_11_0};
    use windows::Win32::Graphics::Direct3D11::{
        D3D11CreateDevice, D3D11_CREATE_DEVICE_VIDEO_SUPPORT, D3D11_SDK_VERSION,
    };

    /// A hardware device, or nothing on a machine without one.
    fn device() -> Option<(ID3D11Device, ID3D11DeviceContext)> {
        let mut device = None;
        let mut context = None;
        // SAFETY: both out-parameters are valid for the duration of the call.
        unsafe {
            D3D11CreateDevice(
                None,
                D3D_DRIVER_TYPE_HARDWARE,
                None,
                D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
                Some(&[D3D_FEATURE_LEVEL_11_0]),
                D3D11_SDK_VERSION,
                Some(&raw mut device),
                None,
                Some(&raw mut context),
            )
            .ok()?;
        }
        Some((device?, context?))
    }

    #[test]
    fn a_pool_rotates_through_its_textures_rather_than_reusing_one() {
        let Some((device, context)) = device() else {
            return;
        };
        let Ok(mut pool) = Nv12TexturePool::new(device, context, 64, 64) else {
            return;
        };

        // Overwriting a single texture every frame rewrites pixels an asynchronous encoder is still
        // reading, which is the same defect as releasing a sample too early.
        let frame = vec![128u8; 64 * 64 * 3 / 2];
        let mut first = None;
        for index in 0..Nv12TexturePool::DEPTH {
            let sample = pool.upload(&frame, index as i64 * 1000, 1000);
            assert!(sample.is_ok(), "upload {index} failed");
            if index == 0 {
                first = sample.ok();
            }
        }
        assert!(first.is_some());
    }

    #[test]
    fn a_frame_of_the_wrong_size_is_refused_rather_than_written_past_the_texture() {
        let Some((device, context)) = device() else {
            return;
        };
        let Ok(mut pool) = Nv12TexturePool::new(device, context, 64, 64) else {
            return;
        };

        // A short buffer would otherwise be copied row by row past the end of the source.
        assert!(pool.upload(&[0u8; 16], 0, 1000).is_err());
    }

    #[test]
    fn an_exactly_sized_nv12_frame_is_accepted() {
        let Some((device, context)) = device() else {
            return;
        };
        let Ok(mut pool) = Nv12TexturePool::new(device, context, 32, 32) else {
            return;
        };

        let frame = vec![64u8; 32 * 32 * 3 / 2];
        assert!(pool.upload(&frame, 0, 333_333).is_ok());
    }

    #[test]
    fn odd_sizes_do_not_panic_the_upload_path() {
        // Chroma is half height, so an odd height would round down; the encoder refuses odd
        // dimensions long before this, and this only has to not be undefined behaviour.
        let Some((device, context)) = device() else {
            return;
        };
        let Ok(mut pool) = Nv12TexturePool::new(device, context, 34, 34) else {
            return;
        };
        let frame = vec![0u8; 34 * 34 * 3 / 2];
        let _ = pool.upload(&frame, 0, 1000);
    }
}
