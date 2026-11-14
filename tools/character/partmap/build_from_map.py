"""Puppet layers cut from the part map: each layer is the final picture where the map says that part (exact, so the
rest pose is the final picture), plus what an earlier layer already had painted for the hidden area, only where a
part drawn above it covers it. Edges facing the background are solved once: C = a*H + (1-a)*B.
usage: python build_from_map.py <labels.png> <labels.json> <old model dir> <out model dir>"""
import json, os, sys, shutil, numpy as np, cv2
from PIL import Image
lab_png, legend, OLD, OUT = sys.argv[1:5]
fin = np.asarray(Image.open('C:/ai/iori_live/source/final.png').convert('RGB')).astype(np.float32)
H, W = fin.shape[:2]
lab = np.asarray(Image.open(lab_png)); parts = json.load(open(legend, encoding='utf8'))
V = {p['id']: p['value'] for p in parts}
om = json.load(open(OLD + '/model.json', encoding='utf8'))
def full(l, tex=None):
    t = np.asarray(Image.open(OLD + '/' + (tex or l['texture'])).convert('RGBA')).astype(np.float32)
    c = np.zeros((H, W, 4), np.float32); h, w = t.shape[:2]; c[l['y']:l['y'] + h, l['x']:l['x'] + w] = t[:H - l['y'], :W - l['x']]
    return c
old = {l['id']: l for l in om['layers']}
# draw order, bottom first: (layer id, map parts it takes, old layer whose hidden painting it may keep)
ORDER = [('base', ['base', 'strap'], None), ('back_hair', ['back_hair'], 'back_hair'), ('body', ['body'], 'body'),
         ('neck', ['neck'], 'body'), ('face', ['face', 'mouth'], 'face'),
         ('eye_L', ['eye_L'], 'eye_L'), ('iris_L', ['iris_L'], None), ('lash_L', ['lash_L'], None), ('lid_L', None, 'lid_L'),
         ('eye_R', ['eye_R'], 'eye_R'), ('iris_R', ['iris_R'], None), ('lash_R', ['lash_R'], None), ('lid_R', None, 'lid_R'),
         ('mouth', None, 'mouth'), ('side_hair', ['side_hair'], None), ('earring', ['earring'], None), ('tag', ['tag'], None),
         ('front_hair', ['front_hair'], None), ('ahoge', ['ahoge'], None), ('goggles', ['goggles'], None)]
# which map parts lie above each layer (drawn later): a hidden painting may only sit under those
pos = {}
for k, (lid, ps, _) in enumerate(ORDER):
    for p in ps or []: pos[p] = k
above = lambda k: np.isin(lab, [V[p] for p, q in pos.items() if q > k])
# background, smoothed locally, for the edge solve
Bg = np.median(fin[:6].reshape(-1, 3), 0)
bgL = lab == V['bg']
bw = cv2.blur(bgL.astype(np.float32), (41, 41))
Bs = np.where((bw > 0.02)[..., None], cv2.blur(fin * bgL[..., None], (41, 41)) / np.maximum(bw, 1e-3)[..., None], Bg)
Dn = np.abs(fin - Bs).max(2)
# pixels the map leaves as background but that hold something in the final (faint hair tips beside a part): to the
# nearest part, solved as its edge
from scipy import ndimage as ndi
stray = bgL & (Dn > 12)
if stray.any():
    idx = ndi.distance_transform_edt(bgL, return_distances=False, return_indices=True)
    near = lab[idx[0], idx[1]]
    lab = lab.copy(); lab[stray] = near[stray]
    print('background pixels with something drawn, given to the nearest part:', int(stray.sum()))
    bgL = lab == V['bg']
# gaps between the locks that the map's fattened hair covers: where a hair part shows plain background, it is background
hair_ids = [V[p] for p in ('back_hair', 'side_hair', 'front_hair', 'ahoge')]
gap = np.isin(lab, hair_ids) & (Dn < 10)
_n, _cl, _st, _ = cv2.connectedComponentsWithStats(gap.astype(np.uint8), connectivity=4)
gap = gap & (_st[_cl, cv2.CC_STAT_AREA] >= 4)            # a few pixels together, not a single light pixel inside a lock
lab = lab.copy(); lab[gap] = V['bg']; bgL = lab == V['bg']
print('gaps between locks given back to the background:', int(gap.sum()))
near_bg = cv2.dilate(bgL.astype(np.uint8), np.ones((7, 7), np.uint8)) > 0
layers = []
MAPA = {}
for _pass in (0, 1):
  layers = []
  for k, (lid, ps, keep) in enumerate(ORDER):
      rgb = fin.copy(); a = np.zeros((H, W), np.float32)
      if ps:
          m = np.isin(lab, [V[p] for p in ps]); a[m] = 1.0
          # the edge facing the background (chico fattened the parts in front into it): solve a, colour = the core's
          core = m & (Dn > 30)
          Dm = np.where(core, Dn, -1.0); best = Dm.copy(); Hc = np.where(core[..., None], fin, 0.0)
          for dy in range(-5, 6):
              for dx in range(-5, 6):
                  if dy * dy + dx * dx > 25 or (dy == 0 and dx == 0): continue
                  sD = np.roll(np.roll(Dm, dy, 0), dx, 1); up = sD > best
                  best = np.where(up, sD, best); Hc = np.where(up[..., None], np.roll(np.roll(fin, dy, 0), dx, 1), Hc)
          edge = m & near_bg & ~core
          HB = Hc - Bs; den = (HB ** 2).sum(2)
          ae = np.clip(((fin - Bs) * HB).sum(2) / np.maximum(den, 1e-3), 0, 1)
          ok = edge & (best > 0) & (den > 20 ** 2)
          res = np.abs(fin - (ae[..., None] * Hc + (1 - ae[..., None]) * Bs)).max(2)
          ok &= res < 16                                  # only a pixel that lies between the core and the background is a mix of them
          a[ok] = np.where(ae[ok] < 0.04, 0.0, ae[ok])
          ac = np.maximum(a, 0.05)[..., None]
          sol = np.clip((fin - (1 - ac) * Bs) / ac, Hc - 40, Hc + 40).clip(0, 255)   # exact at rest, kept near the core's colour
          a2 = np.clip(((fin - Bs) * (sol - Bs)).sum(2) / np.maximum(((sol - Bs) ** 2).sum(2), 1e-3), 0, 1)
          a[ok] = np.where(a2[ok] < 0.04, 0.0, a2[ok]); rgb[ok] = sol[ok]
          a[edge & ~ok & (Dn < 8)] = 0.0              # background inside the fattened margin with no core near: background
      if ps: MAPA[lid] = a.copy()                    # the map's own cover (before any hidden painting)
      if keep and keep in old:
          o = full(old[keep])
          if lid in ('lid_L', 'lid_R', 'mouth'):        # hidden at rest or drawn as frames: as they were
              layers.append((lid, o, None, old[keep])); continue
          cov = np.zeros((H, W), np.float32)
          for j, (lj, pj, _) in enumerate(ORDER):
              if j > k and lj in MAPA: cov = 1 - (1 - cov) * (1 - MAPA[lj])
          hid = (o[..., 3] > 127) & above(k) & ~(a > 0) & ((cov > 0.99) | (_pass == 0))
          if lid.startswith('eye_'): hid |= np.isin(lab, [V['iris_' + lid[-1]]]) & ~(a > 0)   # the white goes on under the iris
          rgb[hid] = np.where((o[..., 3] > 127)[hid][:, None], o[..., :3][hid], rgb[hid]); a[hid] = 1.0
          if lid.startswith('eye_'): rgb[hid & ~(o[..., 3] > 127)] = np.median(fin[lab == V[lid]], 0)
      layers.append((lid, np.dstack([rgb, a * 255]), ps, old.get(keep or lid)))
      print(f'{lid:10s} map px {int((a > 0.5).sum() if ps else 0):7d}')
# write
shutil.rmtree(OUT, ignore_errors=True); os.makedirs(OUT + '/textures')
ml = []
for lid, c, ps, ol in layers:
    if ps is None and ol is not None and lid in ('lid_L', 'lid_R', 'mouth'):
        d = dict(ol)
        for t in ([d['texture']] + d.get('frames', [])):
            shutil.copy(OLD + '/' + t, OUT + '/' + t)
        if d.get('mask'): shutil.copy(OLD + '/' + d['mask'], OUT + '/' + d['mask'])
        ml.append(d); continue
    c = c.round().clip(0, 255).astype(np.uint8)
    ys, xs = np.where(c[..., 3] > 0); P = 8
    x0, y0, x1, y1 = max(0, xs.min() - P), max(0, ys.min() - P), min(W, xs.max() + 1 + P), min(H, ys.max() + 1 + P)
    Image.fromarray(c[y0:y1, x0:x1], 'RGBA').save(f'{OUT}/textures/{lid}.png', optimize=True)
    d = {k: v for k, v in (ol or {}).items() if k not in ('texture', 'frames', 'visemes', 'mask', 'x', 'y', 'w', 'h')}
    d.update({'id': lid, 'x': int(x0), 'y': int(y0), 'w': int(x1 - x0), 'h': int(y1 - y0), 'texture': f'textures/{lid}.png'})
    if lid == 'neck': d = {'id': 'neck', 'group': 'body', 'x': d['x'], 'y': d['y'], 'w': d['w'], 'h': d['h'], 'texture': d['texture']}
    if (ol or {}).get('mask'):
        mk = (c[y0:y1, x0:x1, 3] > 127).astype(np.uint8) * 255
        Image.fromarray(mk, 'L').save(f'{OUT}/textures/{lid}_mask.png'); d['mask'] = f'textures/{lid}_mask.png'
    ml.append(d)
model = dict(om); model['layers'] = ml
json.dump(model, open(OUT + '/model.json', 'w', encoding='utf8'), ensure_ascii=False, indent=1)
print('layers', len(ml))
