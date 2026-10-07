#![allow(
    clippy::undocumented_unsafe_blocks,
    reason = "every unsafe block is the FFI call under test, made with the arguments the test names"
)]

use super::*;
use crate::capture::outputs::Rotation;

fn display(index: u32, left: i32, name: &str) -> DisplayOutput {
    DisplayOutput {
        index,
        adapter_luid: 77,
        desktop: (left, 0, left + 1920, 1080),
        rotation: Rotation::Identity,
        attached: true,
        device_name: name.into(),
    }
}

fn empty() -> FlintOutput {
    FlintOutput::from_display(&display(9, 9, ""))
}

#[test]
fn the_struct_has_a_stable_layout() {
    // The managed struct must match byte for byte.
    assert_eq!(std::mem::size_of::<FlintOutput>(), 104);
    assert_eq!(std::mem::align_of::<FlintOutput>(), 8);
    assert_eq!(std::mem::offset_of!(FlintOutput, adapter_luid), 8);
    assert_eq!(std::mem::offset_of!(FlintOutput, left), 16);
    assert_eq!(std::mem::offset_of!(FlintOutput, attached), 32);
    assert_eq!(std::mem::offset_of!(FlintOutput, device_name_len), 36);
    assert_eq!(std::mem::offset_of!(FlintOutput, device_name), 40);
}

#[test]
fn null_pointers_are_refused_but_a_count_query_is_not() {
    let mut found = 0u32;
    assert_eq!(
        unsafe { flint_probe_outputs(std::ptr::null_mut(), 0, std::ptr::null_mut()) },
        FlintStatus::NullArgument as i32
    );
    assert_eq!(
        unsafe { flint_probe_outputs(std::ptr::null_mut(), 2, &raw mut found) },
        FlintStatus::NullArgument as i32
    );

    // Asking for the count alone, as the shell does first. A host without a desktop answers zero.
    let status = unsafe { flint_probe_outputs(std::ptr::null_mut(), 0, &raw mut found) };
    assert!(
        status == FlintStatus::Ok as i32 || status == FlintStatus::PlatformError as i32,
        "unexpected status {status}"
    );
}

#[test]
fn this_hosts_displays_are_numbered_from_zero_in_order() {
    let mut found = 0u32;
    if unsafe { flint_probe_outputs(std::ptr::null_mut(), 0, &raw mut found) }
        != FlintStatus::Ok as i32
    {
        return;
    }

    let mut outputs = vec![empty(); found as usize];
    let mut again = 0u32;
    assert_eq!(
        unsafe { flint_probe_outputs(outputs.as_mut_ptr(), outputs.len(), &raw mut again) },
        FlintStatus::Ok as i32
    );
    for (position, output) in outputs.iter().take(again as usize).enumerate() {
        assert_eq!(output.index as usize, position);
        assert!((1..=4).contains(&output.rotation));
        assert!(output.device_name_len as usize <= output.device_name.len());
    }
}

#[test]
fn copying_truncates_to_the_capacity_and_reports_the_true_count() {
    let displays = [
        display(0, 0, r"\\.\DISPLAY1"),
        display(1, 1920, r"\\.\DISPLAY2"),
    ];
    let mut outputs = [empty()];
    let mut found = 0u32;

    write_outputs(&displays, outputs.as_mut_ptr(), 1, &raw mut found);

    assert_eq!(found, 2);
    let written = outputs[0];
    assert_eq!(written.index, 0);
    assert_eq!(written.main, 1);
    assert_eq!(written.attached, 1);
    assert_eq!(written.rotation, 1);
    assert_eq!(written.adapter_luid, 77);
    assert_eq!(
        (written.left, written.top, written.right, written.bottom),
        (0, 0, 1920, 1080)
    );
    assert_eq!(
        String::from_utf16(&written.device_name[..written.device_name_len as usize]).unwrap(),
        r"\\.\DISPLAY1"
    );
}

#[test]
fn a_second_display_and_a_turned_one_are_described_as_such() {
    let turned = DisplayOutput {
        rotation: Rotation::Rotate270,
        attached: false,
        ..display(1, 1920, r"\\.\DISPLAY2")
    };

    let flat = FlintOutput::from_display(&turned);

    assert_eq!(flat.main, 0);
    assert_eq!(flat.attached, 0);
    assert_eq!(flat.rotation, 4);
}

#[test]
fn a_name_longer_than_windows_allows_is_cut_rather_than_overflowing() {
    let long = display(0, 0, &"D".repeat(40));

    let flat = FlintOutput::from_display(&long);

    assert_eq!(flat.device_name_len, 32);
    assert!(flat.device_name.iter().all(|&unit| unit == u16::from(b'D')));
}
