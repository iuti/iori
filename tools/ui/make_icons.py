"""IORI の UI アイコンのうち、リストと検索で足したもの(白、128 角、線は 16 の枠で 1.3)を描く。

使い方: python3 tools/ui/make_icons.py Assets/IORI/Resources/IoriUI/Icons
線の端は角ばらせる(butt)。ほかのアイコンと同じ太さに見えるように、16 の枠の座標で書く。
"""
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

S = 128          # できあがりの大きさ
K = 8            # 描くときの倍率(縮めてなめらかにする)
UNIT = S * K / 16.0
W = 1.3          # 線の太さ(16 の枠で)


def seg(d, a, b, w=W):
    """端を角ばらせた線 a→b。"""
    (x1, y1), (x2, y2) = a, b
    dx, dy = x2 - x1, y2 - y1
    n = math.hypot(dx, dy) or 1.0
    ox, oy = -dy / n * w / 2, dx / n * w / 2
    pts = [(x1 + ox, y1 + oy), (x2 + ox, y2 + oy), (x2 - ox, y2 - oy), (x1 - ox, y1 - oy)]
    d.polygon([(x * UNIT, y * UNIT) for x, y in pts], fill=255)


def poly(d, pts, w=W):
    """折れ線。角はとがらせずに継ぐ(継ぎ目に線の太さの四角を置く)。"""
    for a, b in zip(pts, pts[1:]):
        seg(d, a, b, w)
    for p in pts[1:-1]:
        r = w / 2
        d.ellipse([(p[0] - r) * UNIT, (p[1] - r) * UNIT, (p[0] + r) * UNIT, (p[1] + r) * UNIT], fill=255)


def rect(d, x, y, w, h):
    d.rectangle([x * UNIT, y * UNIT, (x + w) * UNIT, (y + h) * UNIT], fill=255)


def plus(d):
    seg(d, (8, 2.5), (8, 13.5))
    seg(d, (2.5, 8), (13.5, 8))


def minus(d):
    seg(d, (3, 8), (13, 8))


def close(d):
    seg(d, (3.5, 3.5), (12.5, 12.5))
    seg(d, (12.5, 3.5), (3.5, 12.5))


def check(d):
    poly(d, [(2.6, 8.4), (6.2, 12), (13.4, 4.4)])


def grip(d):
    for y in (5.5, 8, 10.5):
        seg(d, (3.5, y), (12.5, y), 1.1)


def pencil(d):
    # 斜めの軸(左下が先)
    a, b = (3.2, 12.8), (11.4, 4.6)
    ux, uy = (b[0] - a[0]), (b[1] - a[1])
    n = math.hypot(ux, uy)
    ux, uy = ux / n, uy / n
    px, py = -uy * 1.5, ux * 1.5
    tip = (a[0] - ux * 0.6, a[1] - uy * 0.6)
    top = (b[0] + ux * 1.6, b[1] + uy * 1.6)
    l1, l2 = (a[0] + px, a[1] + py), (a[0] - px, a[1] - py)
    t1, t2 = (top[0] + px, top[1] + py), (top[0] - px, top[1] - py)
    poly(d, [tip, l1, t1, t2, l2, tip])
    # 先の手前の区切り
    c = (b[0] - ux * 0.6, b[1] - uy * 0.6)
    seg(d, (c[0] + px, c[1] + py), (c[0] - px, c[1] - py))


def trash(d):
    seg(d, (2.5, 4.2), (13.5, 4.2))
    poly(d, [(6, 4.2), (6, 2.4), (10, 2.4), (10, 4.2)])
    poly(d, [(4, 4.2), (4.8, 13.6), (11.2, 13.6), (12, 4.2)])
    seg(d, (6.9, 6.6), (7.1, 11.4), 1.0)
    seg(d, (9.1, 6.6), (8.9, 11.4), 1.0)


ICONS = {"plus": plus, "minus": minus, "close": close, "check": check, "grip": grip, "pencil": pencil, "trash": trash}


def main(out):
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    for name, draw in ICONS.items():
        mask = Image.new("L", (S * K, S * K), 0)
        draw(ImageDraw.Draw(mask))
        mask = mask.resize((S, S), Image.LANCZOS)
        img = Image.new("RGBA", (S, S), (255, 255, 255, 0))
        img.putalpha(mask)
        img.save(out / f"{name}.png")
        print(out / f"{name}.png")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "Assets/IORI/Resources/IoriUI/Icons")
