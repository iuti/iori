"""ComfyUI inpainting of the hair over the shoulders: only the masked area is redrawn (Hassaku XL, InpaintModelConditioning),
then pasted back on the final. Writes inp/inpaint_NN.png (final canvas) and the raw crops."""
import os, json, time, uuid, urllib.request, sys
import numpy as np, cv2
from PIL import Image

os.chdir(os.path.dirname(os.path.abspath(__file__)))
URL = 'http://127.0.0.1:8000'
INPUT = r'C:\ai\ComfyUI\input'
OUTPUT = r'C:\ai\ComfyUI\output'
os.makedirs('inp', exist_ok=True)

final = np.asarray(Image.open('../../source/final.png').convert('RGB')).astype(np.float32)
H, W = final.shape[:2]
yy, xx = np.mgrid[0:H, 0:W]
M = lambda n: np.asarray(Image.open(f'm/{n}.png')) > 0
dil = lambda m, k: cv2.dilate(m.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k))).astype(bool)
edit = np.asarray(Image.open('inp/edit_mask.png')) > 0


X0, Y0, X1, Y1 = 290, 210, 830, 570
S = 2
crop = final[Y0:Y1, X0:X1]
cm = edit[Y0:Y1, X0:X1]
crop = cv2.inpaint(crop.clip(0, 255).astype(np.uint8), cm.astype(np.uint8), 7, cv2.INPAINT_TELEA).astype(np.float32)
big = cv2.resize(crop, ((X1 - X0) * S, (Y1 - Y0) * S), interpolation=cv2.INTER_LANCZOS4)
bm = cv2.resize(cm.astype(np.uint8) * 255, ((X1 - X0) * S, (Y1 - Y0) * S), interpolation=cv2.INTER_NEAREST)
# LoadImage gives MASK = 1 - alpha: transparent where it may repaint
rgba = np.dstack([big.clip(0, 255).astype(np.uint8), 255 - bm])
name = 'iori_shoulder_inpaint.png'
Image.fromarray(rgba, 'RGBA').save(os.path.join(INPUT, name))

POS = ('masterpiece, best quality, 1girl, white hooded jacket on shoulders, cream hood lining, white collared shirt, black headphones around neck, '
       'neck, plain grey background, flat color, cel shading, clean thin lineart, anime')
NEG = ('hair, long hair, purple hair, hair strands, blue hair, lowres, blurry, noise, jpeg artifacts, extra lines, sketch, text, watermark, nsfw, deformed')


def graph(seed, denoise):
    return {
        '1': {'class_type': 'CheckpointLoaderSimple', 'inputs': {'ckpt_name': 'hassakuXLIllustrious_v22.safetensors'}},
        '2': {'class_type': 'CLIPSetLastLayer', 'inputs': {'clip': ['1', 1], 'stop_at_clip_layer': -2}},
        '3': {'class_type': 'CLIPTextEncode', 'inputs': {'clip': ['2', 0], 'text': POS}},
        '4': {'class_type': 'CLIPTextEncode', 'inputs': {'clip': ['2', 0], 'text': NEG}},
        '5': {'class_type': 'LoadImage', 'inputs': {'image': name}},
        '6': {'class_type': 'GrowMask', 'inputs': {'mask': ['5', 1], 'expand': 2, 'tapered_corners': True}},
        '7': {'class_type': 'InpaintModelConditioning', 'inputs': {'positive': ['3', 0], 'negative': ['4', 0], 'vae': ['1', 2],
                                                                    'pixels': ['5', 0], 'mask': ['6', 0], 'noise_mask': True}},
        '8': {'class_type': 'KSampler', 'inputs': {'model': ['1', 0], 'seed': seed, 'steps': 28, 'cfg': 6.0, 'sampler_name': 'dpmpp_2m',
                                                   'scheduler': 'karras', 'positive': ['7', 0], 'negative': ['7', 1], 'latent_image': ['7', 2],
                                                   'denoise': denoise}},
        '9': {'class_type': 'VAEDecode', 'inputs': {'samples': ['8', 0], 'vae': ['1', 2]}},
        '10': {'class_type': 'SaveImage', 'inputs': {'images': ['9', 0], 'filename_prefix': f'iori_inpaint_{seed}'}},
    }


def run(g):
    data = json.dumps({'prompt': g, 'client_id': str(uuid.uuid4())}).encode()
    pid = json.loads(urllib.request.urlopen(urllib.request.Request(URL + '/prompt', data, {'Content-Type': 'application/json'})).read())['prompt_id']
    while True:
        time.sleep(2)
        h = json.loads(urllib.request.urlopen(URL + '/history/' + pid).read())
        if pid in h:
            st = h[pid].get('status', {})
            if st.get('status_str') == 'error': raise RuntimeError(json.dumps(st)[:800])
            outs = h[pid]['outputs']['10']['images']
            return os.path.join(OUTPUT, outs[0].get('subfolder', ''), outs[0]['filename'])


jobs = [(11, 0.5), (11, 0.7), (11, 0.9), (22, 0.7), (22, 0.9), (33, 0.9)]
alpha_full = edit.astype(np.float32)
for k, (seed, dn) in enumerate(jobs, 1):
    t0 = time.time()
    p = run(graph(seed, dn))
    out = np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
    Image.fromarray(out.astype(np.uint8)).save(f'inp/inpaint_{k:02d}_raw.png')
    small = cv2.resize(out, (X1 - X0, Y1 - Y0), interpolation=cv2.INTER_AREA)
    canvas = final.copy(); canvas[Y0:Y1, X0:X1] = small
    BGc = final[5, 5]
    bgm = ((np.abs(final - BGc).max(2) < 10) & ~dil(edit, 3)).astype(np.float32)
    num = cv2.GaussianBlur(final * bgm[..., None], (0, 0), 25); den = cv2.GaussianBlur(bgm, (0, 0), 25)[..., None]
    bgfill = np.where(den > 1e-3, num / np.maximum(den, 1e-3), BGc)
    nb = edit & (np.abs(canvas - BGc).max(2) < 30)
    canvas[nb] = bgfill[nb]
    comp = final * (1 - alpha_full[..., None]) + canvas * alpha_full[..., None]
    Image.fromarray(comp.clip(0, 255).astype(np.uint8)).save(f'inp/inpaint_{k:02d}.png')
    dd = np.abs(comp - final).max(2)
    print(k, 'seed', seed, 'denoise', dn, f'{time.time() - t0:.0f}s', 'outside mask: max diff', int(dd[~edit].max()), 'px>24', int((dd[~edit] > 24).sum()))
