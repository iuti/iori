import json, sys, numpy as np
from PIL import Image, ImageDraw, ImageFont
F=ImageFont.truetype('C:/Windows/Fonts/meiryo.ttc',20)
fin=np.asarray(Image.open('../../source/final.png').convert('RGB')).astype(np.float32)
def comp(d,bg):
    m=json.load(open(d+'/iori/model.json',encoding='utf8')); c=np.ones(fin.shape,np.float32)*bg
    for l in m['layers']:
        if l.get('role','').startswith('lid_'): continue
        t=np.asarray(Image.open(d+'/iori/'+l['texture'])).astype(np.float32); h,w=t.shape[:2]; a=t[...,3:]/255
        s=c[l['y']:l['y']+h,l['x']:l['x']+w]; s[:]=s*(1-a)+t[...,:3]*a
    return c
blue=np.array([40,90,170.]); gray=np.array([131,130,131.])
P,N,G,Ng=comp('out_prev',blue),comp('out',blue),comp('out_prev',gray),comp('out',gray)
boxes={'頭の左':(350,60,500,330),'頭の右':(600,60,760,330)}
S=3; rows=[]
for nm,(x0,y0,x1,y1) in boxes.items():
    w=(x1-x0)*S;h=(y1-y0)*S
    panels=[('最終版',fin),('前 灰',G),('今 灰',Ng),('前 青',P),('今 青',N)]
    row=Image.new('RGB',(len(panels)*(w+6),h+28),(25,25,30)); d=ImageDraw.Draw(row)
    for k,(lab,im) in enumerate(panels):
        row.paste(Image.fromarray(im[y0:y1,x0:x1].clip(0,255).astype(np.uint8)).resize((w,h),Image.NEAREST),(k*(w+6),28)); d.text((k*(w+6)+4,2),f'{nm} {lab}',font=F,fill=(255,255,255))
    rows.append(row)
out=Image.new('RGB',(rows[0].width,sum(r.height for r in rows)),(25,25,30)); y=0
for r in rows: out.paste(r,(0,y)); y+=r.height
out.save(sys.argv[1]); print(out.size)
