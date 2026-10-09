use super::*;

const GREY: [u8; 4] = [0x40, 0x80, 0xC0, 0xFF];

/// A 4 by 4 canvas of one colour, with a padded stride so row arithmetic is exercised.
fn canvas_bytes() -> Vec<u8> {
    let mut bytes = vec![0u8; 20 * 4];
    for row in 0..4 {
        for column in 0..4 {
            bytes[row * 20 + column * 4..][..4].copy_from_slice(&GREY);
        }
    }
    bytes
}

fn canvas(bytes: &mut [u8]) -> Canvas<'_> {
    Canvas {
        pixels: bytes,
        width: 4,
        height: 4,
        stride: 20,
    }
}

fn pixel(bytes: &[u8], x: usize, y: usize) -> [u8; 4] {
    bytes[y * 20 + x * 4..][..4].try_into().unwrap()
}

#[test]
fn a_frame_read_back_to_memory_gets_the_pointer_only_when_there_is_one() {
    let mut frame = SourceFrame {
        width: 4,
        height: 4,
        presentation_time_us: 0,
        data: FrameData::Bgra {
            pixels: canvas_bytes(),
            stride: 20,
        },
    };
    let mut track = PointerTrack::default();
    track.moved_to(Some((1, 1)));
    track.reshape(monochrome());

    assert!(!draw_into(&mut frame, None, (4, 4)));
    let FrameData::Bgra { pixels, .. } = &frame.data else {
        panic!("a frame read back to memory");
    };
    assert_eq!(pixel(pixels, 1, 1), GREY, "untouched without a pointer");

    assert!(draw_into(&mut frame, Some(&track), (4, 4)));
    let FrameData::Bgra { pixels, .. } = &frame.data else {
        panic!("a frame read back to memory");
    };
    assert_eq!(
        pixel(pixels, 1, 1)[..3],
        [0x00; 3],
        "the shape's black corner"
    );
    assert_eq!(pixel(pixels, 2, 1)[..3], [0xFF; 3], "and its white one");
}

/// A 2 by 2 monochrome shape: black, white, transparent, inverted.
fn monochrome() -> PointerShape {
    // AND rows then XOR rows; one byte a row, most significant bit first.
    PointerShape {
        kind: PointerKind::Monochrome,
        width: 2,
        height: 2,
        pitch: 1,
        hotspot: (0, 0),
        data: vec![0b0000_0000, 0b1100_0000, 0b0100_0000, 0b0100_0000],
    }
}

fn color(pixels: &[[u8; 4]], width: u32) -> PointerShape {
    PointerShape {
        kind: PointerKind::Color,
        width,
        height: (pixels.len() as u32) / width,
        pitch: width * 4,
        hotspot: (0, 0),
        data: pixels.iter().flatten().copied().collect(),
    }
}

#[test]
fn a_monochrome_pointer_draws_black_white_transparent_and_inverted() {
    let mut bytes = canvas_bytes();

    assert!(draw(&mut canvas(&mut bytes), &monochrome(), 1, 1));

    assert_eq!(
        pixel(&bytes, 1, 1)[..3],
        [0x00, 0x00, 0x00],
        "AND 0, XOR 0: black"
    );
    assert_eq!(
        pixel(&bytes, 2, 1)[..3],
        [0xFF, 0xFF, 0xFF],
        "AND 0, XOR 1: white"
    );
    assert_eq!(
        pixel(&bytes, 1, 2),
        GREY,
        "AND 1, XOR 0: the picture shows through"
    );
    assert_eq!(
        pixel(&bytes, 2, 2)[..3],
        [!0x40, !0x80, !0xC0],
        "AND 1, XOR 1: inverted"
    );
    assert_eq!(pixel(&bytes, 2, 2)[3], 0xFF, "alpha is left alone");
    assert_eq!(
        pixel(&bytes, 0, 0),
        GREY,
        "outside the pointer is untouched"
    );
}

#[test]
fn a_color_pointer_is_blended_by_its_alpha() {
    let mut bytes = canvas_bytes();
    let shape = color(
        &[
            [0xFF, 0x00, 0x00, 0xFF],
            [0xFF, 0x00, 0x00, 0x80],
            [0, 0, 0, 0],
            [0x00, 0xFF, 0x00, 0xFF],
        ],
        2,
    );

    draw(&mut canvas(&mut bytes), &shape, 0, 0);

    assert_eq!(pixel(&bytes, 0, 0)[..3], [0xFF, 0x00, 0x00], "opaque");
    assert_eq!(
        pixel(&bytes, 1, 0)[..3],
        [0xA0, 0x40, 0x60],
        "half way between the two"
    );
    assert_eq!(pixel(&bytes, 0, 1), GREY, "fully transparent");
    assert_eq!(pixel(&bytes, 1, 1)[..3], [0x00, 0xFF, 0x00]);
}

#[test]
fn a_masked_color_pointer_draws_or_inverts_by_its_mask() {
    let mut bytes = canvas_bytes();
    let mut shape = color(&[[0x11, 0x22, 0x33, 0x00], [0xFF, 0xFF, 0xFF, 0xFF]], 2);
    shape.kind = PointerKind::MaskedColor;

    draw(&mut canvas(&mut bytes), &shape, 2, 3);

    assert_eq!(
        pixel(&bytes, 2, 3)[..3],
        [0x11, 0x22, 0x33],
        "mask 0: the colour"
    );
    assert_eq!(
        pixel(&bytes, 3, 3)[..3],
        [!0x40, !0x80, !0xC0],
        "mask 0xFF: the picture XORed"
    );
}

#[test]
fn the_hotspot_sits_on_the_position() {
    let mut bytes = canvas_bytes();
    let mut shape = color(&[[0xFF, 0xFF, 0xFF, 0xFF]; 4], 2);
    shape.hotspot = (1, 1);

    draw(&mut canvas(&mut bytes), &shape, 2, 2);

    assert_eq!(pixel(&bytes, 1, 1)[..3], [0xFF; 3]);
    assert_eq!(pixel(&bytes, 2, 2)[..3], [0xFF; 3]);
    assert_eq!(pixel(&bytes, 3, 3), GREY);
}

#[test]
fn a_pointer_partly_off_any_edge_or_corner_is_clipped() {
    let shape = color(&[[0xFF, 0xFF, 0xFF, 0xFF]; 9], 3);
    for (x, y, inside) in [
        (-1, 1, 6),
        (3, 1, 3),
        (1, -2, 3),
        (1, 3, 3),
        (-2, -2, 1),
        (3, 3, 1),
        (-2, 3, 1),
        (3, -2, 1),
    ] {
        let mut bytes = canvas_bytes();

        assert!(draw(&mut canvas(&mut bytes), &shape, x, y), "at {x},{y}");

        let painted = (0..4)
            .flat_map(|row| (0..4).map(move |column| (column, row)))
            .filter(|&(column, row)| pixel(&bytes, column, row)[..3] == [0xFF; 3])
            .count();
        assert_eq!(painted, inside, "at {x},{y}");
        assert!(
            bytes[16..20].iter().all(|&byte| byte == 0),
            "the row padding is never written"
        );
    }
}

#[test]
fn a_pointer_entirely_off_the_picture_draws_nothing() {
    let mut bytes = canvas_bytes();
    let shape = color(&[[0xFF, 0xFF, 0xFF, 0xFF]], 1);

    assert!(!draw(&mut canvas(&mut bytes), &shape, 4, 0));
    assert!(!draw(&mut canvas(&mut bytes), &shape, 0, -1));
    assert_eq!(bytes, canvas_bytes());
}

#[test]
fn a_shape_too_large_or_short_of_bytes_is_refused_not_overrun() {
    let mut bytes = canvas_bytes();
    let mut huge = color(&[[0xFF; 4]], 1);
    huge.width = MAX_POINTER_SIDE + 1;
    let mut empty = color(&[[0xFF; 4]], 1);
    empty.height = 0;
    let mut short = color(&[[0xFF; 4]; 4], 2);
    short.data.pop();
    let mut narrow = color(&[[0xFF; 4]; 4], 2);
    narrow.pitch = 4;
    let mut endless = monochrome();
    endless.pitch = u32::MAX;

    for shape in [huge, empty, short, narrow, endless] {
        assert!(!shape.is_drawable(), "{shape:?}");
        assert!(!draw(&mut canvas(&mut bytes), &shape, 0, 0));
    }
    assert_eq!(bytes, canvas_bytes());
    assert!(monochrome().is_drawable());
}

#[test]
fn a_canvas_smaller_than_it_claims_is_left_alone() {
    let mut bytes = vec![0u8; 8];
    let mut lying = Canvas {
        pixels: &mut bytes,
        width: 4,
        height: 4,
        stride: 16,
    };
    assert!(!draw(&mut lying, &monochrome(), 0, 0));

    let mut more = vec![0u8; 64];
    let mut cramped = Canvas {
        pixels: &mut more,
        width: 4,
        height: 4,
        stride: 8,
    };
    assert!(!draw(&mut cramped, &monochrome(), 0, 0));
}

#[test]
fn a_position_on_the_desktop_is_scaled_onto_a_smaller_picture() {
    assert_eq!(scaled(1280, 720, (2560, 1440), (1920, 1080)), (960, 540));
    assert_eq!(scaled(-10, 4, (100, 100), (50, 50)), (-5, 2));
    assert_eq!(
        scaled(7, 9, (0, 0), (50, 50)),
        (7, 9),
        "no desktop size, no scaling"
    );
}

#[test]
fn the_track_says_when_the_picture_would_change() {
    let mut track = PointerTrack::default();

    assert!(!track.moved_to(None), "hidden, and still hidden");
    assert!(
        !track.reshape(monochrome()),
        "a new shape while hidden changes nothing on screen"
    );
    assert!(track.moved_to(Some((1, 1))), "it appears");
    assert!(
        !track.moved_to(Some((1, 1))),
        "a report of where it already is"
    );
    assert!(track.moved_to(Some((2, 1))), "it moves");
    assert!(!track.reshape(monochrome()), "the same shape again");
    assert!(
        track.reshape(color(&[[0xFF; 4]], 1)),
        "a new shape while it shows"
    );
    assert!(track.moved_to(None), "it goes");
    assert_eq!(track.place(), None);
    assert_eq!(
        track.shape().map(|shape| shape.kind),
        Some(PointerKind::Color)
    );
}

#[test]
fn the_track_draws_where_the_pointer_is_once_it_has_a_shape() {
    let mut bytes = canvas_bytes();
    let mut track = PointerTrack::default();
    track.moved_to(Some((4, 4)));

    assert!(
        !track.draw_on(&mut canvas(&mut bytes), (8, 8)),
        "no shape yet"
    );
    track.reshape(color(&[[0xFF, 0xFF, 0xFF, 0xFF]], 1));
    assert!(track.draw_on(&mut canvas(&mut bytes), (8, 8)));
    assert_eq!(
        pixel(&bytes, 2, 2)[..3],
        [0xFF; 3],
        "half the desktop's size, so half the position"
    );

    track.moved_to(None);
    assert!(!track.draw_on(&mut canvas(&mut bytes), (8, 8)), "hidden");
}
