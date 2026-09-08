use super::*;

#[test]
fn version_is_written_with_a_nul_terminator() {
    let mut buffer = [0u8; 32];
    let status = unsafe { flint_version(buffer.as_mut_ptr(), buffer.len()) };

    assert_eq!(status, FlintStatus::Ok as i32);
    let end = buffer.iter().position(|&b| b == 0).unwrap();
    assert_eq!(std::str::from_utf8(&buffer[..end]).unwrap(), crate::VERSION);
}

#[test]
fn version_rejects_a_null_buffer() {
    let status = unsafe { flint_version(std::ptr::null_mut(), 32) };
    assert_eq!(status, FlintStatus::NullArgument as i32);
}

#[test]
fn version_refuses_a_buffer_with_no_room_for_the_terminator() {
    // Exactly the string length is still too small: the NUL needs a byte of its own.
    let mut buffer = [0u8; 5];
    let status = unsafe { flint_version(buffer.as_mut_ptr(), crate::VERSION.len()) };

    assert_eq!(status, FlintStatus::BufferTooSmall as i32);
    assert_eq!(
        buffer, [0u8; 5],
        "nothing may be written on a rejected call"
    );
}

#[test]
fn probe_rejects_a_null_count() {
    let mut encoders = [FlintEncoder {
        vendor: 0,
        _reserved: [0; 7],
        adapter_luid: 0,
        codec_mask: 0,
        _reserved2: 0,
    }; 1];

    let status = unsafe { flint_probe_encoders(encoders.as_mut_ptr(), 1, std::ptr::null_mut()) };
    assert_eq!(status, FlintStatus::NullArgument as i32);
}

#[test]
fn probe_rejects_a_null_buffer_with_a_nonzero_capacity() {
    let mut found = 0u32;
    let status = unsafe { flint_probe_encoders(std::ptr::null_mut(), 4, &mut found) };
    assert_eq!(status, FlintStatus::NullArgument as i32);
}

#[test]
fn adapter_probe_rejects_a_null_count() {
    let status = unsafe { flint_probe_adapters(std::ptr::null_mut(), 0, std::ptr::null_mut()) };
    assert_eq!(status, FlintStatus::NullArgument as i32);
}

#[test]
fn adapter_probe_rejects_a_null_buffer_with_a_nonzero_capacity() {
    let mut found = 0u32;
    let status = unsafe { flint_probe_adapters(std::ptr::null_mut(), 1, &mut found) };
    assert_eq!(status, FlintStatus::NullArgument as i32);
}

#[test]
fn adapter_count_rejects_a_null_pointer() {
    let status = unsafe { flint_adapter_count(std::ptr::null_mut()) };
    assert_eq!(status, FlintStatus::NullArgument as i32);
}

#[test]
fn status_codes_are_stable() {
    // The managed side matches on these; changing one silently changes its meaning.
    assert_eq!(FlintStatus::Ok as i32, 0);
    assert_eq!(FlintStatus::NullArgument as i32, -1);
    assert_eq!(FlintStatus::BufferTooSmall as i32, -2);
    assert_eq!(FlintStatus::PlatformError as i32, -3);
    assert_eq!(FlintStatus::InternalError as i32, -4);
    assert_eq!(FlintStatus::InvalidArgument as i32, -5);
}

#[test]
fn the_encoder_struct_has_a_stable_layout() {
    // The managed struct must match byte for byte.
    assert_eq!(std::mem::size_of::<FlintEncoder>(), 24);
    assert_eq!(std::mem::align_of::<FlintEncoder>(), 8);
}

#[test]
fn the_adapter_struct_has_a_stable_layout() {
    // The managed struct must match byte for byte.
    assert_eq!(std::mem::size_of::<FlintAdapter>(), 272);
    assert_eq!(std::mem::align_of::<FlintAdapter>(), 8);
}

#[test]
fn codec_bits_do_not_overlap() {
    assert_eq!(codec_bits::H264 & codec_bits::H265, 0);
    assert_eq!(codec_bits::H265 & codec_bits::AV1, 0);
    assert_eq!(codec_bits::H264 & codec_bits::AV1, 0);
}
