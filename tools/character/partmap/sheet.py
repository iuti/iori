"""Pictures of a part map to look at: the whole map over the final, the head enlarged with the borders drawn,
and each head part cut out alone (what a layer cut along this map would hold).
  python3 sheet.py <final.png> <labels.png> <labels.json> <out folder>"""
import json, sys, numpy as np
from PIL import Image, ImageDraw, ImageFont
FONT = '/usr/share/fonts/opentype/ipafont-gothic/ipag.ttf'
final, labels, legend, out = sys.argv[1:5]
f = np.array(Image.open(final).convert('RGB')); lab = np.array(Image.open(labels)); parts = json.load(open(legend, encoding='utf8'))
pal = np.array([p['color'] for p in parts], np.uint8); ID = {p['id']: p['value'] for p in parts}; NAME = {p['id']: p['name'] for p in parts}
Image.fromarray((f * 0.45 + pal[lab] * 0.55).astype(np.uint8)).save(out + '/preview.png')
# the head, 3x, light tint and white borders
box = (300, 30, 820, 480); k = 3
mix = (f * 0.62 + pal[lab] * 0.38).astype(np.uint8)
im = Image.fromarray(mix).crop(box).resize(((box[2] - box[0]) * k, (box[3] - box[1]) * k), Image.LANCZOS)
b = np.zeros(lab.shape, bool); b[:, 1:] |= lab[:, 1:] != lab[:, :-1]; b[1:, :] |= lab[1:, :] != lab[:-1, :]
bb = np.array(Image.fromarray((b * 255).astype(np.uint8)).crop(box).resize(im.size, Image.NEAREST)) > 0
a = np.array(im); a[bb] = (255, 255, 255); Image.fromarray(a).save(out + '/preview_head.png')
# each head part alone
groups = [('back_hair',), ('side_hair',), ('front_hair',), ('ahoge',), ('goggles',), ('face', 'eye_L', 'iris_L', 'lash_L', 'eye_R', 'iris_R', 'lash_R', 'mouth'),
          ('neck',), ('tag', 'earring')]
tw, th = box[2] - box[0], box[3] - box[1]; font = ImageFont.truetype(FONT, 22)
sheet = Image.new('RGB', (tw * 4, th * 2), (0, 0, 0))
for i, g in enumerate(groups):
    m = np.isin(lab, [ID[n] for n in g])
    t = np.zeros_like(f); t[...] = (40, 150, 70); t[m] = f[m]
    tile = Image.fromarray(t).crop(box); d = ImageDraw.Draw(tile)
    d.rectangle([0, 0, tw - 1, th - 1], outline=(0, 0, 0)); d.text((8, 6), ' + '.join(NAME[n] for n in g[:1]) + (' ほか' if len(g) > 2 else (' + ' + NAME[g[1]] if len(g) == 2 else '')), fill=(255, 255, 255), font=font)
    sheet.paste(tile, ((i % 4) * tw, (i // 4) * th))
sheet.save(out + '/parts_sheet.png')
