"""Add the hidden continuation of a layer under the layers above it, without changing the still picture.

  python3 extend_under.py <in.psd> <out.psd> <layer>:<pixels>:<x0,y0,x1,y1>[:flat] [...]
  python3 extend_under.py <in.psd> <out.psd> <layer>:restore:<x0,y0,x1,y1>:<ref.psd> [...]   (undoes a fill in the box)

For each rule the named layer grows by up to <pixels> inside the box, only into pixels where it holds nothing and
which the layers drawn above it cover completely at rest (so the flattened picture stays exactly the same). The
colour continues the layer's own edge. `restore` copies the layer's pixels in the box back from another PSD (a fill
that turned out to show when the layer moves); the line printed at the end says whether the still picture changed. Everything else in the PSD is written back as it was."""
import sys, numpy as np, cv2
from scipy import ndimage as ndi
from psd_tools import PSDImage
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import psd_map
src, dst = sys.argv[1:3]; rules = [r.split(':') for r in sys.argv[3:]]
psd = PSDImage.open(src); W, H = psd.size
def full(l):
    a = np.zeros((H, W, 4), np.uint8); n = (l.numpy() * 255 + 0.5).astype(np.uint8)
    if n.shape[-1] == 3: n = np.dstack([n, np.full(n.shape[:2], 255, np.uint8)])
    x0, y0, x1, y1 = max(l.left, 0), max(l.top, 0), min(l.right, W), min(l.bottom, H)
    if x1 > x0 and y1 > y0: a[y0:y1, x0:x1] = n[y0 - l.top:y1 - l.top, x0 - l.left:x1 - l.left]
    return a
L = [[l.name, full(l), l.opacity, l.visible] for l in psd]
def flat():
    c = np.zeros((H, W, 3), np.float32); t = np.zeros((H, W), np.float32)
    for nm, px, op, vis in L:
        if not vis: continue
        a = px[..., 3].astype(np.float32) / 255 * op / 255; c = c * (1 - a[..., None]) + px[..., :3] * a[..., None]; t = a + t * (1 - a)
    return c, t
before, tb = flat()
for name, n, box, *opt in rules:
    x0, y0, x1, y1 = [int(v) for v in box.split(',')]
    k = next(i for i, l in enumerate(L) if l[0].split(' ')[0] == name); px = L[k][1]
    cov = np.zeros((H, W), np.float32)
    for nm, q, op, vis in L[k + 1:]:
        if vis: cov = 1 - (1 - cov) * (1 - q[..., 3].astype(np.float32) / 255 * op / 255)
    if n == 'restore':  # the layer's pixels in the box go back to what another PSD (opt[0]) holds there
        ref = next(full(l) for l in PSDImage.open(opt[0]) if l.name.split(' ')[0] == name)
        out = px.copy(); out[y0:y1, x0:x1] = ref[y0:y1, x0:x1]; L[k][1] = out
        print(f'{name}: {int((out != px).any(-1).sum())} pixels back to {opt[0]} in {box}')
        continue
    n = int(n)
    solid = px[..., 3] >= 250
    d, idx = ndi.distance_transform_edt(~solid, return_distances=True, return_indices=True)
    inbox = np.zeros((H, W), bool); inbox[y0:y1, x0:x1] = True
    tgt = (~solid) & (cov >= 0.995) & (d <= n) & inbox       # also the layer's own soft edge: it goes solid, over the fill
    if opt and opt[0] == 'flat':      # one colour: the layer's commonest in the box (a flat underpainting continues flat)
        c = px[..., :3][solid & inbox]; q = (c // 4).astype(np.int64); key = q[:, 0] * 4096 + q[:, 1] * 64 + q[:, 2]
        v, cnt = np.unique(key, return_counts=True); sel = key == v[cnt.argmax()]
        fill = np.zeros((H, W, 3), np.uint8); fill[...] = np.median(c[sel], 0).round()
    else:
        rgb = px[..., :3][idx[0], idx[1]]                 # nearest solid colour of the layer, then smoothed along the edge
        fill = cv2.inpaint(np.ascontiguousarray(rgb), tgt.astype(np.uint8) * 255, 3, cv2.INPAINT_TELEA)
    a = px[..., 3:4].astype(np.float32) / 255
    out = px.copy(); mix = (px[..., :3] * a + fill * (1 - a)).round().astype(np.uint8)
    out[tgt, :3] = mix[tgt]; out[tgt, 3] = 255; L[k][1] = out
    print(f'{name}: {int(tgt.sum())} hidden pixels added in {box}')
after, ta = flat()
print('flattened picture changed by at most', float(np.abs(after - before).max()), '(alpha', float(np.abs(ta - tb).max()), ')')
merged = np.dstack([after, ta * 255]).round().astype(np.uint8)
psd_map.write_psd(dst, W, H, [(nm, px, op, True, not vis) for nm, px, op, vis in L], merged)
