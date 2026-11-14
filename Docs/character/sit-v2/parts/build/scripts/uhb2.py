"""Shoulder-only hair removal (under_hair_body_04-06): warp each onto the final, keep the final's pixels everywhere
except where its hair covered the body, and make the check images."""
import os
import numpy as np, cv2
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as ndi

os.chdir(os.path.dirname(os.path.abspath(__file__)))
final = np.asarray(Image.open('../../source/final.png').convert('RGB')).astype(np.float32)
H, W = final.shape[:2]
M = lambda n: np.asarray(Image.open(f'm/{n}.png')) > 0
hair = M('hair_side') | M('hair_front') | M('ahoge')
skull, body, phones, U = M('skull'), M('body'), M('phones'), M('U')
yy, xx = np.mgrid[0:H, 0:W]
dil = lambda m, k: cv2.dilate(m.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k))).astype(bool)

# the crop the edit was made from: final (290,210)-(830,570) enlarged to 1536x1024
CX, CY, CS = 290, 210, 540 / 1536
# what may change: hair below the jaw over the body/background, not the head (skull), not the headphones
edit = hair & ~dil(skull, 3) & (yy > 280) & ~phones
sat = final.max(2) - final.min(2)
hairc = ((sat > 38) & (final[..., 2] > final[..., 0] + 5)) | (final.mean(2) < 110)
zone = (yy > 280) & (((xx < 560) & (yy < 470)) | ((xx > 615) & (yy < 425))) & ~dil(skull, 1) & ~phones
edit = dil(edit | (hairc & zone), 5) & ~phones & ~dil(skull, 1)
gray = lambda a: cv2.cvtColor(a.clip(0, 255).astype(np.uint8), cv2.COLOR_RGB2GRAY)

F = ImageFont.truetype('C:/Windows/Fonts/meiryo.ttc', 26)
results = []
for i in (4, 5, 6):
    src = np.asarray(Image.open(f'../gen/under_hair_body/under_hair_body_0{i}.png').convert('RGB')).astype(np.float32)
    # initial placement as the crop, then a similarity fit on the body outside the edit area (ECC, affine)
    M0 = np.float32([[CS, 0, CX], [0, CS, CY]])
    placed = cv2.warpAffine(src, M0, (W, H), flags=cv2.INTER_AREA)
    roi = (~dil(edit, 25)) & (xx > CX + 10) & (xx < CX + 530) & (yy > CY + 10) & (yy < CY + 350) & U
    warp = np.eye(2, 3, dtype=np.float32)
    try:
        _, warp = cv2.findTransformECC(gray(final).astype(np.float32), gray(placed).astype(np.float32), warp, cv2.MOTION_AFFINE,
                                      (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 200, 1e-6), roi.astype(np.uint8), 5)
    except cv2.error as e:
        print('ECC failed', i, e)
    aligned = cv2.warpAffine(placed, warp, (W, H), flags=cv2.INTER_LINEAR + cv2.WARP_INVERSE_MAP)
    valid = cv2.warpAffine(cv2.warpAffine(np.ones(src.shape[:2], np.float32), M0, (W, H)), warp, (W, H), flags=cv2.INTER_LINEAR + cv2.WARP_INVERSE_MAP) > 0.99
    sx = float(np.hypot(warp[0, 0], warp[1, 0])); print(f'{i}: affine scale {sx:.3f}, shift {warp[0, 2]:+.1f},{warp[1, 2]:+.1f}')
    resid = np.abs(aligned - final).max(2)[roi & valid]
    print(f'   residual on the body outside the edit: median {np.median(resid):.1f}, p90 {np.percentile(resid, 90):.1f}')
    BGc = final[5, 5]
    nearbg = (np.abs(aligned - BGc).max(2) < 30) | ((aligned.max(2) - aligned.min(2) < 18) & (aligned.mean(2) > 120) & (aligned.mean(2) < 185))
    bgm = ((np.abs(final - BGc).max(2) < 10) & ~dil(edit, 3)).astype(np.float32)
    num = cv2.GaussianBlur(final * bgm[..., None], (0, 0), 25); den = cv2.GaussianBlur(bgm, (0, 0), 25)[..., None]
    bgfill = np.where(den > 1e-3, num / np.maximum(den, 1e-3), BGc)
    aligned[nearbg & edit] = bgfill[nearbg & edit]
    e = edit & valid
    a = cv2.GaussianBlur(e.astype(np.float32), (0, 0), 1.5) * dil(e, 3)
    a = np.maximum(a, e.astype(np.float32))
    comp = final * (1 - a[..., None]) + aligned * a[..., None]
    Image.fromarray(comp.clip(0, 255).astype(np.uint8)).save(f'uhb_{i:02d}_composite.png')
    np.save(f'uhb_{i:02d}_alpha.npy', a)
    vis_diff = np.abs(comp - final).max(2)[~dil(edit, 3)]
    print(f'   outside the edit area: max diff {vis_diff.max():.0f}')
    results.append((i, comp, aligned, e))

# sheet: final / 04 / 05 / 06 on the shoulders, each also with the final hair at 50 %, and the body outline over the final
box = (300, 230, 820, 560)
hair_rgba = np.dstack([final, (hair * 255).astype(np.float32)])
def outline(img_rgb, m, col):
    m = ndi.binary_fill_holes(m)
    lab, n = ndi.label(m)
    if n: m = lab == (1 + int(np.argmax(ndi.sum(m, lab, range(1, n + 1)))))
    o = img_rgb.copy(); edge = dil(m, 3) & ~m; o[edge] = col; return o
bg_like = np.abs(final - final[5, 5]).max(2) < 14
final_body = ~bg_like & ~hair & ~dil(skull, 1) & (yy > 280)
cells = [('最終版', final, final_body)]
for i, comp, al, e in results:
    sil = (~(np.abs(comp - final[5, 5]).max(2) < 14)) & ~dil(skull, 1) & (yy > 280) & ~(hair & ~e)
    cells.append((f'under_hair_body_0{i}', comp, sil))
cw = int((box[2] - box[0]) * 1.2); ch = int((box[3] - box[1]) * 1.2)
S = Image.new('RGB', (len(cells) * (cw + 8), 3 * (ch + 36)), (34, 34, 42)); d = ImageDraw.Draw(S)
for k, (n, img, sil) in enumerate(cells):
    x = k * (cw + 8)
    rows = [(n, img), (n + ' + 最終版の髪 50%', img * 0.5 + np.where(hair[..., None], final, img) * 0.5),
            (n + ' 体の輪郭(赤)/最終版(水色)', outline(outline(img, final_body, np.array([80, 220, 255])), sil, np.array([255, 40, 40])))]
    for r, (lab, im) in enumerate(rows):
        y = r * (ch + 36)
        d.text((x + 4, y + 2), lab, font=F, fill=(255, 255, 255))
        S.paste(Image.fromarray(im.clip(0, 255).astype(np.uint8)).crop(box).resize((cw, ch), Image.LANCZOS), (x, y + 34))
S.save('sheet_under_hair_body_v2.jpg', quality=90)
