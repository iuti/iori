"""Mask of the hair locks that hang over the body below the jaw (only hair, no clothing shadows), and its check image."""
import os
import numpy as np, cv2
from PIL import Image
from scipy import ndimage as ndi

os.chdir(os.path.dirname(os.path.abspath(__file__)))
final = np.asarray(Image.open('../../source/final.png').convert('RGB')).astype(np.float32)
H, W = final.shape[:2]
yy, xx = np.mgrid[0:H, 0:W]
M = lambda n: np.asarray(Image.open(f'm/{n}.png')) > 0
dil = lambda m, k: cv2.dilate(m.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k))).astype(bool)
skull, phones = M('skull'), M('phones')
hairmask = M('hair_side') | M('hair_front')
r, g, b = final[..., 0], final[..., 1], final[..., 2]
sat = final.max(2) - final.min(2)
huehair = (sat > 22) & (b > r + 6) & (b > g + 14) & (final.mean(2) < 175)          # saturated purple / blue hair
dark = final.mean(2) < 95                                   # hair outlines

# regions drawn by hand around the two locks that lie over the body (final picture pixels)
regions = np.zeros((H, W), np.uint8)
cv2.fillPoly(regions, [np.array([(320, 300), (470, 300), (522, 330), (522, 400), (480, 470), (380, 470), (320, 420)], np.int32)], 1)   # left locks
cv2.fillPoly(regions, [np.array([(600, 290), (660, 270), (720, 300), (760, 340), (760, 400), (690, 420), (640, 400), (610, 340)], np.int32)], 1)  # right lock
regions = regions.astype(bool) & ~dil(skull, 3) & ~phones & ~dil(M('tag') | M('earring'), 3)

core = regions & huehair & hairmask
core = cv2.morphologyEx(core.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))).astype(bool)
# keep only pieces that belong to the hair mass (connected to hair above the region)
lab, n = ndi.label(dil(core, 3) | (hairmask & (yy < 330) & ~skull))
top = np.unique(lab[(yy < 300) & hairmask & (lab > 0)])
core &= np.isin(lab, top)
# the locks' own outlines: dark pixels touching them, inside the regions
lines = dark & dil(core, 5) & regions & ~phones
mask = dil(core | lines, 4) & regions & ~phones
Image.fromarray((mask * 255).astype(np.uint8)).save('inp/edit_mask.png')
print('mask px', int(mask.sum()))

box = (300, 260, 790, 500)
ov = final.copy()
ov[mask] = ov[mask] * 0.35 + np.array([255, 0, 0]) * 0.65
img = Image.fromarray(ov.clip(0, 255).astype(np.uint8)).crop(box)
img.resize((img.width * 2, img.height * 2), Image.NEAREST).save('inp/mask_check.png')
