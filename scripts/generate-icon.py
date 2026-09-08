#!/usr/bin/env python3
"""Generate the Flint application icon.

A REX Technologies product mark. Kept as a script rather than a checked-in binary alone so the
icon is reproducible: the palette is the same one the UI reads from FlintColors.axaml, and a
change there can be mirrored here rather than requiring a lost design file.

The mark is deliberately a sibling of REX Cast's, not a copy. Both sit on the ink ground inside the
same off-white square frame; Cast fills it with three horizontal signal bars, Flint with a struck
spark. Side by side they read as one family, which is the point.

Geometry uses REX Cast's 108-unit viewport so the two marks can be compared directly.

Usage:
    python scripts/generate-icon.py
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw

# The REX ink/signal palette, matching FlintColors.axaml.
INK = (0x08, 0x0A, 0x09, 255)
SIGNAL = (0xD7, 0xFF, 0x3F, 255)
TEXT = (0xF2, 0xF5, 0xEE, 255)

VIEWPORT = 108.0
CENTRE = 54.0

# The square frame, matching REX Cast's ic_launcher_foreground: 21..87 with a 3-unit stroke.
FRAME_INSET = 21.0
FRAME_STROKE = 3.0

# Corner radius for the Windows tile, matching the RadiusLarge token.
CORNER_RADIUS = 18.0

# The spark. Taller than it is wide, so it reads as struck rather than as a generic sparkle.
SPARK_RADIUS_Y = 29.0
SPARK_RADIUS_X = 21.0
SPARK_WAIST = 6.0

# Off-axis so the spark reads as struck rather than as decoration.
SPARK_TILT_DEGREES = 20.0

# Below this pixel size the frame collapses into an unreadable smudge and the spark needs the whole
# tile to stay legible, so small icons drop the frame and draw the mark larger. This is ordinary
# icon practice, not a shortcut.
FRAME_MIN_SIZE = 32

# Sizes Windows actually asks for: Explorer, the taskbar, Alt-Tab, and the large tile views.
ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)

# Draw large and downsample, because PIL has no anti-aliasing of its own.
SUPERSAMPLE = 8


def spark_points(
    centre: float,
    radius_x: float,
    radius_y: float,
    waist: float,
    tilt_degrees: float = 0.0,
) -> list[tuple[float, float]]:
    """The concave four-point spark, as a polygon.

    Straight edges rather than curves: at 16 pixels a Bezier waist and a straight one are
    indistinguishable, and the polygon renders more crisply after downsampling.

    The tilt is what separates this from a generic sparkle. Off-axis, the mark reads as a spark
    thrown from a strike, with a direction; square-on it reads as decoration.
    """
    diagonal = waist * math.sqrt(0.5)
    points = [
        (0.0, -radius_y),               # long axis, one end
        (diagonal, -diagonal),
        (radius_x, 0.0),                # short axis
        (diagonal, diagonal),
        (0.0, radius_y),                # long axis, other end
        (-diagonal, diagonal),
        (-radius_x, 0.0),
        (-diagonal, -diagonal),
    ]

    angle = math.radians(tilt_degrees)
    cos, sin = math.cos(angle), math.sin(angle)
    return [
        (centre + x * cos - y * sin, centre + x * sin + y * cos)
        for x, y in points
    ]


def render(size: int) -> Image.Image:
    """Render one square icon at the given pixel size."""
    scale = size * SUPERSAMPLE / VIEWPORT
    canvas = size * SUPERSAMPLE
    image = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    def units(value: float) -> float:
        return value * scale

    # The ink tile.
    draw.rounded_rectangle(
        [(0, 0), (canvas - 1, canvas - 1)],
        radius=units(CORNER_RADIUS),
        fill=INK,
    )

    with_frame = size >= FRAME_MIN_SIZE
    if with_frame:
        draw.rectangle(
            [
                (units(FRAME_INSET), units(FRAME_INSET)),
                (units(VIEWPORT - FRAME_INSET), units(VIEWPORT - FRAME_INSET)),
            ],
            outline=TEXT,
            width=max(1, round(units(FRAME_STROKE))),
        )
        radius_x, radius_y, waist = SPARK_RADIUS_X, SPARK_RADIUS_Y, SPARK_WAIST
    else:
        # No frame to sit inside, so the spark takes the tile.
        radius_x, radius_y, waist = 30.0, 41.0, 9.0

    spark = spark_points(CENTRE, radius_x, radius_y, waist, SPARK_TILT_DEGREES)
    draw.polygon([(units(x), units(y)) for x, y in spark], fill=SIGNAL)

    return image.resize((size, size), Image.LANCZOS)


def main() -> None:
    assets = Path(__file__).resolve().parent.parent / "src" / "Flint.App" / "Assets"
    assets.mkdir(parents=True, exist_ok=True)

    frames = [render(size) for size in ICO_SIZES]

    # Pillow rewrites the largest frame into every requested size unless each is supplied, so the
    # per-size renders above are appended explicitly to keep the small ones frameless.
    ico_path = assets / "flint.ico"
    frames[-1].save(ico_path, format="ICO", sizes=[(s, s) for s in ICO_SIZES], append_images=frames[:-1])
    print(f"wrote {ico_path}")

    png_path = assets / "flint-icon-256.png"
    render(256).save(png_path, format="PNG")
    print(f"wrote {png_path}")


if __name__ == "__main__":
    main()
