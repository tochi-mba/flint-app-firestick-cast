//! BGRA to NV12 conversion.
//!
//! Every H.264 encoder on Windows wants NV12; desktop duplication produces BGRA. Something has to
//! bridge the two, and getting the colour maths wrong is the kind of bug that ships: the picture
//! still appears, it just looks washed out or slightly green, and nobody can point at the frame
//! where it broke. So the coefficients are written out in full and pinned by tests against known
//! colours rather than eyeballed against a screenshot.
//!
//! BT.709 with limited (studio) range is the pairing H.264 decoders assume for HD content, which is
//! all a TV receiver ever sees here. Full-range Y would render as crushed blacks and blown
//! highlights on a receiver that reads the stream as limited, which every Fire TV does.

/// The luma value that limited-range BT.709 uses for black.
const LUMA_BLACK: i32 = 16;
/// The chroma value for "no colour", which both U and V sit at for any shade of grey.
const CHROMA_NEUTRAL: i32 = 128;

// Coefficients are Q8 fixed point: the real BT.709 luma weights (0.2126, 0.7152, 0.0722) scaled
// for limited range (219/255) and then by 256. Integer maths here is not a shortcut — it is what
// keeps a full-screen conversion inside the frame budget, and it is exactly reproducible, which
// floating point across machines is not.
const LUMA_R: i32 = 47;
const LUMA_G: i32 = 157;
const LUMA_B: i32 = 16;

// Each chroma triple must sum to exactly zero, because chroma measures how far a colour is from
// grey: if the weights do not cancel for R == G == B, every neutral shade drifts off-centre. The
// unrounded BT.709 values do cancel; rounding each to Q8 independently does not, so the middle
// weight is derived from the other two rather than rounded on its own. Getting this wrong tints
// the whole picture by a step — visible on a grey desktop, and near-impossible to attribute.
const CB_R: i32 = -26;
const CB_B: i32 = 112;
const CB_G: i32 = -(CB_R + CB_B);

const CR_R: i32 = 112;
const CR_B: i32 = -10;
const CR_G: i32 = -(CR_R + CR_B);

/// Why a conversion could not be performed.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ConvertError {
    /// The frame's dimensions are not usable for 4:2:0 chroma subsampling.
    ///
    /// NV12 stores one chroma pair per 2x2 luma block, so an odd width or height has no
    /// representation at all rather than merely losing a row.
    OddDimensions {
        /// The width that was asked for.
        width: u32,
        /// The height that was asked for.
        height: u32,
    },
    /// The source buffer is smaller than its own stride and height imply.
    SourceTooSmall {
        /// Bytes the stride and height require.
        required: usize,
        /// Bytes actually supplied.
        supplied: usize,
    },
    /// The stride cannot hold a row of this width.
    StrideTooSmall {
        /// Bytes one row of pixels needs.
        required: usize,
        /// Bytes the stride allows.
        supplied: usize,
    },
}

impl std::fmt::Display for ConvertError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::OddDimensions { width, height } => write!(
                formatter,
                "{width}x{height} cannot be subsampled; NV12 needs even dimensions"
            ),
            Self::SourceTooSmall { required, supplied } => write!(
                formatter,
                "the source holds {supplied} bytes but its stride and height need {required}"
            ),
            Self::StrideTooSmall { required, supplied } => write!(
                formatter,
                "a row needs {required} bytes but the stride allows {supplied}"
            ),
        }
    }
}

impl std::error::Error for ConvertError {}

/// The number of bytes an NV12 frame of this size occupies.
///
/// One byte of luma per pixel, plus one interleaved chroma pair per 2x2 block — three bytes for
/// every two pixels.
#[must_use]
pub fn nv12_len(width: u32, height: u32) -> usize {
    let pixels = width as usize * height as usize;
    pixels + pixels / 2
}

/// Converts a BGRA8 frame into NV12, writing into `destination`.
///
/// `destination` is resized to exactly [`nv12_len`], so the caller can reuse one buffer across
/// every frame of a session and never allocate on the frame path after the first.
///
/// # Errors
/// See [`ConvertError`].
pub fn bgra_to_nv12(
    source: &[u8],
    width: u32,
    height: u32,
    stride: u32,
    destination: &mut Vec<u8>,
) -> Result<(), ConvertError> {
    if width == 0 || height == 0 || width % 2 != 0 || height % 2 != 0 {
        return Err(ConvertError::OddDimensions { width, height });
    }

    let row_bytes = width as usize * 4;
    let stride = stride as usize;
    if stride < row_bytes {
        return Err(ConvertError::StrideTooSmall {
            required: row_bytes,
            supplied: stride,
        });
    }

    let required = stride * height as usize;
    if source.len() < required {
        return Err(ConvertError::SourceTooSmall {
            required,
            supplied: source.len(),
        });
    }

    let width = width as usize;
    let height = height as usize;
    destination.clear();
    destination.resize(nv12_len(width as u32, height as u32), 0);
    let (luma, chroma) = destination.split_at_mut(width * height);

    for y in 0..height {
        let row = &source[y * stride..y * stride + row_bytes];
        let luma_row = &mut luma[y * width..(y + 1) * width];

        for x in 0..width {
            let pixel = &row[x * 4..x * 4 + 4];
            luma_row[x] = luma_of(pixel);
        }
    }

    // Chroma is sampled once per 2x2 block, averaging the four pixels rather than picking one of
    // them: on the sharp edges that dominate a desktop — text, window borders — taking a single
    // corner makes colour fringes that averaging does not.
    for block_y in 0..height / 2 {
        for block_x in 0..width / 2 {
            let mut blue = 0i32;
            let mut green = 0i32;
            let mut red = 0i32;

            for offset_y in 0..2 {
                let row_start = (block_y * 2 + offset_y) * stride;
                for offset_x in 0..2 {
                    let pixel_start = row_start + (block_x * 2 + offset_x) * 4;
                    let pixel = &source[pixel_start..pixel_start + 4];
                    blue += i32::from(pixel[0]);
                    green += i32::from(pixel[1]);
                    red += i32::from(pixel[2]);
                }
            }

            let (blue, green, red) = (blue / 4, green / 4, red / 4);
            let index = (block_y * (width / 2) + block_x) * 2;
            chroma[index] = clamp_chroma(CB_R * red + CB_G * green + CB_B * blue);
            chroma[index + 1] = clamp_chroma(CR_R * red + CR_G * green + CR_B * blue);
        }
    }

    Ok(())
}

/// The limited-range BT.709 luma for one BGRA pixel.
fn luma_of(pixel: &[u8]) -> u8 {
    let blue = i32::from(pixel[0]);
    let green = i32::from(pixel[1]);
    let red = i32::from(pixel[2]);
    let value = ((LUMA_R * red + LUMA_G * green + LUMA_B * blue + 128) >> 8) + LUMA_BLACK;
    value.clamp(LUMA_BLACK, 235) as u8
}

/// Finishes a Q8 chroma sum: rounds, re-centres on neutral, and clamps to the legal range.
fn clamp_chroma(weighted: i32) -> u8 {
    (((weighted + 128) >> 8) + CHROMA_NEUTRAL).clamp(16, 240) as u8
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Builds a tightly packed BGRA frame from one repeated colour.
    fn solid(width: u32, height: u32, blue: u8, green: u8, red: u8) -> Vec<u8> {
        let mut pixels = Vec::with_capacity((width * height * 4) as usize);
        for _ in 0..width * height {
            pixels.extend_from_slice(&[blue, green, red, 255]);
        }
        pixels
    }

    fn convert(source: &[u8], width: u32, height: u32) -> Vec<u8> {
        let mut destination = Vec::new();
        bgra_to_nv12(source, width, height, width * 4, &mut destination).unwrap();
        destination
    }

    #[test]
    fn an_nv12_frame_is_three_bytes_for_every_two_pixels() {
        assert_eq!(nv12_len(1920, 1080), 1920 * 1080 * 3 / 2);
        assert_eq!(nv12_len(2, 2), 6);
    }

    #[test]
    fn black_maps_to_the_limited_range_floor_not_to_zero() {
        // Writing 0 here is the classic full-range mistake: a receiver reading the stream as
        // limited range would render it as crushed, below-black detail.
        let converted = convert(&solid(2, 2, 0, 0, 0), 2, 2);
        assert!(converted[..4].iter().all(|&luma| luma == 16));
        assert_eq!(&converted[4..6], &[128, 128]);
    }

    #[test]
    fn white_maps_to_the_limited_range_ceiling_not_to_255() {
        let converted = convert(&solid(2, 2, 255, 255, 255), 2, 2);
        assert!(converted[..4].iter().all(|&luma| luma == 235));
        // White is colourless, so both chroma channels stay neutral.
        assert_eq!(&converted[4..6], &[128, 128]);
    }

    #[test]
    fn grey_stays_colourless() {
        let converted = convert(&solid(2, 2, 128, 128, 128), 2, 2);
        assert_eq!(&converted[4..6], &[128, 128]);
    }

    #[test]
    fn the_chroma_weights_cancel_so_neutral_colours_cannot_drift() {
        // Rounding each BT.709 weight to Q8 on its own leaves Cb summing to -1, which pushed every
        // grey one step off neutral — a whole-picture tint that is trivial to introduce and very
        // hard to attribute after the fact.
        assert_eq!(CB_R + CB_G + CB_B, 0);
        assert_eq!(CR_R + CR_G + CR_B, 0);
    }

    #[test]
    fn every_shade_of_grey_lands_exactly_on_neutral() {
        for level in [0u8, 16, 64, 128, 192, 235, 255] {
            let converted = convert(&solid(2, 2, level, level, level), 2, 2);
            assert_eq!(
                &converted[4..6],
                &[128, 128],
                "grey level {level} produced a colour cast"
            );
        }
    }

    #[test]
    fn pure_blue_pushes_cb_up_and_cr_down() {
        let converted = convert(&solid(2, 2, 255, 0, 0), 2, 2);
        let (cb, cr) = (converted[4], converted[5]);
        assert!(cb > 200, "blue should drive Cb high, got {cb}");
        assert!(cr < 128, "blue should drive Cr below neutral, got {cr}");
    }

    #[test]
    fn pure_red_pushes_cr_up_and_cb_down() {
        let converted = convert(&solid(2, 2, 0, 0, 255), 2, 2);
        let (cb, cr) = (converted[4], converted[5]);
        assert!(cr > 200, "red should drive Cr high, got {cr}");
        assert!(cb < 128, "red should drive Cb below neutral, got {cb}");
    }

    #[test]
    fn green_is_the_brightest_primary_as_bt709_weights_it() {
        // The luma weights are what make a green screen brighter than a red or blue one; if these
        // were swapped the picture would still appear, just with the wrong contrast.
        let green = convert(&solid(2, 2, 0, 255, 0), 2, 2)[0];
        let red = convert(&solid(2, 2, 0, 0, 255), 2, 2)[0];
        let blue = convert(&solid(2, 2, 255, 0, 0), 2, 2)[0];
        assert!(
            green > red && red > blue,
            "got green {green}, red {red}, blue {blue}"
        );
    }

    #[test]
    fn every_channel_stays_inside_the_legal_range() {
        for (blue, green, red) in [
            (0, 0, 0),
            (255, 255, 255),
            (255, 0, 0),
            (0, 255, 0),
            (0, 0, 255),
        ] {
            let converted = convert(&solid(2, 2, blue, green, red), 2, 2);
            let (luma, chroma) = converted.split_at(4);
            assert!(luma.iter().all(|&value| (16..=235).contains(&value)));
            assert!(chroma.iter().all(|&value| (16..=240).contains(&value)));
        }
    }

    #[test]
    fn padding_between_rows_is_skipped_rather_than_encoded() {
        // Desktop duplication routinely hands back a stride wider than the visible row. Treating
        // that padding as pixels shears the picture diagonally — an unmistakable symptom, but only
        // if a test catches it before a person does.
        let width = 2u32;
        let height = 2u32;
        let stride = 16u32;
        let mut source = vec![0u8; (stride * height) as usize];
        for y in 0..height as usize {
            for x in 0..width as usize {
                let at = y * stride as usize + x * 4;
                source[at..at + 4].copy_from_slice(&[255, 255, 255, 255]);
            }
        }

        let mut destination = Vec::new();
        bgra_to_nv12(&source, width, height, stride, &mut destination).unwrap();

        assert!(
            destination[..4].iter().all(|&luma| luma == 235),
            "padding leaked into the luma plane: {:?}",
            &destination[..4]
        );
    }

    #[test]
    fn chroma_averages_a_block_rather_than_sampling_one_corner() {
        // A 2x2 block of one white and three black pixels must land between the two, not on
        // whichever corner happened to be read first.
        let mut source = solid(2, 2, 0, 0, 0);
        source[..4].copy_from_slice(&[255, 0, 0, 255]);

        let converted = convert(&source, 2, 2);

        let cb = converted[4];
        assert!(cb > 128 && cb < 200, "expected an averaged Cb, got {cb}");
    }

    #[test]
    fn odd_dimensions_are_refused_because_they_have_no_nv12_representation() {
        let mut destination = Vec::new();
        assert_eq!(
            bgra_to_nv12(&solid(2, 2, 0, 0, 0), 3, 2, 12, &mut destination),
            Err(ConvertError::OddDimensions {
                width: 3,
                height: 2
            })
        );
        assert_eq!(
            bgra_to_nv12(&solid(2, 2, 0, 0, 0), 2, 3, 8, &mut destination),
            Err(ConvertError::OddDimensions {
                width: 2,
                height: 3
            })
        );
    }

    #[test]
    fn a_truncated_source_is_refused_rather_than_read_past_its_end() {
        let mut destination = Vec::new();
        let error = bgra_to_nv12(&[0u8; 8], 2, 2, 8, &mut destination).unwrap_err();
        assert_eq!(
            error,
            ConvertError::SourceTooSmall {
                required: 16,
                supplied: 8
            }
        );
    }

    #[test]
    fn a_stride_narrower_than_a_row_is_refused() {
        let mut destination = Vec::new();
        let error = bgra_to_nv12(&[0u8; 64], 4, 2, 8, &mut destination).unwrap_err();
        assert_eq!(
            error,
            ConvertError::StrideTooSmall {
                required: 16,
                supplied: 8
            }
        );
    }

    #[test]
    fn the_destination_buffer_is_reused_without_growing() {
        // The frame path must not allocate; converting repeatedly into one buffer is how that is
        // achieved, so the buffer must end at exactly the frame size every time.
        let mut destination = Vec::new();
        for _ in 0..3 {
            bgra_to_nv12(&solid(4, 4, 10, 20, 30), 4, 4, 16, &mut destination).unwrap();
            assert_eq!(destination.len(), nv12_len(4, 4));
        }
    }

    #[test]
    fn errors_describe_themselves_for_the_diagnostics_page() {
        assert!(ConvertError::OddDimensions {
            width: 3,
            height: 2
        }
        .to_string()
        .contains("3x2"));
        assert!(ConvertError::SourceTooSmall {
            required: 16,
            supplied: 8
        }
        .to_string()
        .contains("16"));
    }
}
