use super::*;

/// A real 1920x1080 High-profile SPS, as an x264-class encoder emits one.
///
/// Kept as bytes rather than constructed, because the value of this test is that it agrees with
/// what encoders actually produce, and a hand-built SPS would only agree with the parser.
const SPS_1920X1080: &[u8] = &[
    0x00, 0x00, 0x00, 0x01, 0x67, 0x64, 0x00, 0x28, 0xAC, 0xD9, 0x40, 0x78, 0x02, 0x27, 0xE5, 0x84,
    0x00, 0x00, 0x03, 0x00, 0x04, 0x00, 0x00, 0x03, 0x00, 0xF0, 0x3C, 0x60, 0xC6, 0x58,
];

#[test]
fn a_real_1080p_sequence_parameter_set_reads_as_1920_by_1080() {
    // 1080 is not a multiple of sixteen, so this is coded as 1088 lines and cropped. An
    // implementation that ignores cropping reports 1088 here, and the receiver then builds a
    // decoder eight lines too tall.
    let dimensions = dimensions_from_annex_b(SPS_1920X1080).expect("a readable SPS");

    assert_eq!(dimensions.width, 1920);
    assert_eq!(dimensions.height, 1080);
}

#[test]
fn a_sequence_parameter_set_is_found_inside_a_whole_access_unit() {
    // A hardware encoder puts its parameter sets in band, behind an access unit delimiter, so the
    // SPS is rarely the first NAL in the buffer.
    let mut access_unit = vec![0x00, 0x00, 0x00, 0x01, 0x09, 0x10];
    access_unit.extend_from_slice(SPS_1920X1080);

    let dimensions = dimensions_from_annex_b(&access_unit).expect("a readable SPS");

    assert_eq!((dimensions.width, dimensions.height), (1920, 1080));
}

#[test]
fn a_three_byte_start_code_is_recognised_as_well_as_a_four_byte_one() {
    // Three-byte start codes throughout, including for the SPS itself: the four-byte prefix in the
    // constant is dropped and replaced rather than left dangling.
    let mut bitstream = vec![0x00, 0x00, 0x01, 0x09, 0x10, 0x00, 0x00, 0x01];
    bitstream.extend_from_slice(&SPS_1920X1080[4..]);

    assert!(dimensions_from_annex_b(&bitstream).is_ok());
}

#[test]
fn a_bitstream_with_no_sequence_parameter_set_is_an_error_rather_than_a_guess() {
    // Reporting a default size here would be the same failure this module exists to catch.
    let only_a_delimiter = [0x00, 0x00, 0x00, 0x01, 0x09, 0x10];

    assert_eq!(
        dimensions_from_annex_b(&only_a_delimiter),
        Err(SpsError::NoSequenceParameterSet)
    );
}

#[test]
fn an_empty_bitstream_is_an_error() {
    assert_eq!(
        dimensions_from_annex_b(&[]),
        Err(SpsError::NoSequenceParameterSet)
    );
}

#[test]
fn a_truncated_sequence_parameter_set_is_an_error_rather_than_a_panic() {
    // A short read must not index past the end. This is reached with real data whenever a frame is
    // split across transport packets and reassembled wrongly.
    let truncated = [0x00, 0x00, 0x00, 0x01, 0x67, 0x64];

    assert_eq!(
        dimensions_from_rbsp(&truncated[5..]),
        Err(SpsError::Truncated)
    );
}

#[test]
fn emulation_prevention_bytes_are_removed_before_parsing() {
    // 0x03 after two zero bytes is an escape, not data. Leaving it in shifts every field after it
    // and yields dimensions that look like real numbers and are wrong.
    let escaped = [0x00, 0x00, 0x03, 0x01, 0x02];

    assert_eq!(remove_emulation_prevention(&escaped), vec![0, 0, 1, 2]);
}

#[test]
fn a_zero_zero_three_that_is_not_an_escape_is_kept() {
    // Only a 0x03 preceded by two zeros is an escape. Removing others corrupts the payload.
    let plain = [0x01, 0x03, 0x00, 0x03_u8];

    assert_eq!(remove_emulation_prevention(&plain), vec![1, 3, 0, 3]);
}

#[test]
fn the_bit_reader_reads_most_significant_bit_first() {
    let mut reader = BitReader::new(&[0b1011_0000]);

    assert_eq!(reader.bits(1).unwrap(), 1);
    assert_eq!(reader.bits(1).unwrap(), 0);
    assert_eq!(reader.bits(2).unwrap(), 0b11);
}

#[test]
fn the_bit_reader_crosses_byte_boundaries() {
    let mut reader = BitReader::new(&[0b0000_0001, 0b1000_0000]);

    assert_eq!(reader.bits(9).unwrap(), 0b0_0000_0011);
}

#[test]
fn the_bit_reader_reports_truncation_rather_than_reading_past_the_end() {
    let mut reader = BitReader::new(&[0xFF]);

    assert_eq!(reader.bits(9), Err(SpsError::Truncated));
}

#[test]
fn exp_golomb_reads_zero_from_a_single_one_bit() {
    let mut reader = BitReader::new(&[0b1000_0000]);

    assert_eq!(reader.exp_golomb().unwrap(), 0);
}

#[test]
fn exp_golomb_reads_the_first_few_codes_correctly() {
    // 1 -> 0, 010 -> 1, 011 -> 2, 00100 -> 3. Packed end to end.
    let mut reader = BitReader::new(&[0b1010_0110, 0b0100_0000]);

    assert_eq!(reader.exp_golomb().unwrap(), 0);
    assert_eq!(reader.exp_golomb().unwrap(), 1);
    assert_eq!(reader.exp_golomb().unwrap(), 2);
    assert_eq!(reader.exp_golomb().unwrap(), 3);
}

#[test]
fn exp_golomb_refuses_an_impossibly_long_zero_run_rather_than_spinning() {
    // All-zero input has no terminating one bit. Without the bound this walks to the end of the
    // buffer on every malformed stream.
    let mut reader = BitReader::new(&[0x00; 16]);

    assert_eq!(reader.exp_golomb(), Err(SpsError::Truncated));
}

#[test]
fn signed_exp_golomb_alternates_around_zero() {
    // 1 -> 0, 010 -> 1, 011 -> -1.
    let mut reader = BitReader::new(&[0b1010_0110]);

    assert_eq!(reader.signed_exp_golomb().unwrap(), 0);
    assert_eq!(reader.signed_exp_golomb().unwrap(), 1);
    assert_eq!(reader.signed_exp_golomb().unwrap(), -1);
}

#[test]
fn the_error_messages_say_what_went_wrong() {
    assert!(SpsError::NoSequenceParameterSet
        .to_string()
        .contains("no sequence parameter set"));
    assert!(SpsError::Truncated.to_string().contains("ended early"));
}
