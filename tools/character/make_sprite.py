#!/usr/bin/env python3
"""Turns a generated character picture (flat light, plain grey backdrop) into what the camp scene
needs: a cut-out picture and a normal map, so that IoriCharacter.shader can light her with the
fire and the sky.

  python3 tools/character/make_sprite.py <picture.png> <out_dir> [--name iori_sit]

Writes <name>.png (color with alpha; her own colors are carried outward past the outline so that
filtering never picks up the backdrop) and <name>_n.png (normals as plain numbers: r = right,
g = up, b = toward the viewer, each 0..255 for -1..1; import it with sRGB off, not as a "normal map").

There is no 3D model behind the normals. Every shape that the line art closes off is blown up
like a cushion (see inflate), which is enough for light to come from a side: the side of an arm
or a leg that faces the fire lights up, the far side falls into the sky's blue.

Needs numpy, scipy and opencv-python-headless.

The numbers under PICTURE fit the first sitting picture (1024 x 1536). A new picture needs the
face outline redrawn, and the two heights checked against where its chair begins.
"""
import argparse
import os

import cv2
import numpy as np
import scipy.sparse as sp
from scipy import ndimage as ndi
from scipy.sparse.linalg import spsolve

PICTURE = dict(
    # the face keeps one smooth roundness (eyes, brows and mouth are not folds): its outline in pixels
    face=[(432, 205), (470, 182), (560, 178), (604, 200), (612, 262), (590, 305), (540, 338), (498, 348), (455, 318), (428, 262)],
    # above this height small enclosed bits of backdrop are real gaps (between strands of hair)
    hair_below_y=450,
    # below this height the outline is followed tightly: the chair's legs are nearly the backdrop's grey
    chair_from_y=870,
)

# normal map
PARTS = 1.0        # roundness of each outlined part
BODY = 0.25        # roundness of the figure as a whole
SMOOTH = 2.5       # blur of the height before normals are taken (pixels)
GAIN = 0.8         # 1 = a strip comes out as half a cylinder
LINE_PULL = 0.6    # how firmly a drawn line holds the surface down


def backdrop_distance(src):
    lab = cv2.cvtColor(src, cv2.COLOR_BGR2LAB).astype(np.float32)
    band = np.concatenate([lab[:30].reshape(-1, 3), lab[-30:].reshape(-1, 3), lab[:, :30].reshape(-1, 3), lab[:, -30:].reshape(-1, 3)])
    bg = np.median(band, 0)
    d = np.sqrt(((lab - bg) ** 2 * np.array([1.0, 2.0, 2.0])).sum(-1))
    return cv2.GaussianBlur(d, (0, 0), 0.8)


def border_labels(lbl):
    return np.setdiff1d(np.unique(np.concatenate([lbl[0], lbl[-1], lbl[:, 0], lbl[:, -1]])), [0])


def cut_out(src):
    """Returns the figure's mask. The backdrop is whatever is close to its grey and reaches the edge of
    the picture, plus enclosed pockets of the same grey (between the chair's legs, between strands)."""
    h, w = src.shape[:2]
    d = backdrop_distance(src)
    # loose around the hair (faint ghost lines surround it), tight around the chair's legs
    thr = np.where(np.arange(h)[:, None] < PICTURE['chair_from_y'], 13.0, 8.5) * np.ones((1, w))
    lo, _ = ndi.label(d < thr)
    outside = np.isin(lo, border_labels(lo))

    tight = d < 6.5
    lt, nt = ndi.label(tight)
    ids = np.arange(1, nt + 1)
    size = ndi.sum(tight, lt, ids)
    cy = np.array(ndi.center_of_mass(tight, lt, ids))[:, 0]
    thick = ndi.maximum(ndi.distance_transform_edt(tight), lt, ids)   # the inside of a chair leg is a thin strip, a real gap is wide
    edge = set(border_labels(lt).tolist())
    core_ids = [i for i, s, y, t in zip(ids, size, cy, thick)
                if i not in edge and ((s > 300 and t > 11) or (s > 25 and y < PICTURE['hair_below_y']))]
    core = np.isin(lt, core_ids) & ~outside
    ll, _ = ndi.label(d < 10)
    pockets = np.isin(ll, np.setdiff1d(np.unique(ll[core]), [0])) & ~outside
    pockets = ndi.binary_dilation(pockets, iterations=1)

    fg = ~(outside | pockets)
    fg = ndi.binary_opening(fg, iterations=1)
    lbl, n = ndi.label(fg)
    sz = ndi.sum(fg, lbl, range(1, n + 1))
    fg = np.isin(lbl, [i + 1 for i, s in enumerate(sz) if s > 2000])
    fg = ndi.binary_fill_holes(fg | pockets) & ~pockets
    # the outermost pixel row is half backdrop: drop it, and round off single-pixel teeth
    fg = ndi.binary_erosion(fg, iterations=1)
    fg = ndi.median_filter(fg.astype(np.uint8), 3) > 0
    return fg


def color_with_alpha(src, fg):
    alpha = cv2.GaussianBlur(fg.astype(np.float32), (0, 0), 0.7)
    alpha = np.clip((alpha - 0.2) / 0.6, 0, 1)
    solid = ndi.binary_erosion(fg, iterations=1)
    idx = ndi.distance_transform_edt(~solid, return_distances=False, return_indices=True)
    rgb = np.where(solid[..., None], src, src[idx[0], idx[1]])
    return np.dstack([rgb, (alpha * 255 + 0.5).astype(np.uint8)])


def inflate(mask, hold):
    """Solves  laplace(h) - hold * h = -1  inside the mask with h = 0 outside: a soap film blown up
    from the outline, held down where `hold` is large (on drawn lines)."""
    h, w = mask.shape
    n = int(mask.sum())
    idx = -np.ones((h, w), np.int64)
    idx[mask] = np.arange(n)
    ys, xs = np.where(mask)
    rows, cols, vals = [np.arange(n)], [np.arange(n)], [4.0 + hold[mask]]
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        yy, xx = ys + dy, xs + dx
        ok = (yy >= 0) & (yy < h) & (xx >= 0) & (xx < w)
        j = np.full(n, -1)
        j[ok] = idx[yy[ok], xx[ok]]
        m = j >= 0
        rows.append(np.arange(n)[m]); cols.append(j[m]); vals.append(-np.ones(int(m.sum())))
    a = sp.csc_matrix((np.concatenate(vals), (np.concatenate(rows), np.concatenate(cols))), shape=(n, n))
    out = np.zeros((h, w), np.float32)
    out[mask] = spsolve(a, np.ones(n))
    return out


def normals(src, fg):
    h, w = src.shape[:2]
    light = cv2.cvtColor(src, cv2.COLOR_BGR2LAB)[..., 0].astype(np.float32)
    # drawn lines: thin dark strokes against lighter paint
    dip = cv2.morphologyEx(light, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (11, 11))) - light
    lines = np.clip((dip - 18) / 30, 0, 1)
    # borders between differently colored paint count as weaker lines
    lab = cv2.cvtColor(cv2.medianBlur(src, 5), cv2.COLOR_BGR2LAB).astype(np.float32)
    g = np.sqrt(sum(cv2.Sobel(lab[..., c], cv2.CV_32F, 1, 0, ksize=3) ** 2 + cv2.Sobel(lab[..., c], cv2.CV_32F, 0, 1, ksize=3) ** 2 for c in range(3)))
    hold = np.maximum(lines, np.clip((g - 120) / 250, 0, 1) * 0.6)
    face = np.zeros((h, w), np.uint8)
    cv2.fillPoly(face, [np.array(PICTURE['face'])], 255)
    hold *= 1 - cv2.GaussianBlur(face, (0, 0), 6).astype(np.float32) / 255

    s = 2                                   # solved at half size: the result is smooth anyway
    small = lambda a: cv2.resize(a, (w // s, h // s), interpolation=cv2.INTER_AREA)
    m = small(fg.astype(np.float32)) > 0.5
    # the square root turns the film into a round profile: a strip becomes half a cylinder
    parts = np.sqrt(2 * inflate(m, LINE_PULL * small(hold))) * s
    body = np.sqrt(2 * inflate(m, np.zeros((h // s, w // s), np.float32))) * s
    height = cv2.resize(PARTS * parts + BODY * body, (w, h), interpolation=cv2.INTER_CUBIC)
    height = cv2.GaussianBlur(height, (0, 0), SMOOTH)
    gx = cv2.Sobel(height, cv2.CV_32F, 1, 0, ksize=3) / 8
    gy = cv2.Sobel(height, cv2.CV_32F, 0, 1, ksize=3) / 8
    n = np.dstack([-gx * GAIN, gy * GAIN, np.ones_like(gx)])     # x right, y up, z toward the viewer
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    solid = ndi.binary_erosion(fg, iterations=1)
    idx = ndi.distance_transform_edt(~solid, return_distances=False, return_indices=True)
    return n[idx[0], idx[1]]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('picture')
    ap.add_argument('out_dir')
    ap.add_argument('--name', default='iori_sit')
    a = ap.parse_args()
    src = cv2.imread(a.picture)
    if src is None:
        raise SystemExit('cannot read ' + a.picture)
    fg = cut_out(src)
    rgba = color_with_alpha(src, fg)
    n = normals(src, fg)
    nimg = ((n[..., ::-1] * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)

    # trim to the figure: feet near the bottom edge (the board stands on the ground), sizes in multiples of 4
    ys, xs = np.where(fg)
    pad = 12
    x0, x1 = max(xs.min() - pad, 0), min(xs.max() + pad + 1, src.shape[1])
    y0, y1 = max(ys.min() - pad, 0), min(ys.max() + pad + 1, src.shape[0])
    x1 -= (x1 - x0) % 4
    y0 += (y1 - y0) % 4
    os.makedirs(a.out_dir, exist_ok=True)
    cv2.imwrite(os.path.join(a.out_dir, a.name + '.png'), rgba[y0:y1, x0:x1])
    cv2.imwrite(os.path.join(a.out_dir, a.name + '_n.png'), nimg[y0:y1, x0:x1])
    fig = (ys.max() - ys.min() + 1) / (y1 - y0)
    print(f'{a.name}: {x1 - x0} x {y1 - y0}, the figure fills {fig:.3f} of the height, feet {y1 - 1 - ys.max()} px above the bottom edge')


if __name__ == '__main__':
    main()
