"""Campfire: a ring of stones, a bed of ash and embers, and the logs."""
import math, random
from campkit import *

STONE_COLS = [hexc('#7b7f8e'), hexc('#6c7080'), hexc('#8a8a90'), hexc('#5f6474'), hexc('#77706e')]
SOOT = hexc('#26232a')


def rock(size, rng, color, n=20, squash=0.72, bevel=0.08, stretch=1.0):
    """A chiseled stone: convex hull of a few random points, edges chamfered. Flat facets on purpose."""
    pts = []
    for _ in range(n):
        v = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        r = rng.uniform(0.72, 1.0)
        pts.append((v.x * r * size * stretch, v.y * r * size, v.z * r * size * squash))
    p = hull(pts, color)
    p.bevel(size * bevel, 2, 18)
    p.sharp(26)
    # each facet a slightly different tone, lighter on top
    def tone(f, l):
        up = max(0.0, f.normal.z)
        c = mix(color, tuple(min(1, ch * 1.25) for ch in color), up * 0.6)
        return jitter_color(c, rng, .07, .012)
    cache = {}
    def per_face(f, l):
        if f.index not in cache:
            cache[f.index] = tone(f, l)
        return cache[f.index]
    p.bm.faces.index_update()
    p.paint_fn(per_face)
    return p


def log(length, radius, rng, sides=9, bark=hexc('#30252a'), wood=hexc('#7d6046'), char=0.0, char_from=0.5, split=False):
    """A log along +X from 0 to length. char: how burnt the far end is (0..1), starting at char_from.
    Charred parts carry the ember glow in their cracks (glow channel 'ember')."""
    rings = 7
    verts, faces = [], []
    bend = rng.uniform(-.03, .03) * length
    prof = [rng.uniform(.85, 1.12) for _ in range(sides)]
    for j in range(rings + 1):
        t = j / rings
        x = t * length
        r = radius * (1.0 + 0.10 * math.sin(t * 5 + rng.random()) - 0.12 * t)
        for k in range(sides):
            a = 2 * math.pi * k / sides
            if split and math.sin(a) > 0.25:      # flat split face on top
                y, z = math.cos(a) * r * prof[k], 0.25 * r
            else:
                y, z = math.cos(a) * r * prof[k], math.sin(a) * r * prof[k]
            verts.append((x, y + bend * math.sin(t * math.pi), z))
    for j in range(rings):
        for k in range(sides):
            a, b = j * sides + k, j * sides + (k + 1) % sides
            faces.append((a, a + sides, b + sides, b))
    faces.append(tuple(range(sides)))
    faces.append(tuple(reversed(range(rings * sides, (rings + 1) * sides))))
    p = P.from_pydata(verts, faces, bark)
    bmesh.ops.recalc_face_normals(p.bm, faces=p.bm.faces[:])
    p.sharp(38)
    black = hexc('#17130f')
    for f in p.bm.faces:
        is_end = abs(f.normal.x) > 0.8
        is_split = split and f.normal.z > 0.8
        base = wood if (is_end or is_split) else jitter_color(bark, rng, .12, .015)
        for l in f.loops:
            cx = l.vert.co.x / length
            burnt = sstep(char_from, min(1.0, char_from + 0.3), cx) * char
            c = mix(base, black, burnt)
            l[p.col] = (c[0], c[1], c[2], 1.0)
            # the shader draws glowing cracks where this is high
            l[p.dat] = (float(GLOW['ember']) if burnt > 0.2 else 0.0, burnt, 0.0, 0.0)
    return p


def build_fire(rng):
    """Returns pieces in fire-local coordinates (origin on the ground at the center of the ring)."""
    out = {}
    # ---- stones
    stones = P()
    n = 10
    a0 = rng.uniform(0, 6.28)
    for i in range(n):
        a = a0 + i / n * 2 * math.pi + rng.uniform(-.12, .12)
        size = rng.uniform(.085, .13)
        r = 0.37 + rng.uniform(-.025, .03)
        st = rock(size, rng, rng.choice(STONE_COLS), squash=rng.uniform(.6, .85), stretch=rng.uniform(1.0, 1.35))
        st.rot('Z', math.degrees(a) + 90 + rng.uniform(-25, 25))
        st.move(math.cos(a) * r, math.sin(a) * r, size * 0.42)
        # soot on the faces looking at the fire
        cen = Vector((math.cos(a) * r, math.sin(a) * r, 0))
        for f in st.bm.faces:
            inward = (-cen.normalized()).dot(f.normal)
            if inward > 0.15:
                k = min(1.0, inward * 1.1) * 0.75
                for l in f.loops:
                    c = l[st.col]
                    m = mix(c[:3], SOOT, k)
                    l[st.col] = (m[0], m[1], m[2], c[3])
        stones.add(st)
    for i in range(4):   # a few small ones tucked in
        a = rng.uniform(0, 6.28)
        r = 0.44 + rng.uniform(0, .08)
        st = rock(rng.uniform(.035, .06), rng, rng.choice(STONE_COLS), squash=.7)
        st.move(math.cos(a) * r, math.sin(a) * r, 0.02)
        stones.add(st)
    out['FireStones'] = stones

    # ---- ash bed with ember spots
    ash = lathe([(0.0, 0.03), (0.10, 0.045), (0.20, 0.035), (0.29, 0.012), (0.31, -0.02)], n=18, color=hexc('#2a2729'), cap_bottom=False, cap_top=False)
    ash.subdivide(1)
    ash.displace(lambda co, nrm: co + Vector((0, 0, fbm(co.x * 9, co.y * 9, 3.0) * 0.018)))
    for f in ash.bm.faces:
        g = jitter_color(hexc('#2e2a2c'), rng, .2, .01)
        for l in f.loops:
            d = math.hypot(l.vert.co.x, l.vert.co.y)
            heat = sstep(0.28, 0.04, d)
            l[ash.col] = (g[0], g[1], g[2], 1.0)
            l[ash.dat] = (float(GLOW['ember']), heat * 1.3, 0.0, 0.0)
    ash.shade(False)
    out['FireAsh'] = ash

    # ---- logs: a loose teepee over two crossed base logs
    logs = P()
    def place(lg, base, tip):
        b, t = Vector(base), Vector(tip)
        d = (t - b)
        q = Vector((1, 0, 0)).rotation_difference(d.normalized())
        lg.xform(Matrix.Translation(b) @ q.to_matrix().to_4x4())
        logs.add(lg)
    # base logs lying flat
    place(log(0.62, 0.05, rng, char=0.85, char_from=0.25), (-0.30, -0.06, 0.07), (0.30, 0.05, 0.085))
    place(log(0.56, 0.045, rng, char=0.9, char_from=0.2, split=True), (0.10, -0.27, 0.075), (-0.06, 0.26, 0.10))
    # Leaning logs, burnt at the top where they meet. They lie low, so the flame stands above the
    # pile, and none of them points at the camera (which looks from -Y): seen from there the two in
    # front lean in from the left and the right, and the flame shows between and over them.
    for a_deg in (35, 100, 165, 228, 318):
        a = math.radians(a_deg + rng.uniform(-8, 8))
        rb = rng.uniform(.25, .30)
        base = (math.cos(a) * rb, math.sin(a) * rb, 0.03)
        tip = (math.cos(a + 2.6) * 0.05 + rng.uniform(-.02, .02), math.sin(a + 2.6) * 0.05 + rng.uniform(-.02, .02), rng.uniform(0.19, 0.25))
        place(log(rng.uniform(.38, .46), rng.uniform(.034, .046), rng, char=0.95, char_from=rng.uniform(.3, .45), split=rng.random() < .3), base, tip)
    # kindling
    for i in range(6):
        a = rng.uniform(0, 6.28)
        rb = rng.uniform(.12, .26)
        base = (math.cos(a) * rb, math.sin(a) * rb, 0.04)
        tip = (rng.uniform(-.05, .05), rng.uniform(-.05, .05), rng.uniform(.10, .17))
        place(log(rng.uniform(.22, .32), rng.uniform(.010, .016), rng, sides=5, char=1.0, char_from=.2), base, tip)
    out['FireLogs'] = logs
    return out
