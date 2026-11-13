"""Builds every model of the camp and writes them for Unity.

    python tools/camp/blender/build_camp.py [--preview out.png] [--wide] [--yaw degrees] [--fast] [--draft]

--fast   fewer plants and a coarser bake (for trying things out)
--draft  writes nothing but the preview picture

Writes  Assets/IORI/Art/Camp/Models/camp.iomesh       all meshes (read by IoMeshImporter)
        Assets/IORI/Art/Camp/Models/camp_layout.json   where each one goes
        Assets/IORI/Art/Camp/Textures/iori_ground.png  ground cover (R dirt, G moss, B sand) and baked shade (A)
"""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np
from PIL import Image
from campkit import *
import terrain as T
import models_fire, models_props, models_nature as N

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
ART = os.path.join(REPO, 'Assets', 'IORI', 'Art', 'Camp')
FAST = '--fast' in sys.argv
DRAFT = '--draft' in sys.argv

# the view the scene is composed for (authoring frame: x, depth, height)
CAM_POS, CAM_TARGET, CAM_FOV = (0.0, -4.67, 1.48), (0.0, 0.0, 0.95), 42.0
# ground texture covers this square of the world
MAP_X0, MAP_Y0, MAP_SIZE = -9.0, -6.5, 18.0

rng = random.Random(20261113)
S = Scene()
gz = lambda x, y: float(T.height(x, y))


# ------------------------------------------------------------------ ground
def build_ground():
    nx, ny = (110, 90) if FAST else (170, 140)
    us, vs = np.linspace(-1, 1, nx + 1), np.linspace(-1, 1, ny + 1)
    X = 30.0 * (0.16 * us + 0.84 * np.sign(us) * np.abs(us) ** 2.7)
    Y = -0.3 + np.where(vs >= 0, 12.5, 6.2) * (0.20 * np.abs(vs) + 0.80 * np.abs(vs) ** 2.6) * np.sign(vs)
    gx, gy = np.meshgrid(X, Y)
    jit = np.random.RandomState(7)
    gx = gx + (jit.rand(*gx.shape) - .5) * np.gradient(X)[None, :] * 0.5
    gy = gy + (jit.rand(*gy.shape) - .5) * np.gradient(Y)[:, None] * 0.5
    gh = T.height(gx, gy)
    verts = np.stack([gx, gy, gh], -1).reshape(-1, 3)
    faces = []
    for j in range(ny):
        for i in range(nx):
            a = j * (nx + 1) + i
            faces.append((a, a + 1, a + nx + 2, a + nx + 1))
    g = P.from_pydata([tuple(v) for v in verts], faces, (1, 1, 1), smooth=True)
    dirt, sand, moss = T.cover(verts[:, 0], verts[:, 1])
    cg, cd, cs = np.array(hexc('#41683f')), np.array(hexc('#5d4a3c')), np.array(hexc('#8d8672'))
    vc = cg[None] * (1 - dirt - sand)[:, None] + cd[None] * dirt[:, None] + cs[None] * sand[:, None]
    g.bm.verts.ensure_lookup_table()
    for f in g.bm.faces:
        for l in f.loops:
            c = vc[l.vert.index]
            l[g.col] = (c[0], c[1], c[2], 1.0)
    g.planar_uv((MAP_X0, MAP_Y0, 0), (1, 0, 0), (0, 1, 0), 1.0 / MAP_SIZE)
    return S.put('Ground', g, 'Ground', layer='CampGround', ao=False, shadows=False)


def write_ground_map(ground_item):
    n = 256 if FAST else 512
    lin = (np.arange(n) + .5) / n
    mx, my = np.meshgrid(MAP_X0 + lin * MAP_SIZE, MAP_Y0 + lin * MAP_SIZE)
    dirt, sand, moss = T.cover(mx, my)
    size = 512 if FAST else 1024
    ao = S.bake_ao_image(ground_item, size, 16 if FAST else 64, 0.7)
    def up(a):
        return np.asarray(Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC), np.float32) / 255
    rgba = np.stack([up(dirt), up(moss), up(sand), np.clip(ao, 0, 1)], -1)
    img = Image.fromarray((rgba * 255 + .5).astype(np.uint8)[::-1], 'RGBA')   # row 0 of the file = far edge (v = 1)
    os.makedirs(os.path.join(ART, 'Textures'), exist_ok=True)
    img.save(os.path.join(ART, 'Textures', 'iori_ground.png'))


# ------------------------------------------------------------------ props
def place(name, piece, xy, z=None, rot=0.0, anchor=None, material='Prop', **kw):
    x, y = xy
    return S.put(name, piece, material, pos=(x, y, gz(x, y) if z is None else z), rot_z=rot, anchor=anchor, **kw)


def build_props():
    fx, fy = T.FIRE
    fz = gz(fx, fy)
    for name, piece in models_fire.build_fire(rng).items():
        # the logs cast no shadows: the fire's light is one point inside the pile, and their shadows would
        # fall across the whole camp as hard stripes (a real fire is wide and its light flows around them)
        S.put(name, piece, 'Rock' if name == 'FireStones' else 'Ember', pos=(fx, fy, fz), anchor='fire', shadows=(name == 'FireStones'))
    # table with the lantern, a mug and the kettle
    tx, ty = T.TABLE
    tz, trot = gz(tx, ty), 10.0
    place('Table', models_props.table(rng), T.TABLE, rot=trot, anchor='table')
    def on_table(lx, ly):
        a = math.radians(trot)
        return (tx + lx * math.cos(a) - ly * math.sin(a), ty + lx * math.sin(a) + ly * math.cos(a))
    place('Lantern', models_props.lantern(rng), on_table(-0.10, -0.04), z=tz + 0.40, rot=20, anchor='lantern')
    place('Mug', models_props.mug(rng), on_table(0.17, -0.10), z=tz + 0.40, rot=-140, anchor='table')
    place('Kettle', models_props.kettle(rng), on_table(0.13, 0.09), z=tz + 0.40, rot=150, anchor='table')
    # chair, turned toward the fire
    cx, cy = T.CHAIR
    ang = math.degrees(math.atan2(fy - cy, fx - cx)) + 90
    place('Chair', models_props.chair(rng), T.CHAIR, rot=ang, anchor='iori')
    # radio on the crate
    kx, ky = T.CRATE
    kz = gz(kx, ky)
    place('Crate', models_props.crate(rng), T.CRATE, rot=-14, anchor='radio')
    place('Radio', models_props.radio(rng), (kx - 0.02, ky - 0.03), z=kz + 0.306, rot=14, anchor='radio')
    # tape deck on the stump
    sx, sy = T.STUMP
    place('Stump', models_props.stump(rng, 0.19, 0.29), T.STUMP, rot=30, anchor='player')
    place('Deck', models_props.deck(rng), (sx - 0.01, sy - 0.01), z=gz(sx, sy) + 0.292, rot=-16, anchor='player')
    place('Firewood', models_props.firewood(rng), (1.42, 1.25), rot=65)
    place('Tent', models_props.tent(rng), T.TENT, rot=-12, anchor='tent', material='Tent', shadows=False)
    # fence along the water, a few leaning posts with a slack rope
    posts = [(-0.35, 6.75), (0.35, 6.9), (1.05, 6.85), (1.8, 7.0), (2.6, 6.9), (3.4, 7.05)]
    fence = P()
    tops = []
    for (px, py) in posts:
        h = rng.uniform(.85, 1.0)
        lean = (rng.uniform(-.06, .06), rng.uniform(-.06, .06))
        z0 = gz(px, py) - 0.1
        fence.add(tube([(px, py, z0), (px + lean[0], py + lean[1], z0 + h)], [0.036, 0.03], 6, jitter_color(hexc('#4a3d3a'), rng, .1)))
        tops.append(Vector((px + lean[0] * .8, py + lean[1] * .8, z0 + h * .82)))
    for a, b in zip(tops[:-1], tops[1:]):
        pts = [a.lerp(b, i / 8) - Vector((0, 0, math.sin(i / 8 * math.pi) * 0.09)) for i in range(9)]
        fence.add(tube(pts, 0.006, 3, hexc('#6a5a50'), caps=False))
    S.put('Fence', fence, 'Prop', ao=False)


# ------------------------------------------------------------------ rocks
def build_rocks():
    rocks = P()
    cols = [hexc('#6a7286'), hexc('#5d6679'), hexc('#77798a'), hexc('#646a74')]
    spots = [(1.95, 3.1, .55), (2.55, 3.45, .30), (-2.7, 1.2, .42), (-3.2, 0.7, .22), (0.9, 7.2, .45), (1.5, 7.5, .28), (2.6, 7.3, .5), (-0.9, 7.6, .4),
             (-3.6, 4.9, .6), (4.4, 0.6, .7), (-4.8, -1.2, .65), (3.1, -2.2, .4), (-1.55, -1.75, .2), (1.35, -2.05, .24), (-4.2, 7.4, .7), (5.6, 6.6, .9),
             (-1.5, 1.15, .13), (1.55, 0.2, .11)]
    for (x, y, s) in spots:
        r = N.boulder(rng, s, rng.choice(cols))
        r.rot('Z', rng.uniform(0, 360)).move(x, y, gz(x, y) + s * 0.22)
        rocks.add(r)
    for _ in range(60 if not FAST else 20):       # pebbles, mostly near the water and around the camp
        if rng.random() < .6:
            x = rng.uniform(-6, 6); y = float(T.shore_y(x)) - rng.uniform(-.4, 1.2)
        else:
            x, y = rng.uniform(-3.5, 3.5), rng.uniform(-2.4, 4.5)
        if math.hypot(x - T.FIRE[0], y - T.FIRE[1]) < .7: continue
        s = rng.uniform(.03, .09)
        r = models_fire.rock(s, rng, rng.choice(cols), n=12, squash=.6)
        r.move(x, y, gz(x, y) + s * .25)
        rocks.add(r)
    S.put('Rocks', rocks, 'Rock')


# ------------------------------------------------------------------ trees
def build_trees():
    wood_all, leaves_all = P(), N.Plants()
    def plant(xy, wood, pl):
        x, y = xy
        z = gz(x, y)
        wood.move(x, y, z)
        wood_all.add(wood)
        leaves_all.extend(pl, (x, y, z))

    # The two trees that frame the view are drawn by hand. Only their lower three metres are in
    # the picture, so what shows of them is: the trunk, the fork, and one bough each that arches
    # toward the camera just above the top edge, with leafy twigs hanging down into the picture
    # (heavy in the top left corner, light across the middle, where the moon passes).
    # left: a big tree whose foot stands outside the picture; it leans in as it rises
    t = N.Tree(random.Random(202), leaf_size=0.15)
    path, radii = t.limb([(0, 0, -.15), (.03, -.01, .55), (.13, -.03, 1.35), (.29, -.06, 2.15), (.44, -.10, 2.85), (.52, -.08, 3.7), (.46, .02, 4.7), (.62, .10, 5.9), (.50, .20, 7.4)], 0.105, 0.03, sides=9, flare=0.7)
    t.roots(0.105, 5)
    t.stubs(path, radii, 3, 0.12, 0.33, 0.3)
    arch, ar = t.limb([(.44, -.10, 2.85), (.70, -.50, 3.20), (1.00, -1.10, 3.38), (1.30, -1.80, 3.36), (1.50, -2.40, 3.20), (1.60, -2.85, 3.00)], 0.058, 0.012, sides=7)
    t.hangers(arch, ar, 5, 0.80, 0.12, 0.50, twig_leaves=10)
    t.hangers(arch, ar, 5, 0.52, 0.50, 1.00, twig_leaves=8)
    t.spray(arch, ar, 3, 1.1, 0.25, 0.9, 1, 0.2, 0.3, 9, toward=(0.2, -0.4, 1.0))        # the bough's own leaves, above the picture
    back, br = t.limb([(.29, -.06, 2.15), (-.15, .30, 2.65), (-.80, .65, 3.25), (-1.50, .85, 3.60)], 0.045, 0.012, sides=6)
    t.spray(back, br, 4, 1.2, 0.3, 1.0, 1, 0.3, 0.3, 9)
    t.crown(path, radii, 7, 2.1, 0.5, 0.98, 9)
    plant(T.TREE_L, t.wood, t.pl)
    # right: a slimmer one, nearly upright, with a slow double bend
    t = N.Tree(random.Random(101), leaf_size=0.15)
    path, radii = t.limb([(0, 0, -.15), (-.02, 0, .6), (.04, -.02, 1.4), (-.03, -.03, 2.2), (-.13, -.06, 3.0), (-.12, -.10, 4.0), (-.28, -.10, 5.2), (-.22, 0, 6.6)], 0.075, 0.02, sides=8, flare=0.7)
    t.roots(0.075, 5)
    t.stubs(path, radii, 3, 0.2, 0.42, 0.26)
    arch, ar = t.limb([(-.13, -.06, 3.0), (-.40, -.45, 3.30), (-.65, -1.05, 3.42), (-.80, -1.70, 3.30), (-.85, -2.30, 3.05)], 0.042, 0.010, sides=6)
    t.hangers(arch, ar, 6, 0.62, 0.15, 1.0, twig_leaves=8)
    t.spray(arch, ar, 3, 1.0, 0.25, 0.9, 1, 0.2, 0.3, 9, toward=(-0.2, -0.3, 1.0))
    side, sr = t.limb([(-.03, -.03, 2.2), (.35, .25, 2.7), (.95, .45, 3.2), (1.55, .50, 3.5)], 0.034, 0.010, sides=6)
    t.spray(side, sr, 4, 1.1, 0.3, 1.0, 1, 0.3, 0.3, 9)
    t.crown(path, radii, 6, 1.9, 0.5, 0.98, 9)
    plant(T.TREE_R, t.wood, t.pl)

    # more trees further back and to the sides, for depth when the camera looks around: each leans
    # and bends its own way, some fork, and a few low limbs keep leaves where the picture can see them
    spots = [((-2.7, 0.9), 7.0), ((2.85, 0.5), 7.4), ((-3.5, 4.3), 8.0), ((3.3, 4.8), 7.2), ((4.9, 1.8), 8.4), ((-5.2, 2.2), 8.0),
             ((-4.4, -1.6), 7.5), ((4.2, -2.0), 7.8), ((6.3, 4.6), 8.5), ((-6.6, 5.2), 8.2)]
    for i, (xy, h) in enumerate(spots):
        r = random.Random(300 + i)
        away = 1 if xy[0] > 0 else -1
        wood, pl = N.tree(r, height=h, base_r=r.uniform(.06, .09), lean=(away * r.uniform(-.04, .12), r.uniform(-.06, .06)), bend=r.uniform(.28, .6),
                          fork=0.55, leaf_size=0.14, twig_leaves=7, crown_limbs=6, low_limbs=r.randint(1, 3), twigs=False, leaves=0.8)
        plant(xy, wood, pl)
    # young trees, whole in the picture: thin stems, few leaves
    for i, (xy, h) in enumerate([((-3.0, 2.6), 3.2), ((3.7, 2.9), 2.8), ((-2.2, 6.3), 3.4), ((2.7, 6.4), 3.0), ((5.4, 4.0), 3.6), ((-5.0, 4.6), 3.3), ((-3.4, -0.9), 2.6)]):
        r = random.Random(400 + i)
        wood, pl = N.tree(r, height=h, base_r=r.uniform(.022, .032), lean=(r.uniform(-.1, .1), r.uniform(-.06, .06)), bend=r.uniform(.08, .16),
                          fork=0.4, leaf_size=0.13, twig_leaves=6, crown_limbs=5, low_limbs=0, twigs=True, leaves=0.9, crown_length=0.34)
        plant(xy, wood, pl)
    S.put('TreeWood', wood_all, 'Bark')
    S.put('TreeLeaves', leaves_all.piece(), 'Leaf', ao=False, occluder=False)

    # firs in the middle distance, on both sides and on the far points of the shore
    cw, cp = P(), N.Plants()
    spots = []
    for _ in range(46 if not FAST else 16):
        side = -1 if rng.random() < .5 else 1
        x = side * rng.uniform(6.5, 26)
        y = rng.uniform(-3, 8.5)
        if T.height(x, y) < T.WATER_Z + 0.15: continue
        spots.append((x, y, rng.uniform(7, 12)))
    for (x, y, h) in spots:
        r = random.Random(int(x * 100 + y * 7))
        w, pl = N.conifer(r, h)
        z = gz(x, y) - 0.2
        w.move(x, y, z); cw.add(w)
        cp.extend(pl, (x, y, z), sway=0.25, center=(x, y))
    S.put('FirTrunks', cw, 'Bark', ao=False, occluder=False, shadows=False)
    S.put('Firs', cp.piece(), 'Fir', ao=False, occluder=False, shadows=False)


# ------------------------------------------------------------------ plants
def blocked(x, y):
    if math.hypot(x - T.FIRE[0], y - T.FIRE[1]) < 0.52: return True
    if math.hypot(x - T.STUMP[0], y - T.STUMP[1]) < 0.30: return True
    if abs(x - T.CRATE[0]) < 0.25 and abs(y - T.CRATE[1]) < 0.20: return True
    if math.hypot(x - T.TENT[0], y - T.TENT[1]) < 1.2: return True
    return False


def build_plants():
    grasses, plants = [N.Plants(), N.Plants(), N.Plants()], N.Plants()
    pick = lambda: grasses[0] if rng.random() < 0.40 else grasses[1 + (rng.random() < 0.5)]   # density tiers: Grass0 is always drawn
    def scatter(count, xr, yr, hmin, hmax, blades, width, wild=0.0, segs=3):
        xs = np.array([rng.uniform(*xr) for _ in range(count)]); ys = np.array([rng.uniform(*yr) for _ in range(count)])
        dirt, sand, moss = T.cover(xs, ys)
        hs = T.height(xs, ys)
        bw = T.bowl(xs, ys)
        for i in range(count):
            x, y = float(xs[i]), float(ys[i])
            keep = (1 - dirt[i]) * (1 - sand[i])
            if rng.random() > keep * keep or bw[i] > 0.35 or hs[i] < T.WATER_Z + 0.08 or blocked(x, y): continue
            dry = rng.random() < 0.10
            N.tuft(pick(), x, y, float(hs[i]), rng, height=rng.uniform(hmin, hmax) * (1 + wild * moss[i]), blades=blades, spread=0.045,
                   color=N.GRASS_DRY if dry else N.GRASS_A, tip=hexc('#b0a868') if dry else N.GRASS_B, width=width, segs=segs)
    k = 0.35 if FAST else 1.0
    # thin blades, many to a tuft: fine marks close to the camera (the far ones are simpler and a little wider)
    scatter(int(5200 * k), (-2.6, 2.6), (-2.6, 2.2), .06, .13, 6, .015)                    # short grass around the camp
    scatter(int(3600 * k), (-5.5, 5.5), (-3.0, 7.6), .09, .20, 6, .020, wild=.8, segs=2)   # longer further out
    scatter(int(1600 * k), (-11, 11), (-4.0, 8.0), .14, .30, 5, .032, wild=1.0, segs=2)
    # taller clumps hugging things
    for (cx, cy, r, n) in [(T.STUMP[0], T.STUMP[1], .34, 16), (T.CRATE[0], T.CRATE[1], .36, 14), (T.TREE_R[0], T.TREE_R[1], .32, 22), (T.TREE_L[0], T.TREE_L[1], .34, 22),
                           (1.95, 3.1, .7, 26), (-2.7, 1.2, .55, 20), (T.TABLE[0] - .35, T.TABLE[1] + .25, .25, 8), (1.42, 1.25, .4, 12)]:
        for _ in range(n):
            a = rng.uniform(0, 6.28); d = r * rng.uniform(.85, 1.25)
            x, y = cx + math.cos(a) * d, cy + math.sin(a) * d
            N.tuft(grasses[0], x, y, gz(x, y), rng, height=rng.uniform(.16, .30), blades=8, spread=.05, width=.02)
    for i, g in enumerate(grasses):
        S.put('Grass%d' % i, g.piece(), 'Grass', layer='CampGround', ao=False, occluder=False, shadows=False)

    # ferns closing the bottom of the frame, bushes, reeds in the shallows, a few flowers
    for (x, y, L) in [(-0.66, -1.62, .50), (0.86, -1.80, .5), (0.12, -1.86, .46), (-1.25, -1.2, .42), (1.25, -0.9, .4), (-1.8, 0.3, .5), (1.75, 0.9, .5), (-1.45, 2.9, .5), (1.4, 2.3, .45), (2.3, 2.8, .5), (-2.3, 1.6, .5)]:
        N.fern(plants, x, y, gz(x, y), rng, fronds=rng.randint(6, 9), length=L)
    for (x, y, r) in [(-1.5, 3.2, .45), (2.45, 3.9, .55), (-3.1, 1.6, .5), (3.2, 0.2, .55), (-2.6, 5.0, .5), (0.6, 6.6, .4), (3.9, 5.6, .6), (-4.2, 3.2, .7), (4.8, 3.4, .7), (-0.9, 6.4, .35)]:
        N.bush(plants, x, y, gz(x, y), rng, radius=r, count=int(260 * r / .45))
    for _ in range(90 if not FAST else 30):
        x = rng.uniform(-7, 7)
        y = float(T.shore_y(x)) + rng.uniform(.5, 1.5)
        if -0.2 < x < 1.6 and rng.random() < .7: continue      # keep the view to the water open in the middle
        N.reed(plants, x, y, T.WATER_Z - 0.05, rng, height=rng.uniform(.7, 1.3))
    for _ in range(70 if not FAST else 20):
        x, y = rng.uniform(-2.6, 2.6), rng.uniform(-2.4, 3.0)
        dirt, sand, moss = T.cover(x, y)
        if dirt > .3 or blocked(x, y): continue
        N.flower(plants, x, y, gz(x, y), rng)
    S.put('Plants', plants.piece(), 'Plant', ao=False, occluder=False, shadows=False)


# ------------------------------------------------------------------ run
ground = build_ground()
build_props()
build_rocks()
build_trees()
build_plants()
anchors = dict(fire=T.FIRE, iori=T.IORI, table=T.TABLE, radio=T.CRATE, player=T.STUMP, tent=T.TENT)
extra = dict(
    camera=dict(position=[CAM_POS[0], CAM_POS[2], CAM_POS[1]], target=[CAM_TARGET[0], CAM_TARGET[2], CAM_TARGET[1]], fov=CAM_FOV),
    anchors={k: [v[0], gz(*v), v[1]] for k, v in anchors.items()},
    groundMap=dict(x0=MAP_X0, z0=MAP_Y0, size=MAP_SIZE),
    waterY=T.WATER_Z, puddleY=T.PUDDLE_Z,
    puddleArea=dict(halfWidth=T.PUDDLE_AREA[0], z0=T.PUDDLE_AREA[1], z1=T.PUDDLE_AREA[2]),
)
if not DRAFT:
    if not FAST:
        S.bake_ao(samples=24, distance=0.6)
    write_ground_map(ground)
    mesh_path = os.path.join(ART, 'Models', 'camp.iomesh')
    layout_path = os.path.join(ART, 'Models', 'camp_layout.json')
    S.export(mesh_path, layout_path)
    lay = json.load(open(layout_path, encoding='utf-8'))
    # the lantern gets its own anchor position (it sits on the table)
    for o in lay['objects']:
        if o['name'] == 'Lantern':
            extra['anchors']['lantern'] = o['position']
    lay.update(extra)
    json.dump(lay, open(layout_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

if '--preview' in sys.argv:
    out = sys.argv[sys.argv.index('--preview') + 1]
    fx, fy = T.FIRE
    lights = [((fx, fy, gz(fx, fy) + 0.35), (1.0, 0.5, 0.2), 60, 0.08)]
    if 'lantern' in extra['anchors']:
        lx, ly, lz = extra['anchors']['lantern'][0], extra['anchors']['lantern'][2], extra['anchors']['lantern'][1]
        lights.append(((lx, ly, lz + 0.14), (1.0, 0.8, 0.55), 6, 0.03))
    res = (540, 1200)
    if '--wide' in sys.argv: res = (1280, 720)
    # --yaw: walk the camera around the point it looks at, the way dragging does in the app
    yaw = math.radians(float(sys.argv[sys.argv.index('--yaw') + 1])) if '--yaw' in sys.argv else 0.0
    ox, oy = CAM_POS[0] - CAM_TARGET[0], CAM_POS[1] - CAM_TARGET[1]
    pos = (CAM_TARGET[0] + ox * math.cos(yaw) - oy * math.sin(yaw), CAM_TARGET[1] + ox * math.sin(yaw) + oy * math.cos(yaw), CAM_POS[2])
    S.preview(out, pos, CAM_TARGET, CAM_FOV if res[0] < res[1] else 30, res, 24 if FAST else 64, lights=lights, sky_strength=0.9 if DRAFT else 0.22)
