"""Render one asset group on a neutral floor to check its shape: python _preview_asset.py fire out.png"""
import sys, math, random
sys.path.insert(0, '.')
from campkit import *
which, outp = sys.argv[1], sys.argv[2]
S = Scene()
rng = random.Random(11)
floor = grid(8, 8, 8, 8, hexc('#566a58'))
S.put('floor', floor, 'Ground', ao=False)
cam, tgt, fov, lights = (0.9, -1.6, 1.0), (0, 0, .15), 36, [((0, 0, .3), (1, .55, .25), 25, .04)]
if which == 'fire':
    import models_fire
    for n, p in models_fire.build_fire(rng).items():
        S.put(n, p, 'Prop')
else:
    import models_props, models_nature
    fn = getattr(models_props, which, None) or getattr(models_nature, which)
    VIEWS = {'tent': ((2.2, -4.2, 1.6), (0, 0, .6), 36), 'tree_test': ((1.5, -9, 1.6), (0, 0, 3.4), 46), 'chair': ((1.3, -2.0, 1.1), (0, 0, .4), 34),
             'lantern': ((.35, -.6, .35), (0, 0, .15), 34), 'radio': ((.35, -.75, .32), (0, 0, .12), 34), 'deck': ((.3, -.55, .42), (0, 0, .03), 34), 'mug': ((.2, -.3, .2), (0, 0, .04), 34),
             'kettle': ((.3, -.5, .3), (0, 0, .06), 34), 'stump': ((.7, -1.1, .8), (0, 0, .2), 34), 'crate': ((.7, -1.1, .7), (0, 0, .15), 34), 'firewood': ((.7, -1.1, .7), (0, 0, .12), 34),
             'plants_test': ((.6, -2.2, .7), (0, 0, .2), 40), 'conifer_test': ((2, -16, 2), (0, 0, 4.5), 40), 'boulder': ((1.0, -1.6, .9), (0, 0, .25), 34), 'table': ((1.0, -1.5, .9), (0, 0, .25), 34)}
    res = fn(rng)
    if which in VIEWS: cam, tgt, fov = VIEWS[which]
    lights = [((cam[0] * -.6, cam[1] * .5, tgt[2] + (abs(cam[1]) * .5)), (1, .6, .3), 40 * abs(cam[1]) ** 2, .05)]
    for n, p in (res.items() if isinstance(res, dict) else [(which, res)]):
        S.put(n, p, 'Prop')
S.bake_ao(samples=16, distance=0.5)
res = (720, 720)
S.preview(outp, cam, tgt, fov, res, 48, lights=lights + [((2.5, -3, 3.5), (0.75, 0.8, 1.0), 300, .5)], sky_strength=0.35)
