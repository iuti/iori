"""LaMa pre-fill + Fooocus inpaint patch on Hassaku XL, only inside the hair-over-body mask (same mask as candidate 13).
Writes inp/fooocus_NN.png (final canvas) and prints the outside-mask difference."""
import os, json, time, uuid, urllib.request
import numpy as np, cv2
from PIL import Image

os.chdir(os.path.dirname(os.path.abspath(__file__)))
URL = 'http://127.0.0.1:8000'
INPUT, OUTPUT = r'C:\ai\ComfyUI\input', r'C:\ai\ComfyUI\output'
final = np.asarray(Image.open('../../source/final.png').convert('RGB')).astype(np.float32)
edit = np.asarray(Image.open('inp/mask_body.png')) > 0
X0, Y0, X1, Y1, S = 290, 210, 830, 570, 2
crop = final[Y0:Y1, X0:X1]; cm = edit[Y0:Y1, X0:X1]
big = cv2.resize(crop, ((X1 - X0) * S, (Y1 - Y0) * S), interpolation=cv2.INTER_LANCZOS4)
bm = cv2.resize(cm.astype(np.uint8) * 255, ((X1 - X0) * S, (Y1 - Y0) * S), interpolation=cv2.INTER_NEAREST)
name = 'iori_shoulder_fooocus.png'
Image.fromarray(np.dstack([big.clip(0, 255).astype(np.uint8), 255 - bm]), 'RGBA').save(os.path.join(INPUT, name))
POS = ('masterpiece, best quality, 1girl, white jacket shoulder, cream hood edge, white shirt collar, black headphones, plain grey background, '
       'flat color, cel shading, clean thin lineart, anime')
NEG = 'hair, long hair, purple hair, hair strands, blue hair, lowres, blurry, noise, jpeg artifacts, extra lines, text, watermark, nsfw'


def graph(seed, denoise):
    return {
        '1': {'class_type': 'CheckpointLoaderSimple', 'inputs': {'ckpt_name': 'hassakuXLIllustrious_v22.safetensors'}},
        '2': {'class_type': 'CLIPSetLastLayer', 'inputs': {'clip': ['1', 1], 'stop_at_clip_layer': -2}},
        '3': {'class_type': 'CLIPTextEncode', 'inputs': {'clip': ['2', 0], 'text': POS}},
        '4': {'class_type': 'CLIPTextEncode', 'inputs': {'clip': ['2', 0], 'text': NEG}},
        '5': {'class_type': 'LoadImage', 'inputs': {'image': name}},
        '6': {'class_type': 'INPAINT_LoadInpaintModel', 'inputs': {'model_name': 'big-lama.pt'}},
        '7': {'class_type': 'INPAINT_InpaintWithModel', 'inputs': {'inpaint_model': ['6', 0], 'image': ['5', 0], 'mask': ['5', 1], 'seed': seed}},
        '8': {'class_type': 'INPAINT_LoadFooocusInpaint', 'inputs': {'head': 'fooocus_inpaint_head.pth', 'patch': 'inpaint_v26.fooocus.patch'}},
        '9': {'class_type': 'INPAINT_VAEEncodeInpaintConditioning', 'inputs': {'positive': ['3', 0], 'negative': ['4', 0], 'vae': ['1', 2], 'pixels': ['7', 0], 'mask': ['5', 1]}},
        '10': {'class_type': 'INPAINT_ApplyFooocusInpaint', 'inputs': {'model': ['1', 0], 'patch': ['8', 0], 'latent': ['9', 2]}},
        '11': {'class_type': 'KSampler', 'inputs': {'model': ['10', 0], 'seed': seed, 'steps': 28, 'cfg': 5.0, 'sampler_name': 'dpmpp_2m', 'scheduler': 'karras',
                                                    'positive': ['9', 0], 'negative': ['9', 1], 'latent_image': ['9', 3], 'denoise': denoise}},
        '12': {'class_type': 'VAEDecode', 'inputs': {'samples': ['11', 0], 'vae': ['1', 2]}},
        '13': {'class_type': 'SaveImage', 'inputs': {'images': ['12', 0], 'filename_prefix': f'iori_fooocus_{seed}_{int(denoise * 100)}'}},
    }


def run(g):
    data = json.dumps({'prompt': g, 'client_id': str(uuid.uuid4())}).encode()
    pid = json.loads(urllib.request.urlopen(urllib.request.Request(URL + '/prompt', data, {'Content-Type': 'application/json'})).read())['prompt_id']
    while True:
        time.sleep(2)
        h = json.loads(urllib.request.urlopen(URL + '/history/' + pid).read())
        if pid in h:
            st = h[pid].get('status', {})
            if st.get('status_str') == 'error': raise RuntimeError(json.dumps(st)[:1200])
            im = h[pid]['outputs']['13']['images'][0]
            return os.path.join(OUTPUT, im.get('subfolder', ''), im['filename'])


jobs = [(11, 0.3), (11, 0.5), (22, 0.5), (11, 0.8)]
for k, (seed, dn) in enumerate(jobs, 1):
    t0 = time.time()
    out = np.asarray(Image.open(run(graph(seed, dn))).convert('RGB')).astype(np.float32)
    small = cv2.resize(out, (X1 - X0, Y1 - Y0), interpolation=cv2.INTER_AREA)
    canvas = final.copy(); canvas[Y0:Y1, X0:X1] = small
    comp = np.where(edit[..., None], canvas, final)
    Image.fromarray(comp.clip(0, 255).astype(np.uint8)).save(f'inp/fooocus_{k:02d}.png')
    dd = np.abs(comp - final).max(2)
    print(k, 'seed', seed, 'denoise', dn, f'{time.time() - t0:.0f}s', 'outside mask max', int(dd[~edit].max()), 'px>24', int((dd[~edit] > 24).sum()))
