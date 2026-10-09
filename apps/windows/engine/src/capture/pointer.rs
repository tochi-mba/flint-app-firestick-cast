//! The mouse pointer, drawn into the shared picture.
//!
//! Desktop Duplication hands the pointer over apart from the picture: its shape when the shape
//! changes and its position when it moves. Neither is in the frame, so without this the TV shows
//! a desktop with no pointer on it. Everything here is pure, so it is tested against known pixels.

use crate::encode::video::{FrameData, SourceFrame};

/// The largest pointer side drawn. A shape any larger is refused, never overrun.
pub const MAX_POINTER_SIDE: u32 = 256;

/// How a pointer shape's bytes are meant, as `DXGI_OUTDUPL_POINTER_SHAPE_TYPE` says.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PointerKind {
    /// One bit a pixel: an AND mask, then an XOR mask, each the pointer's height.
    Monochrome,
    /// BGRA with straight alpha, blended over the picture.
    Color,
    /// BGR whose alpha byte is a mask: 0 draws the colour, 0xFF inverts the picture by it.
    MaskedColor,
}

/// A pointer's shape, as Windows gave it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PointerShape {
    /// How the bytes are meant.
    pub kind: PointerKind,
    /// Width in pixels.
    pub width: u32,
    /// Height in pixels: the pointer's own, not the two masks a monochrome shape carries.
    pub height: u32,
    /// Bytes from one row to the next.
    pub pitch: u32,
    /// The pixel within the shape that sits on the pointer's position.
    pub hotspot: (i32, i32),
    /// The shape's bytes.
    pub data: Vec<u8>,
}

impl PointerShape {
    /// Whether the shape can be drawn: a size within bounds, and every byte it claims present.
    #[must_use]
    pub fn is_drawable(&self) -> bool {
        if !(1..=MAX_POINTER_SIDE).contains(&self.width)
            || !(1..=MAX_POINTER_SIDE).contains(&self.height)
        {
            return false;
        }
        let rows = match self.kind {
            PointerKind::Monochrome => self.height * 2,
            PointerKind::Color | PointerKind::MaskedColor => self.height,
        };
        let row_bytes = match self.kind {
            PointerKind::Monochrome => self.width.div_ceil(8),
            PointerKind::Color | PointerKind::MaskedColor => self.width * 4,
        };
        // In 64 bits: a pitch Windows reported can be anything, and must not overflow the check.
        let needed = u64::from(self.pitch) * u64::from(rows - 1) + u64::from(row_bytes);
        self.pitch >= row_bytes && self.data.len() as u64 >= needed
    }
}

/// A BGRA picture the pointer is drawn into.
pub struct Canvas<'a> {
    /// The pixels.
    pub pixels: &'a mut [u8],
    /// Width in pixels.
    pub width: u32,
    /// Height in pixels.
    pub height: u32,
    /// Bytes from one row to the next.
    pub stride: u32,
}

/// Draws `shape` with its hotspot at `(x, y)` on `canvas`, clipped at the canvas's edges.
///
/// Returns whether anything was drawn: a shape that cannot be drawn, or one entirely off the
/// canvas, leaves the picture as it was.
pub fn draw(canvas: &mut Canvas<'_>, shape: &PointerShape, x: i32, y: i32) -> bool {
    if !shape.is_drawable()
        || canvas.pixels.len() < (canvas.stride * canvas.height) as usize
        || canvas.stride < canvas.width * 4
    {
        return false;
    }

    let left = x - shape.hotspot.0;
    let top = y - shape.hotspot.1;
    let mut drew = false;
    for row in 0..shape.height as i32 {
        let canvas_y = top + row;
        if canvas_y < 0 || canvas_y >= canvas.height as i32 {
            continue;
        }
        for column in 0..shape.width as i32 {
            let canvas_x = left + column;
            if canvas_x < 0 || canvas_x >= canvas.width as i32 {
                continue;
            }
            let at = canvas_y as usize * canvas.stride as usize + canvas_x as usize * 4;
            let pixel = &mut canvas.pixels[at..at + 4];
            put(pixel, shape, column as u32, row as u32);
            drew = true;
        }
    }
    drew
}

/// Draws one pixel of `shape` over the picture's pixel.
fn put(pixel: &mut [u8], shape: &PointerShape, column: u32, row: u32) {
    match shape.kind {
        PointerKind::Monochrome => {
            let and = bit(shape, column, row);
            let xor = bit(shape, column, row + shape.height);
            match (and, xor) {
                (false, false) => pixel[..3].fill(0x00),
                (false, true) => pixel[..3].fill(0xFF),
                (true, false) => {}
                (true, true) => pixel[..3].iter_mut().for_each(|value| *value = !*value),
            }
        }
        PointerKind::Color => {
            let source = &shape.data[(row * shape.pitch + column * 4) as usize..][..4];
            let alpha = u32::from(source[3]);
            for channel in 0..3 {
                let over = u32::from(source[channel]) * alpha;
                let under = u32::from(pixel[channel]) * (255 - alpha);
                pixel[channel] = ((over + under + 127) / 255) as u8;
            }
        }
        PointerKind::MaskedColor => {
            let source = &shape.data[(row * shape.pitch + column * 4) as usize..][..4];
            if source[3] == 0 {
                pixel[..3].copy_from_slice(&source[..3]);
            } else {
                for channel in 0..3 {
                    pixel[channel] ^= source[channel];
                }
            }
        }
    }
}

/// One bit of a monochrome shape's masks, most significant bit first.
fn bit(shape: &PointerShape, column: u32, row: u32) -> bool {
    let byte = shape.data[(row * shape.pitch + column / 8) as usize];
    byte & (0x80 >> (column % 8)) != 0
}

/// What the duplication last said about the pointer: where it is, and its shape.
#[derive(Debug, Default)]
pub struct PointerTrack {
    place: Option<(i32, i32)>,
    shape: Option<PointerShape>,
}

impl PointerTrack {
    /// Takes a position report, `None` when the pointer is hidden.
    ///
    /// Returns whether the picture would change: the pointer moved, appeared or went.
    pub fn moved_to(&mut self, place: Option<(i32, i32)>) -> bool {
        let changed = self.place != place;
        self.place = place;
        changed
    }

    /// Takes a new shape. Returns whether the picture would change: only while the pointer shows.
    pub fn reshape(&mut self, shape: PointerShape) -> bool {
        let changed = self.shape.as_ref() != Some(&shape);
        self.shape = Some(shape);
        changed && self.place.is_some()
    }

    /// Where the pointer is on the desktop, when it shows.
    #[must_use]
    pub fn place(&self) -> Option<(i32, i32)> {
        self.place
    }

    /// The pointer's shape, once Windows has said what it is.
    #[must_use]
    pub fn shape(&self) -> Option<&PointerShape> {
        self.shape.as_ref()
    }

    /// Draws the pointer onto `canvas`, a picture of the desktop scaled from `desktop` size.
    ///
    /// Returns whether anything was drawn: nothing is while the pointer is hidden or its shape is
    /// not yet known.
    pub fn draw_on(&self, canvas: &mut Canvas<'_>, desktop: (u32, u32)) -> bool {
        let (Some((x, y)), Some(shape)) = (self.place, self.shape.as_ref()) else {
            return false;
        };
        let (x, y) = scaled(x, y, desktop, (canvas.width, canvas.height));
        draw(canvas, shape, x, y)
    }
}

/// Draws the pointer `track` follows, when there is one, into a frame read back to memory from a
/// desktop of `desktop` size. A frame kept on the GPU is the overlay's to draw on.
///
/// Returns whether anything was drawn.
pub fn draw_into(
    frame: &mut SourceFrame,
    track: Option<&PointerTrack>,
    desktop: (u32, u32),
) -> bool {
    let (Some(track), FrameData::Bgra { pixels, stride }) = (track, &mut frame.data) else {
        return false;
    };
    let mut canvas = Canvas {
        pixels,
        width: frame.width,
        height: frame.height,
        stride: *stride,
    };
    track.draw_on(&mut canvas, desktop)
}

/// Where a pointer at `(x, y)` on a desktop of `desktop` size lands on a picture of `picture`
/// size, which is the desktop scaled.
#[must_use]
pub fn scaled(x: i32, y: i32, desktop: (u32, u32), picture: (u32, u32)) -> (i32, i32) {
    if desktop.0 == 0 || desktop.1 == 0 {
        return (x, y);
    }
    (
        (i64::from(x) * i64::from(picture.0) / i64::from(desktop.0)) as i32,
        (i64::from(y) * i64::from(picture.1) / i64::from(desktop.1)) as i32,
    )
}

#[cfg(test)]
#[path = "pointer_tests.rs"]
mod tests;
