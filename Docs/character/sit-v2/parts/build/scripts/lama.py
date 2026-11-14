"""Remove the hair over the body with LaMa (big-lama.pt) loaded through spandrel (already in ComfyUI's venv).
usage: <comfy venv python> lama.py <mask.png> <out.png>"""
import sys
import numpy as np, torch
from PIL import Image
from spandrel import ModelLoader

final = np.asarray(Image.open(r'C:\ai\iori_live\source\final.png').convert('RGB')).astype(np.float32) / 255
mask = np.asarray(Image.open(sys.argv[1])) > 0
X0, Y0, X1, Y1 = 260, 180, 860, 600            # work on the shoulders, padded
crop = final[Y0:Y1, X0:X1]; cm = mask[Y0:Y1, X0:X1]
model = ModelLoader().load_from_file(r'C:\ai\ComfyUI\models\inpaint\big-lama.pt').eval()
dev = 'cuda' if torch.cuda.is_available() else 'cpu'
model = model.to(dev)
img = torch.from_numpy(crop).permute(2, 0, 1)[None].to(dev)
m = torch.from_numpy(cm.astype(np.float32))[None, None].to(dev)
# LaMa wants sizes in multiples of 8
H, W = img.shape[2:]; ph, pw = (8 - H % 8) % 8, (8 - W % 8) % 8
img = torch.nn.functional.pad(img, (0, pw, 0, ph), mode='reflect'); m = torch.nn.functional.pad(m, (0, pw, 0, ph))
with torch.no_grad():
    out = model(img, m)[0, :, :H, :W].permute(1, 2, 0).clamp(0, 1).cpu().numpy()
res = final.copy(); res[Y0:Y1, X0:X1] = np.where(cm[..., None], out, crop)
Image.fromarray((res * 255).round().astype(np.uint8)).save(sys.argv[2])
print('ok', dev)
