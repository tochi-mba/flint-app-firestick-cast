//! Nearest-neighbour resize for tightly packed BGRA frames.
//!
//! A 4K desktop mirrored to a 1080p television buys nothing from encoding at source resolution.
//! This is the cheapest correct resize: no filtering, no extra buffers beyond the destination the
//! caller already owns. A later hardware scaler can replace it without touching the session.

/// Why a resize could not run.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ScaleError {
    /// Width or height was zero.
    Empty,
    /// The destination is not even in both axes, so it has no NV12 representation.
    OddDestination,
    /// The source buffer is shorter than `stride * height`.
    TruncatedSource,
}

impl std::fmt::Display for ScaleError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Empty => write!(formatter, "cannot scale an empty frame"),
            Self::OddDestination => {
                write!(
                    formatter,
                    "the destination size is odd, so it has no NV12 representation"
                )
            }
            Self::TruncatedSource => {
                write!(formatter, "the source buffer is shorter than its geometry")
            }
        }
    }
}

impl std::error::Error for ScaleError {}

/// Resizes packed BGRA into `destination`, reusing that buffer's allocation.
///
/// # Errors
/// [`ScaleError`] when the geometry cannot produce a legal encoder input.
pub fn scale_bgra(
    source: &[u8],
    source_width: u32,
    source_height: u32,
    source_stride: u32,
    destination_width: u32,
    destination_height: u32,
    destination: &mut Vec<u8>,
) -> Result<(), ScaleError> {
    if source_width == 0 || source_height == 0 || destination_width == 0 || destination_height == 0
    {
        return Err(ScaleError::Empty);
    }
    if destination_width % 2 != 0 || destination_height % 2 != 0 {
        return Err(ScaleError::OddDestination);
    }

    let row_bytes = source_width as usize * 4;
    let stride = source_stride as usize;
    let needed = stride
        .checked_mul(source_height as usize)
        .ok_or(ScaleError::TruncatedSource)?;
    if source.len() < needed || stride < row_bytes {
        return Err(ScaleError::TruncatedSource);
    }

    let dest_row = destination_width as usize * 4;
    let dest_len = dest_row * destination_height as usize;
    destination.clear();
    destination.resize(dest_len, 0);

    if source_width == destination_width && source_height == destination_height {
        for y in 0..source_height as usize {
            let from = y * stride;
            let to = y * dest_row;
            destination[to..to + dest_row].copy_from_slice(&source[from..from + dest_row]);
        }
        return Ok(());
    }

    for y in 0..destination_height as usize {
        let source_y =
            (y as u64 * u64::from(source_height) / u64::from(destination_height)) as usize;
        let source_row = source_y * stride;
        let dest_row_start = y * dest_row;
        for x in 0..destination_width as usize {
            let source_x =
                (x as u64 * u64::from(source_width) / u64::from(destination_width)) as usize;
            let from = source_row + source_x * 4;
            let to = dest_row_start + x * 4;
            destination[to..to + 4].copy_from_slice(&source[from..from + 4]);
        }
    }

    Ok(())
}

/// Chooses an even encoded size, optionally capping the long edge.
///
/// The cap is applied to width and height is scaled to keep the aspect ratio, then both edges are
/// forced even so chroma subsampling has a representation.
#[must_use]
pub fn encoded_frame_size(width: u32, height: u32, max_width: u32) -> (u32, u32) {
    let (width, height) = if max_width > 0 && width > max_width {
        let scaled_height =
            (u64::from(height) * u64::from(max_width) / u64::from(width.max(1))) as u32;
        (max_width, scaled_height)
    } else {
        (width, height)
    };

    ((width & !1).max(2), (height & !1).max(2))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn packed(width: u32, height: u32, pixel: [u8; 4]) -> Vec<u8> {
        let mut bytes = Vec::with_capacity((width * height * 4) as usize);
        for _ in 0..(width * height) {
            bytes.extend_from_slice(&pixel);
        }
        bytes
    }

    #[test]
    fn a_same_size_copy_strips_stride_padding() {
        let mut source = vec![0u8; 16 * 2];
        source[0..8].copy_from_slice(&[1, 2, 3, 4, 5, 6, 7, 8]);
        source[16..24].copy_from_slice(&[9, 8, 7, 6, 5, 4, 3, 2]);
        let mut destination = Vec::new();

        scale_bgra(&source, 2, 2, 16, 2, 2, &mut destination).unwrap();

        assert_eq!(
            destination,
            vec![1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2]
        );
    }

    #[test]
    fn a_one_by_two_source_with_padding_packs_into_an_even_destination() {
        // 1x2 is legal as a source; the destination must still be even.
        let mut source = vec![0u8; 16];
        source[0..4].copy_from_slice(&[9, 8, 7, 6]);
        source[8..12].copy_from_slice(&[5, 4, 3, 2]);
        let mut destination = Vec::new();

        scale_bgra(&source, 1, 2, 8, 2, 2, &mut destination).unwrap();

        assert_eq!(destination.len(), 16);
        assert_eq!(&destination[0..4], &[9, 8, 7, 6]);
        assert_eq!(&destination[8..12], &[5, 4, 3, 2]);
    }

    #[test]
    fn an_empty_geometry_is_refused() {
        let mut destination = Vec::new();
        assert_eq!(
            scale_bgra(&[0; 4], 0, 1, 4, 2, 2, &mut destination).unwrap_err(),
            ScaleError::Empty
        );
    }

    #[test]
    fn an_odd_destination_is_refused() {
        let source = packed(2, 2, [1, 2, 3, 4]);
        let mut destination = Vec::new();
        assert_eq!(
            scale_bgra(&source, 2, 2, 8, 3, 2, &mut destination).unwrap_err(),
            ScaleError::OddDestination
        );
    }

    #[test]
    fn a_truncated_source_is_refused_rather_than_read_past_its_end() {
        let mut destination = Vec::new();
        assert_eq!(
            scale_bgra(&[0; 4], 2, 2, 8, 2, 2, &mut destination).unwrap_err(),
            ScaleError::TruncatedSource
        );
    }

    #[test]
    fn downscaling_samples_the_mapped_pixel_rather_than_averaging() {
        let mut source = packed(4, 2, [0, 0, 0, 255]);
        source[0..4].copy_from_slice(&[255, 0, 0, 255]);
        let mut destination = Vec::new();

        scale_bgra(&source, 4, 2, 16, 2, 2, &mut destination).unwrap();

        assert_eq!(&destination[0..4], &[255, 0, 0, 255]);
        assert_eq!(destination.len(), 16);
    }

    #[test]
    fn the_destination_buffer_is_reused_without_growing_past_need() {
        let source = packed(2, 2, [1, 2, 3, 4]);
        let mut destination = Vec::with_capacity(64);
        let capacity = destination.capacity();

        scale_bgra(&source, 2, 2, 8, 2, 2, &mut destination).unwrap();

        assert_eq!(destination.len(), 16);
        assert_eq!(destination.capacity(), capacity);
    }

    #[test]
    fn errors_describe_themselves_for_the_diagnostics_page() {
        assert!(ScaleError::Empty.to_string().contains("empty"));
        assert!(ScaleError::OddDestination.to_string().contains("odd"));
        assert!(ScaleError::TruncatedSource.to_string().contains("shorter"));
    }

    #[test]
    fn a_cap_shrinks_width_and_keeps_the_aspect_ratio() {
        assert_eq!(encoded_frame_size(2560, 1600, 1920), (1920, 1200));
    }

    #[test]
    fn a_zero_cap_keeps_the_desktop_size_forced_even() {
        assert_eq!(encoded_frame_size(1921, 1081, 0), (1920, 1080));
    }

    #[test]
    fn a_tiny_desktop_is_never_rounded_to_zero() {
        assert_eq!(encoded_frame_size(1, 1, 0), (2, 2));
    }

    #[test]
    fn a_cap_already_above_the_desktop_does_not_upscale() {
        assert_eq!(encoded_frame_size(1280, 720, 1920), (1280, 720));
    }
}
