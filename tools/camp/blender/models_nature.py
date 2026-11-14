"""Rocks, trees and plants.

Plants (leaves, grass blades, fern leaflets) are shaded one piece at a time in Unity: every leaf
or blade stores its own center so the shader can light it as a single brush stroke and sway it.
    uv0 = (center x, center depth)   Data.z = center height   Data.w = sway weight (0 = fixed)
    Data.y = tone (0 = base of a blade, 1 = tip)
Centers are in the coordinates of the piece being built (the scene puts plants at the origin).
"""
import math, random
from campkit import *
from models_fire import rock

LEAF = hexc('#2f4a3c')
LEAF2 = hexc('#3d5a3e')
BARK = hexc('#3a3038')
BARK_LIGHT = hexc('#5a4c50')
GRASS_A = hexc('#3f6f4a')
GRASS_B = hexc('#58864e')
GRASS_DRY = hexc('#8a8a52')


def boulder(rng, size=0.5, color=hexc('#6a7286')):
    p = rock(size, rng, color, n=30, squash=rng.uniform(.62, .8), bevel=0.06, stretch=rng.uniform(1.0, 1.4))
    return p


# ------------------------------------------------------------------------------ plant pieces
class Plants:
    """Collects leaf / blade polygons with their centers into one piece. Written out as plain
    arrays (every leaf or blade shares its corners, one normal each), so the files stay small."""

    def __init__(self):
        self.verts, self.nors, self.cols, self.dats, self.uvs, self.tris = [], [], [], [], [], []

    def add(self, pts, normal, center, colors, weights, tones, tris):
        base = len(self.verts)
        for i, q in enumerate(pts):
            self.verts.append(tuple(q))
            self.nors.append(tuple(normal))
            self.cols.append(colors[i] if isinstance(colors, list) else colors)
            self.dats.append((0.0, tones[i], center[2], weights[i]))
            self.uvs.append((center[0], center[1]))
        self.tris += [(base + i, base + j, base + k) for (i, j, k) in tris]

    def poly(self, pts, center, color, weights, tones=None):
        n = Vector((0, 0, 0))
        for i in range(len(pts)):
            a, b = Vector(pts[i]), Vector(pts[(i + 1) % len(pts)])
            n += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
        n = n.normalized() if n.length > 1e-12 else Vector((0, 0, 1))
        self.add(pts, n, center, color, weights, tones or [0.5] * len(pts), [(0, i, i + 1) for i in range(1, len(pts) - 1)])

    def extend(self, other, offset=(0.0, 0.0, 0.0), sway=1.0, center=None):
        """Appends another set moved by offset. center: use this (x, depth) as every piece's center."""
        base = len(self.verts)
        ox, oy, oz = offset
        self.verts += [(v[0] + ox, v[1] + oy, v[2] + oz) for v in other.verts]
        self.nors += other.nors
        self.cols += other.cols
        self.dats += [(d[0], d[1], d[2] + oz, d[3] * sway) for d in other.dats]
        self.uvs += [center] * len(other.uvs) if center else [(u[0] + ox, u[1] + oy) for u in other.uvs]
        self.tris += [(a + base, b + base, c + base) for (a, b, c) in other.tris]

    def piece(self):
        import numpy as np
        p = P()
        bv = [p.bm.verts.new(v) for v in self.verts]
        for f in self.tris:
            try:
                bf = p.bm.faces.new([bv[i] for i in f])
            except ValueError:
                continue
            for l, i in zip(bf.loops, f):
                c = self.cols[i]
                l[p.col] = (c[0], c[1], c[2], 1.0)
                l[p.dat] = self.dats[i]
                l[p.uv].uv = self.uvs[i]
        p.bm.normal_update()
        p.shade(False)
        n = len(self.verts)
        col = np.ones((n, 4), np.float32)
        col[:, :3] = np.array(self.cols, np.float32).reshape(n, -1)[:, :3] if n else 0
        p.raw = dict(pos=np.array(self.verts, np.float32).reshape(n, 3), nor=np.array(self.nors, np.float32).reshape(n, 3), col=col,
                     dat=np.array(self.dats, np.float32).reshape(n, 4), uv=np.array(self.uvs, np.float32).reshape(n, 2),
                     tri=np.array(self.tris, np.int32).reshape(-1))
        return p


def leaf(pl, pos, direction, size, rng, color):
    """An angular leaf: a kite folded a little along its midrib."""
    d = Vector(direction).normalized()
    side = d.cross(Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))).normalized()
    nrm = side.cross(d).normalized()
    w = size * rng.uniform(.42, .58)
    base = Vector(pos)
    tip = base + d * size
    mid = base + d * size * rng.uniform(.34, .46)
    fold = nrm * size * 0.07
    c = jitter_color(color, rng, .16, .02)
    cen = base + d * size * 0.45
    pl.poly([base, mid + side * w / 2 + fold, tip, mid - side * w / 2 + fold], cen, c, [1.0, 1.0, 1.0, 1.0])


def blade(pl, root, direction, length, width, rng, color, tip_color, lean=0.35, segs=3):
    """A grass blade: a few quads bending over, ending in a point, like one flick of a thin brush."""
    r = Vector(root)
    d = Vector((direction[0], direction[1], 0.0)).normalized()
    side = Vector((-d.y, d.x, 0.0))
    pts_l, pts_r = [], []
    for i in range(segs + 1):
        t = i / segs
        c = r + Vector((0, 0, 1)) * length * (t - lean * 0.45 * t * t) + d * length * lean * t * t
        wd = width * (1 - t) ** 0.8 * 0.5
        pts_l.append(c - side * wd)
        pts_r.append(c + side * wd)
    col = jitter_color(color, rng, .12, .02)
    tipc = jitter_color(tip_color, rng, .1, .02)
    pts, tn = [], []
    for i in range(segs):
        pts += [pts_l[i], pts_r[i]]
        tn += [i / segs, i / segs]
    pts.append((pts_l[segs] + pts_r[segs]) / 2)
    tn.append(1.0)
    tris = []
    for i in range(segs - 1):
        a = 2 * i
        tris += [(a, a + 1, a + 3), (a, a + 3, a + 2)]
    a = 2 * (segs - 1)
    tris.append((a, a + 1, a + 2))
    # blades are lit as a whole, mostly from above; a little of the way they face keeps a tuft from going flat
    nrm = (Vector((0, 0, 1)) + d * 0.35).normalized()
    pl.add(pts, nrm, (r.x, r.y, r.z), [mix(col, tipc, t) for t in tn], [t * t for t in tn], tn, tris)


def tuft(pl, x, y, z, rng, height=0.10, blades=6, spread=0.05, color=GRASS_A, tip=GRASS_B, width=0.022, segs=3):
    for _ in range(blades):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0, spread)
        blade(pl, (x + math.cos(a) * r, y + math.sin(a) * r, z - 0.01), (math.cos(a), math.sin(a)), height * rng.uniform(.6, 1.15), width * rng.uniform(.75, 1.25), rng, color, tip, lean=rng.uniform(.15, .6), segs=segs)


def fern(pl, x, y, z, rng, fronds=7, length=0.5, color=hexc('#2c4f3e')):
    """Arching fronds with rows of leaflets; reads as a dark, toothed silhouette."""
    a0 = rng.uniform(0, 6.28)
    for i in range(fronds):
        a = a0 + i / fronds * 2 * math.pi + rng.uniform(-.3, .3)
        d = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-d.y, d.x, 0))
        L = length * rng.uniform(.7, 1.1)
        n = 11
        rise, reach = rng.uniform(.45, .7), rng.uniform(.7, .95)
        prev = Vector((x, y, z))
        cen_root = Vector((x, y, z))
        col = jitter_color(color, rng, .12, .02)
        for j in range(1, n + 1):
            t = j / n
            cur = Vector((x, y, z)) + d * L * reach * t + Vector((0, 0, 1)) * L * rise * math.sin(t * math.pi * 0.62) * (1 - 0.35 * t)
            wl = L * 0.20 * math.sin(min(1.0, t * 1.25) * math.pi) ** 0.8 + 0.01
            fwd = (cur - prev)
            for s in (-1, 1):
                tipp = prev + side * s * wl + fwd * 0.8 - Vector((0, 0, wl * 0.25))
                wt = t
                ws = [max(0.0, t - 1 / n), t, min(1.0, t + .15)]
                tri = [prev, cur, tipp] if s > 0 else [cur, prev, tipp]
                wsx = ws if s > 0 else [ws[1], ws[0], ws[2]]
                nn = (Vector(tri[1]) - Vector(tri[0])).cross(Vector(tri[2]) - Vector(tri[0]))
                nn = nn.normalized() if nn.length > 1e-12 else Vector((0, 0, 1))
                pl.add(tri, nn, (cur.x, cur.y, cen_root.z + L * rise * 0.5), col, [w_ * w_ * 0.8 for w_ in wsx], wsx, [(0, 1, 2)])
            prev = cur


def reed(pl, x, y, z, rng, height=1.1, color=hexc('#3c5a4a')):
    d = (rng.uniform(-1, 1), rng.uniform(-1, 1))
    n = rng.randint(3, 6)
    for _ in range(n):
        a = rng.uniform(0, 6.28)
        blade(pl, (x + rng.uniform(-.05, .05), y + rng.uniform(-.05, .05), z), (math.cos(a), math.sin(a)), height * rng.uniform(.6, 1.1), 0.028, rng, color, hexc('#56704c'), lean=rng.uniform(.05, .3))


def flower(pl, x, y, z, rng, color=hexc('#d9c3e6')):
    h = rng.uniform(.09, .16)
    a = rng.uniform(0, 6.28)
    blade(pl, (x, y, z), (math.cos(a), math.sin(a)), h, 0.007, rng, GRASS_A, GRASS_B, lean=.2)
    top = Vector((x + math.cos(a) * h * .04, y + math.sin(a) * h * .04, z + h))
    c = jitter_color(color, rng, .06, .02)
    for k in range(5):
        b = k / 5 * 2 * math.pi
        d = Vector((math.cos(b), math.sin(b), 0.25)).normalized()
        s = Vector((-math.sin(b), math.cos(b), 0))
        r = 0.018
        pl.poly([top, top + d * r * .6 + s * r * .38, top + d * r, top + d * r * .6 - s * r * .38], (x, y, z), c, [1, 1, 1, 1], [1, 1, 1, 1])


# ------------------------------------------------------------------------------ trees
def _wander(start, direction, length, rng, segs=6, droop=0.0, wander=0.3, lift=0.0):
    pts = [Vector(start)]
    d = Vector(direction).normalized()
    for i in range(segs):
        t = (i + 1) / segs
        d = (d + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * wander / segs * 2.2 + Vector((0, 0, lift * (1 - t) - droop * t)) / segs * 2.0).normalized()
        pts.append(pts[-1] + d * length / segs)
    return pts


def _smooth(ctrl, per=4):
    """A curve through the corner points (Catmull-Rom), `per` steps between each two."""
    q = [Vector(c) for c in ctrl]
    q = [q[0] * 2 - q[1]] + q + [q[-1] * 2 - q[-2]]
    out = []
    for i in range(1, len(q) - 2):
        p0, p1, p2, p3 = q[i - 1], q[i], q[i + 1], q[i + 2]
        for k in range(per):
            t = k / per
            out.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t))
    out.append(q[-2])
    return out


def _at(path, t):
    """Point and direction at t (0..1) along a path."""
    f = max(0.0, min(1.0, t)) * (len(path) - 1)
    i = min(len(path) - 2, int(f))
    return path[i].lerp(path[i + 1], f - i), (path[i + 1] - path[i]).normalized()


def _bark_tube(pts, radii, rng, sides=8):
    t = tube(pts, radii, sides, BARK, caps=True, angle=70)
    strips = {}
    t.bm.faces.index_update()
    def col(f, l):
        k = f.index % sides
        if k not in strips:
            strips[k] = jitter_color(BARK if rng.random() < .7 else BARK_LIGHT, rng, .14, .015)
        return strips[k]
    t.paint_fn(col)
    return t


class Tree:
    """A broadleaf tree drawn the way one sketches it: the trunk and the main boughs are curves
    through given points (so the trees that frame the view are composed, not rolled), and smaller
    branches, twigs and leaves grow out of them by rule.
    All coordinates are relative to the foot of the tree. Ends up as .wood (a piece) and .pl (leaves)."""

    def __init__(self, rng, leaf_color=LEAF, leaf_size=0.14, twigs=True, leaves=1.0):
        self.rng, self.wood, self.pl = rng, P(), Plants()
        self.leaf_color, self.leaf_size, self.twigs, self.leafiness = leaf_color, leaf_size, twigs, leaves

    def limb(self, ctrl, r0, r1=None, sides=8, per=4, flare=0.0):
        """A trunk or a bough through the points. r0, r1: radius at its start and end. flare: widening at the foot."""
        path = _smooth(ctrl, per)
        n = len(path) - 1
        r1 = r0 * 0.4 if r1 is None else r1
        radii = [r0 + (r1 - r0) * (i / n) ** 0.85 for i in range(n + 1)]
        if flare:
            for i in range(min(per, n)):
                radii[i] *= 1 + flare * (1 - i / per) ** 2
        self.wood.add(_bark_tube(path, radii, self.rng, sides))
        return path, radii

    def roots(self, r, count=5):
        rng = self.rng
        for k in range(count):
            a = k / count * 6.28 + rng.uniform(-.35, .35)
            c, s = math.cos(a), math.sin(a)
            L = rng.uniform(2.2, 3.4)
            rp = [Vector((c * r * .7, s * r * .7, 0.24)), Vector((c * r * 1.7, s * r * 1.7, 0.06)), Vector((c * r * L, s * r * L, -0.08))]
            self.wood.add(_bark_tube(rp, [r * .42, r * .3, r * .1], rng, 6))

    def stubs(self, path, radii, count, t0=0.15, t1=0.7, length=0.3):
        """Short bare twigs and broken-off branches along a trunk."""
        rng = self.rng
        for _ in range(count):
            t = rng.uniform(t0, t1)
            p0, along = _at(path, t)
            a = rng.uniform(0, 6.28)
            d = Vector((math.cos(a), math.sin(a), rng.uniform(.2, .9)))
            L = length * rng.uniform(.5, 1.4)
            pts = _wander(p0, d, L, rng, 3, droop=-0.1, wander=.5)
            r = radii[min(len(radii) - 1, int(t * (len(radii) - 1)))] * rng.uniform(.14, .22)
            self.wood.add(_bark_tube(pts, [r, r * .7, r * .45, r * .15], rng, 4))

    def leaves(self, path, count, t0=0.15):
        """Leaves along a twig, left and right in turn, hanging a little."""
        rng = self.rng
        count = max(1, int(round(count * self.leafiness)))
        for i in range(count):
            t = t0 + (1 - t0) * (i + rng.uniform(.1, .9)) / count
            pos, along = _at(path, t)
            side = along.cross(Vector((0, 0, 1)))
            side = side.normalized() if side.length > 1e-3 else Vector((1, 0, 0))
            s = 1 if i % 2 == 0 else -1
            d = along * rng.uniform(.25, .8) + side * s * rng.uniform(.5, 1.0) + Vector((rng.uniform(-.3, .3), rng.uniform(-.3, .3), -rng.uniform(.15, .8)))
            leaf(self.pl, pos, d, self.leaf_size * rng.uniform(.75, 1.25), rng, self.leaf_color)
        tip, along = _at(path, 1.0)
        leaf(self.pl, tip, along + Vector((0, 0, -.3)), self.leaf_size * rng.uniform(.9, 1.3), rng, self.leaf_color)

    def grow(self, start, direction, length, radius, depth=1, droop=0.3, lift=0.2, twig_leaves=8):
        """A branch that wanders off from a point and splits again; the last ones carry the leaves."""
        rng = self.rng
        depth = min(depth, 2)
        segs = (6, 5, 4)[depth]
        path = _wander(start, direction, length, rng, segs, droop=droop * (0.6 + 0.4 * depth), wander=0.3 + 0.12 * depth, lift=lift if depth < 2 else 0.0)
        rr = [max(radius * (1 - 0.82 * i / segs), 0.0035) for i in range(segs + 1)]
        if depth < 2 or self.twigs:           # the thinnest twigs of far trees are only their leaves
            self.wood.add(_bark_tube(path, rr, rng, (6, 5, 3)[depth]))
        if depth >= 2:
            self.leaves(path, twig_leaves)
            return path
        self.spray(path, rr, 3 if depth == 0 else rng.randint(2, 3), length * 0.55, 0.3, 0.95, depth + 1, droop + 0.15, lift * 0.5, twig_leaves)
        if depth == 1:
            self.leaves(path, twig_leaves // 2, 0.5)
        return path

    def spray(self, path, radii, count, length, t0=0.3, t1=1.0, depth=1, droop=0.3, lift=0.2, twig_leaves=8, toward=None):
        """Branches out of a limb, turning around it as they go up. toward: lean them all that way (x, y, z)."""
        rng = self.rng
        turn = rng.uniform(0, 6.28)
        for j in range(count):
            t = t0 + (t1 - t0) * (j + rng.uniform(.2, .8)) / count
            p0, along = _at(path, t)
            r = radii[min(len(radii) - 1, int(t * (len(radii) - 1)))]
            ref = Vector((0, 0, 1)) if abs(along.z) < 0.9 else Vector((1, 0, 0))
            s = along.cross(ref).normalized()
            u = s.cross(along).normalized()
            a = turn + j * 2.4 + rng.uniform(-.5, .5)
            d = along * rng.uniform(.35, .7) + (s * math.cos(a) + u * math.sin(a)) * rng.uniform(.7, 1.0)
            if toward is not None:
                d = d.normalized() * 0.6 + Vector(toward).normalized()
            self.grow(p0, d, length * rng.uniform(.7, 1.15) * (1.0 - 0.45 * t), r * 0.5, depth, droop, lift, twig_leaves)

    def hangers(self, path, radii, count, length, t0=0.2, t1=1.0, twig_leaves=9, sway=(0.0, 0.0)):
        """Leafy twigs that hang down from a bough, the kind that dip into the top of a picture."""
        rng = self.rng
        for j in range(count):
            t = t0 + (t1 - t0) * (j + rng.uniform(.15, .85)) / count
            p0, along = _at(path, t)
            r = radii[min(len(radii) - 1, int(t * (len(radii) - 1)))]
            d = Vector((rng.uniform(-.5, .5) + sway[0], rng.uniform(-.5, .5) + sway[1], -1.0)) + along * rng.uniform(.1, .5)
            L = length * rng.uniform(.6, 1.2)
            twig = _wander(p0, d, L, rng, 5, droop=0.5, wander=0.3)
            rr = [max(r * 0.28 * (1 - 0.8 * i / 5), 0.0035) for i in range(6)]
            self.wood.add(_bark_tube(twig, rr, rng, 4))
            self.leaves(twig, twig_leaves, 0.25)
            # one or two side twigs
            for _ in range(rng.randint(1, 2)):
                q, al = _at(twig, rng.uniform(.25, .7))
                dd = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), -rng.uniform(.3, 1.0))) + al * .3
                side = _wander(q, dd, L * rng.uniform(.3, .5), rng, 3, droop=0.5, wander=0.3)
                self.wood.add(_bark_tube(side, [0.006, 0.005, 0.004, 0.0035], rng, 3))
                self.leaves(side, max(2, twig_leaves // 2), 0.2)

    def crown(self, path, radii, count, length, t0=0.55, t1=0.98, twig_leaves=8):
        """Limbs fanning out of the upper part of a trunk."""
        self.spray(path, radii, count, length, t0, t1, 0, 0.25, 0.5, twig_leaves)


def tree(rng, height=7.0, base_r=0.09, lean=(0.0, 0.0), bend=0.25, fork=0.5, leaf_color=LEAF, leaf_size=0.14, twig_leaves=8, crown_limbs=6, low_limbs=0, twigs=True, leaves=1.0, crown_length=0.24):
    """A slender tree for the background, never straight: it leans, bends once or twice, and may fork.
    lean: how far the top ends up from the foot (x, y), as a part of the height."""
    t = Tree(rng, leaf_color, leaf_size, twigs, leaves)
    a = rng.uniform(0, 6.28)
    bx, by = math.cos(a) * bend, math.sin(a) * bend
    ctrl = []
    n = 6
    ph = rng.uniform(0, 6.28)
    for i in range(n + 1):
        u = i / n
        w = math.sin(u * 3.6 + ph) * (0.35 + 0.65 * u)
        ctrl.append((lean[0] * height * u ** 1.4 + bx * w, lean[1] * height * u ** 1.4 + by * w, -0.15 if i == 0 else height * u))
    path, radii = t.limb(ctrl, base_r, base_r * 0.22, sides=8, per=3, flare=0.6)
    t.roots(base_r, 4)
    t.stubs(path, radii, rng.randint(1, 3), 0.15, 0.5, 0.28)
    if rng.random() < fork:
        # a second stem leaving the trunk low and rising beside it
        ft = rng.uniform(.22, .4)
        p0, along = _at(path, ft)
        fa = rng.uniform(0, 6.28)
        out = Vector((math.cos(fa), math.sin(fa), 0))
        L = height * (1 - ft) * rng.uniform(.7, .95)
        c2 = [p0, p0 + out * L * .12 + Vector((0, 0, L * .18)), p0 + out * L * .22 + Vector((0, 0, L * .5)), p0 + out * L * .26 + Vector((bx * .5, by * .5, L))]
        p2, r2 = t.limb(c2, radii[int(ft * (len(radii) - 1))] * .7, base_r * 0.15, sides=6, per=3)
        t.crown(p2, r2, max(2, crown_limbs // 2), height * .22, 0.45, 0.98, twig_leaves)
    t.crown(path, radii, crown_limbs, height * crown_length * rng.uniform(.85, 1.15), 0.5, 0.98, twig_leaves)
    for _ in range(low_limbs):
        # a limb low on the trunk, reaching out level and lifting at its end
        lt = rng.uniform(.2, .4)
        p0, along = _at(path, lt)
        la = rng.uniform(0, 6.28)
        t.grow(p0, (math.cos(la), math.sin(la), rng.uniform(.1, .5)), height * rng.uniform(.2, .3), radii[int(lt * (len(radii) - 1))] * .45, 0, 0.15, 0.6, twig_leaves)
    return t.wood, t.pl


def conifer(rng, height=9.0, color=hexc('#274238')):
    """Jagged fir for the middle distance: tiers of drooping boughs."""
    wood = cylinder(height * 0.022, height * 0.006, height * 0.98, 6, BARK)
    pl = Plants()
    tiers = int(height * 1.6)
    for i in range(tiers):
        t = i / (tiers - 1)
        z = height * (0.16 + 0.84 * t)
        r = height * 0.21 * (1 - t) ** 0.9 + 0.08
        boughs = max(5, int(9 - 4 * t))
        a0 = rng.uniform(0, 6.28)
        for k in range(boughs):
            a = a0 + k / boughs * 6.28 + rng.uniform(-.2, .2)
            d = Vector((math.cos(a), math.sin(a), 0))
            s = Vector((-d.y, d.x, 0))
            rr = r * rng.uniform(.75, 1.15)
            wdt = rr * rng.uniform(.34, .5)
            base = Vector((0, 0, z))
            tip = base + d * rr - Vector((0, 0, rr * rng.uniform(.35, .6)))
            mid = base + d * rr * .45 - Vector((0, 0, rr * .08))
            c = jitter_color(color, rng, .14, .02)
            pl.poly([base, mid + s * wdt, tip, mid - s * wdt], (0, 0, z), c, [0, .5, 1, .5], [0, .5, 1, .5])
    return wood, pl


def bush(pl, x, y, z, rng, radius=0.45, color=LEAF2, leaf_size=0.10, count=140):
    for _ in range(count):
        v = Vector((rng.gauss(0, 1), rng.gauss(0, 1), abs(rng.gauss(0, 1)) * 0.8)).normalized()
        r = radius * rng.uniform(.55, 1.0)
        pos = Vector((x, y, z)) + Vector((v.x * r, v.y * r, v.z * r * 0.8 + 0.04))
        d = (v + Vector((rng.uniform(-.6, .6), rng.uniform(-.6, .6), rng.uniform(-.3, .5)))).normalized()
        leaf(pl, pos, d, leaf_size * rng.uniform(.7, 1.3), rng, color)


# ------------------------------------------------------------------------------ previews
def tree_test(rng):
    wood, pl = tree(rng, 7.0, 0.09, lean=(-0.06, 0.02))
    return {'wood': wood, 'leaves': pl.piece()}


def conifer_test(rng):
    wood, pl = conifer(rng, 9.0)
    return {'wood': wood, 'boughs': pl.piece()}


def plants_test(rng):
    pl = Plants()
    for i in range(160):
        x, y = rng.uniform(-1, 1), rng.uniform(-.8, .8)
        tuft(pl, x, y, 0, rng, height=rng.uniform(.07, .14))
    fern(pl, -0.5, -0.3, 0, rng)
    fern(pl, 0.55, 0.1, 0, rng, length=.42)
    bush(pl, 0.0, 0.6, 0, rng, radius=.35)
    for i in range(8): reed(pl, 0.8 + rng.uniform(-.1, .1), -0.5 + rng.uniform(-.1, .1), 0, rng)
    for i in range(10): flower(pl, rng.uniform(-.3, .3), rng.uniform(-.7, -.3), 0, rng)
    return {'plants': pl.piece()}
