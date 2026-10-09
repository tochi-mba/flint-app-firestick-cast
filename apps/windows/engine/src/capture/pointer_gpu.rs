//! Drawing the pointer onto a frame that stays on the GPU.
//!
//! Only the small region under the pointer leaves the GPU: it is copied to a staging texture,
//! drawn on with the same code as a frame in system memory, and copied back. That costs a short
//! wait for the copy each frame, which is far less than reading the whole frame back.

use windows::Win32::Graphics::Direct3D11::{
    ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D, D3D11_BOX, D3D11_CPU_ACCESS_READ,
    D3D11_CPU_ACCESS_WRITE, D3D11_MAPPED_SUBRESOURCE, D3D11_MAP_READ_WRITE, D3D11_TEXTURE2D_DESC,
    D3D11_USAGE_STAGING,
};
use windows::Win32::Graphics::Dxgi::Common::{DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_SAMPLE_DESC};

use super::pointer::{Canvas, PointerTrack, MAX_POINTER_SIDE};
use super::CaptureError;

/// The staging texture the region under the pointer is drawn on, made once and kept.
#[derive(Default)]
pub struct PointerOverlay {
    staging: Option<ID3D11Texture2D>,
}

impl PointerOverlay {
    /// Draws the pointer onto `frame`, a BGRA texture of the desktop at its own size.
    ///
    /// Returns whether anything was drawn.
    ///
    /// # Errors
    /// [`CaptureError::Platform`] when the staging texture cannot be made or mapped. The frame is
    /// then sent without its pointer rather than not at all.
    pub fn draw(
        &mut self,
        device: &ID3D11Device,
        context: &ID3D11DeviceContext,
        frame: &ID3D11Texture2D,
        size: (u32, u32),
        track: Option<&PointerTrack>,
    ) -> Result<bool, CaptureError> {
        let (Some((x, y)), Some(shape)) = (
            track.and_then(PointerTrack::place),
            track.and_then(PointerTrack::shape),
        ) else {
            return Ok(false);
        };
        let left = (x - shape.hotspot.0).clamp(0, size.0 as i32) as u32;
        let top = (y - shape.hotspot.1).clamp(0, size.1 as i32) as u32;
        let right = (x - shape.hotspot.0 + shape.width as i32).clamp(0, size.0 as i32) as u32;
        let bottom = (y - shape.hotspot.1 + shape.height as i32).clamp(0, size.1 as i32) as u32;
        if right <= left || bottom <= top || !shape.is_drawable() {
            return Ok(false);
        }

        let staging = self.staging(device)?;
        let region = D3D11_BOX {
            left,
            top,
            front: 0,
            right,
            bottom,
            back: 1,
        };
        // SAFETY: both textures are live BGRA textures on this device, the region lies inside the
        // frame, and it fits the staging texture, which is as large as any drawable pointer.
        unsafe {
            context.CopySubresourceRegion(&staging, 0, 0, 0, 0, frame, 0, Some(&raw const region));
        }

        let mut mapped = D3D11_MAPPED_SUBRESOURCE::default();
        // SAFETY: the staging texture allows CPU reads and writes; it is unmapped below.
        unsafe { context.Map(&staging, 0, D3D11_MAP_READ_WRITE, 0, Some(&raw mut mapped)) }
            .map_err(platform)?;
        let width = right - left;
        let height = bottom - top;
        // SAFETY: a mapped texture of MAX_POINTER_SIDE rows, each RowPitch bytes long.
        let pixels = unsafe {
            std::slice::from_raw_parts_mut(
                mapped.pData.cast::<u8>(),
                (mapped.RowPitch * MAX_POINTER_SIDE) as usize,
            )
        };
        let mut canvas = Canvas {
            pixels,
            width,
            height,
            stride: mapped.RowPitch,
        };
        // Drawn at the pointer's place within the region, which starts at (left, top).
        let drew = super::pointer::draw(&mut canvas, shape, x - left as i32, y - top as i32);
        // SAFETY: mapped above.
        unsafe { context.Unmap(&staging, 0) };

        let back = D3D11_BOX {
            left: 0,
            top: 0,
            front: 0,
            right: width,
            bottom: height,
            back: 1,
        };
        // SAFETY: as for the first copy, the other way round.
        unsafe {
            context.CopySubresourceRegion(
                frame,
                0,
                left,
                top,
                0,
                &staging,
                0,
                Some(&raw const back),
            );
        }
        Ok(drew)
    }

    fn staging(&mut self, device: &ID3D11Device) -> Result<ID3D11Texture2D, CaptureError> {
        if let Some(staging) = &self.staging {
            return Ok(staging.clone());
        }

        let description = D3D11_TEXTURE2D_DESC {
            Width: MAX_POINTER_SIDE,
            Height: MAX_POINTER_SIDE,
            MipLevels: 1,
            ArraySize: 1,
            Format: DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc: DXGI_SAMPLE_DESC {
                Count: 1,
                Quality: 0,
            },
            Usage: D3D11_USAGE_STAGING,
            BindFlags: 0,
            CPUAccessFlags: (D3D11_CPU_ACCESS_READ.0 | D3D11_CPU_ACCESS_WRITE.0) as u32,
            MiscFlags: 0,
        };
        let mut staging = None;
        // SAFETY: a complete description and a valid out-parameter.
        unsafe { device.CreateTexture2D(&raw const description, None, Some(&raw mut staging)) }
            .map_err(platform)?;
        let staging = staging.ok_or_else(|| CaptureError::Platform("no staging texture".into()))?;
        self.staging = Some(staging.clone());
        Ok(staging)
    }
}

fn platform(error: windows::core::Error) -> CaptureError {
    CaptureError::Platform(error.message())
}

#[cfg(test)]
#[path = "pointer_gpu_tests.rs"]
mod tests;
