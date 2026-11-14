"""Head movement between neighbouring frames by phase correlation in a head box: px per frame."""
import glob, sys, numpy as np, cv2
from PIL import Image
def run(pat, box):
    fs = sorted(glob.glob(pat)); prev = None; s = []
    win = None
    for f in fs:
        a = np.asarray(Image.open(f).convert('L').crop(box)).astype(np.float32)
        if win is None: win = cv2.createHanningWindow(a.shape[::-1], cv2.CV_32F)
        if prev is not None:
            (dx, dy), _ = cv2.phaseCorrelate(prev, a, win); s.append(np.hypot(dx, dy))
        prev = a
    s = np.array(s); return len(fs), np.median(s), np.percentile(s, 99), s.max(), int(np.argmax(s)) + 1
for lab, pat, box in [(a, b, tuple(map(int, c.split(',')))) for a, b, c in zip(sys.argv[1::3], sys.argv[2::3], sys.argv[3::3])]:
    n, med, p99, mx, at = run(pat, box)
    print(f'{lab}: frames {n}, px/frame median {med:.2f}, p99 {p99:.2f}, max {mx:.2f} (frame {at})')
