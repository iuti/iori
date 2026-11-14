"""Put every chosen picture on the final picture's canvas (1024x1536, RGB float), aligned.
Writes al/<name>.png, plus valid masks (where that picture has content)."""
import os
import numpy as np, cv2
from PIL import Image

os.chdir(os.path.dirname(os.path.abspath(__file__)))
G = '../gen/'
W, H = 1024, 1536
os.makedirs('al', exist_ok=True)


def place(path, x0, y0, size, shift=(0.0, 0.0)):
    im = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    s = size / im.shape[1]
    # affine: final = src * s + (x0, y0) + shift
    M = np.float32([[s, 0, x0 + shift[0]], [0, s, y0 + shift[1]]])
    out = cv2.warpAffine(im, M, (W, H), flags=cv2.INTER_AREA if s < 1 else cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0))
    valid = cv2.warpAffine(np.ones(im.shape[:2], np.float32), M, (W, H), flags=cv2.INTER_LINEAR)
    return out, valid


def phase(a, b, box):
    x0, y0, x1, y1 = box
    ga = cv2.cvtColor(a[y0:y1, x0:x1].astype(np.uint8), cv2.COLOR_RGB2GRAY).astype(np.float32)
    gb = cv2.cvtColor(b[y0:y1, x0:x1].astype(np.uint8), cv2.COLOR_RGB2GRAY).astype(np.float32)
    win = cv2.createHanningWindow((x1 - x0, y1 - y0), cv2.CV_32F)
    return cv2.phaseCorrelate(ga, gb, win)


final = np.asarray(Image.open('../../source/final.png').convert('RGB')).astype(np.float32)
jobs = {
    'head': (G + 'head/head_02.png', 320, 30, 480),
    'back_hair': (G + 'back_hair/back_hair_01.png', 320, 30, 480),
    'under_goggles': (G + 'under_goggles/under_goggles_03.png', 320, 30, 480),
    'behind_tag': (G + 'behind_tag/behind_tag_02.png', 320, 30, 480),
    'white': (G + 'eyes/white_01.png', 420, 180, 200),
    'iris': (G + 'eyes/iris_01.png', 420, 180, 200),
    'closed': (G + 'eyes/closed_04.png', 420, 180, 200),
    'm_small': (G + 'mouth/halfopen_05.png', 420, 180, 200),
    'm_half': (G + 'mouth/halfopen_02.png', 420, 180, 200),
    'm_open': (G + 'mouth/open_03.png', 420, 180, 200),
    'm_round': (G + 'mouth/u_02.png', 420, 180, 200),
    'behind_body': (G + 'behind_body/behind_body_02.png', 0, 0, 1024),
}
for name, (p, x0, y0, size) in jobs.items():
    out, valid = place(p, x0, y0, size)
    if name != 'iris':
        # refine the global placement against the final picture where both show the same thing
        box = {'head': (330, 400, 790, 500), 'back_hair': (330, 400, 790, 500)}.get(name, None)
        if name.startswith('m_') or name in ('white', 'closed'):
            box = (430, 190, 610, 370)
        if name in ('under_goggles', 'behind_tag'):
            box = (340, 280, 780, 500)
        if name == 'behind_body':
            box = (100, 1000, 1000, 1500)
        (dx, dy), resp = phase(final, out, box)
        out, valid = place(p, x0, y0, size, (-dx, -dy))
        print(f'{name}: corrected by {-dx:+.2f},{-dy:+.2f} (resp {resp:.2f})')
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(f'al/{name}.png')
    Image.fromarray((valid * 255).clip(0, 255).astype(np.uint8)).save(f'al/{name}_valid.png')

# the bare head's face sits higher than the final face: measure on the face itself and keep a shifted copy
head, _ = place(G + 'head/head_02.png', 320, 30, 480)
(dx, dy), resp = phase(final, head, (440, 200, 620, 350))
print(f'head face offset: {dx:+.2f},{dy:+.2f} (resp {resp:.2f})')
headf, vf = place(G + 'head/head_02.png', 320, 30, 480, (-dx, -dy))
Image.fromarray(headf.clip(0, 255).astype(np.uint8)).save('al/head_face.png')
Image.fromarray((vf * 255).astype(np.uint8)).save('al/head_face_valid.png')
