"""Camp props: table, lantern, mug, kettle, chair, radio on a crate, tape deck on a stump, tent, firewood."""
import math, random
from campkit import *

WOOD = hexc('#a9875f')
WOOD_DARK = hexc('#7a5c40')
STEEL = hexc('#4a4e5a')
BRASS = hexc('#b08a3c')
CANVAS = hexc('#8c8870')
ENAMEL = hexc('#cfd4da')


def arc(p0, p1, bulge, n=10, plane_up=(0, 0, 1)):
    """Points on an arc from p0 to p1 bulging along plane_up."""
    a, b, up = Vector(p0), Vector(p1), Vector(plane_up)
    pts = []
    for i in range(n + 1):
        t = i / n
        pts.append(a.lerp(b, t) + up * math.sin(t * math.pi) * bulge)
    return pts


def ring(radius, z, n=20, cx=0.0, cy=0.0):
    return [(cx + radius * math.cos(2 * math.pi * i / n), cy + radius * math.sin(2 * math.pi * i / n), z) for i in range(n)]


# ------------------------------------------------------------------------------ lantern
def lantern(rng):
    body = hexc('#3d4b57')
    p = P()
    # tank and burner collar
    tank = lathe([(0.050, 0.0), (0.066, 0.006), (0.070, 0.022), (0.066, 0.042), (0.048, 0.056), (0.040, 0.060), (0.040, 0.066), (0.034, 0.070), (0.034, 0.084), (0.030, 0.088)], 18, body)
    tank.data(gloss=0.55)
    p.add(tank)
    collar = lathe([(0.041, 0.060), (0.043, 0.062), (0.043, 0.066), (0.041, 0.068)], 18, BRASS, cap_bottom=False, cap_top=False).data(gloss=0.8)
    p.add(collar)
    filler = cylinder(0.011, 0.011, 0.014, 8, BRASS).move(0.045, 0.03, 0.045).data(gloss=0.8)
    p.add(filler)
    knob = cylinder(0.009, 0.009, 0.006, 8, BRASS).rot('X', 90).move(0.0, -0.047, 0.077).data(gloss=0.8)
    p.add(knob)
    # glass globe, lit from inside
    glass = lathe([(0.030, 0.088), (0.046, 0.108), (0.054, 0.135), (0.052, 0.160), (0.042, 0.184), (0.033, 0.198)], 18, hexc('#ffe9b8'), cap_bottom=False, cap_top=False, angle=80)
    for f in glass.bm.faces:
        for l in f.loops:
            z = l.vert.co.z
            k = 1.0 - abs(z - 0.125) / 0.075        # brightest around the flame
            l[glass.dat] = (float(GLOW['lantern']), 0.55 + 0.75 * max(0.0, k), 0.9, 0.0)
    p.add(glass)
    # hood and chimney
    hood = lathe([(0.034, 0.198), (0.044, 0.202), (0.046, 0.212), (0.030, 0.232), (0.022, 0.236), (0.022, 0.246), (0.034, 0.249), (0.036, 0.255), (0.0, 0.262)], 18, body).data(gloss=0.55)
    p.add(hood)
    # side tubes from the tank up to the hood
    for sx in (-1, 1):
        pts = [(sx * 0.058, 0, 0.050), (sx * 0.074, 0, 0.075), (sx * 0.078, 0, 0.135), (sx * 0.072, 0, 0.190), (sx * 0.050, 0, 0.222), (sx * 0.040, 0, 0.226)]
        p.add(tube(pts, 0.0065, 7, body).data(gloss=0.55))
    # wire guard around the glass
    for z, r in ((0.112, 0.056), (0.160, 0.060)):
        p.add(tube(ring(r, z, 16), 0.0016, 4, BRASS, closed=True, caps=False).data(gloss=0.8))
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        pts = [(0.036 * math.cos(a), 0.036 * math.sin(a), 0.090), (0.058 * math.cos(a), 0.058 * math.sin(a), 0.115), (0.061 * math.cos(a), 0.061 * math.sin(a), 0.160), (0.040 * math.cos(a), 0.040 * math.sin(a), 0.200)]
        p.add(tube(pts, 0.0015, 4, BRASS, caps=False).data(gloss=0.8))
    # carrying handle, resting slightly to one side
    hp = arc((-0.076, 0, 0.185), (0.076, 0, 0.185), 0.135, 14)
    hp = [Vector((q.x, 0.022 * math.sin(i / 14 * math.pi), q.z)) for i, q in enumerate(hp)]
    p.add(tube(hp, 0.0022, 5, BRASS, caps=False).data(gloss=0.8))
    return p


# ------------------------------------------------------------------------------ table, mug, kettle
def table(rng):
    p = P()
    top_z, w, d = 0.40, 0.62, 0.40
    n = 6
    sw = d / n
    for i in range(n):
        c = jitter_color(WOOD, rng, .08, .01)
        slat = box(w, sw - 0.005, 0.016, c, bevel=0.0035).move(0, -d / 2 + sw * (i + .5), top_z - 0.008)
        p.add(slat)
    for sx in (-1, 1):   # battens under the slats
        p.add(box(0.03, d - 0.02, 0.016, WOOD_DARK, bevel=0.003).move(sx * (w / 2 - 0.07), 0, top_z - 0.024))
    # X legs at both ends
    for sx in (-1, 1):
        x = sx * (w / 2 - 0.07)
        for sy in (-1, 1):
            a = Vector((x + sx * 0.012 * sy, -sy * (d / 2 - 0.03), top_z - 0.03))
            b = Vector((x + sx * 0.012 * sy, sy * (d / 2 + 0.02), 0.0))
            leg = box((b - a).length, 0.026, 0.018, jitter_color(WOOD_DARK, rng, .06, .01), bevel=0.003)
            dirv = (b - a).normalized()
            q = Vector((1, 0, 0)).rotation_difference(dirv)
            leg.xform(Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4())
            p.add(leg)
    p.add(tube([(-w / 2 + 0.07, 0, top_z * 0.5 - 0.015), (w / 2 - 0.07, 0, top_z * 0.5 - 0.015)], 0.008, 6, STEEL).data(gloss=0.5))
    return p


def mug(rng):
    p = lathe([(0.030, 0.0), (0.036, 0.004), (0.038, 0.075), (0.040, 0.080), (0.036, 0.080), (0.034, 0.012), (0.0, 0.010)], 14, ENAMEL, cap_bottom=True, cap_top=False, angle=50)
    p.paint(hexc('#2e3654'), where=lambda f: f.calc_center_median().z > 0.076)
    p.paint(hexc('#3a2a22'), where=lambda f: f.calc_center_median().z < 0.03 and abs(f.normal.z) > 0.5 and math.hypot(f.calc_center_median().x, f.calc_center_median().y) < 0.03)
    p.data(gloss=0.6)
    h = tube([(0.037, 0, 0.066), (0.058, 0, 0.062), (0.064, 0, 0.042), (0.056, 0, 0.022), (0.037, 0, 0.018)], 0.0045, 6, ENAMEL).data(gloss=0.6)
    p.add(h)
    return p


def kettle(rng):
    c = hexc('#3b3f4a')
    p = lathe([(0.070, 0.0), (0.086, 0.008), (0.090, 0.035), (0.082, 0.075), (0.060, 0.105), (0.040, 0.115), (0.040, 0.120), (0.0, 0.126)], 16, c, angle=45).data(gloss=0.6)
    for f in p.bm.faces:       # soot at the bottom
        for l in f.loops:
            k = sstep(0.05, 0.0, l.vert.co.z) * 0.6
            cc = mix(c, hexc('#15151a'), k)
            l[p.col] = (cc[0], cc[1], cc[2], 1)
    p.add(cylinder(0.012, 0.008, 0.016, 8, hexc('#22242c')).move(0, 0, 0.124))
    p.add(tube([(0.078, 0, 0.050), (0.118, 0, 0.078), (0.140, 0, 0.112)], [0.016, 0.011, 0.008], 7, c).data(gloss=0.6))
    hp = arc((-0.062, 0, 0.098), (0.062, 0, 0.098), 0.085, 10)
    p.add(tube(hp, 0.0045, 5, hexc('#22242c'), caps=False).data(gloss=0.4))
    return p


# ------------------------------------------------------------------------------ chair
def chair(rng):
    """Low folding camp chair. Faces -Y (toward the camera / the fire)."""
    p = P()
    frame = hexc('#3a3d49')
    hw = 0.25
    seat_f, seat_b = Vector((0, -0.21, 0.37)), Vector((0, 0.19, 0.30))
    back_b, back_t = Vector((0, 0.21, 0.33)), Vector((0, 0.36, 0.80))
    for sx in (-1, 1):
        x = sx * hw
        p.add(tube([(x, -0.26, 0.0), (x, 0.20, 0.47)], 0.011, 7, frame).data(gloss=0.5))   # front leg up to the arm
        p.add(tube([(x, 0.30, 0.0), (x, -0.20, 0.38)], 0.011, 7, frame).data(gloss=0.5))   # rear leg up to the seat front
        p.add(tube([(x, 0.20, 0.30), (x, 0.365, 0.82)], 0.011, 7, frame).data(gloss=0.5))  # back post
        arm = box(0.045, 0.40, 0.016, jitter_color(WOOD, rng, .05), bevel=0.004).rot('X', 4).move(x, -0.02, 0.485)
        p.add(arm)
        p.add(cylinder(0.014, 0.014, 0.012, 8, hexc('#22232b')).move(x, -0.26, 0.0))
        p.add(cylinder(0.014, 0.014, 0.012, 8, hexc('#22232b')).move(x, 0.30, 0.0))
    p.add(tube([(-hw, -0.26, 0.04), (hw, -0.26, 0.04)], 0.008, 6, frame).data(gloss=0.5))
    p.add(tube([(-hw, 0.30, 0.04), (hw, 0.30, 0.04)], 0.008, 6, frame).data(gloss=0.5))
    # canvas seat and back, sagging in the middle
    def cloth(a, b, sag, nu=8, nv=6):
        verts, faces = [], []
        axis = (b - a)
        nrm = Vector((1, 0, 0)).cross(axis).normalized()
        if nrm.z < 0: nrm = -nrm
        for j in range(nv + 1):
            for i in range(nu + 1):
                u, v = i / nu, j / nv
                s = math.sin(u * math.pi) * (0.55 + 0.45 * math.sin(v * math.pi))
                pt = a + axis * v + Vector((1, 0, 0)) * ((u - .5) * 2 * (hw - 0.012)) - nrm * s * sag
                verts.append(tuple(pt))
        for j in range(nv):
            for i in range(nu):
                k = j * (nu + 1) + i
                faces.append((k, k + 1, k + nu + 2, k + nu + 1))
        c = P.from_pydata(verts, faces, CANVAS, smooth=True)
        c.solidify(0.006)
        c.sharp(60)
        c.paint_fn(lambda f, l: jitter_color(CANVAS, rng, .035, .006))
        return c
    p.add(cloth(seat_f, seat_b, 0.05))
    p.add(cloth(back_b + Vector((0, 0.02, 0.10)), back_t, 0.045, nv=5))
    return p


# ------------------------------------------------------------------------------ radio and crate
def radio(rng):
    """Portable radio, front toward -Y. Sits with its base at z = 0."""
    p = P()
    w, h, d = 0.30, 0.185, 0.085
    shell = hexc('#b9b5a8')
    face = hexc('#3f4350')
    yellow = hexc('#d0a136')
    body = box(w, d, h, shell, bevel=0.014, segments=3).move(0, 0, h / 2).data(gloss=0.35)
    p.add(body)
    p.add(box(w - 0.03, 0.006, h - 0.03, face, bevel=0.004).move(0, -d / 2 - 0.001, h / 2))
    p.add(box(w - 0.03, 0.0065, 0.010, yellow).move(0, -d / 2 - 0.0012, h - 0.028))        # accent stripe
    # speaker: round recess with slats
    sx, sz, sr = -0.068, h / 2 - 0.008, 0.058
    p.add(cylinder(sr + 0.006, sr + 0.006, 0.006, 20, hexc('#8d8a80')).rot('X', 90).move(sx, -d / 2 - 0.002, sz).data(gloss=0.5))
    p.add(cylinder(sr, sr, 0.004, 20, hexc('#1e2028')).rot('X', 90).move(sx, -d / 2 - 0.0065, sz))
    for i in range(-4, 5):
        z = i * 0.0125
        half = math.sqrt(max(1e-6, sr * sr - z * z)) - 0.004
        p.add(box(half * 2, 0.004, 0.0055, hexc('#5c5f6c'), bevel=0.001).move(sx, -d / 2 - 0.0095, sz + z))
    # tuning dial: a lit window with a needle and a few ticks
    dx, dz, dw, dh = 0.075, h / 2 + 0.022, 0.105, 0.046
    p.add(box(dw + 0.012, 0.005, dh + 0.012, hexc('#8d8a80'), bevel=0.003).move(dx, -d / 2 - 0.003, dz).data(gloss=0.5))
    win = box(dw, 0.004, dh, hexc('#ffd68a')).move(dx, -d / 2 - 0.0056, dz)
    win.data(glow='radio', strength=1.0)
    p.add(win)
    for i in range(7):
        tx = dx - dw / 2 + 0.012 + i * (dw - 0.024) / 6
        p.add(box(0.0022, 0.002, 0.014 if i % 2 == 0 else 0.009, hexc('#3a2c20')).move(tx, -d / 2 - 0.0080, dz + 0.010))
    p.add(box(0.0028, 0.002, dh - 0.006, hexc('#c0381e')).move(dx + 0.018, -d / 2 - 0.0082, dz))
    # knobs
    for kx in (0.045, 0.105):
        k = lathe([(0.016, 0.0), (0.016, 0.010), (0.013, 0.014), (0.0, 0.014)], 14, hexc('#24262e')).rot('X', 90).move(kx, -d / 2 - 0.003, h / 2 - 0.038).data(gloss=0.5)
        p.add(k)
        p.add(box(0.003, 0.002, 0.012, hexc('#d8d4c8')).move(kx, -d / 2 - 0.0175, h / 2 - 0.032))
    # little lamp that shows it is on
    lamp = cylinder(0.005, 0.005, 0.004, 8, hexc('#ffb060')).rot('X', 90).move(0.135 - 0.02, -d / 2 - 0.004, h / 2 - 0.038)
    lamp.data(glow='radio', strength=1.6)
    p.add(lamp)
    # handle and antenna
    hp = arc((-0.10, 0, h), (0.10, 0, h), 0.045, 12)
    p.add(tube(hp, [0.006] * 13, 6, hexc('#5a4634'), caps=False))
    for sxn in (-1, 1):
        p.add(cylinder(0.009, 0.009, 0.008, 8, hexc('#8d8a80')).move(sxn * 0.10, 0, h - 0.002).data(gloss=0.6))
    base = Vector((w / 2 - 0.025, 0.02, h))
    dirv = Vector((0.28, 0.10, 1.0)).normalized()
    p.add(cylinder(0.006, 0.006, 0.012, 8, hexc('#8d8a80')).move(*base).data(gloss=0.7))
    segs = [(0.0, 0.16, 0.0034), (0.16, 0.30, 0.0026), (0.30, 0.42, 0.0019)]
    for a, b, r in segs:
        p.add(tube([base + dirv * a, base + dirv * b], r, 5, hexc('#c9ccd4')).data(gloss=0.9))
    p.add(cylinder(0.0045, 0.0045, 0.008, 6, hexc('#c9ccd4')).xform(Matrix.Translation(base + dirv * 0.42)).data(gloss=0.9))
    return p


def crate(rng):
    p = P()
    w, d, h = 0.44, 0.32, 0.30
    c = hexc('#8f7352')
    n = 4
    sh = h / n
    for side, (sx, sy, rotz) in enumerate(((0, -d / 2, 0), (0, d / 2, 0), (-w / 2, 0, 90), (w / 2, 0, 90))):
        length = w if rotz == 0 else d
        for i in range(n):
            s = box(length - 0.004, 0.014, sh - 0.012, jitter_color(c, rng, .1, .012), bevel=0.003)
            s.rot('Z', rotz).move(sx, sy, sh * (i + .5))
            p.add(s)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.add(box(0.03, 0.03, h + 0.006, jitter_color(WOOD_DARK, rng, .06), bevel=0.003).move(sx * (w / 2 - 0.022), sy * (d / 2 - 0.022), h / 2))
    for i in range(5):
        sw = w / 5
        p.add(box(sw - 0.006, d - 0.008, 0.014, jitter_color(c, rng, .1, .012), bevel=0.003).move(-w / 2 + sw * (i + .5), 0, h - 0.004))
    return p


# ------------------------------------------------------------------------------ tape deck and stump
def deck(rng):
    """Shoebox tape recorder lying flat, keys toward -Y. Base at z = 0."""
    p = P()
    w, d, h = 0.27, 0.165, 0.052
    shell = hexc('#454956')
    p.add(box(w, d, h, shell, bevel=0.008, segments=2).move(0, 0, h / 2).data(gloss=0.4))
    top = h
    # cassette door with a window and two hubs
    p.add(box(0.125, 0.082, 0.004, hexc('#8f8c82'), bevel=0.002).move(0.055, 0.022, top + 0.001).data(gloss=0.5))
    p.add(box(0.098, 0.040, 0.003, hexc('#14151b')).move(0.055, 0.026, top + 0.0032).data(gloss=0.9))
    for hx in (0.030, 0.080):
        p.add(cylinder(0.011, 0.011, 0.003, 10, hexc('#d8d4c8')).move(hx, 0.026, top + 0.004))
    # speaker slats
    for i in range(6):
        p.add(box(0.095, 0.0065, 0.003, hexc('#23252d'), bevel=0.001).move(-0.075, -0.012 + i * 0.013, top + 0.0005))
    # level meter, lit
    met = box(0.040, 0.022, 0.003, hexc('#ffd68a')).move(-0.092, -0.050, top + 0.001)
    met.data(glow='player', strength=1.0)
    p.add(met)
    p.add(box(0.002, 0.018, 0.0015, hexc('#3a2c20')).rot('Z', -22).move(-0.088, -0.050, top + 0.003))
    # piano keys along the front edge, one of them orange
    for i in range(5):
        c = hexc('#c9571f') if i == 0 else hexc('#c9c5b8')
        p.add(box(0.026, 0.030, 0.014, c, bevel=0.003).move(0.012 + i * 0.030, -d / 2 + 0.012, top + 0.002).data(gloss=0.4))
    # carry handle folded at the back
    p.add(tube([(-0.09, d / 2 + 0.004, 0.030), (-0.09, d / 2 + 0.022, 0.034), (0.09, d / 2 + 0.022, 0.034), (0.09, d / 2 + 0.004, 0.030)], 0.005, 6, hexc('#8f8c82')).data(gloss=0.5))
    return p


def stump(rng, radius=0.22, height=0.36):
    n = 22
    bark = hexc('#4b3b33')
    prof = [1 + 0.10 * fbm(math.cos(2 * math.pi * k / n) * 1.7, math.sin(2 * math.pi * k / n) * 1.7, 4.2) for k in range(n)]
    verts, faces = [], []
    zs = [0.0, 0.03, 0.09, 0.2, height - 0.02, height]
    flare = [1.45, 1.28, 1.10, 1.0, 0.97, 0.94]
    for j, z in enumerate(zs):
        for k in range(n):
            a = 2 * math.pi * k / n
            root = 1 + (0.35 * max(0.0, math.sin(a * 2.5 + 1.0)) ** 3) * (flare[j] - 0.94)
            r = radius * prof[k] * flare[j] * root
            verts.append((math.cos(a) * r, math.sin(a) * r, z))
    for j in range(len(zs) - 1):
        for k in range(n):
            a, b = j * n + k, j * n + (k + 1) % n
            faces.append((a, b, b + n, a + n))
    p = P.from_pydata(verts, faces, bark)
    p.sharp(50)
    strip = {}
    def bark_col(f, l):
        k = int((math.atan2(f.calc_center_median().y, f.calc_center_median().x) + math.pi) / (2 * math.pi) * n * 0.5)
        if k not in strip: strip[k] = jitter_color(bark, rng, .16, .015)
        return strip[k]
    p.paint_fn(bark_col)
    # top: growth rings as concentric bands, slightly dished
    rings = 5
    top_light, top_dark = hexc('#c7ab85'), hexc('#a4865f')
    tv, tf = [(0, 0, height - 0.004)], []
    for j in range(1, rings + 1):
        for k in range(n):
            a = 2 * math.pi * k / n
            r = radius * prof[k] * 0.94 * j / rings
            tv.append((math.cos(a) * r + 0.012 * (1 - j / rings), math.sin(a) * r, height - 0.004 * (1 - j / rings)))
    for k in range(n):
        tf.append((0, 1 + k, 1 + (k + 1) % n))
    for j in range(1, rings):
        for k in range(n):
            a, b = 1 + (j - 1) * n + k, 1 + (j - 1) * n + (k + 1) % n
            tf.append((a, a + n, b + n, b))
    t = P.from_pydata(tv, tf, top_light)
    bmesh.ops.recalc_face_normals(t.bm, faces=t.bm.faces[:])
    if sum(f.normal.z for f in t.bm.faces) < 0:
        bmesh.ops.reverse_faces(t.bm, faces=t.bm.faces[:])
    def ring_col(f, l):
        c = f.calc_center_median()
        j = int(math.hypot(c.x, c.y) / (radius * 0.94) * rings + 0.3)
        return jitter_color(top_light if j % 2 == 0 else top_dark, rng, .03)
    t.paint_fn(ring_col)
    t.shade(False)
    p.add(t)
    return p


def firewood(rng):
    """A small stack of split logs."""
    from models_fire import log
    p = P()
    rows = [(4, 0.0), (3, 0.085), (2, 0.165)]
    for count, z in rows:
        for i in range(count):
            lg = log(rng.uniform(.34, .40), rng.uniform(.040, .050), rng, split=rng.random() < .6)
            lg.rot('X', rng.uniform(0, 360)).rot('Z', rng.uniform(-4, 4))
            lg.move(-0.19, (i - (count - 1) / 2) * 0.092 + rng.uniform(-.006, .006), z + 0.045)
            p.add(lg)
    return p


# ------------------------------------------------------------------------------ tent
def tent(rng, radius=1.15, height=1.38, detail=3):
    """Geodesic dome tent with a lamp hanging inside. Door toward -Y.
    The cloth carries how much of the lamp's light arrives at each panel (glow strength 0.3..1.25)
    and each panel's middle; the shader turns that, and where the lamp is behind the cloth as seen
    from the camera, into color, one panel at a time."""
    p = P()
    cloth_hot, cloth_edge = hexc('#f0b658'), hexc('#d8842e')
    dome = ico(1.0, detail)
    bmesh.ops.bisect_plane(dome.bm, geom=dome.bm.verts[:] + dome.bm.edges[:] + dome.bm.faces[:], plane_co=(0, 0, -0.02), plane_no=(0, 0, -1), clear_outer=True)
    dome.scale(radius, radius, height)
    dome.shade(False)
    lamp = Vector((0.0, -0.1, 0.55))
    dome.bm.faces.index_update()
    tone = {f.index: rng.uniform(-.18, .18) for f in dome.bm.faces}
    # light arriving from the lamp: stronger where the cloth is close and faces it
    def arriving(co, nrm):
        to = lamp - co
        d = to.length
        return abs(nrm.dot(to)) / d / (d * d)
    most = max(arriving(f.calc_center_median(), f.normal) for f in dome.bm.faces)
    for f in dome.bm.faces:
        # A panel is mostly one tone (taken at its middle) with a little of the slope across it.
        # Its middle also goes to the shader (uv0 and data.w, in the engine's axes: x, up, depth),
        # which uses it to tell how far the panel is from the line between the camera and the lamp.
        mid = f.calc_center_median()
        e_mid = min(1.0, arriving(mid, f.normal) / most)
        for l in f.loops:
            e_here = min(1.0, arriving(l.vert.co, f.normal) / most)
            e = (0.75 * e_mid + 0.25 * e_here) ** 1.3
            c = mix(cloth_edge, cloth_hot, e)
            c = tuple(min(1.0, ch * (1 + tone[f.index] * 0.4)) for ch in c)
            l[dome.col] = (c[0], c[1], c[2], 1.0)
            l[dome.dat] = (float(GLOW['tent']), 0.36 + 0.82 * e + tone[f.index], 0.0, mid.y)
            l[dome.uv].uv = (mid.x, mid.z)
    p.add(dome)
    # poles along the panel seams, dark against the light
    seam = hexc('#5a3418')
    seen = set()
    for e in dome.bm.edges:
        a, b = e.verts[0].co.copy(), e.verts[1].co.copy()
        if a.z < 0.01 and b.z < 0.01:
            continue
        key = (round(a.x + b.x, 3), round(a.y + b.y, 3), round(a.z + b.z, 3))
        if key in seen: continue
        seen.add(key)
        s = tube([a * 1.006, b * 1.006], 0.008, 4, seam, caps=False)
        s.data(glow='tent', strength=0.10)
        p.add(s)
    # ground skirt
    p.add(lathe([(radius * 1.0, 0.0), (radius * 1.02, 0.03)], 20, hexc('#b56a2c'), cap_bottom=False, cap_top=False))
    # guy lines and pegs
    for a_deg in (200, 250, 290, 340, 20, 160):
        a = math.radians(a_deg)
        top = Vector((math.cos(a) * radius * 0.80, math.sin(a) * radius * 0.80, height * 0.62))
        peg = Vector((math.cos(a) * (radius + 0.75), math.sin(a) * (radius + 0.75), 0.0))
        p.add(tube([top, peg], 0.004, 3, hexc('#c8c2b0'), caps=False))
        p.add(cylinder(0.008, 0.004, 0.14, 5, hexc('#8d8a80')).rot('X', 25).xform(Matrix.Translation(peg + Vector((0, 0, -0.03)))))
    return p
