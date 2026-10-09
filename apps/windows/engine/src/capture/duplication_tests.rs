use super::*;
use windows::Win32::Graphics::Dxgi::Common::{
    DXGI_MODE_ROTATION_ROTATE180, DXGI_MODE_ROTATION_ROTATE270, DXGI_MODE_ROTATION_ROTATE90,
};

// These run against the real desktop. On a build agent with no interactive session they
// correctly report no capture, which is the same answer the capability report would give.

#[test]
fn probing_never_panics_whatever_the_session() {
    let _ = is_available();
}

#[test]
fn opening_an_absurd_output_index_is_refused() {
    let result = DesktopDuplication::open(9_999);
    assert!(matches!(
        result,
        Err(CaptureError::NoSuchOutput(9_999) | CaptureError::NoDisplayAdapter)
    ));
}

#[test]
fn a_capture_reports_a_plausible_geometry_when_one_is_available() {
    let Ok(duplication) = DesktopDuplication::open_primary() else {
        // No interactive desktop here; nothing to assert.
        return;
    };

    let format = duplication.format();
    assert!(format.width > 0 && format.height > 0);
    assert!(format.adapter_luid != 0);
}

#[test]
fn releasing_without_a_held_frame_is_harmless() {
    let Ok(mut duplication) = DesktopDuplication::open_primary() else {
        return;
    };

    duplication.release();
    duplication.release();
}

#[test]
fn an_idle_desktop_reports_unchanged_rather_than_failing() {
    // A still desktop produces no frame, and that must not read as an error.
    let Ok(mut duplication) = DesktopDuplication::open_primary() else {
        return;
    };

    let outcome = duplication.acquire(50);
    assert!(outcome.is_ok(), "a timeout must not surface as an error");
}

#[test]
fn a_frame_is_new_for_new_pixels_or_for_a_drawn_pointer_that_moved() {
    assert!(is_new(1, false, false), "new pixels");
    assert!(is_new(1, true, true));
    assert!(is_new(0, true, true), "the pointer moved, and it is drawn");
    assert!(
        !is_new(0, true, false),
        "the pointer moved, but it is not drawn"
    );
    assert!(!is_new(0, false, true), "nothing changed");
}

#[test]
fn the_pointer_is_drawn_only_when_asked_and_not_on_a_turned_display() {
    let Ok(mut duplication) = DesktopDuplication::open_primary() else {
        return;
    };

    assert!(duplication.pointer().is_none(), "off until asked");
    duplication.set_show_pointer(true);
    assert_eq!(duplication.pointer().is_some(), !duplication.rotated);
    duplication.set_show_pointer(false);
    assert!(duplication.pointer().is_none());

    duplication.rotated = true;
    duplication.set_show_pointer(true);
    assert!(duplication.pointer().is_none(), "never on a turned display");
}

#[test]
fn only_a_turned_display_counts_as_turned() {
    assert!(!is_turned(DXGI_MODE_ROTATION_IDENTITY));
    assert!(!is_turned(DXGI_MODE_ROTATION_UNSPECIFIED));
    assert!(is_turned(DXGI_MODE_ROTATION_ROTATE90));
    assert!(is_turned(DXGI_MODE_ROTATION_ROTATE180));
    assert!(is_turned(DXGI_MODE_ROTATION_ROTATE270));
}

#[test]
fn each_kind_of_shape_windows_describes_is_read() {
    let info = |kind: i32| DXGI_OUTDUPL_POINTER_SHAPE_INFO {
        Type: kind as u32,
        Width: 32,
        Height: 64,
        Pitch: 4,
        HotSpot: windows::Win32::Foundation::POINT { x: 3, y: 5 },
    };

    let monochrome = pointer_shape(
        &info(DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MONOCHROME.0),
        vec![0; 256],
    )
    .unwrap();
    assert_eq!(monochrome.kind, PointerKind::Monochrome);
    assert_eq!(monochrome.height, 32, "both masks count in Windows' height");
    assert_eq!(monochrome.hotspot, (3, 5));
    let color = pointer_shape(&info(DXGI_OUTDUPL_POINTER_SHAPE_TYPE_COLOR.0), Vec::new()).unwrap();
    assert_eq!(
        (color.kind, color.height, color.width, color.pitch),
        (PointerKind::Color, 64, 32, 4)
    );
    let masked = pointer_shape(
        &info(DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MASKED_COLOR.0),
        Vec::new(),
    )
    .unwrap();
    assert_eq!(masked.kind, PointerKind::MaskedColor);
    assert!(
        pointer_shape(&info(8), Vec::new()).is_none(),
        "a kind this code does not know"
    );
}
