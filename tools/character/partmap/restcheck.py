import json, sys, numpy as np
from PIL import Image
fin = np.asarray(Image.open('C:/ai/iori_live/source/final.png').convert('RGB')).astype(np.float32); H, W = fin.shape[:2]
def comp(d, bg):
    m = json.load(open(d + '/model.json', encoding='utf8')); c = np.ones((H, W, 3), np.float32) * bg; masks = {}
    for l in m['layers']:
        if l.get('role', '').startswith('lid_'): continue
        t = np.asarray(Image.open(d + '/' + l['texture']).convert('RGBA')).astype(np.float32); h, w = t.shape[:2]
        a = np.zeros((H, W), np.float32); a[l['y']:l['y'] + h, l['x']:l['x'] + w] = t[..., 3] / 255
        rgb = np.zeros((H, W, 3), np.float32); rgb[l['y']:l['y'] + h, l['x']:l['x'] + w] = t[..., :3]
        if l.get('clip'): a = a * masks[l['clip']]
        if l.get('mask'):
            mk = np.zeros((H, W), np.float32); mt = np.asarray(Image.open(d + '/' + l['mask'])).astype(np.float32) / 255
            mk[l['y']:l['y'] + mt.shape[0], l['x']:l['x'] + mt.shape[1]] = mt; masks[l['id']] = mk
        c = c * (1 - a[..., None]) + rgb * a[..., None]
    return c
if __name__ == '__main__':
    for d in sys.argv[1:]:
        c = comp(d, np.median(fin[:6].reshape(-1, 3), 0)); dd = np.abs(c - fin).max(2)
        print(d, 'rest vs final: >12', int((dd > 12).sum()), '>24', int((dd > 24).sum()), 'max', int(dd.max()))
        Image.fromarray(c.clip(0, 255).astype(np.uint8)).save(d.replace('/', '_') + '_rest.png')
