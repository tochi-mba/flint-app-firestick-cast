use super::*;
use crate::audio::fake_outputs::FakeOutputs;

const fn ms(value: u64) -> Duration {
    Duration::from_millis(value)
}

fn read(follower: &mut Follower<FakeOutputs>, now: Duration) -> (Change, Vec<i16>) {
    let mut out = Vec::new();
    let change = follower
        .read(now, &mut out)
        .expect("a read that does not fail");
    (change, out)
}

#[test]
fn the_named_output_or_the_default_is_opened_and_read() {
    let outputs = FakeOutputs::with(&[("speakers", 1), ("headphones", 2)], Some("speakers"));

    let mut default = Follower::open(outputs.clone(), None).unwrap();
    assert_eq!(read(&mut default, ms(0)), (Change::None, vec![1]));
    let mut named = Follower::open(outputs.clone(), Some("headphones".into())).unwrap();
    assert_eq!(read(&mut named, ms(0)), (Change::None, vec![2]));

    assert!(matches!(
        Follower::open(outputs.clone(), Some("hdmi".into())),
        Err(AudioError::DeviceMissing)
    ));
    outputs.plug(&[], None);
    assert!(matches!(
        Follower::open(outputs, None),
        Err(AudioError::NoOutput)
    ));
}

#[test]
fn a_lost_default_output_moves_to_the_new_default_without_a_gap() {
    let outputs = FakeOutputs::with(&[("headphones", 2), ("speakers", 1)], Some("headphones"));
    let mut follower = Follower::open(outputs.clone(), None).unwrap();
    assert_eq!(read(&mut follower, ms(10)), (Change::None, vec![2]));

    outputs.plug(&[("speakers", 1)], Some("speakers"));

    assert_eq!(
        read(&mut follower, ms(20)),
        (Change::None, vec![]),
        "nothing half-read is kept"
    );
    assert_eq!(read(&mut follower, ms(30)), (Change::None, vec![1]));
}

#[test]
fn a_lost_named_output_is_unavailable_until_it_comes_back() {
    let outputs = FakeOutputs::with(&[("usb", 3), ("speakers", 1)], Some("speakers"));
    let mut follower = Follower::open(outputs.clone(), Some("usb".into())).unwrap();

    outputs.plug(&[("speakers", 1)], Some("speakers"));
    assert_eq!(read(&mut follower, ms(500)), (Change::Lost, vec![]));
    assert_eq!(follower.meter(), 0.0);

    // Looked for at once, then a second after the loss, not a second after the share started.
    let opens = outputs.desk().opens;
    assert_eq!(read(&mut follower, ms(1_100)), (Change::None, vec![]));
    assert_eq!(
        outputs.desk().opens,
        opens,
        "not looked for again within a second of the loss"
    );
    assert_eq!(read(&mut follower, ms(1_500)), (Change::None, vec![]));
    assert_eq!(
        outputs.desk().opens,
        opens + 1,
        "looked for again a second after the loss"
    );

    outputs.plug(&[("usb", 3), ("speakers", 1)], Some("speakers"));
    assert_eq!(read(&mut follower, ms(2_000)), (Change::None, vec![]));
    assert_eq!(read(&mut follower, ms(2_500)), (Change::Back, vec![]));
    assert_eq!(read(&mut follower, ms(2_510)), (Change::None, vec![3]));
}

#[test]
fn with_no_output_at_all_the_default_is_lost_then_found_again() {
    let outputs = FakeOutputs::with(&[("speakers", 1)], Some("speakers"));
    let mut follower = Follower::open(outputs.clone(), None).unwrap();

    outputs.plug(&[], None);
    assert_eq!(read(&mut follower, ms(0)), (Change::Lost, vec![]));

    outputs.plug(&[("hdmi", 4)], Some("hdmi"));
    assert_eq!(read(&mut follower, ms(1_000)), (Change::Back, vec![]));
    assert_eq!(read(&mut follower, ms(1_010)), (Change::None, vec![4]));
}

#[test]
fn the_default_is_followed_within_a_second_and_a_named_output_is_not() {
    let outputs = FakeOutputs::with(&[("speakers", 1), ("headphones", 2)], Some("speakers"));
    let mut default = Follower::open(outputs.clone(), None).unwrap();
    let mut named = Follower::open(outputs.clone(), Some("speakers".into())).unwrap();

    outputs.plug(&[("speakers", 1), ("headphones", 2)], Some("headphones"));

    assert_eq!(read(&mut default, ms(999)), (Change::None, vec![1]));
    assert_eq!(read(&mut default, ms(1_000)), (Change::None, vec![1]));
    assert_eq!(read(&mut default, ms(1_010)), (Change::None, vec![2]));
    assert_eq!(read(&mut named, ms(1_000)), (Change::None, vec![1]));
    assert_eq!(read(&mut named, ms(1_010)), (Change::None, vec![1]));
    assert_eq!(
        outputs.desk().opens,
        3,
        "two opened, the default moved once, and the named output was never reopened"
    );
}

#[test]
fn an_unchanged_default_is_not_opened_again() {
    let outputs = FakeOutputs::with(&[("speakers", 1)], Some("speakers"));
    let mut follower = Follower::open(outputs.clone(), None).unwrap();

    read(&mut follower, ms(1_000));
    read(&mut follower, ms(2_000));

    assert_eq!(outputs.desk().opens, 1);
}

#[test]
fn a_new_default_that_will_not_open_leaves_capture_where_it_was() {
    let outputs = FakeOutputs::with(&[("speakers", 1), ("headphones", 2)], Some("speakers"));
    let mut follower = Follower::open(outputs.clone(), None).unwrap();

    outputs.plug(&[("speakers", 1), ("headphones", 2)], Some("headphones"));
    outputs.desk().refuse_opens = true;

    assert_eq!(read(&mut follower, ms(1_000)), (Change::None, vec![1]));
    assert_eq!(read(&mut follower, ms(1_010)), (Change::None, vec![1]));
}

#[test]
fn a_failure_other_than_loss_ends_the_share() {
    let outputs = FakeOutputs::with(&[("speakers", 1)], Some("speakers"));
    let mut follower = Follower::open(outputs.clone(), None).unwrap();
    outputs.desk().failure = Some(AudioError::Platform("broken".into()));

    let mut out = Vec::new();
    assert_eq!(
        follower.read(ms(0), &mut out),
        Err(AudioError::Platform("broken".into()))
    );
}

#[test]
fn the_meter_is_the_outputs_own() {
    let outputs = FakeOutputs::with(&[("speakers", 1)], Some("speakers"));
    let follower = Follower::open(outputs.clone(), None).unwrap();

    outputs.desk().meter = 0.25;

    assert_eq!(follower.meter(), 0.25);
}
