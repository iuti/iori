# Draft of the part map: every pixel of the final picture goes to exactly one part, and the borders sit on the drawn
# lines. Things with a clear outline start from hand-read outlines (shapes.py), hair starts from its colour, and a
# watershed over the picture's edges settles each border. Below the shoulders the map starts from `--below` (the
# parts as the first model had them) and is only re-settled on the lines.
#   python3 build_draft.py --final <final.png> --below <labels of the old layers.png> --legend <labels.json> --out <folder> [--debug 1]
# Writes <folder>/labels.png (pixel value = part). The pictures and the PSD are made from it by sheet.py and psd_map.py.
import json, sys, os, importlib.util, numpy as np, cv2
from PIL import Image, ImageDraw
from skimage.segmentation import watershed
from scipy import ndimage as ndi
A=dict(zip(sys.argv[1::2],sys.argv[2::2]))
FINAL=A['--final']; BELOW=A['--below']; LEG=A['--legend']; OUT=A['--out']; CUT=int(A.get('--cut',470))
spec=importlib.util.spec_from_file_location('shapes',A.get('--shapes',os.path.join(os.path.dirname(os.path.abspath(__file__)),'shapes.py')))
sh=importlib.util.module_from_spec(spec); spec.loader.exec_module(sh)
f=np.array(Image.open(FINAL).convert('RGB')); H,W=f.shape[:2]
parts=json.load(open(LEG,encoding='utf8')); ID={p['id']:p['value'] for p in parts}
below=np.array(Image.open(BELOW))
hsv=cv2.cvtColor(f,cv2.COLOR_RGB2HSV).astype(int); h,s,v=hsv[...,0]*2,hsv[...,1],hsv[...,2]
gray=(s<18)&(v>100)&(v<152)
skin=((h<=24)|(h>=350))&(s>=36)&(s<=90)&(v>=170)
light=((h<=30)|(h>=300))&(s<36)&(v>=175)
yellow=(h>=28)&(h<=55)&(s>=90)&(v>=120)
purple=(h>=225)&(h<=300)&(s>=45)&(v<190)
def poly(*ps):
    im=Image.new('L',(W,H),0); d=ImageDraw.Draw(im)
    for p in ps: d.polygon(p,fill=255)
    return np.array(im)>0
def disc(c,r):
    im=Image.new('L',(W,H),0); ImageDraw.Draw(im).ellipse([c[0]-r,c[1]-r,c[0]+r,c[1]+r],fill=255); return np.array(im)>0
def strokes(lines,width=3):
    im=Image.new('L',(W,H),0); d=ImageDraw.Draw(im)
    for l in lines:
        if len(l)==1: x,y=l[0]; d.ellipse([x-width,y-width,x+width,y+width],fill=255)
        else: d.line(l,fill=255,width=width)
    return np.array(im)>0
def er(m,n): return ndi.binary_erosion(m,iterations=n) if n>0 else m
def di(m,n): return ndi.binary_dilation(m,iterations=n) if n>0 else m

HAIR=100   # all hair as one thing first; split afterwards
mk=np.zeros((H,W),np.int32)
def put(val,mask): mk[mask&(mk==0)]=val+1
for name,ls in sh.STROKES.items():
    if ls: put(ID[name],strokes(ls))
# 1. below the shoulders: as given
low=np.zeros((H,W),bool); low[CUT:]=True
# (re-settled on the drawn lines: each given part shrunk a little is the seed; the straight cut across the jacket's hem is left open)
hem=np.zeros((H,W),bool); hem[975:1060,690:900]=True; hem[975:1035,300:420]=True
for p in parts:
    m0=(below==p['value'])&low
    if not m0.any(): continue
    mk[er(m0,2)&~(hem&np.isin(below,[ID['body'],ID['base']]))&(mk==0)]=p['value']+1
# 2. the eyes as the model has them
eyes=np.isin(below,[ID[k] for k in ('eye_L','iris_L','lash_L','eye_R','iris_R','lash_R')])&~low
# 3. things with an outline
gog=poly(sh.LENS_R,sh.LENS_L,sh.BRIDGE,sh.BAND)|disc(sh.RING_C,sh.RING_R)
tag=poly(sh.TAG); earring=poly(sh.EARRING); cups=poly(sh.CUP_L,sh.CUP_R,sh.HPBAND_L,sh.HPBAND_R)
put(ID['goggles'],er(gog,3)); put(ID['goggles'],yellow&di(gog,2)&~di(tag,1))
put(ID['tag'],er(tag,2)); put(ID['earring'],yellow&earring)
lock=poly(sh.LOCK_ON_CUP)
put(ID['neck'],er(cups,3)&~di(lock,1))
put(ID['face'],er(skin&poly(sh.FACE,sh.EAR),1)&~di(eyes,1))
put(ID['neck'],er(skin&poly(sh.NECK),1))
put(ID['bg'],er(gray,3))
put(ID['body'],er(light&poly(sh.BODY_TOP),2))
put(ID['base'],er(np.isin(below,[ID['base']])&~low,2))
# 4. hair by its colour, where none of the above is
rigid=di(gog|tag|earring,2)|(di(cups,5)&~lock)|di(eyes,2)
zone=poly(sh.HAIR_ZONE)&~poly(*sh.NOHAIR)
hairseed=er(purple&zone&~rigid,1)
hairseed|=er(purple&lock&(v>112)&(s>=55),1)      # the lighter lock lying over the right headphone
put(HAIR,hairseed)
# thin strands out in the background: ink that is far from anything but hair and background
ks=61; gm=gray.astype(np.float32); B=cv2.blur(f.astype(np.float32)*gm[...,None],(ks,ks))/np.maximum(cv2.blur(gm,(ks,ks)),1e-3)[...,None]
dB=np.abs(f.astype(np.float32)-B).max(-1)
other=di(skin|light|yellow|rigid,4)
put(HAIR,(dB>30)&zone&~other&di(gray,3))
eseed=er(eyes,1); mk[eseed]=200
g=cv2.GaussianBlur(f,(0,0),0.7).astype(np.float32)
labc=cv2.cvtColor(g/255.0,cv2.COLOR_RGB2LAB)
el=np.zeros((H,W),np.float32)
for c in range(3):
    gx=cv2.Sobel(labc[...,c],cv2.CV_32F,1,0,ksize=3); gy=cv2.Sobel(labc[...,c],cv2.CV_32F,0,1,ksize=3); el=np.maximum(el,np.hypot(gx,gy))
own=watershed(el,mk)-1
# the eyes: exactly the model's pixels, unless hair lies over them
e=eyes&(own!=HAIR); own[(own==199)&~eyes]=ID['face']; own[e]=below[e]
own[own==199]=ID['face']
# hair-coloured specks that are not joined to the hair (shadow lines under the chin and the like) are not hair
hl,hn=ndi.label(own==HAIR)
if hn:
    hs=ndi.sum(own==HAIR,hl,range(1,hn+1)); small=np.isin(hl,[i+1 for i,z in enumerate(hs) if z<200])
    if small.any():
        idx=ndi.distance_transform_edt((own==HAIR)|(own==ID['bg']),return_distances=False,return_indices=True); own[small]=own[idx[0],idx[1]][small]
# 5. split the hair
hair=own==HAIR
hm=np.zeros((H,W),np.int32)
for name,ls in sh.HAIR.items(): hm[strokes(ls)&hair&(hm==0)]=ID[name]+1
sub=watershed(el,hm,mask=hair)
left=hair&(sub==0)          # hair pieces no seed reached: the nearest hair part
if left.any():
    idx=ndi.distance_transform_edt(sub==0,return_distances=False,return_indices=True); sub=sub[idx[0],idx[1]]
own[hair]=sub[hair]-1
# 5b. background is only what really is background: ink goes to the part next to it, clean gray inside a part goes back
hp=[ID[n] for n in ('back_hair','side_hair','front_hair','ahoge')]
top=~low
ink=(own==ID['bg'])&(dB>14)&top
if ink.any():
    idx=ndi.distance_transform_edt(own==ID['bg'],return_distances=True,return_indices=True)
    near=own[idx[1][0],idx[1][1]]; own[ink&(idx[0]<=5)]=near[ink&(idx[0]<=5)]
clean=np.isin(own,hp)&(dB<6)&top
lab,n=ndi.label(clean)
if n:
    sizes=ndi.sum(clean,lab,range(1,n+1)); own[np.isin(lab,[i+1 for i,z in enumerate(sizes) if z>=6])]=ID['bg']
# 5c. the mouth: its drawn line
lum0=cv2.cvtColor(f,cv2.COLOR_RGB2GRAY).astype(np.float32)
ml=(lum0<cv2.GaussianBlur(lum0,(0,0),5)-14)&poly(sh.MOUTH)&(own==ID['face'])
own[di(ml,1)&(own==ID['face'])&poly(sh.MOUTH)]=ID['mouth']
# 6. a drawn line between two parts belongs to the one in front
ORDER=['bg','base','strap','back_hair','body','neck','face','eye_L','iris_L','lash_L','eye_R','iris_R','lash_R','mouth','side_hair','earring','tag','front_hair','ahoge','goggles']
rank=np.zeros(256,np.int32)
for i,n in enumerate(ORDER): rank[ID[n]]=i
lum=cv2.cvtColor(f,cv2.COLOR_RGB2GRAY).astype(np.float32)
line=(lum<cv2.GaussianBlur(lum,(0,0),4)-18)&(lum<120)
for it in range(3):
    r=rank[own]; best=ndi.maximum_filter(r,size=3)
    take=line&(best>r)&(own!=ID['bg'])
    if not take.any(): break
    val=np.array([ID[n] for n in ORDER])[best]
    own[take]=val[take]
for name,pg in getattr(sh,'FORCE',[]): own[poly(pg)]=ID[name]
# 7. specks go to what surrounds them
for it in range(2):
    for p in parts:
        i=p['value']; lab,n=ndi.label(own==i)
        if n<=1: continue
        sizes=ndi.sum(own==i,lab,range(1,n+1))
        for j,z in enumerate(sizes):
            if z<(120 if p['id'] in ('base','body','bg') else 20):
                msk=lab==j+1; ring=di(msk,2)&~msk; vals=own[ring]; vals=vals[vals!=i]
                if len(vals): own[msk]=np.bincount(vals).argmax()
own=own.astype(np.uint8)
os.makedirs(OUT,exist_ok=True)
Image.fromarray(own).save(OUT+'/labels.png')
pal=np.array([p['color'] for p in parts],np.uint8)
if A.get('--debug'):
  sv=(f*0.5).astype(np.uint8); m2=mk.copy(); sel=(m2>0)&(m2<=len(parts)); sv[sel]=pal[m2[sel]-1]; sv[m2==HAIR+1]=(120,0,160); sv[hm>0]=pal[hm[hm>0]-1]
  Image.fromarray(sv).save(OUT+'/seeds_check.png')
for p in parts: print(p['id'],int((own==p['value']).sum()))
