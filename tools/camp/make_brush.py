"""Generates the tileable angular brush-stroke texture used by the camp shaders.

Each stroke is a slanted quadrilateral with flat value, like a flat brush laid down once.
Strokes are drawn on a torus (wrapped) so the texture tiles. The result is grayscale,
mean ~0.5; shaders use (value - 0.5) to nudge band thresholds and palette slots.
Run: python3 tools/camp/make_brush.py Assets/IORI/Art/Camp/Textures/iori_brush.png
"""
import sys, math, random
import numpy as np
from PIL import Image, ImageDraw

N = 512
rng = random.Random(20261113)
acc = np.zeros((N, N), np.float32)

canvas = Image.new('F', (N * 3, N * 3), 0.5)

def stroke_layer(count, length, width, angle_center, angle_spread, value_spread):
    # strokes paint over each other (last one wins), so the result is a mosaic of flat values
    img = canvas
    d = ImageDraw.Draw(img)
    for _ in range(count):
        cx, cy = rng.uniform(0, N), rng.uniform(0, N)
        a = angle_center + rng.uniform(-angle_spread, angle_spread)
        L = length * rng.uniform(0.6, 1.4)
        w = width * rng.uniform(0.6, 1.3)
        v = 0.5 + rng.uniform(-value_spread, value_spread)
        ux, uy = math.cos(a), math.sin(a)
        px, py = -uy, ux
        # slightly tapered, angular ends
        t0, t1 = rng.uniform(0.5, 1.0), rng.uniform(0.5, 1.0)
        pts = [(-L/2*ux + px*w*t0, -L/2*uy + py*w*t0), (L/2*ux + px*w*t1, L/2*uy + py*w*t1),
               (L/2*ux*1.06 - px*w*t1*.6, L/2*uy*1.06 - py*w*t1*.6), (-L/2*ux - px*w*t0, -L/2*uy - py*w*t0)]
        for ox in (0, N, 2 * N):
            for oy in (0, N, 2 * N):
                d.polygon([(cx + ox + x, cy + oy + y) for x, y in pts], fill=v)
    return img

stroke_layer(320, 120, 22, -0.35, 0.35, 0.5)
stroke_layer(600, 60, 11, -0.35, 0.4, 0.5)
stroke_layer(900, 26, 5, -0.3, 0.5, 0.45)
acc = np.asarray(canvas, np.float32)[N:2*N, N:2*N].copy()
acc = acc - acc.mean()
acc = acc / (np.abs(acc).max() + 1e-6) * 0.5 + 0.5
Image.fromarray((acc * 255).astype(np.uint8), 'L').save(sys.argv[1])
print('saved', sys.argv[1], float(acc.mean()))
