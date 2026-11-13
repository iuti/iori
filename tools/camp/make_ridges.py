"""Outlines of the mountains across the lake, and where the town's lights are.

The far view is painted by the sky shader (IoriSky.shader). Its mountains are a deliberate
composition, not noise: this file holds their outlines as lists of corner points (direction in
degrees, 0 = straight across the lake, + = to the right; height in degrees above the horizon)
and writes them into a small texture the shader reads.

  row 0: R,G = height of the near headlands (16 bit, R high byte)   B,A = height of the main range
  row 1: R,G = height of the far range                              B = how many town lights, A = how high they climb
  row 2: R, G, B = which way the main range, the far range and the near headlands climb, taken over
         about a degree (0.5 = level, above = climbing to the right; SLOPE_RANGE at 0 and 1).
         The shader lights the mountains' faces by it: big planes that follow the drawn shapes.
  row 3: unused

Heights are stored as height / MAX_DEG. One texel is 360 / WIDTH degrees; the shader blends
between texels itself, so the outlines stay straight lines with sharp corners.

Run: python3 tools/camp/make_ridges.py Assets/IORI/Art/Camp/Textures/iori_ridges.png [preview.png]
"""
import sys, random
import numpy as np
from PIL import Image

WIDTH = 2048
MAX_DEG = 16.0          # must match IORI_RIDGE_MAX_DEG in IoriFar.hlsl
SLOPE_RANGE = 2.0       # must match IORI_RIDGE_SLOPE in IoriFar.hlsl (degrees of height per degree to the right)
SLOPE_OVER = 0.45       # the faces follow the outline smoothed by this much (degrees)
SEED = 20261113

# ---------------------------------------------------------------- the composition
# The portrait picture shows about -10..+10 degrees; landscape and looking around show more.

# The main range: one big, flat-topped mass left of the middle with a stepped shoulder falling to
# the right, a low saddle, and a second hill beyond the right edge of the portrait picture.
MAIN = [
    (-60, 2.4), (-53, 3.7), (-48.5, 3.5), (-46, 2.6), (-41, 4.5), (-37.5, 4.3), (-35.5, 3.0), (-30, 2.5),
    (-26.5, 2.2), (-24, 3.3), (-21.5, 3.1), (-20, 2.4), (-16.5, 2.9), (-14.2, 2.5), (-12.4, 3.5),
    (-10.5, 3.3), (-9.5, 4.5), (-8.4, 4.75), (-7.5, 6.05), (-6.3, 6.3), (-4.9, 6.9), (-3.4, 6.75),
    (-2.7, 6.25), (-2.15, 5.45), (-1.5, 5.8), (-0.55, 5.65), (0.05, 4.6), (0.9, 4.3), (1.7, 4.4),
    (2.4, 3.4), (3.4, 2.85), (4.6, 2.2), (6.0, 1.8), (7.4, 1.5), (8.8, 1.35), (10.4, 1.5), (11.6, 2.5),
    (12.6, 2.9), (13.7, 4.0), (15.6, 4.3), (17.0, 3.7), (18.2, 3.9), (19.6, 2.8), (22, 2.2), (25, 2.5),
    (27.5, 3.8), (31, 4.1), (33, 3.2), (38, 2.4), (42, 2.2), (45, 3.2), (49, 3.0), (52, 4.0), (56, 3.9), (60, 2.8),
]
# The far range: pale, almost the color of the sky; pointed peaks that show left of the big mass and through the saddle.
FAR = [
    (-60, 3.0), (-54, 4.9), (-50, 4.2), (-46, 5.3), (-42, 3.8), (-37, 4.1), (-34, 5.4), (-30.5, 4.4),
    (-27, 4.7), (-24, 5.9), (-21, 4.6), (-18.5, 4.9), (-17.0, 4.3), (-15.4, 5.7), (-14.3, 5.3),
    (-13.0, 6.3), (-11.9, 5.3), (-10.7, 5.6), (-9.2, 4.4), (-3, 3.4), (1.2, 3.0), (2.4, 3.9),
    (3.2, 3.75), (4.5, 5.1), (5.3, 4.55), (6.3, 5.4), (7.2, 4.4), (8.3, 4.65), (9.7, 3.6), (11.2, 3.9),
    (12.8, 3.0), (16, 3.4), (19.5, 4.9), (22, 4.5), (24.5, 5.6), (28, 4.2), (32, 4.5), (35, 5.5),
    (39, 4.3), (44, 4.6), (48, 5.6), (52, 4.4), (56, 4.8), (60, 3.5),
]
# The near headlands: dark land that reaches into the lake from both sides; open water in the middle.
NEAR = [
    (-60, 2.0), (-48, 2.6), (-40, 2.1), (-33, 2.7), (-27, 2.3), (-22, 2.6), (-18, 2.1), (-14.5, 2.3),
    (-12, 1.8), (-10.2, 1.9), (-8.6, 1.3), (-7.2, 0.9), (-6.0, 0.4), (-5.0, 0.0), (6.4, 0.0),
    (7.6, 0.45), (8.9, 1.0), (10.4, 1.25), (12.5, 1.7), (15, 1.5), (18, 2.1), (22, 1.9), (27, 2.5),
    (34, 2.1), (42, 2.8), (51, 2.2), (60, 2.5),
]
# Town lights: (direction, how many 0..1, how high they climb in degrees). Thick on the shoulder
# of the big mass and along the shore under the saddle; a few elsewhere.
TOWN = [
    (-60, 0.10, 0.6), (-30, 0.15, 0.8), (-20, 0.35, 1.2), (-14, 0.15, 0.8), (-9.5, 0.25, 0.7),
    (-7.0, 0.5, 1.3), (-5.0, 0.35, 1.0), (-3.0, 0.3, 1.1), (-1.2, 0.55, 2.0), (0.4, 0.85, 3.2),
    (1.8, 1.0, 3.0), (3.0, 0.9, 2.1), (4.4, 0.8, 1.5), (6.0, 0.95, 1.15), (7.6, 0.7, 0.9),
    (9.4, 0.4, 0.8), (11.5, 0.3, 1.0), (13.5, 0.7, 2.0), (15.5, 0.6, 1.6), (18, 0.25, 0.9),
    (24, 0.4, 1.3), (32, 0.2, 0.8), (45, 0.3, 1.0), (60, 0.10, 0.6),
]


def roughen(points, rng, amount, rounds=2):
    """Breaks every straight run into shorter ones with small random corners (still straight lines)."""
    pts = list(points)
    for _ in range(rounds):
        out = [pts[0]]
        for (a0, h0), (a1, h1) in zip(pts, pts[1:]):
            if a1 - a0 > 0.5:
                t = rng.uniform(0.35, 0.65)
                span = min(a1 - a0, 4.0)
                h = h0 + (h1 - h0) * t + rng.uniform(-1, 1) * amount * span * (0.25 if min(h0, h1) < 0.05 else 1.0)
                out.append((a0 + (a1 - a0) * t, max(h, 0.0)))
            out.append((a1, h1))
        pts = out
        amount *= 0.55
    return pts


def beyond(points, rng, lo, hi, step):
    """Carries an outline on around the rest of the circle (only seen when the camera turns far)."""
    pts = list(points)
    a, h = pts[-1]
    first_a, first_h = pts[0]
    end = first_a + 360.0
    while a + step < end - step:
        a += rng.uniform(0.6, 1.4) * step
        h = min(max(h + rng.uniform(-1, 1) * (hi - lo) * 0.45, lo), hi)
        pts.append((a, h))
    pts.append((end, first_h))
    return pts


def sample(points, az):
    """Height at each direction in az (degrees, any range), straight lines between the points."""
    xs = np.array([p[0] for p in points])
    cols = [np.array([p[k] for p in points]) for k in range(1, len(points[0]))]
    a = (az - xs[0]) % 360.0 + xs[0]
    return [np.interp(a, xs, c) for c in cols]


def build():
    rng = random.Random(SEED)
    main = beyond(roughen(MAIN, rng, 0.07), rng, 2.0, 4.6, 7.0)
    far = beyond(roughen(FAR, rng, 0.07), rng, 3.0, 5.4, 9.0)
    near = beyond(roughen(NEAR, rng, 0.06), rng, 1.8, 2.9, 8.0)
    town = list(TOWN) + [(TOWN[0][0] + 360.0,) + TOWN[0][1:]]
    az = (np.arange(WIDTH) + 0.5) / WIDTH * 360.0 - 180.0
    h_main, = sample(main, az)
    h_far, = sample(far, az)
    h_near, = sample(near, az)
    t_amount, t_climb = sample(town, az)
    return az, h_near, h_main, h_far, t_amount, t_climb


def lean(h):
    """Which way an outline climbs, with the small notches smoothed away (degrees per degree)."""
    step = 360.0 / WIDTH
    sigma = SLOPE_OVER / step
    r = int(sigma * 4) + 1
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k /= k.sum()
    sm = np.convolve(np.concatenate([h[-r:], h, h[:r]]), k, mode='valid')
    return (np.roll(sm, -1) - np.roll(sm, 1)) / (2 * step)


def pack16(h):
    v = np.clip(np.round(h / MAX_DEG * 65535.0), 0, 65535).astype(np.int64)
    return (v >> 8).astype(np.uint8), (v & 255).astype(np.uint8)


def write(path):
    az, h_near, h_main, h_far, t_amount, t_climb = build()
    img = np.zeros((4, WIDTH, 4), np.uint8)
    img[0, :, 0], img[0, :, 1] = pack16(h_near)
    img[0, :, 2], img[0, :, 3] = pack16(h_main)
    img[1, :, 0], img[1, :, 1] = pack16(h_far)
    img[1, :, 2] = np.clip(np.round(t_amount * 255), 0, 255)
    img[1, :, 3] = np.clip(np.round(t_climb / MAX_DEG * 255 * 4), 0, 255)    # lights never climb past MAX_DEG / 4
    for ch, h in enumerate((h_main, h_far, h_near)):
        img[2, :, ch] = np.clip(np.round((lean(h) / SLOPE_RANGE * 0.5 + 0.5) * 255), 0, 255)
    img[2, :, 3] = 255
    img[3] = img[2]
    # Unity reads images bottom row first: row 0 of the texture is the last row of the file
    Image.fromarray(img[::-1].copy(), 'RGBA').save(path)
    return az, h_near, h_main, h_far, t_amount, t_climb


# ---------------------------------------------------------------- preview (what the picture's far view will be)
def srgb(hexs):
    v = int(hexs.lstrip('#'), 16)
    return np.array([(v >> 16) & 255, (v >> 8) & 255, v & 255], np.float64) / 255.0


def preview(data, path, w=720, h=1600, fov=42.0, pitch=-6.47, yaw=0.0, light_az=-22.0,
            strokes='Assets/IORI/Art/Camp/Textures/iori_strokes.png', tiles=5.0, facet_lean=0.7):
    """A rough picture of the far view at dusk, with the same rules IoriSky.shader uses for the
    mountains' faces, so a composition can be judged without Unity."""
    az, h_near, h_main, h_far, t_amount, t_climb = data
    rng = np.random.RandomState(3)
    t = np.tan(np.radians(fov / 2))
    ys, xs = np.mgrid[0:h, 0:w]
    px = (xs + 0.5 - w / 2) / (h / 2) * t
    py = -(ys + 0.5 - h / 2) / (h / 2) * t
    cp, sp = np.cos(np.radians(pitch)), np.sin(np.radians(pitch))
    dx, dy, dz = px, py * cp + sp, -py * sp + cp
    cy_, sy_ = np.cos(np.radians(yaw)), np.sin(np.radians(yaw))
    dx, dz = dx * cy_ + dz * sy_, -dx * sy_ + dz * cy_
    n = np.sqrt(dx * dx + dy * dy + dz * dz)
    el = np.degrees(np.arcsin(dy / n))
    a = np.degrees(np.arctan2(dx, dz))
    water = el < 0
    el_m = np.abs(el)      # the lake mirrors the far view

    def look(arr, at):
        idx = ((at + 180.0) / 360.0 * WIDTH - 0.5)
        i0 = np.floor(idx).astype(int) % WIDTH
        f = idx - np.floor(idx)
        return arr[i0] * (1 - f) + arr[(i0 + 1) % WIDTH] * f
    L = lambda arr: look(arr, a)
    top_near, top_main, top_far = L(h_near), L(h_main), L(h_far)

    # which stroke each pixel belongs to, as the shader looks it up
    try:
        st = np.asarray(Image.open(strokes).convert('RGBA'), np.float64) / 255.0
        u = (np.radians(a) * tiles % 1.0 * st.shape[1]).astype(int) % st.shape[1]
        v = (np.radians(el_m) * tiles % 1.0 * st.shape[0]).astype(int) % st.shape[0]
        sm = st[st.shape[0] - 1 - v, u]
        rnd = sm[..., 2]
        el_c = el_m + (0.5 - sm[..., 1]) * 0.25 / tiles / np.radians(1.0)
    except OSError:
        rnd, el_c = rng.rand(h, w), el_m

    horizon, mid, zenith = srgb('#eaa0d6'), srgb('#a58ae8'), srgb('#4a6cdc')
    far_near, far_far, mist = srgb('#2a2c80'), srgb('#7f8ee6'), srgb('#a8a0ee')
    hh = np.clip(np.sin(np.radians(el_m)), 0, 1)[..., None]
    ss = lambda e0, e1, x: np.clip((x - e0) / (e1 - e0), 0, 1) ** 2 * (3 - 2 * np.clip((x - e0) / (e1 - e0), 0, 1))
    col = horizon + (mid - horizon) * ss(0.0, 0.22, hh)
    col = col + (zenith - col) * ss(0.14, 0.62, hh)
    behind = col.copy()
    side = np.sign(a - light_az)
    for hgt, top, depth in ((h_far, top_far, 1.0), (h_main, top_main, 0.5), (h_near, top_near, 0.0)):
        mc = (far_near + (far_far - far_near) * depth) * np.ones_like(col)
        ln = lean(hgt)
        below = np.maximum(top - el_c, 0.0)
        at = a + below * np.clip(look(ln, a) * 1.5, -1, 1) * facet_lean
        sk = look(ln, at) if depth > 0 else look(ln, a)
        under = np.clip(1.0 - (top - el_c) / (2.6 + depth), 0, 1)
        facing = np.clip(sk * side * 1.5 + 0.15, 0, 1)
        face = np.clip(np.floor(under * facing * 3.0 + rnd) / 3.0, 0, 1)[..., None] * (0.75 - 0.25 * depth)
        lit = (mc + (behind - mc) * 0.5) * 1.06
        mc = mc + (lit - mc) * face
        mc = mc * (1.0 + (rnd - 0.5) * 0.10 - (rnd < 0.14) * 0.07 * (1 - depth))[..., None]
        low = (1 - ss(0.0, 2.8, el_c))[..., None] * (0.12 + 0.30 * depth)
        mc = mc + (mist - mc) * low
        inside = ((el_m < top) & (top > 0.02))[..., None]
        col = np.where(inside, mc, col)
    # lights
    cell = 0.2
    gx, gy = np.floor(a / cell).astype(int), np.floor(el_m / cell).astype(int)
    hsh = rng.rand(4096, 256)[gx % 4096, gy % 256]
    amount, climb = L(t_amount), L(t_climb)
    k = np.clip(1 - el_m / np.maximum(climb, 0.05), 0, 1)
    on = (hsh > 1 - amount * (0.45 * k ** 0.8 + 0.35 * (el_m < 0.45))) & (el_m < np.maximum(np.maximum(top_main, top_near), 0.5))
    fx, fy = a / cell - gx - 0.5, el_m / cell - gy - 0.5
    dot = (np.hypot(fx, fy) < 0.3) & on
    col = np.where(dot[..., None], np.array([1.0, 0.75, 0.45]), col)
    col = np.where(water[..., None], col * 0.82 + np.array([0.02, 0.02, 0.06]), col)
    Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8), 'RGB').save(path)


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else 'iori_ridges.png'
    data = write(out)
    print('wrote', out, 'main max', round(float(data[2].max()), 2), 'far max', round(float(data[3].max()), 2))
    if len(sys.argv) > 2:
        preview(data, sys.argv[2])
        preview(data, sys.argv[2].replace('.png', '_wide.png'), w=1600, h=720)
        print('preview', sys.argv[2])
