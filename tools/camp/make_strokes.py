"""Stroke map for the camp's painterly shading.

Every surface in the camp is shaded "one brush stroke at a time": the shader looks up which
stroke a pixel belongs to and evaluates light, shadow and color at that stroke's center, so each
stroke comes out as one flat color with a crisp, angular outline.

The texture is a tileable mosaic of overlapping flat-brush strokes (the last one painted wins):
  R, G  offset from the pixel to the center of its stroke (0.5 = none, full range = +-MAX_OFF texels)
  B     a random value per stroke
  A     bristle streaks along the stroke and a slightly darker rim (0.5 = neutral)

Run: python3 tools/camp/make_strokes.py Assets/IORI/Art/Camp/Textures/iori_strokes.png [preview.png]
"""
import sys, math, random
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

N = 1024
MAX_OFF = 128.0         # texels; must match IORI_STROKE_MAX_OFF in IoriCommon.hlsl (128/1024 of the tile)
SEED = 20261113


def build(n=N, seed=SEED):
    rng = random.Random(seed)
    ids = Image.new('I', (n, n), 0)
    draw = ImageDraw.Draw(ids)
    strokes = [(0.0, 0.0, 0.0, 1.0, 1.0)]  # id 0 is never left visible

    def stroke(cx, cy, ang, length, width):
        sid = len(strokes)
        strokes.append((cx, cy, ang, length, width))
        ca, sa = math.cos(ang), math.sin(ang)
        hl, hw = length / 2, width / 2
        # a flat brush laid down once: slanted ends, one end a little narrower, a notch where the bristles split
        s1, s2 = rng.uniform(-.45, .45) * width, rng.uniform(-.45, .45) * width
        taper = rng.uniform(.72, 1.0)
        pts = [(-hl + s1, -hw), (hl + s2, -hw * taper)]
        if rng.random() < .55:
            k = rng.uniform(-.5, .5) * hw
            pts += [(hl - rng.uniform(.12, .3) * width, k)]
        pts += [(hl - s2, hw * taper), (-hl - s1, hw)]
        if rng.random() < .35:
            k = rng.uniform(-.5, .5) * hw
            pts += [(-hl + rng.uniform(.1, .25) * width, k)]
        for ox in (-n, 0, n):
            for oy in (-n, 0, n):
                x0, y0 = cx + ox, cy + oy
                if x0 < -length or x0 > n + length or y0 < -length or y0 > n + length:
                    continue
                draw.polygon([(x0 + px * ca - py * sa, y0 + px * sa + py * ca) for px, py in pts], fill=sid)

    def layer(count, lmin, lmax, wmin, wmax, base, spread, cross=0.0):
        for _ in range(count):
            a = base + rng.gauss(0, spread)
            if rng.random() < cross:
                a += math.pi / 2 + rng.gauss(0, .3)
            stroke(rng.uniform(0, n), rng.uniform(0, n), a, rng.uniform(lmin, lmax), rng.uniform(wmin, wmax))

    s = n / 1024.0
    base = math.radians(8)
    layer(700, 170 * s, 300 * s, 54 * s, 92 * s, base, .20)           # underpainting, broad
    layer(1000, 100 * s, 200 * s, 30 * s, 56 * s, base, .26, .04)     # body
    layer(420, 50 * s, 110 * s, 12 * s, 24 * s, base, .40, .08)       # a few smaller accents on top
    ids = np.array(ids, dtype=np.int32)

    # merge slivers into the neighbor so no confetti is left
    for _ in range(2):
        area = np.bincount(ids.ravel(), minlength=len(strokes))
        small = area[ids] < 220 * s * s
        if not small.any():
            break
        _, (iy, ix) = ndimage.distance_transform_edt(small, return_indices=True)
        ids = ids[iy, ix]

    ns = len(strokes)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    ang_x, ang_y = xx / n * 2 * np.pi, yy / n * 2 * np.pi
    flat = ids.ravel()

    def circ_mean(a):
        sn = np.bincount(flat, weights=np.sin(a).ravel(), minlength=ns)
        cs = np.bincount(flat, weights=np.cos(a).ravel(), minlength=ns)
        return (np.arctan2(sn, cs) % (2 * np.pi)) / (2 * np.pi) * n

    cx, cy = circ_mean(ang_x), circ_mean(ang_y)
    dx = (cx[ids] - xx + n / 2) % n - n / 2
    dy = (cy[ids] - yy + n / 2) % n - n / 2

    rs = np.random.RandomState(seed)
    rnd = rs.rand(ns)
    ang = np.array([st[2] for st in strokes])
    width = np.array([st[4] for st in strokes])
    # stroke-local coordinates (u along, v across), measured from the visible center
    ca, sa = np.cos(ang[ids]), np.sin(ang[ids])
    u = -dx * ca - dy * sa
    v = dx * sa - dy * ca
    w = np.maximum(width[ids], 1)
    # bristles: 1D value noise across the stroke, slightly sheared along it
    def vnoise(t):
        i = np.floor(t).astype(np.int64)
        f = t - i
        f = f * f * (3 - 2 * f)
        h = lambda k: ((np.sin(k * 127.1 + 311.7) * 43758.5453) % 1.0)
        return h(i) * (1 - f) + h(i + 1) * f
    q = v / w
    bristle = (vnoise(q * 9 + rnd[ids] * 91 + u * 0.004) - .5) * .55 + (vnoise(q * 23 + rnd[ids] * 37) - .5) * .3
    # rim: paint piles up thin at the edge of a stroke
    edge = np.zeros((n, n), bool)
    for sh, ax in ((1, 0), (-1, 0), (1, 1), (-1, 1)):
        edge |= ids != np.roll(ids, sh, axis=ax)
    dist = ndimage.distance_transform_edt(~edge)
    rim = np.clip(1 - dist / (2.2 * s), 0, 1)
    a = .5 + bristle * .5 - rim * .22

    out = np.zeros((n, n, 4), np.float64)
    out[..., 0] = .5 + dx / (2 * MAX_OFF * s)
    out[..., 1] = .5 + dy / (2 * MAX_OFF * s)
    out[..., 2] = rnd[ids]
    out[..., 3] = a
    clipped = float((np.abs(dx) > MAX_OFF * s).mean() + (np.abs(dy) > MAX_OFF * s).mean())
    return np.clip(out, 0, 1), ids, clipped


def preview(tex, path, scale=2):
    """A light pool on flat ground, shaded per stroke and cut into bands: what the shader will do."""
    n = tex.shape[0]
    reps = 2
    yy, xx = np.mgrid[0:n * reps, 0:n * reps]
    t = tex[yy % n, xx % n]
    offx = (t[..., 0] - .5) * 2 * MAX_OFF
    offy = (t[..., 1] - .5) * 2 * MAX_OFF
    px, py = xx + offx, yy + offy
    c = n * reps / 2
    d = np.hypot(px - c * .8, py - c * 1.1) / (n * .55)
    light = 1.6 / (1 + 6 * d * d)
    bands = 5.0
    ql = np.floor(light * bands + (t[..., 2] - .5) * .9) / bands
    amb = np.array([.09, .12, .30])
    warm = np.array([1.0, .50, .16])
    grass = np.array([.20, .38, .22]) * (1 + (t[..., 2:3] - .5) * .5) * (1 + (t[..., 3:4] - .5) * .5)
    col = grass * (amb + warm * np.clip(ql, 0, None)[..., None] * 1.2)
    col = col / (1 + col)
    img = (np.clip(col, 0, 1) ** (1 / 2.2) * 255).astype(np.uint8)
    Image.fromarray(img).resize((n * reps // scale, n * reps // scale), Image.LANCZOS).save(path)


if __name__ == '__main__':
    out_path = sys.argv[1]
    tex, ids, clipped = build()
    Image.fromarray((tex * 255 + .5).astype(np.uint8), 'RGBA').save(out_path)
    print('strokes visible:', len(np.unique(ids)), 'offset clipped fraction:', round(clipped, 4))
    if len(sys.argv) > 2:
        preview(tex, sys.argv[2])
