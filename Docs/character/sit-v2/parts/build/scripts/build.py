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
# the inside of the side locks (side_hair_back_01) under the face's edge: what shows at the face's outline when
# the side hair and the face move apart
sbk = L('side_back')
band = skull & ~ero(skull, 31) & (yy > 150)
sat_sb = sbk.max(2) - sbk.min(2)
band &= ((sat_sb > 25) & (sbk[..., 2] > sbk[..., 0])) | (gray(sbk) < 100)
bh_rgb = bh.copy(); bh_rgb[band] = sbk[band]
bhm = bhm | band
add('back_hair', bh_rgb, soft(bhm), group='hair_back', depth=-0.3)

# 3. body (incl. headphones): final pixels; where hair hangs over the shoulders, the bare-head picture's shirt
BG0 = np.median(np.concatenate([final[:6].reshape(-1, 3), final[:, :6].reshape(-1, 3)]), 0)
# the body under the hair, without any generated picture: the visible body (B) is closed over the hair that crosses it,
# the inside is diffused from body pixels only, and the outline is carried along the new edge
satb = final.max(2) - final.min(2)
huehair = (satb > 22) & (final[..., 2] > final[..., 0] + 6) & (final[..., 2] > final[..., 1] + 14) & (gray(final) < 175)
_chest = np.zeros((H, W), np.uint8)
cv2.fillPoly(_chest, [np.array([(470, 335), (520, 318), (640, 305), (705, 330), (760, 345), (800, 380), (800, 445), (470, 445)], np.int32)], 1)
_chest = _chest.astype(bool) & ~skull
strong = (satb > 40) & (final[..., 2] > final[..., 0] + 15)
_hue0 = huehair.copy()
huehair = np.where(_chest, strong, huehair)
_cand = (hair_f | hair_s) & _hue0
_lab, _n = ndi.label(_cand)
_outside = np.unique(_lab[_cand & ~_chest & (_lab > 0)])
huehair |= np.isin(_lab, _outside) & _cand
hcore = (hair_f | hair_s | ahoge) & huehair
hairall = hcore | ((gray(final) < 95) & dil(hcore, 5) & (hair_f | hair_s | ahoge))   # the locks and their own outlines only
# hair tips the first masks missed (over the headphones and the collar): hair-coloured strands joined to the locks
_tips = _hue0 & U & (yy >= 280) & (yy < 450) & ~skull
_tips &= ~(_chest & ~((satb > 30) & (final[..., 2] > final[..., 0] + 10)))
_lab, _n = ndi.label(_tips | hairall)
_keep = np.unique(_lab[hairall & ~_chest & (_lab > 0)])
extra_hair = np.isin(_lab, _keep) & _tips & ~hairall
_cups = ndi.binary_fill_holes(cv2.morphologyEx(phones.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8)).astype(bool))
_spot = (xx > 628) & (xx < 725) & (yy > 288) & (yy < 362) & U & ~_cups & ~((gray(final) > 175) & (satb < 32)) & ~((gray(final) < 85) & (satb < 30)) & ~skull
_cuphl = _cups & ~phones   # the cups' own highlights
extra_hair = (extra_hair | _spot) & ~_cuphl
hairall |= extra_hair
hairall &= ~_cuphl
_leftcup_tips = _hue0 & dil(_cups, 3) & (xx < 560) & ~phones & ~_cuphl & (yy >= 280)
extra_hair |= _leftcup_tips
hairall |= _leftcup_tips
notbody = M('face') | gog | tag | earring | ahoge | (skull & (yy < 330))
_ext = U & (yy >= 280) & (yy < 520) & ~notbody & ~body & ~hairall
_ext &= ((gray(final) > 170) & (satb < 32)) | (final[..., 0] > 190) & (final[..., 0] - final[..., 2] > 12)   # shirt / skin only
_ext &= dil(body, 9)
_ext |= _chest & ~hairall & ~phones & ~M('face')
B = (body | _ext) & ~hairall
_skinlike = (final[..., 0] > 190) & (final[..., 0] - final[..., 2] > 12)
lowhair = ((yy >= 280) & (hair_f | hair_s) & ~hairall & (((gray(final) > 180) & (satb < 30)) | _skinlike)) | (_chest & ~hairall)
B = B & ~(cv2.morphologyEx(B.astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8)) == 0)   # drop 1-px specks
Bc = cv2.morphologyEx(B.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (31, 31))).astype(bool)
Bc = ndi.binary_fill_holes(Bc)
F = Bc & ~B & hairall & (yy >= 280) & (yy < 415) & ~skull & ~phones
Image.fromarray((F * 255).astype(np.uint8)).save('body_fill_F.png')
src = final.clip(0, 255).astype(np.uint8)
filled = cv2.inpaint(src, (~B).astype(np.uint8), 6, cv2.INPAINT_TELEA).astype(np.float32)
body_rgb = final.copy(); body_rgb[F] = filled[F]
# the outline along the new edge: the colour of the body's own outline
edgeF = F & ~cv2.erode((B | F).astype(np.uint8), np.ones((3, 3), np.uint8)).astype(bool)
line_px = final[B & ~cv2.erode(B.astype(np.uint8), np.ones((3, 3), np.uint8)).astype(bool) & (gray(final) < 90)]
line_col = np.median(line_px, 0) if len(line_px) else np.array([60, 50, 70.])
body_rgb[edgeF] = line_col
Image.fromarray(np.where(F[..., None], body_rgb, final).clip(0, 255).astype(np.uint8)).save('nogen_composite.png')
body_a = (B | F) | (dil(B | F, 3) & darkf & ~hairall)
_cl = cv2.morphologyEx(body_a.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8)).astype(bool) & dil(B | F, 3)
body_a = body_a | (_cl & ~hairall)
_lab, _n = ndi.label(~body_a)
_sizes = ndi.sum(np.ones_like(_lab), _lab, range(1, _n + 1))
_holes = np.isin(_lab, 1 + np.where(_sizes < 600)[0]) & (_lab > 0)
_known = (B | F) & ~_holes
_fill = cv2.inpaint(body_rgb.clip(0, 255).astype(np.uint8), (~_known).astype(np.uint8), 4, cv2.INPAINT_TELEA).astype(np.float32)
body_rgb[_holes] = np.where(hairall[_holes][:, None], _fill[_holes], body_rgb[_holes])
body_rgb[_holes & ~hairall] = final[_holes & ~hairall]
body_a = body_a | _holes | _cups
_under = _cups & hairall
body_rgb[_under] = _fill[_under]
body_rgb[_cuphl] = final[_cuphl]
print('body holes filled', int(_holes.sum()))
# left of the left cup, above the shoulder line: only the jacket/hood itself, no hair outlines or background between locks
_zone = (xx < 560) & (yy > 290) & (yy < 400) & ~dil(_cups, 5)
_solid = cv2.morphologyEx((B & _zone & ~darkf).astype(np.uint8), cv2.MORPH_OPEN, np.ones((5, 5), np.uint8)).astype(bool)
body_a = body_a & ~(_zone & ~dil(_solid, 3))
# chico's own painting of what the locks hid (source/chico_underhair.png): where it differs from the final and
# shows body (not hair, not background), the body layer takes it
_ch = np.asarray(Image.open('../../source/chico_underhair.png').convert('RGB')).astype(np.float32)
_chg = dil(np.abs(_ch - final).max(2) > 12, 3)
_chsat = _ch.max(2) - _ch.min(2)
_chhair = (_chsat > 30) & (_ch[..., 2] > _ch[..., 0] + 12)
_chbg = np.abs(_ch - final[5, 5]).max(2) < 14
_take = _chg & ~_chhair & ~_chbg & (yy >= 260)
body_rgb[_take] = _ch[_take]
body_a = body_a | _take
print('body takes chico painting px', int(_take.sum()))
add('body', body_rgb, soft(body_a), group='body')

# 4. face: visible skin from the final, hidden forehead/ears/neck from the shifted bare head
# the open eyes: where the final differs from the closed-eye picture, per eye
closedA = L('closed')
dE = diff(final, closedA)
whites0 = M('whites') | M('iris')
eyeM = np.zeros((H, W), bool)
eyeM_side = {}
for _s, (x0, y0, x1, y1) in (('L', (436, 246, 516, 318)), ('R', (508, 212, 596, 286))):
    inb_ = (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)
    m_ = (dE > 40) & inb_
    m_ = cv2.morphologyEx(m_.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))).astype(bool)
    lab_, n_ = ndi.label(m_)
    keep_ = np.zeros_like(m_)
    seed = dil(whites0 & inb_, 9)
    for k_ in range(1, n_ + 1):
        c_ = lab_ == k_
        if c_.sum() > 25 and (c_ & seed).any(): keep_ |= c_
    keep_ = dil(ndi.binary_fill_holes(keep_ | (whites0 & inb_)), 3) & inb_
    eyeM_side[_s] = keep_; eyeM |= keep_
save_dbg = Image.fromarray((eyeM * 255).astype(np.uint8)).save('eyeM.png')
hidden = face & (hair_f | hair_s | gog | tag | earring | phones | ahoge)
face_rgb = final.copy(); face_rgb[hidden] = headf[hidden]
face_rgb[skull & ~face] = headf[skull & ~face]
face_a = skull & ~(M('U') & ~skull)
shirt = (gray(final) > 185) & (yy > 300) & ~((final[..., 0] > 205) & (final[..., 0] - final[..., 2] > 15))
face_a = skull & ~(shirt & ~hair_f & ~hair_s & ~phones) & ~phones
fr8 = face_rgb.clip(0, 255).astype(np.uint8)
face_rgb = cv2.inpaint(fr8, dil(eyeM, 3).astype(np.uint8), 9, cv2.INPAINT_TELEA).astype(np.float32)
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
    lm = eyeM_side[side] & ~ero(opening, 2)
    add(f'lash_{side}', final, soft(lm, 0.5), group='head', role=f'eye_{side}', pivot=[round(cx, 1), round(cy, 1)])
    # the closed lid: the closed-eye picture over the whole eye area
    core = dil(eyeM_side[side], 3)
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
hs_a &= ~eyeM
hs_a &= ~lowhair
hs_a |= extra_hair & ~phones
hs_a &= ~_cuphl
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
hf_a = ((hair_f & ~eyezone & ~lashy & ~(skin_f & ~g_in)) | g_in) & ~eyeM & ~lowhair
hf_a = cv2.morphologyEx(hf_a.astype(np.uint8), cv2.MORPH_OPEN, np.ones((2, 2), np.uint8)).astype(bool)
add('front_hair', hf_rgb, soft(rim(hf_a, 3) & ~eyezone & ~lashy), group='hair_front', depth=0.1)
add('ahoge', final, soft(ahoge, 0.5), group='hair_back', depth=0.0, bones=['ahoge'])
add('goggles', final, soft(rim(gog, 3)), group='head', depth=0.12)

# ---- second pass between layers (found with a flat-coloured debug render of the poses)
_ix = {l[0]: i for i, l in enumerate(layers)}
def _get(n): return layers[_ix[n]]
def _set(n, rgb, a): i = _ix[n]; layers[i] = (layers[i][0], rgb, a, layers[i][3])
_, sh_rgb, sh_a, _ = _get('side_hair'); _, fh_rgb, fh_a, _ = _get('front_hair'); _, bk_rgb, bk_a, _ = _get('back_hair')
_, fc_rgb, fc_a, _ = _get('face'); _, bd_rgb, bd_a, _ = _get('body')
# 2: pieces of the front hair below the chin (cut-off strands over the neck) go with the side hair they belong to
_frag = (fh_a > 0.5) & (yy > 300) & (xx > 540) & (_hue0 | (gray(final) < 110))
sh_rgb = sh_rgb.copy(); sh_rgb[_frag] = final[_frag]; sh_a = np.maximum(sh_a, _frag.astype(np.float32))
fh_a = fh_a * ~_frag
# 1: below the jaw the back hair only where the side hair covers it (its own tips were showing between the locks)
bk_a = bk_a * ((yy < 330) | dil((sh_a > 0.5) | (fh_a > 0.5), 3) | dil(bd_a > 0.5, 9) & (xx > 560))
# 4: the neck belongs to the body: the face stops where the body already holds the neck skin
_neck = (bd_a > 0.5) & (yy > 300) & (fc_a > 0) & ((final[..., 0] > 185) & (final[..., 0] - final[..., 2] > 10))
fc_a = fc_a * ~dil(_neck, 1)
fc_a = fc_a * ~((bd_a > 0.5) & (yy > 300) & (xx > 540))
# 3: dark line bits of the hood edge on the right shoulder are body, not hair
_hood = (xx > 690) & (yy > 320) & (yy < 440) & (sh_a > 0.5) & darkf & ~huehair & (bd_a > 0.5)
sh_a = sh_a * ~_hood
# the locks chico painted out of the body picture belong whole to the side hair (over her painted body), nowhere else
_chi = np.asarray(Image.open('../../source/chico_underhair.png').convert('RGB')).astype(np.float32)
_chd = np.abs(_chi - final).max(2) > 12
_bgf = np.abs(final - final[5, 5]).max(2) < 14
_strand = ((_chd & ~_bgf) | (dil(_chd, 2) & (_hue0 | (gray(final) < 110)) & ~_bgf)) & ~_cuphl
sh_rgb[_strand] = final[_strand]; sh_a = np.maximum(sh_a, _strand.astype(np.float32))
# un-mix the locks' edges from chico's under-colour B: C = a*H + (1-a)*B, H = the lock's core colour nearby
_Bc = _chi; _C = final
_D = np.abs(_C - _Bc).max(2)
_overbody = _strand & ~(np.abs(_Bc - final[5, 5]).max(2) < 14)          # only where the under-colour is body, not background
_core = _overbody & (_D > 60)
# H = the colour of the pixel with the largest |C-B| within 5px (thin 1-2px locks have no flat core, so their
# own strongest pixel is the core and gets a = 1)
_Dm = np.where(_overbody, _D, -1.0)
_best = _Dm.copy(); _Hc = np.where(_overbody[..., None], _C, 0.0)
for _dy in range(-5, 6):
    for _dx in range(-5, 6):
        if _dy * _dy + _dx * _dx > 25 or (_dy == 0 and _dx == 0): continue
        _sD = np.roll(np.roll(_Dm, _dy, 0), _dx, 1); _sC = np.roll(np.roll(_C, _dy, 0), _dx, 1)
        _up = _sD > _best; _best = np.where(_up, _sD, _best); _Hc = np.where(_up[..., None], _sC, _Hc)
_core = _overbody & (_D >= _best - 1e-3) & (_D > 30)
_HB = _Hc - _Bc; _den = (_HB ** 2).sum(2)
_solv = _overbody & (_den > 20 ** 2)
_a = np.clip(((_C - _Bc) * _HB).sum(2) / np.maximum(_den, 1e-3), 0, 1)
_a[_core] = 1.0
# smooth a along the lock: halfway between the 3x3 max and the 3x3 mean (joined thin line rather than dots)
_am = np.where(_solv, _a, 0).astype(np.float32)
_a = np.where(_solv, 0.5 * (cv2.dilate(_am, np.ones((3, 3), np.uint8)) + cv2.blur(_am, (3, 3))), _a).clip(0, 1)
_unmixed = _solv & (_a < 0.97)
# the colour is solved back from the smoothed a, so at rest C = a*rgb + (1-a)*B still holds; it is kept near H
_ac = np.maximum(_a, 0.05)[..., None]
_rs = ((_C - (1 - _ac) * _Bc) / _ac)
_rs = np.clip(_rs, _Hc - 40, _Hc + 40).clip(0, 255)
sh_rgb[_unmixed] = _rs[_unmixed]
sh_a[_solv] = np.where(_a[_solv] < 0.05, 0.0, np.where(_a[_solv] >= 0.97, 1.0, _a[_solv]))
print('lock edges un-mixed', int(_unmixed.sum()), 'dropped', int((_solv & (_a < 0.05)).sum()))
fh_a = fh_a * ~_strand; fc_a = fc_a * ~_strand
# non-hair pixels the hair layers still held below the chin (neck skin, collar) are the body's
_nh = (((fh_a > 0.5) | (sh_a > 0.5)) & (yy > 300) & (xx > 540) & ~(_hue0 | (gray(final) < 110))) & ~_strand & ~(np.abs(final - final[5, 5]).max(2) < 16)
fh_a = fh_a * ~_nh; sh_a = sh_a * ~_nh
bd_rgb = bd_rgb.copy(); bd_rgb[_nh] = np.where(_chd[_nh][:, None], _chi[_nh], final[_nh]); bd_a = np.maximum(bd_a, _nh.astype(np.float32))
_jaw = darkf & (fc_a > 0.5) & (yy > 295) & (xx > 540) & ~_strand     # the jaw's own outline stays with the face
bd_a = bd_a * ~_jaw
_set('body', bd_rgb, bd_a)
fc_a = fc_a * ~((bd_a > 0.5) & (yy > 300) & (xx > 540) & ~_jaw)
# 5: the body holds the whole neck behind the face and the locks, up under the jaw, fully opaque, so the back
# hair never shows through when the head moves (the face stops at y300 and the body used to start at y301)
_nbox = (xx >= 565) & (xx <= 665) & (yy >= 275) & (yy <= 400)
_chbg = np.abs(_chi - final[5, 5]).max(2) < 14
_over = (np.maximum(np.maximum(fc_a, sh_a), fh_a) > 0.3)
_nfill = _nbox & _over & ~_chbg & (bd_a < 0.98)
_nfill = _nfill & ((yy > 300) | (fc_a > 0.3) | (sh_a > 0.3) | (fh_a > 0.3))
# colour: chico's picture; above y305 its dark/purple pixels (jaw line, hair) become skin filled from around
_drk = (gray(_chi) < 130) | ((_chi[..., 2] > _chi[..., 0] + 4) & (gray(_chi) < 175))
_unk = _nfill & _drk & (yy <= 305)
_srcm = (bd_a > 0.98) | (_nfill & ~_unk)
_fillc = np.where(_srcm[..., None], np.where(_nfill[..., None], _chi, bd_rgb), 0).clip(0, 255).astype(np.uint8)
_fillc = cv2.inpaint(_fillc, (_unk | (_nbox & ~_srcm)).astype(np.uint8), 6, cv2.INPAINT_TELEA).astype(np.float32)
_new = _nfill & (bd_a < 0.5)
bd_rgb[_new] = _fillc[_new]
bd_rgb[_unk] = _fillc[_unk]
bd_a = np.where(_nfill, 1.0, bd_a)
print('neck under the face/locks filled into the body', int(_nfill.sum()), 'of which skin-filled', int(_unk.sum()))
# 6: tiny isolated bits of the side hair over the neck (< 20 px, not touching any lock) go; chico's skin shows
_sho = sh_a > 0.3
_lab, _nl = ndi.label(_sho, np.ones((3, 3)))
_sz = ndi.sum(np.ones_like(_lab), _lab, range(1, _nl + 1))
_cent = ndi.center_of_mass(np.ones_like(_lab), _lab, range(1, _nl + 1))
_drop = np.zeros_like(_sho)
for _k, (_s, (_cy, _cx)) in enumerate(zip(_sz, _cent), 1):
    if _s < 20 and 565 <= _cx <= 665 and 300 <= _cy <= 400:
        _drop |= _lab == _k
_drop &= ~_chbg
sh_a = sh_a * ~_drop
bd_rgb[_drop] = _chi[_drop]; bd_a = np.maximum(bd_a, _drop.astype(np.float32))
print('isolated side-hair bits dropped', int(_drop.sum()), 'px in', int(len(np.unique(_lab[_drop])) - (0 in _lab[_drop])), 'pieces')
_set('body', bd_rgb, bd_a)
_set('side_hair', sh_rgb, sh_a); _set('front_hair', fh_rgb, fh_a); _set('back_hair', bk_rgb, bk_a); _set('face', fc_rgb, fc_a)

# ---- what the hair cleanup dropped stays where it was drawn: bake it into the layer below
idx = {l[0]: i for i, l in enumerate(layers)}
left_f = (hair_f | g_in) & ~hf_a & ~(hair_s)
left_s = hair_s & ~hs_a
fa = face_a
for name, extra in (('face', (left_f & skull) | (left_s & fa)), ('body', ((left_s & ~fa) | (left_f & ~skull)))):
    i = idx[name]; _id, rgb, a, ex = layers[i]
    extra = extra & ~dil(_chd, 3) & ~_strand   # never over chico's painting or the locks over it
    rgb = rgb.copy(); rgb[extra] = final[extra]
    a = np.maximum(a, extra.astype(np.float32))
    layers[i] = (_id, rgb, a, ex)

# ---- key out the flat gray background (every source has the same gray), unmixing edge colors from it
edge = np.concatenate([final[:6].reshape(-1, 3), final[-6:].reshape(-1, 3), final[:, :6].reshape(-1, 3), final[:, -6:].reshape(-1, 3)])
BG = np.median(edge, 0)
def key(rgb, a, loose=False, protect=None):
    d = np.abs(rgb - BG).max(2)
    # background = near-gray pixels connected to the picture's border; only there (and in a thin band
    # around it) does the key act, so gray-ish fabric inside the figure stays opaque
    near = (d < 12).astype(np.uint8)
    n, lab = cv2.connectedComponents(near)
    border = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    bgm = np.isin(lab, border[border > 0]) | (np.isin(lab, np.arange(1, n))[...] & (ndi.sum(near, lab, np.arange(n + 1))[lab] > 400) & (d < 8))
    if loose == 'big':
        _l, _n = ndi.label(d < 10)
        if _n:
            _sz = ndi.sum(np.ones_like(_l), _l, range(1, _n + 1))
            bgm = bgm | (np.isin(_l, 1 + np.where(_sz > 40)[0]) & (_l > 0))
        bgm = bgm | ((d < 14) & (xx < 565) & (yy > 290) & (yy < 430) & ~dil(_cups, 3))   # background between the locks left of the left cup
    elif loose: bgm = bgm | (d < 10)
    if protect is not None: bgm = bgm & ~protect
    band = dil(bgm, 5)
    ka = np.clip((d - 5) / (24 - 5), 0, 1)
    ka = ka * ka * (3 - 2 * ka)
    ka = np.where(bgm, 0.0, np.where(band, ka, 1.0))
    a2 = a * ka
    safe = np.maximum(ka, 0.15)[..., None]
    un = (rgb - (1 - safe) * BG) / safe
    rgb2 = np.where((ka < 1)[..., None], un, rgb)
    return rgb2.clip(0, 255), a2
layers = [(i, *key(r, a, loose=('big' if i == 'body' else i != 'base'), protect=(_strand if i == 'side_hair' else None)), e) for i, r, a, e in layers]

# ---- soft outer edges against the background: C = a*H + (1-a)*B (as for the locks over the neck), in place of the
# gray key / binary masks. B = the background gray smoothed locally, H = the colour of the pixel with the largest
# |C-B| within 5px among the layer's own visible core, a solved per pixel; the ring reaches 3px past the old edge
# so the thin hair tips that were cut away come back as faint pixels.
_fin_d = np.abs(final - BG).max(2)
_n, _lab = cv2.connectedComponents((_fin_d < 12).astype(np.uint8))
_bd = np.unique(np.concatenate([_lab[0], _lab[-1], _lab[:, 0], _lab[:, -1]]))
_bgF = np.isin(_lab, _bd[_bd > 0])
_bw = cv2.blur(_bgF.astype(np.float32), (41, 41))
_Bsm = cv2.blur(final * _bgF[..., None], (41, 41)) / np.maximum(_bw, 1e-3)[..., None]
_Bsm = np.where((_bw > 0.02)[..., None], _Bsm, BG)
_Dn = np.abs(final - _Bsm).max(2)
_EDGE = ('back_hair', 'side_hair', 'front_hair', 'ahoge', 'goggles', 'tag', 'earring', 'body', 'base')
_li = {l[0]: k for k, l in enumerate(layers)}
_top = np.full((H, W), -1, np.int32)
for k, (_id, _r, _a, _e) in enumerate(layers):
    if _e.get('role', '').startswith('lid_'): continue
    _top[_a > 0.5] = k
_near_bg = dil(_bgF, 4)
_cand = {}
for n_ in _EDGE:
    k = _li[n_]; a_ = layers[k][2]
    vis = (_top == k) & (a_ > 0.9) & ~_bgF
    _cand[n_] = vis
# owner of each ring pixel = the layer whose visible core is nearest (within 5px)
_dist = np.full((H, W), 1e9, np.float32); _own = np.full((H, W), -1, np.int32)
for n_, vis in _cand.items():
    dt = cv2.distanceTransform((~vis).astype(np.uint8), cv2.DIST_L2, 5)
    up = dt < _dist; _dist = np.where(up, dt, _dist); _own = np.where(up, _li[n_], _own)
_ring = _near_bg & (_dist > 0) & (_dist <= 4.5)
# pixels already given to some layer that is not an edge layer (face, eyes...) stay as they are
_other = np.zeros((H, W), bool)
for k, (_id, _r, _a, _e) in enumerate(layers):
    if _id not in _EDGE and not _e.get('role', '').startswith('lid_'): _other |= _a > 0.5
_ring &= ~_other
_ring[:6] = False; _ring[-6:] = False; _ring[:, :6] = False; _ring[:, -6:] = False   # np.roll below wraps around
# H: the strongest pixel of the owner's visible core within 5px
_Dm = np.full((H, W), -1.0, np.float32); _Hc = np.zeros_like(final)
for n_, vis in _cand.items():
    _Dm = np.where(vis, _Dn, _Dm)
_best = np.full((H, W), -1.0, np.float32); _bestc = np.zeros_like(final); _bown = np.full((H, W), -1, np.int32)
_ownmap = np.full((H, W), -1, np.int32)
for n_, vis in _cand.items(): _ownmap[vis] = _li[n_]
for _dy in range(-5, 6):
    for _dx in range(-5, 6):
        if _dy * _dy + _dx * _dx > 25: continue
        sD = np.roll(np.roll(_Dm, _dy, 0), _dx, 1); sO = np.roll(np.roll(_ownmap, _dy, 0), _dx, 1)
        ok = (sO == _own) & (sD > _best)
        _best = np.where(ok, sD, _best); _bestc = np.where(ok[..., None], np.roll(np.roll(final, _dy, 0), _dx, 1), _bestc)
_HB = _bestc - _Bsm; _den = (_HB ** 2).sum(2)
_solve = _ring & (_best > 0) & (_den > 20 ** 2)
_ae = np.clip(((final - _Bsm) * _HB).sum(2) / np.maximum(_den, 1e-3), 0, 1)
_ae = np.where(_ae < 0.04, 0.0, _ae)
_gone = _ring & ~_solve          # ring pixels with no core colour to solve from: background
before = {n_: layers[_li[n_]][2].copy() for n_ in _EDGE}
for n_ in _EDGE:
    k = _li[n_]; _id, r_, a_, e_ = layers[k]
    r_ = r_.copy(); a_ = a_.copy()
    mine = _solve & (_own == k)
    r_[mine] = _bestc[mine]; a_[mine] = _ae[mine]
    a_[(_solve | _gone) & (_own != k)] = 0.0       # one layer per ring pixel
    a_[_gone & (_own == k)] = 0.0
    layers[k] = (_id, r_, a_, e_)
    print('edge', n_, 'ring px', int(mine.sum()), 'semi', int((mine & (_ae > 0) & (_ae < 0.97)).sum()),
          'new px', int((mine & (_ae > 0) & (before[n_] < 0.004)).sum()))

# thin tails (the end of the ahoge, curl tips) have no core within 5px: grow from the solved edge along the faint
# pixels, a pixel at a time, each taking H from the neighbour it grew from (at most 40 steps)
_lay = np.full((H, W), -1, np.int32); _Hg = np.zeros_like(final)
for n_ in _EDGE:
    k = _li[n_]; a_ = layers[k][2]
    m_ = (a_ > 0.25) & _near_bg
    _lay[m_] = k; _Hg[m_] = layers[k][1][m_]
_taken = np.zeros((H, W), bool)
for k_, (_id, _r, _a, _e) in enumerate(layers):
    if not _e.get('role', '').startswith('lid_'): _taken |= _a > 0.004
_grown = 0
for _it in range(40):
    front = (_lay >= 0)
    newc = np.zeros((H, W), bool); newl = np.full((H, W), -1, np.int32); newH = np.zeros_like(final)
    for _dy, _dx in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)):
        sl = np.roll(np.roll(_lay, _dy, 0), _dx, 1); sH = np.roll(np.roll(_Hg, _dy, 0), _dx, 1)
        c = (sl >= 0) & ~front & ~_taken & ~newc & _near_bg & (_Dn > 7)
        newc |= c; newl = np.where(c, sl, newl); newH = np.where(c[..., None], sH, newH)
    newc[:6] = False; newc[-6:] = False; newc[:, :6] = False; newc[:, -6:] = False
    if not newc.any(): break
    HB = newH - _Bsm; den = (HB ** 2).sum(2)
    a2 = np.clip(((final - _Bsm) * HB).sum(2) / np.maximum(den, 1e-3), 0, 1)
    ok = newc & (den > 20 ** 2) & (a2 > 0.06)
    for n_ in _EDGE:
        k = _li[n_]; m_ = ok & (newl == k)
        if m_.any():
            _id, r_, a_, e_ = layers[k]; r_ = r_.copy(); a_ = a_.copy()
            r_[m_] = newH[m_]; a_[m_] = a2[m_]; layers[k] = (_id, r_, a_, e_)
    _lay[ok] = newl[ok]; _Hg[ok] = newH[ok]; _taken |= ok; _grown += int(ok.sum())
print('thin tails grown', _grown, 'px')

# nothing of hers touches the picture's border: a 1px dark column the key left at x=0 above y440 goes
for k, (_id, r_, a_, e_) in enumerate(layers):
    a_ = a_.copy(); a_[:2] = 0; a_[-2:] = 0; a_[:, :2] = 0; a_[:, -2:] = 0; layers[k] = (_id, r_, a_, e_)

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
