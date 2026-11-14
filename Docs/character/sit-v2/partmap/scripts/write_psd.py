"""labels.png + labels.json -> partmap_draft.psd: the final picture at the bottom (reference), then one layer per part,
filled with the part's flat colour exactly where the map gives it that part (layer opacity 55%, so the lines show).
No outside libraries: the PSD is written by hand (raw channels)."""
import json, struct, os, numpy as np
from PIL import Image
os.chdir(os.path.dirname(os.path.abspath(__file__)))
fin = np.asarray(Image.open('C:/ai/iori_live/source/final.png').convert('RGBA'))
lab = np.asarray(Image.open('labels.png')); parts = json.load(open('labels.json', encoding='utf8'))
H, W = lab.shape
layers = [('最終版(下敷き・さわらない)', fin, 255, False)]
for p in parts:
    c = np.zeros((H, W, 4), np.uint8); mk = lab == p['value']
    c[mk, :3] = p['color']; c[mk, 3] = 255
    layers.append((f"{p['value']:02d} {p['name']} ({p['id']})", c, 140, False))
def pascal(s):
    b = s.encode('ascii', 'replace')[:255]; r = bytes([len(b)]) + b
    return r + b'\0' * ((-len(r)) % 4)
recs, chans = b'', b''
for nm, c, op, hid in layers:
    ys, xs = np.where(c[..., 3] > 0)
    t, l, b, r = (int(ys.min()), int(xs.min()), int(ys.max()) + 1, int(xs.max()) + 1) if len(ys) else (0, 0, 1, 1)
    crop = c[t:b, l:r]
    data = {cid: struct.pack('>H', 0) + crop[..., idx].tobytes() for cid, idx in ((-1, 3), (0, 0), (1, 1), (2, 2))}
    rec = struct.pack('>iiiiH', t, l, b, r, 4)
    for cid in (-1, 0, 1, 2): rec += struct.pack('>hI', cid, len(data[cid]))
    rec += b'8BIMnorm' + struct.pack('>BBBB', op, 0, 0x02 if hid else 0, 0)
    u = nm.encode('utf-16-be'); luni = struct.pack('>I', len(nm)) + u; luni += b'\0' * ((-len(luni)) % 4)
    extra = struct.pack('>II', 0, 0) + pascal(nm) + b'8BIMluni' + struct.pack('>I', len(luni)) + luni
    rec += struct.pack('>I', len(extra)) + extra
    recs += rec
    for cid in (-1, 0, 1, 2): chans += data[cid]
linfo = struct.pack('>h', -len(layers)) + recs + chans; linfo += b'\0' * ((-len(linfo)) % 4)
lm = struct.pack('>I', len(linfo)) + linfo + struct.pack('>I', 0)
# merged image: the final with the map over it at 55%
pal = np.array([p['color'] for p in parts], np.float32)
merged = (fin[..., :3] * 0.45 + pal[lab] * 0.55).clip(0, 255).astype(np.uint8)
merged = np.dstack([merged, np.full((H, W), 255, np.uint8)])
psd = b'8BPS' + struct.pack('>H6xHIIHH', 1, 4, H, W, 8, 3) + struct.pack('>II', 0, 0)
psd += struct.pack('>I', len(lm)) + lm + struct.pack('>H', 0) + b''.join(merged[..., i].tobytes() for i in range(4))
open('partmap_draft.psd', 'wb').write(psd)
print('layers', len(layers), 'MB', round(len(psd) / 1e6, 1))
