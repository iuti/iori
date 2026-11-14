"""Masks on the final canvas, written to m/<name>.png (0/255), with a check sheet."""
import os
import numpy as np, cv2
from PIL import Image
from scipy import ndimage as ndi

os.chdir(os.path.dirname(os.path.abspath(__file__)))
os.makedirs('m', exist_ok=True)
L = lambda n: np.asarray(Image.open(f'al/{n}.png').convert('RGB')).astype(np.float32)
final = np.asarray(Image.open('../../source/final.png').convert('RGB')).astype(np.float32)
H, W = final.shape[:2]
yy, xx = np.mgrid[0:H, 0:W]


def diff(a, b):
    return np.abs(cv2.GaussianBlur(a, (0, 0), 1.0) - cv2.GaussianBlur(b, (0, 0), 1.0)).max(2)


def clean(m, close=5, open_=2, min_area=200, fill=True):
    m = m.astype(np.uint8)
    if close: m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (close, close)))
    if open_: m = cv2.morphologyEx(m, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (open_, open_)))
    n, lab, st, _ = cv2.connectedComponentsWithStats(m)
    keep = np.zeros(n, bool); keep[1:] = st[1:, 4] >= min_area
    m = keep[lab]
    if fill: m = ndi.binary_fill_holes(m)
    return m


def save(name, m):
    Image.fromarray((m.astype(np.uint8) * 255)).save(f'm/{name}.png')
    return m


bb, head, headf = L('behind_body'), L('head'), L('head_face')
ug, bt = L('under_goggles'), L('behind_tag')
hv = cv2.erode((np.asarray(Image.open('al/head_valid.png')) > 250).astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)

# --- U: everything that is not the seat plate (upper body, arms, mug, head, hair)
U = clean(diff(final, bb) > 22, close=9, open_=3, min_area=800)
U &= yy < 1000          # the plate below the seat never belongs to the moving body
save('U', U)

# --- head area items, from what the bare head removed
gray = lambda a: a.mean(2)
r, g, b = final[..., 0], final[..., 1], final[..., 2]
skin_f = (r > 205) & (r - b > 15) & (g > 170) & (r - g > 8)            # pale pink skin in the final
gpoly = np.zeros((H, W), np.uint8)
cv2.fillPoly(gpoly, [np.array([(385, 95), (470, 70), (560, 78), (600, 150), (650, 205), (660, 232), (628, 248), (590, 190), (545, 165), (400, 180)], np.int32)], 1)
gog = clean((diff(final, ug) > 28) & gpoly.astype(bool) & hv, close=7, open_=2, min_area=150)
goldish = (r > 150) & (g > 100) & (b < 110) & (r - b > 70)
near = lambda m, k: cv2.dilate(m.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k))).astype(bool)
tag_core = clean(goldish & (xx > 640) & (xx < 720) & (yy > 195) & (yy < 310), close=5, open_=0, min_area=30)
tag = near(tag_core, 5) & (diff(final, bt) > 20)
tag |= tag_core
earring_core = goldish & (xx > 615) & (xx < 642) & (yy > 245) & (yy < 300)
earring = (near(earring_core, 5) & (diff(final, bt) > 20)) | earring_core
# headphones: dark, low-saturation blobs around the neck
cups = np.zeros_like(U)
cv2.ellipse(cups.view(np.uint8), (528, 382), (36, 50), 0, 0, 360, 1, -1)
cv2.ellipse(cups.view(np.uint8), (612, 378), (44, 52), 0, 0, 360, 1, -1)
dark = gray(final) < 95
phones = clean(cups & dark, close=7, open_=2, min_area=200)
# skull silhouette of the shifted bare head (skin incl. ears, above the collar)
r2, g2, b2 = headf[..., 0], headf[..., 1], headf[..., 2]
skin_h = (r2 > 200) & (r2 - b2 > 12) & (g2 > 165)
lines_h = gray(headf) < 120
skull = clean((skin_h | lines_h) & (yy < 360) & (yy > 40) & (xx > 380) & (xx < 680), close=7, open_=3, min_area=5000)
lab, n = ndi.label(skull)
if n > 1:
    sizes = ndi.sum(skull, lab, range(1, n + 1)); skull = lab == (1 + int(np.argmax(sizes)))
save('skull', skull)
# hair: in the head area, what differs from the bare head and is not goggles/tag/headphones/skin
bg = (np.abs(final - final[5, 5]).max(2) < 14)
hairish = (diff(final, headf) > 30) & (yy < 440) & hv & ~bg & ~gog & ~tag & ~earring & ~phones
hairish &= ~(skin_f & skull)
hair = clean(hairish, close=5, open_=2, min_area=300, fill=False)
hair = ndi.binary_fill_holes(hair) & (yy < 440) & ~bg
hair &= (gray(final) < 175) | ((yy < 330) & ~skin_f)
hair &= ~(skin_f & skull) & ~phones
save('hair', hair)
# the ahoge: hair above the head's top line that the goggles do not cover
thick = cv2.morphologyEx(hair.astype(np.uint8), cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (11, 11))).astype(bool)
ahoge = hair & (yy < 118) & (xx > 380) & (xx < 600) & ~thick
save('ahoge', ahoge)
hair_front = hair & ~ahoge & cv2.erode(skull.astype(np.uint8), np.ones((3, 3), np.uint8)).astype(bool)
hair_side = hair & ~ahoge & ~hair_front
save('hair_front', hair_front); save('hair_side', hair_side)
save('goggles', gog); save('tag', tag); save('earring', earring); save('phones', phones)
# face: the skull silhouette (the bare head), cut at the collar where the final shows the shirt
shirt = (gray(final) > 185) & ~skin_f & (yy > 300)
face = skull & ~(shirt & ~hair & ~phones)
save('face', face)

# --- eyes: whites = bright, low-saturation inside the eye boxes of the empty-eye picture
wh = L('white')
eyebox = np.zeros_like(U)
eyebox[255:310, 440:512] = True; eyebox[220:276, 515:590] = True
sat = wh.max(2) - wh.min(2)
whites = clean(eyebox & (gray(wh) > 205) & (sat < 40), close=3, open_=0, min_area=40)
save('whites', whites)
iris_g = L('iris')
ir, ig, ib = iris_g[..., 0], iris_g[..., 1], iris_g[..., 2]
iris = clean(eyebox & ~((ig > 180) & (ir < 120) & (ib < 120)), close=3, open_=2, min_area=40)
save('iris', iris)
lash = eyebox & (gray(final) < 90) & ~whites
save('lash', clean(lash, close=0, open_=0, min_area=5, fill=False))

# --- body: U minus the head things; the headphones ride on the body
body = U & ~face & ~hair & ~ahoge & ~gog & ~tag & ~earring
body &= (yy >= 300)
body |= phones
left = U & (yy < 300) & ~face & ~hair & ~ahoge & ~gog & ~tag & ~earring & ~phones
hair_side |= left; save('hair_side', hair_side)
body = clean(body, close=3, open_=2, min_area=500, fill=False)
save('body', body)

# check sheet
cols = {'body': (80, 200, 255), 'face': (255, 90, 90), 'hair_front': (255, 200, 60), 'hair_side': (255, 140, 0), 'ahoge': (255, 255, 0),
        'goggles': (200, 255, 120), 'tag': (255, 0, 255), 'earring': (0, 255, 255), 'phones': (120, 255, 200)}
ov = final.copy() * 0.35
for k, c in cols.items():
    m = np.asarray(Image.open(f'm/{k}.png')) > 0
    ov[m] = ov[m] * 0.3 + np.array(c) * 0.7
Image.fromarray(ov.clip(0, 255).astype(np.uint8)).save('masks_check.png')
