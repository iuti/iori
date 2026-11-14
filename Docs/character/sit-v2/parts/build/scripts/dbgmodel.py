"""Copy out/ to out_dbg/ with every layer painted one flat colour (alpha kept). Prints the legend."""
import json, shutil, os, numpy as np, colorsys
from PIL import Image
os.chdir(os.path.dirname(os.path.abspath(__file__)))
shutil.rmtree('out_dbg', ignore_errors=True); shutil.copytree('out', 'out_dbg')
m = json.load(open('out_dbg/iori/model.json', encoding='utf8'))
ids = [l['id'] for l in m['layers']]
leg = {}
for k, l in enumerate(m['layers']):
    c = (np.array(colorsys.hsv_to_rgb((k * 0.381966) % 1, 1, 1)) * 255).astype(np.uint8)
    fixed={'base':(90,90,90),'body':(40,90,255),'face':(255,230,0),'side_hair':(255,0,40),'front_hair':(255,140,0),'back_hair':(0,220,60),'earring':(0,255,255),'tag':(160,0,255)}
    if l['id'] in fixed: c=np.array(fixed[l['id']],np.uint8)
    leg[l['id']] = c.tolist()
    texs = []
    for key in ('texture',): 
        if key in l: texs.append(l[key])
    for f in l.get('frames', []) or []:
        texs.append(f if isinstance(f, str) else f.get('texture'))
    for t in texs:
        if not t: continue
        p = 'out_dbg/iori/' + t; im = np.asarray(Image.open(p).convert('RGBA')).copy(); im[..., :3] = c; Image.fromarray(im).save(p)
json.dump(leg, open('out_dbg/legend.json', 'w'))
print(leg)
