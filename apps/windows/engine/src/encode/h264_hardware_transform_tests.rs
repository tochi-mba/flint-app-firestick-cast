use super::*;

#[test]
fn the_need_input_event_is_recognised() {
    assert_eq!(
        TransformEvent::from_event_type(TRANSFORM_NEED_INPUT),
        TransformEvent::NeedInput
    );
}

#[test]
fn the_have_output_event_is_recognised() {
    assert_eq!(
        TransformEvent::from_event_type(TRANSFORM_HAVE_OUTPUT),
        TransformEvent::HaveOutput
    );
}

#[test]
fn an_unrelated_event_does_not_end_the_session() {
    // Format changes and drain markers arrive here. Treating one as an error would kill a
    // perfectly healthy encode.
    assert_eq!(TransformEvent::from_event_type(603), TransformEvent::Other);
    assert_eq!(TransformEvent::from_event_type(0), TransformEvent::Other);
}

#[test]
fn the_event_identifiers_match_media_foundations_own_numbering() {
    // These are ABI constants, not arbitrary choices; getting one wrong makes the pump wait
    // forever for an event that never arrives under that name.
    assert_eq!(TRANSFORM_NEED_INPUT, 601);
    assert_eq!(TRANSFORM_HAVE_OUTPUT, 602);
}
