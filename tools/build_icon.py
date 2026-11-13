"""Rebuild the original 모두찾아줘 icon. Build-time dependency: Pillow only.

The SVG and raster outputs share the exact same primitive geometry. No external
images, font files, tracing, or application-runtime dependencies are used.
Run: python document-finder/tools/build_icon.py
"""
from pathlib import Path
from html import escape
from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parents[1] / "assets"
SIZES = (16, 24, 32, 48, 64, 128, 256)
COLORS = {
    "blue": "#1557C8", "navy": "#07336F", "cyan": "#6DE0EF",
    "back": "#8FC9FA", "middle": "#C4E8FF", "paper": "#FFFFFF",
}
# Coordinates are in the same 512 x 512 square in every output.
SHAPES = [
    ("rect", (24, 24, 488, 488), 112, "blue"),
    ("rect", (130, 88, 330, 352), 28, "back"),
    ("rect", (100, 110, 300, 374), 28, "middle"),
    ("rect", (70, 132, 270, 396), 28, "paper"),
    ("line", (112, 192, 218, 192), 18, "blue"),
    ("line", (112, 238, 184, 238), 18, "blue"),
    ("line", (350, 348, 414, 412), 58, "navy"),
    ("line", (350, 348, 414, 412), 30, "cyan"),
    ("circle", (214, 202, 410, 398), 0, "navy"),
    ("circle", (238, 226, 386, 374), 0, "cyan"),
]


def svg_text():
    parts = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-labelledby="title desc">',
             '<title id="title">모두찾아줘</title>',
             '<desc id="desc">Three stacked documents and a cyan magnifying glass on a blue square.</desc>']
    for kind, box, width, color in SHAPES:
        x1, y1, x2, y2 = box
        fill = escape(COLORS[color])
        if kind == "rect":
            parts.append(f'<rect x="{x1}" y="{y1}" width="{x2-x1}" height="{y2-y1}" rx="{width}" fill="{fill}"/>')
        elif kind == "circle":
            parts.append(f'<circle cx="{(x1+x2)//2}" cy="{(y1+y2)//2}" r="{(x2-x1)//2}" fill="{fill}"/>')
        else:
            parts.append(f'<path d="M{x1} {y1}L{x2} {y2}" fill="none" stroke="{fill}" stroke-width="{width}" stroke-linecap="round"/>')
    return "\n".join(parts + ["</svg>", ""])


def render(size):
    scale = max(4, 2048 // size)
    canvas = Image.new("RGBA", (size * scale, size * scale))
    draw = ImageDraw.Draw(canvas)
    factor = size * scale / 512
    for kind, box, width, color in SHAPES:
        coords = tuple(round(n * factor) for n in box)
        color = COLORS[color]
        if kind == "rect":
            draw.rounded_rectangle(coords, radius=round(width * factor), fill=color)
        elif kind == "circle":
            draw.ellipse(coords, fill=color)
        else:
            stroke = round(width * factor)
            draw.line(coords, fill=color, width=stroke)
            for x, y in (coords[:2], coords[2:]):
                r = stroke / 2
                draw.ellipse((x-r, y-r, x+r, y+r), fill=color)
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    (ASSETS / "modufind.svg").write_text(svg_text(), encoding="utf-8")
    master = render(1024)
    master.save(ASSETS / "modufind.png")
    variants = [render(s) for s in SIZES]
    master.save(ASSETS / "modufind.ico", format="ICO", sizes=[(s, s) for s in SIZES], append_images=variants)
    for size, icon in zip(SIZES, variants):
        icon.save(ASSETS / f"modufind-{size}.png")
    with Image.open(ASSETS / "modufind.ico") as icon:
        assert icon.ico.sizes() == {(s, s) for s in SIZES}
    print(f"Built SVG, 1024px PNG, 7 PNG variants, and 7-resolution ICO in {ASSETS}")


if __name__ == "__main__":
    main()
