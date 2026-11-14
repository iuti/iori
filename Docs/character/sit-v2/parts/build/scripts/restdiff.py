import json, sys, numpy as np
from PIL import Image
fin = np.asarray(Image.open('../../source/final.png').convert('RGB')).astype(np.float32)
def comp(d):
    m = json.load(open(d + '/iori/model.json', encoding='utf8'))
    c = np.full(fin.shape, fin[5, 5], np.float32)
    for l in m['layers']:
        t = np.asarray(Image.open(d + '/iori/' + l['texture']).convert('RGBA')).astype(np.float32); h, w = t.shape[:2]
        s = c[l['y']:l['y'] + h, l['x']:l['x'] + w]; a = t[..., 3:] / 255
        s[:] = t[..., :3] * a + s * (1 - a)
    return c
boxes = {'neck': (565, 275, 665, 400), 'shoulders': (290, 210, 830, 570), 'all': (0, 0, 1024, 1536)}
for d in sys.argv[1:]:
    dd = np.abs(comp(d) - fin).max(2)
    print(d, '  '.join(f"{k}: >24 {int((dd[y0:y1, x0:x1] > 24).sum())} mean {dd[y0:y1, x0:x1].mean():.2f}" for k, (x0, y0, x1, y1) in boxes.items()))
