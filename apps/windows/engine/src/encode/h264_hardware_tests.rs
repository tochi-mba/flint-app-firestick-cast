use super::*;

#[test]
fn opening_an_encoder_on_a_nonexistent_adapter_fails_rather_than_picking_another_one() {
    // The whole point of passing a LUID is that capture and encode share an adapter. Silently
    // falling back to a different GPU would reintroduce the cross-adapter copy this avoids.
    let Err(error) = HardwareTransform::open(i64::MAX) else {
        panic!("a nonexistent adapter must not yield an encoder");
    };
    assert!(matches!(error, EncodeError::Platform(_)));
}

#[test]
fn the_key_frame_interval_is_two_seconds_at_ordinary_frame_rates() {
    assert_eq!(gop_size(30), 60);
    assert_eq!(gop_size(60), 120);
}

#[test]
fn a_zero_frame_rate_cannot_produce_a_group_of_pictures_of_zero() {
    // Encoders disagree about what a GOP of zero means: some emit an IDR every frame, some never
    // emit one at all. Either would be discovered only in the field.
    assert_eq!(gop_size(0), MIN_GOP_SIZE);
    assert!(gop_size(0) > 0);
}

#[test]
fn a_very_low_frame_rate_still_recovers_within_a_bounded_number_of_frames() {
    assert_eq!(gop_size(1), MIN_GOP_SIZE);
    assert_eq!(gop_size(14), MIN_GOP_SIZE);
}

#[test]
fn an_absurd_frame_rate_does_not_push_recovery_minutes_away() {
    assert_eq!(gop_size(1_000), MAX_GOP_SIZE);
    assert_eq!(gop_size(u32::MAX), MAX_GOP_SIZE);
}

#[test]
fn the_key_frame_interval_never_overflows() {
    // saturating_mul rather than `*`: a release build wraps silently, and a wrapped GOP is a
    // plausible-looking small number that changes the stream's behaviour for no visible reason.
    let _ = gop_size(u32::MAX);
    let _ = gop_size(u32::MAX / 2);
}

#[test]
fn the_key_frame_interval_rises_with_the_frame_rate_between_the_bounds() {
    // Two seconds of frames means the interval must track the rate, not sit at a fixed count.
    assert!(gop_size(60) > gop_size(30));
    assert!(gop_size(120) > gop_size(60));
}

#[test]
fn the_bounds_are_ordered_so_the_clamp_cannot_panic() {
    // `clamp` panics when min exceeds max, and both bounds are constants a future edit could
    // reverse. Written through a runtime value so the compiler cannot fold the check away and
    // leave the invariant unguarded.
    let low = std::hint::black_box(MIN_GOP_SIZE);
    assert!(low <= MAX_GOP_SIZE);
}
