//! Downscaling a captured frame on the GPU, before it crosses the bus.
//!
//! Readback is the most expensive stage on the host path — measured on this project at a median of
//! 25ms against 5ms for colour conversion and 10ms for encode, because a 2560x1600 desktop is
//! 16MB of BGRA every single frame. Almost all of that is wasted: a Fire TV renders 1080p, so the
//! extra pixels are copied across the bus, converted, and encoded only to be thrown away by the
//! television's scaler.
//!
//! Scaling before the copy fixes the dominant cost rather than the cheap ones. Doing it with the
//! mip chain is what makes it nearly free and shader-free: Direct3D already knows how to build
//! progressively halved copies of a texture with a proper box filter, so a 2x reduction is one
//! `GenerateMips` call and a copy from the level below. That also produces a better picture than
//! the CPU scaler it replaces, because every source pixel contributes.
//!
//! The catch is that mip levels only ever halve, so this handles exact power-of-two reductions and
//! declines everything else. The caller keeps its CPU scaler for the remainder, which is the right
//! split: the common desktop sizes reduce by exactly 2x or 4x to reach 1080p or 720p.

use windows::Win32::Graphics::Direct3D11::{
    ID3D11Device, ID3D11DeviceContext, ID3D11ShaderResourceView, ID3D11Texture2D,
    D3D11_BIND_RENDER_TARGET, D3D11_BIND_SHADER_RESOURCE, D3D11_RESOURCE_MISC_GENERATE_MIPS,
    D3D11_TEXTURE2D_DESC, D3D11_USAGE_DEFAULT,
};

use super::CaptureError;

/// How many times a frame must be halved to reach a target, when that is exact.
///
/// Returns `None` unless both edges reduce by the same exact power of two, because a mip level is
/// a halving of the whole image — accepting an approximate match would silently change the aspect
/// ratio, which shows up as a stretched picture rather than as an error.
#[must_use]
pub fn halvings_to_reach(source: (u32, u32), target: (u32, u32)) -> Option<u32> {
    if source == target {
        return Some(0);
    }
    if target.0 == 0 || target.1 == 0 {
        return None;
    }

    let mut width = source.0;
    let mut height = source.1;
    let mut levels = 0u32;

    // Eight halvings takes even an 8K edge below one pixel, so this cannot spin.
    while levels < 8 {
        if width % 2 != 0 || height % 2 != 0 {
            return None;
        }
        width /= 2;
        height /= 2;
        levels += 1;

        if (width, height) == target {
            return Some(levels);
        }
        if width < target.0 || height < target.1 {
            return None;
        }
    }

    None
}

/// Halves a captured frame on the GPU using the mip chain.
pub struct MipScaler {
    device: ID3D11Device,
    context: ID3D11DeviceContext,
    chain: Option<ID3D11Texture2D>,
    view: Option<ID3D11ShaderResourceView>,
    source_size: (u32, u32),
    levels: u32,
}

impl MipScaler {
    /// Builds a scaler on the device the frames are produced on.
    #[must_use]
    pub fn new(device: ID3D11Device, context: ID3D11DeviceContext) -> Self {
        Self {
            device,
            context,
            chain: None,
            view: None,
            source_size: (0, 0),
            levels: 0,
        }
    }

    /// Scales `texture` down by `halvings` and returns the mip texture plus the level to read.
    ///
    /// The returned texture is owned by the scaler and reused across frames; the caller copies out
    /// of the named level and must not hold on to it.
    ///
    /// # Errors
    /// [`CaptureError::Platform`] when the intermediate texture cannot be created.
    pub fn scale(
        &mut self,
        texture: &ID3D11Texture2D,
        source_size: (u32, u32),
        halvings: u32,
    ) -> Result<(&ID3D11Texture2D, u32), CaptureError> {
        self.ensure_chain(texture, source_size, halvings)?;

        let chain = self
            .chain
            .as_ref()
            .ok_or_else(|| CaptureError::Platform("no mip chain".into()))?;
        let view = self
            .view
            .as_ref()
            .ok_or_else(|| CaptureError::Platform("no mip view".into()))?;

        // SAFETY: both textures live on `self.device`; the destination's level 0 has the same
        // description as the source, which is what CopySubresourceRegion requires.
        unsafe {
            self.context
                .CopySubresourceRegion(chain, 0, 0, 0, 0, texture, 0, None);
            // Direct3D fills every level below 0 with a filtered reduction of the one above it.
            self.context.GenerateMips(view);
        }

        Ok((chain, halvings))
    }

    /// Creates the mip chain, or re-creates it when the geometry changed.
    fn ensure_chain(
        &mut self,
        texture: &ID3D11Texture2D,
        source_size: (u32, u32),
        halvings: u32,
    ) -> Result<(), CaptureError> {
        if self.chain.is_some() && self.source_size == source_size && self.levels == halvings {
            return Ok(());
        }

        let mut description = D3D11_TEXTURE2D_DESC::default();
        // SAFETY: the texture is live; GetDesc fills the description by pointer.
        unsafe { texture.GetDesc(&raw mut description) };

        description.Usage = D3D11_USAGE_DEFAULT;
        description.CPUAccessFlags = 0;
        // GenerateMips needs both bindings and the explicit misc flag; without the render-target
        // binding it silently produces an all-black chain rather than failing.
        description.BindFlags = (D3D11_BIND_SHADER_RESOURCE.0 | D3D11_BIND_RENDER_TARGET.0) as u32;
        description.MiscFlags = D3D11_RESOURCE_MISC_GENERATE_MIPS.0 as u32;
        description.MipLevels = halvings + 1;

        let mut chain = None;
        // SAFETY: the description is a valid render-target texture and the out-parameter is live.
        unsafe {
            self.device
                .CreateTexture2D(&raw const description, None, Some(&raw mut chain))
                .map_err(platform)?;
        }
        let chain = chain.ok_or_else(|| CaptureError::Platform("mip chain not created".into()))?;

        let mut view = None;
        // SAFETY: the texture was just created with SHADER_RESOURCE binding, so a default view
        // over it is valid.
        unsafe {
            self.device
                .CreateShaderResourceView(&chain, None, Some(&raw mut view))
                .map_err(platform)?;
        }

        self.view = view;
        self.chain = Some(chain);
        self.source_size = source_size;
        self.levels = halvings;
        Ok(())
    }
}

fn platform(error: windows::core::Error) -> CaptureError {
    CaptureError::Platform(error.message())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_unchanged_size_needs_no_halving() {
        assert_eq!(halvings_to_reach((1920, 1080), (1920, 1080)), Some(0));
    }

    #[test]
    fn one_halving_is_recognised() {
        assert_eq!(halvings_to_reach((2560, 1600), (1280, 800)), Some(1));
    }

    #[test]
    fn two_halvings_are_recognised() {
        assert_eq!(halvings_to_reach((2560, 1600), (640, 400)), Some(2));
    }

    #[test]
    fn a_non_power_of_two_reduction_is_declined_for_the_cpu_scaler() {
        // 2560 -> 1920 is a 0.75 factor, which no mip level represents.
        assert_eq!(halvings_to_reach((2560, 1600), (1920, 1200)), None);
    }

    #[test]
    fn an_aspect_changing_target_is_declined_rather_than_stretched() {
        // Halving only ever reduces both edges together; accepting this would squash the picture.
        assert_eq!(halvings_to_reach((2560, 1600), (1280, 1600)), None);
    }

    #[test]
    fn an_odd_edge_cannot_be_halved() {
        assert_eq!(halvings_to_reach((1921, 1080), (960, 540)), None);
    }

    #[test]
    fn a_target_larger_than_the_source_is_declined() {
        // Upscaling is not this module's job, and a mip chain cannot do it at all.
        assert_eq!(halvings_to_reach((1280, 800), (2560, 1600)), None);
    }

    #[test]
    fn a_zero_target_is_declined_rather_than_looping() {
        assert_eq!(halvings_to_reach((1920, 1080), (0, 0)), None);
        assert_eq!(halvings_to_reach((1920, 1080), (1920, 0)), None);
    }

    #[test]
    fn a_target_that_never_lands_exactly_terminates() {
        // 1000 halves to 500, 250, 125 and then stops being even; the search must end rather than
        // spin looking for a level it will never reach.
        assert_eq!(halvings_to_reach((1000, 1000), (333, 333)), None);
    }

    #[test]
    fn the_common_desktop_reductions_are_all_representable() {
        // The sizes this optimisation exists for: 4K and 1600p desktops reaching a 1080p or 720p
        // television without touching the CPU scaler.
        assert_eq!(halvings_to_reach((3840, 2160), (1920, 1080)), Some(1));
        assert_eq!(halvings_to_reach((3840, 2160), (960, 540)), Some(2));
        assert_eq!(halvings_to_reach((2560, 1440), (1280, 720)), Some(1));
    }
}
