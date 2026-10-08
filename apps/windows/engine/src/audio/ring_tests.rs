use super::*;
use std::sync::Arc;

fn pop_now(ring: &PacketRing) -> Option<(Vec<u8>, i64)> {
    let mut out = Vec::new();
    ring.pop(&mut out, Duration::ZERO).map(|time| (out, time))
}

#[test]
fn packets_come_out_in_the_order_they_went_in() {
    let ring = PacketRing::new(4);
    assert_eq!(ring.push(&[1], 10), Pushed::Queued);
    assert_eq!(ring.push(&[2, 2], 20), Pushed::Queued);

    assert_eq!(pop_now(&ring), Some((vec![1], 10)));
    assert_eq!(pop_now(&ring), Some((vec![2, 2], 20)));
    assert_eq!(pop_now(&ring), None);
}

#[test]
fn a_full_queue_drops_the_oldest_and_counts_it() {
    let ring = PacketRing::new(2);
    ring.push(&[1], 1);
    ring.push(&[2], 2);

    assert_eq!(ring.push(&[3], 3), Pushed::DroppedOldest);

    assert_eq!(ring.stats().dropped, 1);
    assert_eq!(pop_now(&ring).unwrap().1, 2);
    assert_eq!(pop_now(&ring).unwrap().1, 3);
}

#[test]
fn a_packet_larger_than_a_slot_is_refused_never_cut_short() {
    let ring = PacketRing::new(2);

    assert_eq!(ring.push(&[0; SLOT_BYTES + 1], 1), Pushed::TooLarge);

    assert_eq!(
        ring.stats(),
        RingStats {
            dropped: 0,
            refused: 1
        }
    );
    assert_eq!(pop_now(&ring), None);
    assert_eq!(
        ring.push(&[0; SLOT_BYTES], 2),
        Pushed::Queued,
        "exactly a slot fits"
    );
}

#[test]
fn waiting_gives_up_after_the_timeout_and_wakes_when_a_packet_arrives() {
    let ring = Arc::new(PacketRing::new(4));
    let mut out = Vec::new();

    let started = Instant::now();
    assert_eq!(ring.pop(&mut out, Duration::from_millis(50)), None);
    assert!(started.elapsed() >= Duration::from_millis(45));

    let pusher = {
        let ring = Arc::clone(&ring);
        std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(30));
            ring.push(&[7], 70);
        })
    };
    let started = Instant::now();
    assert_eq!(ring.pop(&mut out, Duration::from_secs(5)), Some(70));
    assert!(
        started.elapsed() < Duration::from_secs(2),
        "woken by the push, not the timeout"
    );
    pusher.join().unwrap();
}

#[test]
fn a_delay_holds_a_packet_until_its_time_and_then_lets_it_go() {
    let ring = PacketRing::new(4);
    ring.set_delay(Duration::from_millis(80));
    ring.push(&[1], 1);

    assert_eq!(pop_now(&ring), None, "not before its time");
    let mut out = Vec::new();
    let started = Instant::now();
    assert_eq!(ring.pop(&mut out, Duration::from_secs(2)), Some(1));
    assert!(started.elapsed() >= Duration::from_millis(60));
}

#[test]
fn shortening_the_delay_never_lets_a_later_packet_overtake() {
    let ring = PacketRing::new(4);
    ring.set_delay(Duration::from_millis(100));
    ring.push(&[1], 1);
    ring.set_delay(Duration::ZERO);
    ring.push(&[2], 2);

    let mut out = Vec::new();
    assert_eq!(ring.pop(&mut out, Duration::from_secs(2)), Some(1));
    assert_eq!(ring.pop(&mut out, Duration::from_secs(2)), Some(2));
}

#[test]
fn clearing_empties_the_queue_and_frees_every_slot() {
    let ring = PacketRing::new(2);
    ring.push(&[1], 1);
    ring.push(&[2], 2);

    ring.clear();

    assert_eq!(pop_now(&ring), None);
    assert_eq!(ring.push(&[3], 3), Pushed::Queued);
    assert_eq!(
        ring.push(&[4], 4),
        Pushed::Queued,
        "both slots are free again"
    );
}

#[test]
fn two_threads_lose_nothing_beyond_what_is_counted() {
    const PACKETS: i64 = 100_000;
    let ring = Arc::new(PacketRing::new(64));
    let pusher = {
        let ring = Arc::clone(&ring);
        std::thread::spawn(move || {
            for time in 0..PACKETS {
                ring.push(&time.to_le_bytes(), time);
            }
        })
    };

    let mut received = 0i64;
    let mut last = -1i64;
    let mut out = Vec::new();
    while let Some(time) = ring.pop(&mut out, Duration::from_millis(500)) {
        assert!(time > last, "in order");
        assert_eq!(out, time.to_le_bytes(), "intact");
        last = time;
        received += 1;
    }
    pusher.join().unwrap();

    assert_eq!(
        received + i64::try_from(ring.stats().dropped).unwrap(),
        PACKETS
    );
}

#[test]
fn slots_are_reused_without_growing() {
    let ring = PacketRing::new(2);
    for round in 0..100 {
        ring.push(&[round as u8; SLOT_BYTES], round);
        pop_now(&ring).unwrap();
    }

    let state = ring.lock();
    assert!(state
        .free
        .iter()
        .all(|slot| slot.data.capacity() == SLOT_BYTES));
}
