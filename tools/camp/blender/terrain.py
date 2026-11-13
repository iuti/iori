"""The shape of the campsite ground and what covers it. Used for the mesh, the ground texture
and for deciding where grass may grow. Everything works on numpy arrays (and plain floats)."""
import numpy as np

WATER_Z = -0.24        # lake level
PUDDLE_Z = -0.030      # puddle water level when fully wet
FIRE = (-0.22, -0.85)
IORI = (0.16, 0.10)
TABLE = (-0.62, 0.45)
CRATE = (0.80, 0.70)
STUMP = (0.47, -1.24)
CHAIR = (0.30, 0.50)
TENT = (-1.45, 5.60)
TREE_R = (1.00, 2.30)
TREE_L = (-1.22, 2.90)

# x, depth, radius, depth-to-width ratio
PUDDLES = [
    (0.55, -0.38, 0.40, 0.62), (-0.15, -1.76, 0.50, 0.68), (-0.85, -0.30, 0.30, 0.70), (1.05, -0.10, 0.32, 0.72),
    (-0.18, 0.95, 0.46, 0.50), (2.20, -0.80, 0.60, 0.60), (-2.40, 0.60, 0.60, 0.70), (1.80, 2.30, 0.70, 0.50),
    (-1.70, 3.00, 0.70, 0.50), (3.40, 1.40, 0.70, 0.60), (-3.50, -0.60, 0.60, 0.80), (0.60, 3.60, 0.80, 0.45),
]


def _hash(ix, iy, seed):
    h = (ix * 374761393 + iy * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFFFF) / float(0xFFFFFF)


def vnoise(x, y, seed=0):
    x, y = np.asarray(x, np.float64), np.asarray(y, np.float64)
    ix, iy = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    fx, fy = x - ix, y - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b = _hash(ix, iy, seed), _hash(ix + 1, iy, seed)
    c, d = _hash(ix, iy + 1, seed), _hash(ix + 1, iy + 1, seed)
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(x, y, seed=0, octaves=4):
    """Roughly in [-1, 1]."""
    s, a, f = 0.0, 0.5, 1.0
    for o in range(octaves):
        s = s + a * (vnoise(np.asarray(x) * f, np.asarray(y) * f, seed + o * 17) - 0.5)
        f *= 2.03
        a *= 0.5
    return s * 2.0


def sstep(a, b, x):
    t = np.clip((np.asarray(x, np.float64) - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def bowl(x, y):
    """0..1, how deep we are inside a puddle dip."""
    d = np.zeros(np.broadcast(x, y).shape)
    for (px, py, r, asp) in PUDDLES:
        q = ((x - px) / r) ** 2 + ((y - py) / (r * asp)) ** 2
        q = q + (vnoise(x * 2.6 + px * 3, y * 2.6 + py, 5) - .5) * 0.6
        d = np.maximum(d, 1 - sstep(0.45, 1.0, q))
    return d


def shore_y(x):
    return 7.7 + np.sin(np.asarray(x) * 0.33) * 0.7 + fbm(np.asarray(x) * 0.15, 2.0, 3, 2) * 1.2


# the part of the campsite that the puddles' water plane covers (x from -PUDDLE_AREA[0], depth from PUDDLE_AREA[1] to [2])
PUDDLE_AREA = (5.4, -3.2, 4.8)


def camp_flat(x, y):
    """1 inside the puddle area, fading to 0 a little outside it."""
    return sstep(PUDDLE_AREA[0] + 1.2, PUDDLE_AREA[0] + 0.2, np.abs(x)) * sstep(PUDDLE_AREA[1] - 1.0, PUDDLE_AREA[1] - 0.1, y) * sstep(PUDDLE_AREA[2] + 1.2, PUDDLE_AREA[2] + 0.1, y)


def height(x, y):
    x, y = np.asarray(x, np.float64), np.asarray(y, np.float64)
    h = fbm(x * 0.22 + 10, y * 0.22 + 3, 1, 3) * 0.20 + fbm(x * 1.1, y * 1.1, 2, 3) * 0.035
    d = np.hypot(x - 0.0, y + 0.2)
    h = h * (sstep(1.2, 4.8, d) * 0.88 + 0.12)
    # The campsite itself never dips below the puddles' water level by accident: hollows are
    # filled up to level ground there, so that rain water only collects in the dips made for it.
    k = 40.0
    filled = np.logaddexp(0.0, k * h) / k
    h = h + (filled - h) * camp_flat(x, y)
    sy = shore_y(x)
    h = h - sstep(sy - 0.5, sy + 3.2, y) * 1.15                       # down into the lake
    h = h + sstep(5.0, 15.0, np.abs(x)) * 1.5 * sstep(9.0, 2.0, y)    # the land rises to both sides
    h = h + sstep(-3.0, -9.0, y) * 0.6                                # and a little behind the camera
    h = h - bowl(x, y) * 0.11
    h = h - sstep(0.46, 0.18, np.hypot(x - FIRE[0], y - FIRE[1])) * 0.03
    return h


def cover(x, y):
    """Returns (dirt, sand, moss) in 0..1. Grass grows where dirt and sand are low."""
    x, y = np.asarray(x, np.float64), np.asarray(y, np.float64)
    n1 = fbm(x * 1.3, y * 1.3, 9, 3)
    n2 = fbm(x * 0.5 + 4, y * 0.5, 11, 3)
    dfire = np.hypot(x - FIRE[0], (y - FIRE[1]) * 1.0)
    dirt = sstep(1.45, 0.80, dfire + n1 * 0.45)
    # trodden ground between the fire, the chair and the table
    for (px, py, r) in ((IORI[0], IORI[1], 0.9), (TABLE[0], TABLE[1], 0.75), (CRATE[0], CRATE[1], 0.6), (0.1, -2.2, 0.9)):
        dirt = np.maximum(dirt, sstep(r, r * 0.45, np.hypot(x - px, y - py) + n1 * 0.35) * 0.9)
    dirt = np.maximum(dirt, sstep(0.9, 0.3, np.hypot(x - TENT[0] - 0.3, y - TENT[1] + 1.5) + n1 * 0.3) * 0.8)
    dirt = np.maximum(dirt, sstep(0.52, 0.70, n2 + 0.5 * 0) * sstep(0.55, 0.75, 0.5 + n2) * 0.0)
    dirt = np.maximum(dirt, sstep(0.38, 0.55, n2) * 0.75)             # scattered bare patches
    h = height(x, y)
    sand = sstep(WATER_Z + 0.34, WATER_Z + 0.10, h + n1 * 0.06)
    dirt = np.maximum(dirt, bowl(x, y) * 0.9)                         # puddle dips are bare mud
    dirt = dirt * (1 - sand)
    moss = sstep(0.1, 0.5, fbm(x * 0.8 + 7, y * 0.8 + 1, 21, 3)) * (1 - dirt) * (1 - sand)
    return dirt, sand, moss
