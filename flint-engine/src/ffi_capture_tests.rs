use super::*;

#[test]
fn probe_rejects_a_null_pointer() {
    let status = unsafe { flint_probe_capture(std::ptr::null_mut()) };
    assert_eq!(status, FlintStatus::NullArgument as i32);
}

#[test]
fn probe_always_succeeds_even_with_no_desktop() {
    // "Cannot capture" is an answer the report needs, not a failure to propagate.
    let mut capture = FlintCapture {
        available: 9,
        backend: 9,
        _reserved: [0; 6],
        width: 1,
        height: 1,
        adapter_luid: 1,
    };

    let status = unsafe { flint_probe_capture(&mut capture) };

    assert_eq!(status, FlintStatus::Ok as i32);
    assert!(capture.available <= 1, "availability must be a clean flag");
}

#[test]
fn an_available_capture_reports_a_usable_geometry() {
    let mut capture = FlintCapture {
        available: 0,
        backend: 0,
        _reserved: [0; 6],
        width: 0,
        height: 0,
        adapter_luid: 0,
    };

    assert_eq!(
        unsafe { flint_probe_capture(&mut capture) },
        FlintStatus::Ok as i32
    );

    if capture.available == 1 {
        assert_eq!(capture.backend, capture_backend::DESKTOP_DUPLICATION);
        assert!(capture.width > 0 && capture.height > 0);
    } else {
        // An unavailable probe must not leave stale geometry behind.
        assert_eq!(
            (capture.width, capture.height, capture.adapter_luid),
            (0, 0, 0)
        );
    }
}

#[test]
fn the_capture_struct_has_a_stable_layout() {
    // The managed struct must match byte for byte.
    assert_eq!(std::mem::size_of::<FlintCapture>(), 24);
    assert_eq!(std::mem::align_of::<FlintCapture>(), 8);
}
