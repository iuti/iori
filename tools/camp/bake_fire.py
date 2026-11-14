#!/usr/bin/env python3
"""Bakes the campfire's flame into a flip book for phones.

The flame of IoriFire.shader is a volume: every pixel walks a ray through it, which a mid-range
phone cannot afford. This script does the same arithmetic once, here, for every frame of a loop
(with many more steps than the shader takes), and writes the result as one picture with all the
frames side by side. IoriFireCard.shader then shows it on a card that turns toward the camera.

What is stored is not the flame's colours but its heat, so the card's shader paints the same bands
of colour with the same crisp outline at any size, and the colours stay adjustable in the material:
  R  square root of the hottest tongue's heat at that pixel
  G  how much a cooler tongue in front dims what burns behind it (0 none, 1 = two tongues deep)
To make the loop close, the rhythms of the tongues and the rising of the noise are nudged to whole
numbers of cycles per loop (by a few percent at most).

  python3 tools/camp/bake_fire.py                 writes Assets/IORI/Resources/IoriCamp/fire_flipbook.png
  python3 tools/camp/bake_fire.py --preview DIR   also writes pictures of the painted result to look at

Keep the numbers below the same as the shader's (IoriFire.shader and the CampFlame material).
"""
import argparse
import math
import os
import re
import sys

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
NOISE_ASSET = os.path.join(ROOT, "Assets/IORI/Art/Camp/Textures/iori_noise3d.asset")
OUT = os.path.join(ROOT, "Assets/IORI/Resources/IoriCamp/fire_flipbook.png")

# ---- the flame (IoriFire.shader)
RISE, STRETCH, SWAY, LEAN, CURL, BREATH, BITE, COOL = 1.5, 0.28, 0.7, 0.45, 0.16, 0.18, 1.4, 0.5
TONGUES = np.array([
    (0.00, 0.02, 1.00, 0.30),
    (0.13, 0.04, 0.74, 0.21),
    (-0.13, -0.03, 0.66, 0.20),
    (0.02, 0.12, 0.56, 0.18),
    (-0.06, -0.13, 0.50, 0.17),
    (0.18, -0.08, 0.42, 0.15),
], dtype=np.float32)
PHASES = np.array([0.0, 1.7, 3.1, 4.4, 5.3, 2.4], dtype=np.float32)
CORE, MID, OUTER, RIM = (1.0, 0.94, 0.67), (1.0, 0.77, 0.27), (0.98, 0.56, 0.15), (0.84, 0.36, 0.12)
BRIGHT = (1.3, 1.2, 1.1, 1.0)
BANDS = (0.68, 0.38, 0.16, 0.08)
SOFT, INNER = 0.05, 0.28

# ---- the flip book (the same numbers are the defaults of _Grid and _Rect in IoriFireCard.shader)
LOOP_SECONDS = 6.0
FRAMES = 120
COLUMNS = 12
FRAME_W, FRAME_H = 144, 204
# the part of the flame's own space a frame shows, in units of the tallest tongue: x from -X_HALF to X_HALF, y from Y_MIN to Y_MAX
X_HALF, Y_MIN, Y_MAX = 0.58, -0.06, 1.60
VIEW_DOWN = math.radians(11.0)   # the camera looks down on the fire by about this much
STEPS = 56
DEPTH = 1.3                      # the ray runs this far before and behind the fire's middle


def load_noise():
    text = open(NOISE_ASSET, encoding="utf-8").read()
    n = int(re.search(r"m_Width: (\d+)", text).group(1))
    data = bytes.fromhex(re.search(r"_typelessdata: ([0-9a-f]+)", text).group(1))
    a = np.frombuffer(data, dtype=np.uint8).astype(np.float32) / 255.0
    return a.reshape(n, n, n, 2)     # [z, y, x, channel]


def sample(noise, p):
    """Trilinear, repeating: p[..., 3] in texture coordinates (x, y, z)."""
    n = noise.shape[0]
    f = p * n - 0.5
    i0 = np.floor(f).astype(np.int32)
    w = (f - i0).astype(np.float32)
    i1 = (i0 + 1) % n
    i0 = i0 % n
    x0, y0, z0 = i0[..., 0], i0[..., 1], i0[..., 2]
    x1, y1, z1 = i1[..., 0], i1[..., 1], i1[..., 2]
    wx, wy, wz = w[..., 0:1], w[..., 1:2], w[..., 2:3]
    c00 = noise[z0, y0, x0] * (1 - wx) + noise[z0, y0, x1] * wx
    c10 = noise[z0, y1, x0] * (1 - wx) + noise[z0, y1, x1] * wx
    c01 = noise[z1, y0, x0] * (1 - wx) + noise[z1, y0, x1] * wx
    c11 = noise[z1, y1, x0] * (1 - wx) + noise[z1, y1, x1] * wx
    return (c00 * (1 - wy) + c10 * wy) * (1 - wz) + (c01 * (1 - wy) + c11 * wy) * wz


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def whole(cycles_per_second):
    """The nearest rate that makes a whole number of cycles in one loop."""
    return max(1.0, round(cycles_per_second * LOOP_SECONDS)) / LOOP_SECONDS


# the rising of the three looks at the noise, in repeats of the texture per second (see FlameNoise)
RATE1 = whole(RISE * STRETCH * 2.0)
RATE2 = whole(RISE * 1.7 * STRETCH * 4.2)
RATE3 = whole(RISE * STRETCH * 0.8)
TWO_PI = 2.0 * math.pi
SWING_X = np.array([whole((1.6 + 0.21 * ph) / TWO_PI) * TWO_PI for ph in PHASES], dtype=np.float32)
SWING_Z = np.array([whole((1.3 + 0.17 * ph) / TWO_PI) * TWO_PI for ph in PHASES], dtype=np.float32)
BREATHE = np.array([whole((2.1 + 0.37 * ph) / TWO_PI) * TWO_PI for ph in PHASES], dtype=np.float32)


def flame_noise(noise, p, t):
    """x = how much is bitten away, yz = sideways wavering (FlameNoise in the shader)."""
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    q1 = np.stack([x * 2.0, y * STRETCH * 2.0 - t * RATE1, z * 2.0], -1)
    q2 = np.stack([x * 4.2 + 0.3, y * STRETCH * 4.2 - t * RATE2 + 0.3, z * 4.2 + 0.3], -1)
    q3 = np.stack([x * 0.8 + 0.37, y * STRETCH * 0.8 - t * RATE3 + 0.37, z * 0.8 + 0.37], -1)
    n1 = sample(noise, q1)[..., 0]
    n2 = sample(noise, q2)[..., 1]
    s = sample(noise, q3)
    return (n1 + n2 * 0.35) / 1.35, s[..., 1] - 0.5, s[..., 0] - 0.5


def tongue_heat(c, now, p, bite, sw_x, sw_z):
    h = np.clip(p[..., 1] / now[2], 0.0, 1.3)
    dx = p[..., 0] - c[0] * (1.0 + LEAN * h) + sw_x * (SWAY * h) + now[0] * (h * h)
    dz = p[..., 2] - c[1] * (1.0 + LEAN * h) + sw_z * (SWAY * h) + now[1] * (h * h)
    r = c[3] * np.clip(1.0 - h / 1.15, 0.0, 1.0) * (0.55 + 0.45 * smoothstep(0.0, 0.14, h)) + 1e-4
    e = 1.0 - np.sqrt(dx * dx + dz * dz) / r - (bite - 0.30) * (0.9 + BITE * h) - COOL * h
    return np.clip(e * 1.15, 0.0, 1.0) * smoothstep(-0.03, 0.05, p[..., 1])


def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


CORE_L, MID_L, OUTER_L, RIM_L = (srgb_to_linear(c) for c in (CORE, MID, OUTER, RIM))
THROUGH = np.array([1.0, 1.0 - 0.6 * INNER, 1.0 - 1.6 * INNER], dtype=np.float32)


def bands(heat):
    """The colour of one tongue at a heat (linear light, before the fire's brightness)."""
    h = heat[..., None]
    c = np.broadcast_to(RIM_L * BRIGHT[3], h.shape[:-1] + (3,)).astype(np.float32)
    c = c + (OUTER_L * BRIGHT[2] - c) * smoothstep(BANDS[2] - SOFT, BANDS[2] + SOFT, h)
    c = c + (MID_L * BRIGHT[1] - c) * smoothstep(BANDS[1] - SOFT, BANDS[1] + SOFT, h)
    c = c + (CORE_L * BRIGHT[0] - c) * smoothstep(BANDS[0] - SOFT, BANDS[0] + SOFT, h)
    return c


def render(noise, t, width, height):
    """One frame, as the shader paints it: (colour premultiplied, alpha, hottest heat)."""
    u = (np.arange(width, dtype=np.float32) + 0.5) / width * (2 * X_HALF) - X_HALF
    v = Y_MAX - (np.arange(height, dtype=np.float32) + 0.5) / height * (Y_MAX - Y_MIN)
    u, v = np.meshgrid(u, v)
    sa, ca = math.sin(VIEW_DOWN), math.cos(VIEW_DOWN)
    rd = np.array([0.0, -sa, ca], dtype=np.float32)
    origin = np.stack([u, v * ca, v * sa], -1)           # on the plane through the fire's foot, facing the camera

    count = len(TONGUES)
    now = np.stack([np.sin(t * SWING_X + PHASES) * CURL, np.cos(t * SWING_Z + PHASES * 1.7) * CURL,
                    TONGUES[:, 2] * (1.0 + BREATH * np.sin(t * BREATHE + PHASES * 2.0))], -1)
    best = np.zeros((count, height, width), dtype=np.float32)
    best_at = np.full((count, height, width), -DEPTH, dtype=np.float32)
    best_nz = np.zeros((count, height, width, 3), dtype=np.float32)
    best_nz[..., 0] = 0.5
    dt = 2 * DEPTH / STEPS
    for s in range(STEPS):
        ts = -DEPTH + (s + 0.5) * dt
        p = origin + rd * ts
        bite, sw_x, sw_z = flame_noise(noise, p, t)
        nz = np.stack([bite, sw_x, sw_z], -1)
        for k in range(count):
            hk = tongue_heat(TONGUES[k], now[k], p, bite, sw_x, sw_z)
            better = hk > best[k]
            best[k] = np.where(better, hk, best[k])
            best_at[k] = np.where(better, ts, best_at[k])
            best_nz[k] = np.where(better[..., None], nz, best_nz[k])
    for k in range(count):
        reach = dt * 0.5
        for _ in range(3):
            ta, tb = np.maximum(best_at[k] - reach, -DEPTH), np.minimum(best_at[k] + reach, DEPTH)
            ha = tongue_heat(TONGUES[k], now[k], origin + rd * ta[..., None], best_nz[k][..., 0], best_nz[k][..., 1], best_nz[k][..., 2])
            hb = tongue_heat(TONGUES[k], now[k], origin + rd * tb[..., None], best_nz[k][..., 0], best_nz[k][..., 1], best_nz[k][..., 2])
            use_a = (ha > best[k]) & (ha >= hb)
            use_b = ~use_a & (hb > best[k])
            best[k] = np.where(use_a, ha, np.where(use_b, hb, best[k]))
            best_at[k] = np.where(use_a, ta, np.where(use_b, tb, best_at[k]))
            reach *= 0.5

    # far tongues first, one painted over the other
    order = np.argsort(-best_at, axis=0, kind="stable")
    heat_sorted = np.take_along_axis(best, order, axis=0)
    col = np.zeros((height, width, 3), dtype=np.float32)
    alpha = np.zeros((height, width), dtype=np.float32)
    for k in range(count):
        heat = heat_sorted[k]
        c = bands(heat)
        c = c + (np.maximum(c, col * THROUGH) - c) * alpha[..., None]
        a2 = smoothstep(BANDS[3] - 0.012, BANDS[3] + 0.012, heat)
        col = col + (c - col) * a2[..., None]
        alpha = alpha + (1.0 - alpha) * a2
    return col, alpha, best.max(axis=0)


def encode(col, alpha, heat):
    """What the flip book keeps of a frame: (square root of the heat, how deep the dimming is)."""
    base = bands(heat)
    # the green of what was painted against the green of the hottest tongue alone says how many tongues deep it is dimmed
    ratio = np.clip(col[..., 1] / np.maximum(alpha, 1e-4) / np.maximum(base[..., 1], 1e-4), 1e-3, 1.0)
    depth = np.clip(np.log(ratio) / math.log(float(THROUGH[1])), 0.0, 2.0)
    depth = np.where(alpha > 0.5, depth, 0.0)
    return np.sqrt(np.clip(heat, 0.0, 1.0)), depth * 0.5


def paint(r, g):
    """What IoriFireCard.shader makes of the two stored values (to look at, and to compare with the real thing)."""
    heat = r * r
    c = bands(heat) * THROUGH ** (g[..., None] * 2.0)
    a = smoothstep(BANDS[3] - 0.012, BANDS[3] + 0.012, heat)
    return c * a[..., None], a


def over_dark(col, alpha, back=(0.035, 0.04, 0.07)):
    out = col + np.asarray(back, dtype=np.float32) * (1.0 - alpha[..., None])
    return (linear_to_srgb(out / 1.0) * 255 + 0.5).astype(np.uint8)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", help="folder for pictures to look at")
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--frames", type=int, default=FRAMES, help="fewer frames for a quick look (the loop then skips)")
    args = ap.parse_args()
    noise = load_noise()
    rows = (FRAMES + COLUMNS - 1) // COLUMNS
    atlas = np.zeros((rows * FRAME_H, COLUMNS * FRAME_W, 3), dtype=np.uint8)
    extent = [1e9, -1e9, 1e9, -1e9]
    worst = 0.0
    sheets = []
    step = max(1, FRAMES // args.frames)
    for f in range(0, FRAMES, step):
        t = f / FRAMES * LOOP_SECONDS
        col, alpha, heat = render(noise, t, FRAME_W, FRAME_H)
        r, g = encode(col, alpha, heat)
        x, y = (f % COLUMNS) * FRAME_W, (f // COLUMNS) * FRAME_H
        atlas[y:y + FRAME_H, x:x + FRAME_W, 0] = (r * 255 + 0.5).astype(np.uint8)
        atlas[y:y + FRAME_H, x:x + FRAME_W, 1] = (g * 255 + 0.5).astype(np.uint8)
        ys, xs = np.nonzero(heat > 0.04)
        if len(xs):
            extent = [min(extent[0], xs.min()), max(extent[1], xs.max()), min(extent[2], ys.min()), max(extent[3], ys.max())]
        if args.preview:
            back_col, back_alpha = paint(atlas[y:y + FRAME_H, x:x + FRAME_W, 0] / 255.0, atlas[y:y + FRAME_H, x:x + FRAME_W, 1] / 255.0)
            worst = max(worst, float(np.abs(back_col - col).max()))
            if f % (FRAMES // 8) == 0:
                sheets.append(np.concatenate([over_dark(col, alpha), over_dark(back_col, back_alpha)], axis=0))
        print(f"frame {f + 1}/{FRAMES}", end="\r", file=sys.stderr)
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    Image.fromarray(atlas, "RGB").save(args.out, optimize=True)
    print(f"\nwrote {args.out}: {atlas.shape[1]}x{atlas.shape[0]}, {FRAMES} frames of {FRAME_W}x{FRAME_H}, {COLUMNS} columns, {rows} rows")
    print(f"the flame reaches x {extent[0]}..{extent[1]} of {FRAME_W}, y {extent[2]}..{extent[3]} of {FRAME_H} (must stay inside the frame)")
    if args.preview:
        os.makedirs(args.preview, exist_ok=True)
        Image.fromarray(np.concatenate(sheets, axis=1), "RGB").save(os.path.join(args.preview, "fire_frames.png"))
        print(f"largest difference between the shader's painting and the card's: {worst:.3f} (top row: the volume, bottom row: the card)")


if __name__ == "__main__":
    main()
