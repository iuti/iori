"""Put the puppet's other frames (the closed eyes, the mouth's open frames) into the parts PSD as hidden layers, so
that they can be drawn on like every other part and the PSD holds everything the puppet shows.

  python3 frames_to_psd.py <in.psd> <out.psd> <model dir>

A closed eye goes right above its lashes, the mouth's frames right above the mouth. model_from_psd.py reads them back
by the first word of their names (lid_L, lid_R, mouth_small, mouth_half, mouth_open, mouth_round), shown or hidden.
Layers of those names already in the PSD are kept as they are; everything else is written back as it was."""
import json, sys, os, numpy as np
from PIL import Image
from psd_tools import PSDImage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import psd_map
src, dst, MD = sys.argv[1:4]
psd = PSDImage.open(src); W, H = psd.size
m = json.load(open(MD + '/model.json', encoding='utf8')); by = {l['id']: l for l in m['layers']}
def full(l):
    a = np.zeros((H, W, 4), np.uint8); n = (l.numpy() * 255 + 0.5).astype(np.uint8)
    if n.shape[-1] == 3: n = np.dstack([n, np.full(n.shape[:2], 255, np.uint8)])
    x0, y0, x1, y1 = max(l.left, 0), max(l.top, 0), min(l.right, W), min(l.bottom, H)
    if x1 > x0 and y1 > y0: a[y0:y1, x0:x1] = n[y0 - l.top:y1 - l.top, x0 - l.left:x1 - l.left]
    return a
def placed(path, x, y):
    t = np.array(Image.open(MD + '/' + path).convert('RGBA')); a = np.zeros((H, W, 4), np.uint8)
    a[y:y + t.shape[0], x:x + t.shape[1]] = t; a[a[..., 3] == 0] = 0
    return a
NAMES = {'lid_L': '閉じ目(絵の左)', 'lid_R': '閉じ目(絵の右)', 'mouth_small': '口・小さく開く', 'mouth_half': '口・半開き', 'mouth_open': '口・開く', 'mouth_round': '口・う'}
have = {l.name.split(' ')[0] for l in psd}
out = []; added = []
for l in psd:
    out.append((l.name, full(l), l.opacity, True, not l.visible))
    first = l.name.split(' ')[0]
    new = []
    if first.startswith('lash_') and 'lid_' + first[-1] in by: new.append(('lid_' + first[-1], by['lid_' + first[-1]]['texture'], by['lid_' + first[-1]]))
    if first == 'mouth':
        o = by['mouth']
        for f, v in zip(o['frames'], o['visemes']):
            if v != 'closed': new.append(('mouth_' + v, f, o))
    for lid, path, rect in new:
        if lid in have: continue
        out.append((f'{lid} {NAMES.get(lid, lid)}', placed(path, rect['x'], rect['y']), 255, True, True)); added.append(lid)
c = np.zeros((H, W, 3), np.float32); t = np.zeros((H, W), np.float32)
for nm, px, op, _, hid in out:
    if hid: continue
    a = px[..., 3].astype(np.float32) / 255 * op / 255; c = c * (1 - a[..., None]) + px[..., :3] * a[..., None]; t = a + t * (1 - a)
psd_map.write_psd(dst, W, H, out, np.dstack([c, t * 255]).round().astype(np.uint8))
print('added, hidden:', ' '.join(added) or 'nothing')
