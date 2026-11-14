"""Cut the final picture into layers exactly along the part map, with the background colour taken out.

  python3 cut_layers.py <final.png> <labels.png> <labels.json> <out folder> [<out.psd>]

Every pixel of the picture goes to the one layer its part names (nothing is decided by colour). The background colour
is removed: what the map calls background, the background-coloured margin a part was given around its outline, and the
gray showing between locks of hair; the soft edge facing the background is solved into alpha, so that the layer laid
over the same gray gives the final picture back. Borders between two parts are cut hard, on the map's line.
The eyebrows (pieces painted in the lashes' colour that do not touch the eye) become layers of their own.
Writes <out>/<id>.png (whole canvas, straight alpha), <out>/layers.json, <out>/check.png, and the PSD."""
import json, sys, os, numpy as np, cv2
from PIL import Image
from scipy import ndimage as ndi
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import psd_map

final, labels, legend, out = sys.argv[1:5]; psd_path = sys.argv[5] if len(sys.argv) > 5 else None
f = np.array(Image.open(final).convert('RGB')).astype(np.float32); lab = np.array(Image.open(labels)).astype(np.int32)
parts = json.load(open(legend, encoding='utf8')); ID = {p['id']: p['value'] for p in parts}; H, W = lab.shape
names = {p['id']: p['name'] for p in parts}
# eyebrows: lash-coloured pieces that do not touch the eye
nxt = max(ID.values()) + 1
for side, jp in (('L', '絵の左'), ('R', '絵の右')):
    lash = lab == ID['lash_' + side]; eye = ndi.binary_dilation(np.isin(lab, [ID['eye_' + side], ID['iris_' + side]]), iterations=2)
    cl, n = ndi.label(lash, structure=np.ones((3, 3)))
    brow = np.zeros((H, W), bool)
    for j in range(1, n + 1):
        m = cl == j
        if not (m & eye).any() and m.sum() >= 30: brow |= m
    if brow.any(): ID['brow_' + side] = nxt; names['brow_' + side] = f'眉({jp})'; lab[brow] = nxt; nxt += 1
ORDER = ['base', 'strap', 'back_hair', 'body', 'neck', 'face', 'eye_L', 'iris_L', 'lash_L', 'eye_R', 'iris_R', 'lash_R', 'mouth',
         'side_hair', 'earring', 'tag', 'front_hair', 'brow_L', 'brow_R', 'ahoge', 'goggles']
ORDER = [n for n in ORDER if n in ID]
bgm = lab == ID['bg']
# the background's colour everywhere (it is not quite flat): from the pixels well inside the background
core = ndi.binary_erosion(bgm, iterations=3).astype(np.float32); ks = 81
num = cv2.blur(f * core[..., None], (ks, ks)); den = cv2.blur(core, (ks, ks))
B = num / np.maximum(den, 1e-4)[..., None]
far = den < 1e-3
if far.any():
    idx = ndi.distance_transform_edt(far, return_distances=False, return_indices=True); B = B[idx[0], idx[1]]
dB = np.abs(f - B).max(-1)
# ink the map left in the background right beside a part (the outline's soft edge) goes to that part
dd, idx = ndi.distance_transform_edt(bgm, return_distances=True, return_indices=True)
stray = bgm & (dB > 10) & (dd <= 4)
lab[stray] = lab[idx[0], idx[1]][stray]; bgm = lab == ID['bg']
print('background pixels with ink beside a part, given to that part:', int(stray.sum()))
# what is background: the map's background, the background-coloured pixels joined to it, and the gray between hair
bgish = dB < 7
cl, n = ndi.label(bgm | bgish)
G = np.isin(cl, np.unique(cl[bgm])) & (bgm | bgish)
hair = np.isin(lab, [ID[n] for n in ('back_hair', 'side_hair', 'front_hair', 'ahoge')])
hl, hn = ndi.label(bgish & hair & ~G)
if hn:
    sz = ndi.sum(bgish & hair & ~G, hl, range(1, hn + 1)); G |= np.isin(hl, [i + 1 for i, z in enumerate(sz) if z >= 12])
# no speckle: a few opaque pixels alone in the background are background, a few background pixels alone in a part are the part
def small(mask, n):
    cl, k = ndi.label(mask)
    if not k: return np.zeros_like(mask)
    sz = ndi.sum(mask, cl, range(1, k + 1)); return np.isin(cl, [i + 1 for i, z in enumerate(sz) if z < n])
G |= small(~G, 6) & (dB < 20)
G &= ~(small(G, 6) & ~bgm)
ink_in_bg = bgm & (dB > 24)
# alpha of the edge facing the background: C = a*F + (1-a)*B, F the same thing a little further in
dist = ndi.distance_transform_edt(~G)
alpha = np.where(G, 0.0, 1.0).astype(np.float32); F = f.copy()
edge = (~G) & (dist <= 2.5)
ys, xs = np.where(edge)
d = f - B
R = 3
best = np.full(len(ys), -1e9, np.float32); Hc = f[ys, xs].copy()
for dy in range(-R, R + 1):
    for dx in range(-R, R + 1):
        if dx * dx + dy * dy > R * R + 1: continue
        y2 = np.clip(ys + dy, 0, H - 1); x2 = np.clip(xs + dx, 0, W - 1)
        ok = (~G[y2, x2]) & (lab[y2, x2] == lab[ys, xs]) & (dB[y2, x2] > 30)     # only something clearly not background can be F
        sc = (d[ys, xs] * d[y2, x2]).sum(-1); sc[~ok] = -1e9
        up = sc > best; best[up] = sc[up]; Hc[up] = f[y2, x2][up]
hb = Hc - B[ys, xs]; a = (d[ys, xs] * hb).sum(-1) / np.maximum((hb * hb).sum(-1), 1e-3)
a[best < 0] = 0                       # nothing solid of its kind next to it: as transparent as its colour allows
a[dB[ys, xs] > 30] = np.maximum(a[dB[ys, xs] > 30], 0.5)
a = np.clip(a, 0, 1); a[a > 0.94] = 1
# never less than what keeps the colour a real one (so that a*F + (1-a)*B is the picture again, exactly)
c = f[ys, xs]; b = B[ys, xs]
need = np.where(c > b, (c - b) / np.maximum(255 - b, 1), (b - c) / np.maximum(b, 1)).max(-1)
a = np.maximum(a, np.minimum(1, need + 0.004))
alpha[ys, xs] = a
F[ys, xs] = np.clip(b + (c - b) / np.maximum(a[:, None], 1e-3), 0, 255)
os.makedirs(out, exist_ok=True)
layers = []; comp = B.copy(); cover = np.zeros((H, W), np.float32); info = []
for name in ORDER:
    m = lab == ID[name]; a = np.where(m, alpha, 0)
    rgb = F.copy()
    # colour under the transparent pixels: the nearest colour of the layer (no dark fringe when the texture is filtered)
    solid = a > 0.5
    if solid.any():
        idx = ndi.distance_transform_edt(~solid, return_distances=False, return_indices=True); rgb = np.where(solid[..., None], rgb, rgb[idx[0], idx[1]])
    rgb[a > 0] = F[a > 0]
    px = np.dstack([rgb, a * 255]).round().astype(np.uint8)
    Image.fromarray(px).save(f'{out}/{name}.png')
    comp = comp * (1 - a[..., None]) + px[..., :3] * a[..., None]; cover += (a > 0)
    yy, xx = np.where(a > 0)
    info.append({'id': name, 'name': names[name], 'bbox': [int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1] if len(yy) else None,
                 'pixels': int((a > 0).sum()), 'soft': int(((a > 0) & (a < 1)).sum())})
    layers.append((f'{name} {names[name]}', px, 255, True))
diff = np.abs(comp - f).max(-1)
dp = np.where(G, 0, diff)       # inside the parts (the background itself is only near the gray, by up to 7)
print('layers', len(layers), '| laid over the gray vs the final, inside the parts: max diff', round(float(dp.max()), 1), ', pixels over 2:', int((dp > 2).sum()),
      '| in what was taken as background: max', round(float(np.where(G & ~ink_in_bg, diff, 0).max()), 1))
if (dp > 2).any():
    yy, xx = np.where(dp > 2); print('  worst at', [(int(x), int(y), round(float(dp[y, x]), 1), round(float(alpha[y, x]), 2)) for y, x in sorted(zip(yy, xx), key=lambda t: -dp[t])[:8]])
print('pixels in more than one layer:', int((cover > 1).sum()), '| part pixels made transparent (background colour):', int((G & ~bgm).sum()),
      '| ink the map leaves in the background (diff > 24):', int(ink_in_bg.sum()))
Image.fromarray(lab.astype(np.uint8)).save(out + '/labels_used.png')
json.dump({'order_bottom_first': ORDER, 'ids': ID, 'layers': info}, open(out + '/layers.json', 'w', encoding='utf8'), ensure_ascii=False, indent=1)
# check picture: the layers over a blue that is not in the picture, and what was taken out as background inside parts
blue = np.zeros((H, W, 3), np.float32); blue[...] = (40, 90, 200)
tot = np.zeros((H, W), np.float32); c2 = blue.copy()
for (nm, px, _, _) in layers:
    a = px[..., 3:4] / 255.0; c2 = c2 * (1 - a) + px[..., :3] * a
Image.fromarray(c2.round().astype(np.uint8)).save(out + '/check.png')
lost = (f * 0.35).astype(np.uint8); lost[G & ~bgm] = (255, 0, 255); lost[ink_in_bg] = (255, 255, 0)
Image.fromarray(lost).save(out + '/check_background.png')
if psd_path:
    fin = np.dstack([f, np.full((H, W), 255)]).astype(np.uint8)
    ta = np.zeros((H, W), np.float32); mc = np.zeros((H, W, 3), np.float32)
    for (nm, px, _, _) in layers:
        a = px[..., 3] / 255.0; mc = mc * (1 - a[..., None]) + px[..., :3] * a[..., None]; ta = a + ta * (1 - a)
    merged = np.dstack([mc, ta * 255]).round().astype(np.uint8)
    psd_map.write_psd(psd_path, W, H, [('ref_final 最終版(見本・非表示)', fin, 255, False, True)] + layers, merged)
    print('psd', psd_path, round(os.path.getsize(psd_path) / 1e6, 1), 'MB')
