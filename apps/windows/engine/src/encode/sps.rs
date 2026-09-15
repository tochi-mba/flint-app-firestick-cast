//! Reading the picture geometry back out of an H.264 sequence parameter set.
//!
//! Flint sends the receiver a `VIDEO_CONFIG` carrying a width, a height, and the encoder's SPS and
//! PPS. The receiver builds a decoder from all three. Nothing checks that they agree, and if they
//! do not, the failure is silent in the worst possible way: the decoder configures happily, accepts
//! every access unit, reports frames rendered, and puts a flat green field on the television —
//! green being what an incorrectly-configured YUV surface looks like. Every counter on the host
//! stays perfect throughout.
//!
//! So the SPS is parsed here and held to the size the encoder was asked for. That is not a
//! formality: the parameter sets come from the encoder, the dimensions come from the session, and
//! the two travel by different routes.

/// The picture geometry an SPS describes.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SpsDimensions {
    /// Display width in luma samples, after cropping.
    pub width: u32,
    /// Display height in luma samples, after cropping.
    pub height: u32,
}

/// Why an SPS could not be read.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SpsError {
    /// No NAL unit of type 7 was present.
    NoSequenceParameterSet,
    /// The bitstream ended in the middle of a field.
    Truncated,
}

impl std::fmt::Display for SpsError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::NoSequenceParameterSet => {
                formatter.write_str("no sequence parameter set in the bitstream")
            }
            Self::Truncated => formatter.write_str("the sequence parameter set ended early"),
        }
    }
}

impl std::error::Error for SpsError {}

/// Finds the first SPS in an Annex B bitstream and reads its picture size.
///
/// Accepts either a bare parameter-set block or a whole access unit, because the two arrive by
/// different routes: the software encoder publishes a sequence header before streaming, while a
/// hardware encoder emits its parameter sets in band at the head of the first key frame.
///
/// # Errors
/// [`SpsError`] when there is no SPS, or when one ends mid-field.
pub fn dimensions_from_annex_b(bitstream: &[u8]) -> Result<SpsDimensions, SpsError> {
    for (start, header) in nal_units(bitstream) {
        // The low five bits carry the NAL type; 7 is a sequence parameter set.
        if header & 0x1f == 7 {
            let end = next_start_code(bitstream, start).unwrap_or(bitstream.len());
            return dimensions_from_rbsp(&bitstream[start..end]);
        }
    }
    Err(SpsError::NoSequenceParameterSet)
}

/// Reads the picture size from a single SPS payload, excluding its NAL header byte.
///
/// # Errors
/// [`SpsError::Truncated`] when the payload ends mid-field.
pub fn dimensions_from_rbsp(payload: &[u8]) -> Result<SpsDimensions, SpsError> {
    let rbsp = remove_emulation_prevention(payload);
    let mut reader = BitReader::new(&rbsp);

    let profile_idc = reader.bits(8)?;
    let _constraints = reader.bits(8)?;
    let _level_idc = reader.bits(8)?;
    let _seq_parameter_set_id = reader.exp_golomb()?;

    // The high profiles carry chroma and bit-depth fields the baseline profiles do not. Skipping
    // them for a High-profile stream misreads every field after this point, and the resulting
    // dimensions are plausible-looking nonsense rather than an obvious failure.
    if matches!(
        profile_idc,
        100 | 110 | 122 | 244 | 44 | 83 | 86 | 118 | 128 | 138 | 139 | 134 | 135
    ) {
        let chroma_format_idc = reader.exp_golomb()?;
        if chroma_format_idc == 3 {
            let _separate_colour_plane_flag = reader.bits(1)?;
        }
        let _bit_depth_luma_minus8 = reader.exp_golomb()?;
        let _bit_depth_chroma_minus8 = reader.exp_golomb()?;
        let _qpprime_y_zero_transform_bypass_flag = reader.bits(1)?;
        if reader.bits(1)? == 1 {
            skip_scaling_matrix(&mut reader, chroma_format_idc)?;
        }
    }

    let _log2_max_frame_num_minus4 = reader.exp_golomb()?;
    let pic_order_cnt_type = reader.exp_golomb()?;
    if pic_order_cnt_type == 0 {
        let _log2_max_pic_order_cnt_lsb_minus4 = reader.exp_golomb()?;
    } else if pic_order_cnt_type == 1 {
        let _delta_pic_order_always_zero_flag = reader.bits(1)?;
        let _offset_for_non_ref_pic = reader.exp_golomb()?;
        let _offset_for_top_to_bottom_field = reader.exp_golomb()?;
        let cycle_length = reader.exp_golomb()?;
        for _ in 0..cycle_length {
            let _offset_for_ref_frame = reader.exp_golomb()?;
        }
    }

    let _max_num_ref_frames = reader.exp_golomb()?;
    let _gaps_in_frame_num_value_allowed_flag = reader.bits(1)?;

    let pic_width_in_mbs_minus1 = reader.exp_golomb()?;
    let pic_height_in_map_units_minus1 = reader.exp_golomb()?;
    let frame_mbs_only_flag = reader.bits(1)?;
    if frame_mbs_only_flag == 0 {
        let _mb_adaptive_frame_field_flag = reader.bits(1)?;
    }
    let _direct_8x8_inference_flag = reader.bits(1)?;

    // Cropping is how an encoder describes a height that is not a multiple of sixteen. A 1200-line
    // picture is 75 macroblocks exactly, but a 1080-line one is coded as 1088 and cropped, so
    // ignoring these offsets reports a size eight lines too tall for the commonest case there is.
    let (mut crop_left, mut crop_right, mut crop_top, mut crop_bottom) = (0, 0, 0, 0);
    if reader.bits(1)? == 1 {
        crop_left = reader.exp_golomb()?;
        crop_right = reader.exp_golomb()?;
        crop_top = reader.exp_golomb()?;
        crop_bottom = reader.exp_golomb()?;
    }

    let width_in_samples = (pic_width_in_mbs_minus1 + 1) * 16;
    let height_in_samples =
        (2 - u32::from(frame_mbs_only_flag != 0)) * (pic_height_in_map_units_minus1 + 1) * 16;

    // Crop offsets are counted in chroma samples, so they scale by the subsampling factor. For the
    // 4:2:0 this project encodes that is two luma samples horizontally; vertically it is two for a
    // progressive stream and four for an interlaced one.
    let horizontal_unit = 2;
    let vertical_unit = if frame_mbs_only_flag != 0 { 2 } else { 4 };

    Ok(SpsDimensions {
        width: width_in_samples.saturating_sub((crop_left + crop_right) * horizontal_unit),
        height: height_in_samples.saturating_sub((crop_top + crop_bottom) * vertical_unit),
    })
}

/// Yields the offset just past each start code, with the NAL header byte found there.
fn nal_units(bitstream: &[u8]) -> impl Iterator<Item = (usize, u8)> + '_ {
    let mut index = 0usize;
    std::iter::from_fn(move || {
        while index + 3 <= bitstream.len() {
            let length = if bitstream[index..].starts_with(&[0, 0, 0, 1]) {
                4
            } else if bitstream[index..].starts_with(&[0, 0, 1]) {
                3
            } else {
                index += 1;
                continue;
            };

            let payload = index + length;
            let header = *bitstream.get(payload)?;
            index = payload;
            // The offset returned skips the NAL header byte, which the payload parser does not want.
            return Some((payload + 1, header));
        }
        None
    })
}

/// Finds the next start code at or after `from`.
fn next_start_code(bitstream: &[u8], from: usize) -> Option<usize> {
    (from..bitstream.len().saturating_sub(2)).find(|&index| {
        bitstream[index..].starts_with(&[0, 0, 1]) || bitstream[index..].starts_with(&[0, 0, 0, 1])
    })
}

/// Strips the 0x03 bytes an encoder inserts to stop payload data looking like a start code.
///
/// Leaving them in shifts every field after the first occurrence, which produces dimensions that
/// look like real numbers and are wrong.
fn remove_emulation_prevention(payload: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(payload.len());
    let mut zeros = 0usize;
    for &byte in payload {
        if zeros >= 2 && byte == 0x03 {
            zeros = 0;
            continue;
        }
        if byte == 0 {
            zeros += 1;
        } else {
            zeros = 0;
        }
        out.push(byte);
    }
    out
}

/// Skips the scaling lists a High-profile SPS may carry.
fn skip_scaling_matrix(reader: &mut BitReader<'_>, chroma_format_idc: u32) -> Result<(), SpsError> {
    let list_count = if chroma_format_idc == 3 { 12 } else { 8 };
    for index in 0..list_count {
        if reader.bits(1)? == 0 {
            continue;
        }
        let size = if index < 6 { 16 } else { 64 };
        let mut last_scale = 8i32;
        let mut next_scale = 8i32;
        for _ in 0..size {
            if next_scale != 0 {
                let delta = reader.signed_exp_golomb()?;
                next_scale = (last_scale + delta + 256) % 256;
            }
            if next_scale != 0 {
                last_scale = next_scale;
            }
        }
    }
    Ok(())
}

/// A most-significant-bit-first bit reader over an RBSP.
struct BitReader<'a> {
    bytes: &'a [u8],
    position: usize,
}

impl<'a> BitReader<'a> {
    fn new(bytes: &'a [u8]) -> Self {
        Self { bytes, position: 0 }
    }

    /// Reads `count` bits, most significant first.
    fn bits(&mut self, count: u32) -> Result<u32, SpsError> {
        let mut value = 0u32;
        for _ in 0..count {
            let byte = self
                .bytes
                .get(self.position / 8)
                .ok_or(SpsError::Truncated)?;
            let bit = (byte >> (7 - (self.position % 8))) & 1;
            value = (value << 1) | u32::from(bit);
            self.position += 1;
        }
        Ok(value)
    }

    /// Reads an unsigned exponential-Golomb code.
    fn exp_golomb(&mut self) -> Result<u32, SpsError> {
        let mut leading_zeros = 0u32;
        while self.bits(1)? == 0 {
            leading_zeros += 1;
            // A run this long is a corrupt stream rather than a very large number, and without the
            // bound a malformed SPS spins here until the reader runs off the end.
            if leading_zeros > 31 {
                return Err(SpsError::Truncated);
            }
        }
        if leading_zeros == 0 {
            return Ok(0);
        }
        let remainder = self.bits(leading_zeros)?;
        Ok((1u32 << leading_zeros) - 1 + remainder)
    }

    /// Reads a signed exponential-Golomb code.
    fn signed_exp_golomb(&mut self) -> Result<i32, SpsError> {
        let value = self.exp_golomb()?;
        Ok(if value % 2 == 0 {
            -((value / 2) as i32)
        } else {
            value.div_ceil(2) as i32
        })
    }
}

#[cfg(test)]
#[path = "sps_tests.rs"]
mod tests;

#[cfg(test)]
#[cfg(windows)]
#[path = "sps_live_tests.rs"]
mod live;
