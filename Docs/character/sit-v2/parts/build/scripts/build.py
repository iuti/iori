"""Cut the layers from the final picture and the chosen fill-ins, write PNGs + model.json into out/iori/."""
import os, json, math
import numpy as np, cv2
from PIL import Image
from scipy import ndimage as ndi

os.chdir(os.path.dirname(os.path.abspath(__file__)))
OUT = 'out/iori'
os.makedirs(OUT + '/textures', exist_ok=True)
L = lambda n: np.asarray(Image.open(f'al/{n}.png').convert('RGB')).astype(np.float32)
M = lambda n: np.asarray(Image.open(f'm/{n}.png')) > 0
final = np.asarray(Image.open('../../source/final.png').convert('RGB')).astype(np.float32)
H, W = final.shape[:2]
yy, xx = np.mgrid[0:H, 0:W]
gray = lambda a: a.mean(2)
dil = lambda m, k: cv2.dilate(m.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k))).astype(bool)
ero = lambda m, k: cv2.erode(m.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k))).astype(bool)
diff = lambda a, b: np.abs(cv2.GaussianBlur(a, (0, 0), 1.0) - cv2.GaussianBlur(b, (0, 0), 1.0)).max(2)

bb, head, headf, ug, bt, bh = L('behind_body'), L('head'), L('head_face'), L('under_goggles'), L('behind_tag'), L('back_hair')
U, face, hair_f, hair_s, ahoge, gog, tag, earring, phones, body, skull = (M(n) for n in
    ('U', 'face', 'hair_front', 'hair_side', 'ahoge', 'goggles', 'tag', 'earring', 'phones', 'body', 'skull'))
hv = ero(np.asarray(Image.open('al/head_valid.png')) > 250, 5)

layers = []          # (id, rgb, alpha float 0..1, extra dict)
darkf = gray(final) < 120
def rim(m, k=5):
    """the mask plus the final's dark outline pixels right next to it"""
    return m | (dil(m, k) & darkf)


def soft(m, s=0.6):
    """mask -> alpha with a slight anti-aliased edge"""
    a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), s)
    return np.maximum(m.astype(np.float32), np.clip(a, 0, 1) * dil(m, 3))


def add(id_, rgb, alpha, **extra):
    layers.append((id_, rgb, alpha, extra))


# 1. base: the seat plate (still). Where the upper body was, the chair/legs picture.
Uw = cv2.GaussianBlur(dil(U, 3).astype(np.float32), (0, 0), 1.5)
base = final * (1 - Uw[..., None]) + bb * Uw[..., None]
base[:440] = bb[:440]   # above the shoulders the plate is only background: no stray hair tips stay behind
add('base', base, np.ones((H, W), np.float32), group='body')

# 2. back hair: what the back-hair picture has as hair (differs from the bare head), inside the head tile
bg_bh = np.abs(bh - bh[40, 340]).max(2) < 16
bhm = (diff(bh, head) > 30) & hv & ~bg_bh & (yy < 445)
bhm = ndi.binary_fill_holes(cv2.morphologyEx(bhm.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8)).astype(bool)) & ~bg_bh
sat_bh = bh.max(2) - bh.min(2)
bhm &= ((sat_bh > 30) & (bh[..., 2] > bh[..., 0])) | (gray(bh) < 100)
bhm = ndi.binary_fill_holes(bhm) & ~bg_bh & ~((gray(bh) > 180) & (sat_bh < 35))
_l, _n = ndi.label(cv2.morphologyEx(bhm.astype(np.uint8), cv2.MORPH_OPEN, np.ones((5, 5), np.uint8)))
_big = _l == (1 + int(np.argmax(ndi.sum(np.ones_like(_l), _l, range(1, _n + 1)))))
bhm &= dil(_big, 7)
add('back_hair', bh, soft(bhm), group='hair_back', depth=-0.3)

# 3. body (incl. headphones): final pixels; where hair hangs over the shoulders, the bare-head picture's shirt
BG0 = np.median(np.concatenate([final[:6].reshape(-1, 3), final[:, :6].reshape(-1, 3)]), 0)
under_hair = (hair_f | hair_s) & U & (yy >= 300) & ~phones & (np.abs(head - BG0).max(2) > 16)
body_rgb = final.copy(); body_rgb[under_hair] = head[under_hair]
body_a = rim(body | under_hair)
add('body', body_rgb, soft(body_a), group='body')

# 4. face: visible skin from the final, hidden forehead/ears/neck from the shifted bare head
hidden = face & (hair_f | hair_s | gog | tag | earring | phones | ahoge)
face_rgb = final.copy(); face_rgb[hidden] = headf[hidden]
face_rgb[skull & ~face] = headf[skull & ~face]
face_a = skull & ~(M('U') & ~skull)
shirt = (gray(final) > 185) & (yy > 300) & ~((final[..., 0] > 205) & (final[..., 0] - final[..., 2] > 15))
face_a = skull & ~(shirt & ~hair_f & ~hair_s & ~phones) & ~phones
add('face', face_rgb, soft(rim(face_a, 3) & ~phones), group='head')

# 5. eyes (image left = L)
wh, iris_g, closed = L('white'), L('iris'), L('closed')
whites = M('whites'); iris_m = M('iris'); lash_m = M('lash')
eyes = {}
for side, box in (('L', (440, 252, 512, 312)), ('R', (512, 218, 592, 280))):
    x0, y0, x1, y1 = box
    inb = (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)
    opening = whites & inb
    ys, xs = np.where(opening)
    cx, cy = float(xs.mean()), float(ys.mean())
    eyes[side] = (cx, cy, [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())])
    # the white of the eye: the empty-eye picture, as a mask of the opening
    add(f'eye_{side}', wh, soft(opening, 0.5), group='head', role=f'eye_{side}', pivot=[round(cx, 1), round(cy, 1)], mask=True)
    # the iris: the final's own iris where it shows, the isolated iris (complete oval) elsewhere
    ir = iris_m & inb
    irgb = iris_g.copy()
    gr = irgb[..., 1] - np.maximum(irgb[..., 0], irgb[..., 2])
    spill = gr > 0
    irgb[..., 1] = np.where(spill, np.maximum(irgb[..., 0], irgb[..., 2]), irgb[..., 1])
    ir = ir & ~(gr > 25)
    ir = ero(ir, 2) | (ir & opening)
    vis = ir & opening
    irgb[vis] = final[vis]
    add(f'iris_{side}', irgb, soft(ir, 0.5), group='head', role=f'iris_{side}', pivot=[round(cx, 1), round(cy, 1)], clip=f'eye_{side}', range=[5.0, 3.0])
    # lashes over the iris: the final's dark lines around the opening
    lm = inb & dil(opening, 9) & ~ero(opening, 3) & (gray(final) < 140)
    add(f'lash_{side}', final, soft(lm, 0.5), group='head', role=f'eye_{side}', pivot=[round(cx, 1), round(cy, 1)])
    # the closed lid: the closed-eye picture over the whole eye area
    core = dil(opening | lm, 5) & inb
    lid = dil(core, 6) & inb
    la = np.maximum(core.astype(np.float32), cv2.GaussianBlur(lid.astype(np.float32), (0, 0), 1.5) * lid)
    add(f'lid_{side}', closed, la, group='head', role=f'lid_{side}')

# 6. mouth: closed (final) + frames
mb = (495, 298, 578, 348)
mm = np.zeros((H, W), np.float32)
cv2.ellipse(mm, (537, 322), (36, 18), -20, 0, 360, 1.0, -1)
mm = cv2.GaussianBlur(mm, (0, 0), 3.0)
frames = [('closed', final), ('small', L('m_small')), ('half', L('m_half')), ('open', L('m_open')), ('round', L('m_round'))]
add('mouth', final, mm, group='head', role='mouth', pivot=[537, 322], frames=frames)

# 7. side hair (+ the hair behind the tag and earring)
hs_rgb = final.copy()
behind = (tag | (earring & ~skull))
hs_rgb[behind] = bt[behind]
hs_a = hair_s | behind
# under the goggles outside the skull: the hair from the no-goggles picture
g_out = gog & ~skull
hs_rgb[g_out] = ug[g_out]; hs_a |= g_out & (diff(ug, final) > 0)
skin_f = (final[..., 0] > 205) & (final[..., 0] - final[..., 2] > 15) & (final[..., 1] > 170)
satf = final.max(2) - final.min(2)
hairc = ((satf > 38) & (final[..., 2] > final[..., 0])) | (gray(final) < 105)
shirtish = (gray(final) > 188) & (satf < 34)
hs_a &= ~((shirtish | (skin_f & ~skull)) & ~behind & ~g_out)
add('side_hair', hs_rgb, soft(rim(hs_a)), group='hair_side', depth=0.05)
add('earring', final, soft(rim(earring, 3), 0.5), group='hair_side', depth=0.05, bones=['earring'])
add('tag', final, soft(tag, 0.5), group='hair_side', depth=0.05, bones=['tag'])

# 8. front hair (+ the hair under the goggles inside the skull)
hf_rgb = final.copy()
g_in = gog & skull
hf_rgb[g_in] = ug[g_in]
opening_all = M('whites') | M('iris')
eyezone = dil(opening_all, 3)
lashy = dil(opening_all, 4) & (gray(final) < 70) & (satf < 40)
hf_a = (hair_f & ~eyezone & ~lashy & ~(skin_f & ~g_in)) | g_in
hf_a = cv2.morphologyEx(hf_a.astype(np.uint8), cv2.MORPH_OPEN, np.ones((2, 2), np.uint8)).astype(bool)
add('front_hair', hf_rgb, soft(rim(hf_a, 3) & ~eyezone & ~lashy), group='hair_front', depth=0.1)
add('ahoge', final, soft(ahoge, 0.5), group='hair_back', depth=0.0, bones=['ahoge'])
add('goggles', final, soft(rim(gog, 3)), group='head', depth=0.12)

# ---- what the hair cleanup dropped stays where it was drawn: bake it into the layer below
idx = {l[0]: i for i, l in enumerate(layers)}
left_f = (hair_f | g_in) & ~hf_a & ~(hair_s)
left_s = hair_s & ~hs_a
fa = face_a
for name, extra in (('face', (left_f & skull) | (left_s & fa)), ('body', ((left_s & ~fa) | (left_f & ~skull)))):
    i = idx[name]; _id, rgb, a, ex = layers[i]
    rgb = rgb.copy(); rgb[extra] = final[extra]
    a = np.maximum(a, extra.astype(np.float32))
    layers[i] = (_id, rgb, a, ex)

# ---- key out the flat gray background (every source has the same gray), unmixing edge colors from it
edge = np.concatenate([final[:6].reshape(-1, 3), final[-6:].reshape(-1, 3), final[:, :6].reshape(-1, 3), final[:, -6:].reshape(-1, 3)])
BG = np.median(edge, 0)
def key(rgb, a, loose=False):
    d = np.abs(rgb - BG).max(2)
    # background = near-gray pixels connected to the picture's border; only there (and in a thin band
    # around it) does the key act, so gray-ish fabric inside the figure stays opaque
    near = (d < 12).astype(np.uint8)
    n, lab = cv2.connectedComponents(near)
    border = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    bgm = np.isin(lab, border[border > 0]) | (np.isin(lab, np.arange(1, n))[...] & (ndi.sum(near, lab, np.arange(n + 1))[lab] > 400) & (d < 8))
    if loose: bgm = bgm | (d < 10)
    band = dil(bgm, 5)
    ka = np.clip((d - 5) / (24 - 5), 0, 1)
    ka = ka * ka * (3 - 2 * ka)
    ka = np.where(bgm, 0.0, np.where(band, ka, 1.0))
    a2 = a * ka
    safe = np.maximum(ka, 0.15)[..., None]
    un = (rgb - (1 - safe) * BG) / safe
    rgb2 = np.where((ka < 1)[..., None], un, rgb)
    return rgb2.clip(0, 255), a2
layers = [(i, *key(r, a, loose=(i != 'base')), e) for i, r, a, e in layers]

# ---- write the textures
model_layers = []
PAD = 8
for id_, rgb, a, ex in layers:
    nz = a > 0.004
    if ex.get('frames'):
        nz = a > 0.004
    ys, xs = np.where(nz)
    x0, y0 = max(0, xs.min() - PAD), max(0, ys.min() - PAD)
    x1, y1 = min(W, xs.max() + 1 + PAD), min(H, ys.max() + 1 + PAD)
    def tex(img, alpha, name):
        rgba = np.dstack([img[y0:y1, x0:x1], alpha[y0:y1, x0:x1] * 255]).round().clip(0, 255).astype(np.uint8)
        Image.fromarray(rgba, 'RGBA').save(f'{OUT}/textures/{name}.png', optimize=True)
        return f'textures/{name}.png'
    d = {'id': id_, 'group': ex['group'], 'x': int(x0), 'y': int(y0), 'w': int(x1 - x0), 'h': int(y1 - y0)}
    if 'depth' in ex: d['depth'] = ex['depth']
    if 'role' in ex: d['role'] = ex['role']
    if 'pivot' in ex: d['pivot'] = ex['pivot']
    if 'clip' in ex: d['clip'] = ex['clip']
    if 'range' in ex: d['range'] = ex['range']
    if 'bones' in ex: d['bones'] = ex['bones']
    if ex.get('frames'):
        fr = []
        for k, (vis, img) in enumerate(ex['frames']):
            fr.append(tex(img, a, f'mouth_{vis}'))
        d['texture'] = fr[0]; d['frames'] = fr; d['visemes'] = [v for v, _ in ex['frames']]
    else:
        d['texture'] = tex(rgb, a, id_)
    if ex.get('mask'):
        m8 = (a[y0:y1, x0:x1] > 0.5).astype(np.uint8) * 255
        Image.fromarray(m8, 'L').save(f'{OUT}/textures/{id_}_mask.png')
        d['mask'] = f'textures/{id_}_mask.png'
    model_layers.append(d)

# eyes' boxes and the head tilt from the eye line
(lx, ly, lb), (rx, ry_, rb) = eyes['L'], eyes['R']
tilt = math.degrees(math.atan2(ry_ - ly, rx - lx))       # clockwise on the picture = positive (y down)
chains = [
    {'id': 'tag', 'attach': 'head', 'depth': 0.05, 'points': [[656, 214], [662, 238], [670, 262], [678, 286], [684, 300]],
     'k': [120, 30], 'damp': 4, 'drive': 0.8, 'hang': 0.5, 'wind': 2.0, 'freq': 0.7, 'limit': 25, 'sigma': 22},
    {'id': 'earring', 'attach': 'head', 'depth': 0.05, 'points': [[628, 250], [629, 266], [630, 282], [631, 296]],
     'k': [160, 40], 'damp': 4, 'drive': 0.9, 'hang': 0.6, 'wind': 1.5, 'freq': 0.9, 'limit': 20, 'sigma': 10},
    {'id': 'ahoge', 'attach': 'head', 'depth': 0.0, 'points': [[470, 118], [462, 96], [450, 78], [430, 66], [405, 70]],
     'k': [170, 45], 'damp': 4.5, 'drive': 0.9, 'hang': 0.1, 'wind': 3.0, 'freq': 0.6, 'limit': 30, 'sigma': 60},
]
model = {
    'format': 'livliv-model', 'version': 4, 'name': 'Iori',
    'meta': {'generator': {'name': 'iori sit-v2 draft'}, 'watermark': {'kind': 'none'}},
    'canvas': [W, H],
    'anchors': {
        'head': {'cx': 532.0, 'cy': 238.0, 'rx': 118.0, 'ry': 150.0},
        'face': [440, 190, 630, 352], 'neck_y': 352, 'top_y': 40, 'bottom_y': 895, 'hair_root_y': 120,
        'eyes': [lb, rb],
    },
    'rig': {'yaw': 0.18, 'pitch': 0.14, 'roll': 0.3, 'bulge': 0.6, 'breath': 1, 'sway': 1, 'wind': 1.2,
            'body': {'yaw': 0.25, 'roll': 0.25, 'breath': 1, 'bust': 0}, 'neck': {'twist': 0.4, 'reach': 0.4},
            'chains': chains},
    'tuning': {'turn': 0.6, 'nod': 0.7, 'hair': 1, 'blink': 1, 'expr': 0, 'mouth': 1, 'breath': 1},
    'layers': model_layers,
    'x-iori': {'head_tilt': round(tilt, 1), 'body_pivot': [585, 885], 'still': ['base']},
}
with open(f'{OUT}/model.json', 'w', encoding='utf-8') as f:
    json.dump(model, f, ensure_ascii=False, indent=1)
print('layers', len(model_layers), 'head_tilt', round(tilt, 1))

# ---- rest-pose recomposite (no lids, mouth frame 0) against the final
comp = np.ones((H, W, 3), np.float32) * BG
masks = {}
for d in model_layers:
    if d.get('role', '').startswith('lid_'): continue
    t = np.asarray(Image.open(f"{OUT}/{d['texture']}")).astype(np.float32)
    a = t[..., 3:] / 255
    if d.get('clip'):
        cm = masks[d['clip']]
        a = a * cm[d['y']:d['y'] + d['h'], d['x']:d['x'] + d['w'], None]
    sl = comp[d['y']:d['y'] + d['h'], d['x']:d['x'] + d['w']]
    sl[:] = sl * (1 - a) + t[..., :3] * a
    if d.get('mask'):
        mk = np.zeros((H, W), np.float32)
        mk[d['y']:d['y'] + d['h'], d['x']:d['x'] + d['w']] = np.asarray(Image.open(f"{OUT}/{d['mask']}")).astype(np.float32) / 255
        masks[d['id']] = mk
Image.fromarray(comp.clip(0, 255).astype(np.uint8)).save('rest_composite.png')
dd = np.abs(comp - final).max(2)
print('rest vs final: mean %.2f, px>8: %d, px>24: %d, max %d' % (dd.mean(), (dd > 8).sum(), (dd > 24).sum(), dd.max()))
Image.fromarray(np.clip(dd * 4, 0, 255).astype(np.uint8)).save('rest_diff.png')
