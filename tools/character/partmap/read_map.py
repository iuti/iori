"""chico's edited PSD -> labels.png, the same rule as tools/character/partmap/psd_map.py read (exact part colour at full
alpha is that part; any other pixel takes the part of the nearest pixel that is)."""
import json, sys, numpy as np
from PIL import Image
from scipy import ndimage as ndi
from psd_read import read_layers
psd, legend, out = sys.argv[1:4]
W, H, L = read_layers(psd); parts = json.load(open(legend, encoding='utf8'))
c = [x for x in L if x[0].lower().startswith('map')]
assert len(c) == 1, [x[0] for x in L]
name, t, l, b, r, ch, op, vis = c[0]
px = np.zeros((H, W, 4), np.uint8)
sub = np.dstack([ch[0], ch[1], ch[2], ch.get(-1, np.full(ch[0].shape, 255, np.uint8))])
y0, x0, y1, x1 = max(t, 0), max(l, 0), min(b, H), min(r, W)
px[y0:y1, x0:x1] = sub[y0 - t:y1 - t, x0 - l:x1 - l]
lab = np.full((H, W), 255, np.uint8)
for p in parts: lab[(px[..., :3] == p['color']).all(-1) & (px[..., 3] == 255)] = p['value']
odd = lab == 255
# colours that are not in the legend but cover a lot: a new part chico may have added
cols, cnt = np.unique(px[odd][:, :3].reshape(-1, 3), axis=0, return_counts=True) if odd.any() else (np.zeros((0, 3)), [])
big = [(tuple(int(v) for v in cc), int(n)) for cc, n in zip(cols, cnt) if n > 500]
if odd.any():
    idx = ndi.distance_transform_edt(odd, return_distances=False, return_indices=True); lab = lab[idx[0], idx[1]]
Image.fromarray(lab).save(out)
print('pixels not in a part colour (given to the nearest part):', int(odd.sum()), ' big unknown colours:', big)
for p in parts: print(p['id'], int((lab == p['value']).sum()))
