# Writes a puppet model's layers as a layered PSD for a paint tool (CLIP STUDIO PAINT, Photoshop): each layer at its
# place on the canvas, bottom to top in draw order, named by its picture's file name (mouth frames and closed eyes
# hidden), with the pictures it was cut from as hidden reference layers on top. Needs psd-tools and Pillow.
#   python3 tools/character/puppet_to_psd.py <model folder> <final picture> <second reference picture> <out.psd>
# Also writes <out>_check.png (the visible layers put together) and <out>_layers.txt (the layers, top first).
import json, sys, numpy as np
from PIL import Image
from psd_tools import PSDImage
from psd_tools.constants import Compression
model_dir, final_png, extra_png, out = sys.argv[1:5]
m = json.load(open(model_dir + '/model.json', encoding='utf-8'))
W, H = m['canvas']
JP = {'base': '脚と椅子(動かない土台)', 'back_hair': '後ろ髪', 'body': '体', 'face': '顔', 'eye_L': '白目 左', 'iris_L': '黒目 左',
      'lash_L': 'まつ毛 左', 'lid_L': '閉じ目 左', 'eye_R': '白目 右', 'iris_R': '黒目 右', 'lash_R': 'まつ毛 右', 'lid_R': '閉じ目 右',
      'mouth_closed': '口 閉じ', 'mouth_small': '口 小さい半開き', 'mouth_half': '口 半開き', 'mouth_open': '口 開き', 'mouth_round': '口 う',
      'side_hair': '横髪', 'earring': '耳飾り', 'tag': 'タグ', 'front_hair': '前髪', 'ahoge': 'アホ毛', 'goggles': 'ゴーグル'}
final = Image.open(final_png).convert('RGBA')
bg = tuple(int(v) for v in np.array(final)[4:40, 4:40, :3].reshape(-1, 3).mean(0)) + (255,)
psd = PSDImage.new('RGBA', (W, H))
def add(img, name, x, y, visible=True):
    L = psd.create_pixel_layer(img.convert('RGBA'), name=name, top=y, left=x, compression=Compression.RLE)
    L.visible = visible
    psd.append(L)
    return L
add(Image.new('RGBA', (W, H), bg), 'bg_gray', 0, 0)
comp = np.zeros((H, W, 4), float); comp[..., :3] = np.array(bg[:3]) / 255.0; comp[..., 3] = 1
def over(img, x, y):
    a = np.array(img.convert('RGBA')).astype(float) / 255.0
    h, w = a.shape[:2]
    x0, y0, x1, y1 = max(x, 0), max(y, 0), min(x + w, W), min(y + h, H)
    s = a[y0 - y:y1 - y, x0 - x:x1 - x]
    d = comp[y0:y1, x0:x1]
    d[..., :3] = s[..., :3] * s[..., 3:4] + d[..., :3] * (1 - s[..., 3:4])
names = []
for L in m['layers']:
    frames = L.get('frames') or [L['texture']]
    for i, t in enumerate(frames):
        stem = t.split('/')[-1][:-4]
        img = Image.open(model_dir + '/' + t)
        assert img.size == (L['w'], L['h']), (stem, img.size, L['w'], L['h'])
        shown = i == 0 and not L.get('role', '').startswith('lid_')
        add(img, stem, L['x'], L['y'], shown)
        names.append((stem, shown, JP.get(stem, stem)))
        if shown and not L.get('clip'): over(img, L['x'], L['y'])
        elif shown:                                   # the iris shows only inside its eye's mask
            mk = next(k for k in m['layers'] if k['id'] == L['clip'])
            msk = Image.new('L', (W, H), 0); msk.paste(Image.open(model_dir + '/' + mk['mask']).convert('L'), (mk['x'], mk['y']))
            full = Image.new('RGBA', (W, H), (0, 0, 0, 0)); full.paste(img.convert('RGBA'), (L['x'], L['y']))
            fa = np.array(full); fa[..., 3] = (fa[..., 3].astype(int) * np.array(msk) // 255).astype(np.uint8)
            over(Image.fromarray(fa), 0, 0)
add(Image.open(extra_png).convert('RGBA'), 'ref_under_hair_body_chico', 0, 0, False)
add(final, 'ref_final', 0, 0, False)
psd.save(out)
c8 = (comp[..., :3] * 255 + 0.5).astype(np.uint8)
Image.fromarray(c8).save(out[:-4] + '_check.png')
d = np.abs(c8.astype(int) - np.array(final)[..., :3].astype(int)).max(-1)
open(out[:-4] + '_layers.txt', 'w', encoding='utf-8').write('\n'.join('%s\t%s\t%s' % (n, j, '表示' if v else '非表示') for n, v, j in reversed(names)))
print('layers', len(names), 'rest vs final: max', d.max(), '>24:', int((d > 24).sum()), 'mean %.2f' % d.mean())
