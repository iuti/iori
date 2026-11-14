"""The part map as a PSD that CLIP STUDIO PAINT opens, and back.

  write: python3 psd_map.py write <final.png> <labels.png> <labels.json> <out.psd>
         layers, bottom first: the final picture / "map" (every part in its flat colour, layer opacity 55%) /
         "legend" (the colours with their names, to pick from)
  read:  python3 psd_map.py read <edited.psd> <labels.json> <out labels.png>
         takes the layer called "map": a pixel in exactly a part's colour is that part; any other pixel (a soft
         brush edge, a hole) takes the part of the nearest pixel that is.
"""
import json, struct, sys, numpy as np
from PIL import Image, ImageDraw, ImageFont

FONT = '/usr/share/fonts/opentype/ipafont-gothic/ipag.ttf'

def packbits(row):
    n = len(row); out = bytearray(); i = 0
    ch = np.flatnonzero(np.diff(row)) + 1
    starts = np.concatenate(([0], ch)); ends = np.concatenate((ch, [n]))
    lit = bytearray()
    def flush():
        nonlocal lit
        j = 0
        while j < len(lit):
            c = lit[j:j + 128]; out.append(len(c) - 1); out.extend(c); j += 128
        lit = bytearray()
    for s, e in zip(starts.tolist(), ends.tolist()):
        L = e - s; v = int(row[s])
        if L >= 3:
            flush()
            while L > 0:
                c = min(L, 128)
                if c == 1: out.append(0); out.append(v)
                else: out.append(257 - c); out.append(v)
                L -= c
        else:
            lit.extend([v] * L)
    flush()
    return bytes(out)

def channel(plane, rle):
    if not rle: return struct.pack('>H', 0) + plane.tobytes()
    rows = [packbits(r) for r in plane]
    return struct.pack('>H', 1) + b''.join(struct.pack('>H', len(r)) for r in rows) + b''.join(rows)

def pascal(s):
    b = s.encode('ascii', 'replace')[:255]; r = bytes([len(b)]) + b
    return r + b'\0' * ((-len(r)) % 4)

def write_psd(path, W, H, layers, merged):
    """layers: (name, RGBA array of the whole canvas, opacity 0..255, rle) bottom first."""
    recs, chans = b'', b''
    for nm, c, op, rle in layers:
        ys, xs = np.where(c[..., 3] > 0)
        t, l, b, r = (int(ys.min()), int(xs.min()), int(ys.max()) + 1, int(xs.max()) + 1) if len(ys) else (0, 0, 1, 1)
        crop = np.ascontiguousarray(c[t:b, l:r])
        data = [channel(crop[..., i], rle) for i in (3, 0, 1, 2)]
        rec = struct.pack('>iiiiH', t, l, b, r, 4)
        for cid, d in zip((-1, 0, 1, 2), data): rec += struct.pack('>hI', cid, len(d))
        rec += b'8BIMnorm' + struct.pack('>BBBB', op, 0, 0, 0)
        u = nm.encode('utf-16-be'); luni = struct.pack('>I', len(nm)) + u; luni += b'\0' * ((-len(luni)) % 4)
        extra = struct.pack('>II', 0, 0) + pascal(nm) + b'8BIMluni' + struct.pack('>I', len(luni)) + luni
        recs += rec + struct.pack('>I', len(extra)) + extra
        chans += b''.join(data)
    linfo = struct.pack('>h', len(layers)) + recs + chans; linfo += b'\0' * ((-len(linfo)) % 2)
    lm = struct.pack('>I', len(linfo)) + linfo + struct.pack('>I', 0)
    psd = b'8BPS' + struct.pack('>H6xHIIHH', 1, 3, H, W, 8, 3) + struct.pack('>II', 0, 0)
    psd += struct.pack('>I', len(lm)) + lm + struct.pack('>H', 0) + b''.join(merged[..., i].tobytes() for i in range(3))
    open(path, 'wb').write(psd)

def legend_layer(parts, W, H):
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im); f = ImageFont.truetype(FONT, 17)
    x0, y0, rh = 8, 8, 27
    d.rectangle([x0, y0, x0 + 262, y0 + rh * len(parts) + 10], fill=(30, 30, 36, 255))
    for k, p in enumerate(parts):
        y = y0 + 6 + k * rh
        d.rectangle([x0 + 6, y, x0 + 46, y + rh - 5], fill=tuple(p['color']) + (255,))
        d.text((x0 + 54, y + 1), f"{p['name']} {p['id']}", fill=(255, 255, 255, 255), font=f)
    return np.array(im)

def write(final_png, labels_png, legend_json, out_psd):
    fin = np.array(Image.open(final_png).convert('RGBA')); lab = np.array(Image.open(labels_png))
    parts = json.load(open(legend_json, encoding='utf8')); H, W = lab.shape
    pal = np.array([p['color'] for p in parts], np.uint8)
    mp = np.dstack([pal[lab], np.full((H, W), 255, np.uint8)])
    leg = legend_layer(parts, W, H)
    merged = (fin[..., :3] * 0.45 + pal[lab] * 0.55).astype(np.uint8)
    a = leg[..., 3:4] / 255.0; merged = (merged * (1 - a) + leg[..., :3] * a).astype(np.uint8)
    write_psd(out_psd, W, H, [('final 最終版(さわらない)', fin, 255, False), ('map 塗り分け(ここを直す)', mp, 140, True),
                              ('legend 色の見本', leg, 255, True)], merged)

def read(psd_path, legend_json, out_png):
    from psd_tools import PSDImage
    from scipy import ndimage as ndi
    psd = PSDImage.open(psd_path); parts = json.load(open(legend_json, encoding='utf8'))
    cands = [l for l in psd.descendants() if l.name.lower().startswith('map')]
    if len(cands) != 1: raise SystemExit('want exactly one layer whose name starts with "map", found: ' + ', '.join(l.name for l in psd.descendants()))
    L = cands[0]; W, H = psd.width, psd.height
    px = np.zeros((H, W, 4), np.uint8); a = (L.numpy() * 255 + 0.5).astype(np.uint8)
    if a.shape[-1] == 3: a = np.dstack([a, np.full(a.shape[:2], 255, np.uint8)])
    x0, y0 = max(L.left, 0), max(L.top, 0); x1, y1 = min(L.right, W), min(L.bottom, H)
    px[y0:y1, x0:x1] = a[y0 - L.top:y1 - L.top, x0 - L.left:x1 - L.left]
    lab = np.full((H, W), 255, np.uint8)
    for p in parts: lab[(px[..., :3] == p['color']).all(-1) & (px[..., 3] == 255)] = p['value']
    odd = lab == 255
    if odd.any():
        idx = ndi.distance_transform_edt(odd, return_distances=False, return_indices=True); lab = lab[idx[0], idx[1]]
    Image.fromarray(lab).save(out_png)
    print('pixels not in a part colour (given to the nearest part):', int(odd.sum()))
    for p in parts: print(p['id'], int((lab == p['value']).sum()))

if __name__ == '__main__':
    {'write': write, 'read': read}[sys.argv[1]](*sys.argv[2:])
