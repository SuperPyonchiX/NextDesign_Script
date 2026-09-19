"""Generate the two ribbon additions in the existing rounded-square icon style.

Run with Python + Pillow. Existing icons are left untouched.
"""
from pathlib import Path
from PIL import Image, ImageDraw


def render(kind, size):
    scale = 8
    unit = size * scale / 32
    image = Image.new('RGBA', (size * scale, size * scale))
    draw = ImageDraw.Draw(image)

    def coords(values):
        return tuple(v * unit for v in values)

    def line(points, width=2):
        draw.line([coords(p) for p in points], fill='white', width=round(width * unit), joint='curve')

    color = {'change': '#7860AA', 'history': '#397F91'}[kind]
    draw.rounded_rectangle(coords((1, 1, 31, 31)), radius=6 * unit, fill=color)
    if kind == 'change':
        # Two document columns with minus and plus: compare before/after.
        draw.rounded_rectangle(coords((6, 7, 14, 25)), radius=1.5 * unit, outline='white', width=round(1.6 * unit))
        draw.rounded_rectangle(coords((18, 7, 26, 25)), radius=1.5 * unit, outline='white', width=round(1.6 * unit))
        line([(8, 16), (12, 16)], 1.6)
        line([(20, 16), (24, 16)], 1.6)
        line([(22, 13.5), (22, 18.5)], 1.6)
    else:
        # Clock with a return arrow: inspect a previous revision.
        draw.arc(coords((7, 7, 26, 26)), start=205, end=510, fill='white', width=round(2.3 * unit))
        line([(6, 7), (6, 13), (12, 13)], 2.2)
        line([(16, 11), (16, 17), (21, 20)], 2)
    return image.resize((size, size), Image.Resampling.LANCZOS)


if __name__ == '__main__':
    root = Path(__file__).resolve().parent
    for kind in ('change', 'history'):
        for size in (16, 32):
            render(kind, size).save(root / f'{kind}{size}.png')
