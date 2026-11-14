"""The puppet's textures and model.json straight from the layered PSD (the PSD is the truth: what a layer holds is
what the puppet shows, nothing is cut or solved again here).

  python3 model_from_psd.py <parts.psd> <model dir (its model.json is the template, rewritten in place)> [<final.png>]

Layers are matched by the first word of their name (base, strap, back_hair, body, neck, face, eye_L, iris_L, lash_L,
eye_R, iris_R, lash_R, mouth, side_hair, earring, tag, front_hair, brow_L, brow_R, ahoge, goggles; 耳 = ear), drawn in
the PSD's own order. Hidden layers are left out, except the puppet's other frames: the closed eyes (lid_L, lid_R) and
the mouth's open frames (mouth_small, mouth_half, mouth_open, mouth_round), which are read shown or hidden
(frames_to_psd.py puts them into a PSD). Where the PSD has none of them they stay as the model has them; the PSD's
mouth becomes the closed frame."""
import json, os, sys, numpy as np
from PIL import Image
from psd_tools import PSDImage
from scipy import ndimage as ndi
psd_path, MD = sys.argv[1:3]; final = sys.argv[3] if len(sys.argv) > 3 else None
ALIAS = {'耳': 'ear'}
NEW = {   # layers the template does not have yet
    'strap': {'group': 'body'}, 'neck': {'group': 'body'}, 'ear': {'group': 'head'}, 'brow_L': {'group': 'head'}, 'brow_R': {'group': 'head'}}
STILL = ['base', 'strap']
psd = PSDImage.open(psd_path); W, H = psd.size
tm = json.load(open(MD + '/model.json', encoding='utf8')); told = {l['id']: l for l in tm['layers']}
def full(l):
    a = np.zeros((H, W, 4), np.uint8); n = (l.numpy() * 255 + 0.5).astype(np.uint8)
    if n.shape[-1] == 3: n = np.dstack([n, np.full(n.shape[:2], 255, np.uint8)])
    x0, y0, x1, y1 = max(l.left, 0), max(l.top, 0), min(l.right, W), min(l.bottom, H)
    if x1 > x0 and y1 > y0: a[y0:y1, x0:x1] = n[y0 - l.top:y1 - l.top, x0 - l.left:x1 - l.left]
    return a
def bleed(c):     # colour under the transparent pixels: the nearest opaque colour (no dark fringe when filtered)
    solid = c[..., 3] > 127
    if not solid.any(): return c
    idx = ndi.distance_transform_edt(~solid, return_distances=False, return_indices=True)
    o = c.copy(); z = c[..., 3] == 0; o[z, :3] = c[idx[0], idx[1]][z, :3]; return o
os.makedirs(MD + '/textures', exist_ok=True)
FRAMES = ('lid_L', 'lid_R', 'mouth_small', 'mouth_half', 'mouth_open', 'mouth_round')
frame = {l.name.split(' ')[0]: full(l) for l in psd if l.name.split(' ')[0] in FRAMES}      # the other frames, shown or hidden
def lid_layer(side):     # the closed eye: from the PSD when it is there, else as the model has it
    d = dict(told['lid_' + side]); c = frame.get('lid_' + side)
    if c is not None and (c[..., 3] > 0).any():
        ys, xs = np.where(c[..., 3] > 0)
        x0, y0, x1, y1 = max(0, int(xs.min()) - 8), max(0, int(ys.min()) - 8), min(W, int(xs.max()) + 9), min(H, int(ys.max()) + 9)
        Image.fromarray(bleed(c)[y0:y1, x0:x1]).save(MD + '/' + d['texture']); d.update({'x': x0, 'y': y0, 'w': x1 - x0, 'h': y1 - y0})
    return d
layers = []; rest = []
for l in psd:
    name = l.name.split(' ')[0].split('　')[0]; lid = ALIAS.get(name, name)
    if not l.visible or lid.startswith('ref_') or lid in FRAMES: continue
    if lid not in told and lid not in NEW: print('! unknown layer, left out:', l.name); continue
    c = full(l)
    if not (c[..., 3] > 0).any(): print('! empty layer, left out:', l.name); continue
    d = {k: v for k, v in told.get(lid, NEW.get(lid, {})).items() if k not in ('texture', 'frames', 'visemes', 'mask', 'x', 'y', 'w', 'h')}
    d['id'] = lid
    if lid == 'mouth':     # the closed frame, in the frames' own rectangle
        o = told['mouth']; x0, y0, x1, y1 = o['x'], o['y'], o['x'] + o['w'], o['y'] + o['h']
        ys, xs = np.where(c[..., 3] > 0)
        if xs.min() < x0 or ys.min() < y0 or xs.max() >= x1 or ys.max() >= y1: print('! the mouth is larger than the frames\' rectangle', (xs.min(), ys.min(), xs.max(), ys.max()))
        Image.fromarray(bleed(c)[y0:y1, x0:x1]).save(MD + '/' + o['frames'][0])
        for f, v in zip(o['frames'], o.get('visemes', [])):     # the open frames the PSD holds, in the same rectangle
            q = frame.get('mouth_' + v)
            if q is None or not (q[..., 3] > 0).any(): continue
            out = q.copy(); out[:y0] = 0; out[y1:] = 0; out[:, :x0] = 0; out[:, x1:] = 0
            if (out[..., 3] > 0).sum() != (q[..., 3] > 0).sum(): print(f'! mouth_{v} reaches outside the frames\' rectangle, cut there')
            Image.fromarray(bleed(q)[y0:y1, x0:x1]).save(MD + '/' + f)
        d = dict(o); layers.append(d); rest.append(c); continue
    ys, xs = np.where(c[..., 3] > 0); P = 8
    x0, y0, x1, y1 = max(0, xs.min() - P), max(0, ys.min() - P), min(W, xs.max() + 1 + P), min(H, ys.max() + 1 + P)
    Image.fromarray(bleed(c)[y0:y1, x0:x1]).save(f'{MD}/textures/{lid}.png')
    d.update({'x': int(x0), 'y': int(y0), 'w': int(x1 - x0), 'h': int(y1 - y0), 'texture': f'textures/{lid}.png'})
    if lid in ('eye_L', 'eye_R'):
        Image.fromarray(((c[y0:y1, x0:x1, 3] > 127) * 255).astype(np.uint8), 'L').save(f'{MD}/textures/{lid}_mask.png'); d['mask'] = f'textures/{lid}_mask.png'
    layers.append(d)
    if lid.startswith('iris_'):     # at rest the iris shows only inside the white
        m = full([q for q in psd if q.name.split(' ')[0] == 'eye_' + lid[-1]][0])[..., 3] > 127; c = c.copy(); c[..., 3] = np.where(m, c[..., 3], 0)
    rest.append(c)
    if lid.startswith('lash_') and 'lid_' + lid[-1] in told: layers.append(lid_layer(lid[-1]))
tm['layers'] = layers
xi = tm.setdefault('x-iori', {}); xi['still'] = [s for s in STILL if any(l['id'] == s for l in layers)]
json.dump(tm, open(MD + '/model.json', 'w', encoding='utf8'), ensure_ascii=False, indent=1)
used = {l['texture'] for l in layers} | {f for l in layers for f in l.get('frames', [])} | {l['mask'] for l in layers if l.get('mask')}
for fn in sorted(os.listdir(MD + '/textures')):
    if fn.endswith('.png') and 'textures/' + fn not in used and not fn.endswith('_n.png'): print('! texture no longer used (left on disk):', fn)
print('layers:', ' '.join(l['id'] for l in layers))
if final:
    f = np.array(Image.open(final).convert('RGB')).astype(np.float32)
    comp = f.copy(); cov = np.zeros((H, W), np.float32)
    for c in rest:
        a = c[..., 3:4].astype(np.float32) / 255; comp = comp * (1 - a) + c[..., :3] * a; cov = np.maximum(cov, a[..., 0])
    d = np.abs(comp - f).max(-1)
    print(f'rest (layers laid over the final\'s own background) vs the final: max {d.max():.0f}, pixels over 8: {int((d > 8).sum())}, over 24: {int((d > 24).sum())}')
    Image.fromarray(comp.round().astype(np.uint8)).save(MD + '/../_rest_from_psd.png') if False else None
