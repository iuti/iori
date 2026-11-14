"""Draft part map: every pixel of the final picture belongs to exactly one part, taken from the current layers
(the layer seen on top at rest), then split to match chico's part plan (neck & headphones, hanging straps).
Writes labels.png (one value per part), preview.png, and labels.json."""
import json, os, numpy as np, cv2
from PIL import Image
os.chdir(os.path.dirname(os.path.abspath(__file__)))
M = 'C:/ai/iori_live/v2/build/out/iori'
fin = np.asarray(Image.open('C:/ai/iori_live/source/final.png').convert('RGB')).astype(np.float32)
H, W = fin.shape[:2]
m = json.load(open(M + '/model.json', encoding='utf8'))
PARTS = [  # id, japanese name, colour (bottom first)
    ('bg', '背景', (60, 60, 60)), ('base', '土台(脚・椅子・座面より下)', (255, 255, 255)), ('strap', '垂れたストラップ', (255, 255, 0)),
    ('back_hair', '後ろ髪', (190, 120, 255)), ('body', '胴・腕・マグ', (80, 200, 255)), ('neck', '首・ヘッドホン', (110, 255, 200)),
    ('face', '顔', (255, 90, 90)), ('eye_L', '白目 左', (200, 255, 200)), ('iris_L', '黒目 左', (0, 160, 90)), ('lash_L', 'まつげ 左', (0, 90, 40)),
    ('eye_R', '白目 右', (220, 255, 160)), ('iris_R', '黒目 右', (60, 170, 0)), ('lash_R', 'まつげ 右', (30, 100, 0)),
    ('mouth', '口', (255, 120, 210)), ('side_hair', '横髪', (255, 140, 0)), ('earring', '耳飾り', (255, 220, 120)),
    ('tag', 'タグ', (230, 200, 0)), ('front_hair', '前髪', (255, 200, 60)), ('ahoge', 'アホ毛', (255, 255, 140)),
    ('goggles', 'ゴーグル', (255, 235, 170))]
ix = {p[0]: k for k, p in enumerate(PARTS)}
lab = np.zeros((H, W), np.uint8)
masks = {}
for l in m['layers']:
    if l.get('role', '').startswith('lid_'): continue
    t = np.asarray(Image.open(M + '/' + l['texture'])).astype(np.float32)
    a = np.zeros((H, W), np.float32); h, w = t.shape[:2]; a[l['y']:l['y'] + h, l['x']:l['x'] + w] = t[..., 3] / 255
    if l.get('clip'): a = a * masks[l['clip']]
    if l.get('mask'): masks[l['id']] = (a > 0.5).astype(np.float32)
    lab[a >= 0.5] = ix[l['id']]
# background: what no layer holds at half or more
# neck & headphones: the body's part around the neck, above the jacket (chico's plan: x~495..655, y~355..435, the
# headphones' whole cups); taken as body pixels above y440 between the shoulders
yy, xx = np.mgrid[:H, :W]
neckzone = (yy < 440) & (xx > 470) & (xx < 690)
lab[(lab == ix['body']) & neckzone] = ix['neck']
# hanging straps (two, below the seat): yellow and dark strap pixels in the base inside chico's boxes
r, g, b = fin[..., 0], fin[..., 1], fin[..., 2]
yellow = (r > 150) & (g > 110) & (b < 110) & (r - b > 70)
for (x0, y0, x1, y1) in ((312, 985, 360, 1120), (708, 1025, 765, 1195)):
    z = (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1) & (lab == ix['base'])
    s = z & yellow
    s = cv2.morphologyEx(s.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8)).astype(bool) & z
    lab[s] = ix['strap']
# the mouth layer carries a patch of skin around the mouth: only the mouth itself is the mouth
mo = lab == ix['mouth']
ell = ((xx - 532) / 26.0) ** 2 + ((yy - 320) / 13.0) ** 2 <= 1
lab[mo & ~ell] = ix['face']
# the chair's back and fabric that the body layer held: the base (it does not move)
chair = (r - b > 12) & (g - b > 8) & (np.abs(r - g) < 30) & (r < 210) & (r > 90)
lab[(lab == ix['body']) & chair & (xx > 800) & (yy > 380) & (yy < 900)] = ix['base']
# the fabric in shadow (gray-olive, never navy like the jacket), right of the jacket
gr = (r + g + b) / 3
fab = (b < np.maximum(r, g) - 2) & (gr < 200) & (xx > 870) & (yy > 400) & (yy < 880)
fab = cv2.morphologyEx(fab.astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8)).astype(bool)
lab[(lab == ix['body']) & fab] = ix['base']
Image.fromarray(lab).save('labels.png')
pal = np.array([p[2] for p in PARTS], np.uint8)
col = pal[lab].astype(np.float32)
prev = (0.45 * fin + 0.55 * col).clip(0, 255).astype(np.uint8)
Image.fromarray(prev).save('preview.png')
json.dump([{'value': k, 'id': p[0], 'name': p[1], 'color': p[2]} for k, p in enumerate(PARTS)], open('labels.json', 'w', encoding='utf8'), ensure_ascii=False, indent=1)
for k, p in enumerate(PARTS): print(p[0], int((lab == k).sum()))
