import glob, os, subprocess, sys, numpy as np, cv2
from PIL import Image
d = sys.argv[1]; D = 'C:/ai/iori-bg/Docs/camp-shots/puppet-iori-draft'
fin = Image.open('C:/ai/iori_live/source/final.png').convert('RGB'); bg = tuple(int(v) for v in np.median(np.asarray(fin)[:6].reshape(-1, 3), 0))
os.makedirs(d + '/up', exist_ok=True); os.makedirs(d + '/full', exist_ok=True)
fs = sorted(glob.glob(d + '/motion/motion_*.png'))
for i, f in enumerate(fs):
    im = Image.open(f).convert('RGBA'); b = Image.new('RGBA', im.size, bg + (255,)); b.alpha_composite(im); b = b.convert('RGB')
    b.save(f'{d}/full/{i:03d}.png'); b.crop((150, 20, 900, 770)).save(f'{d}/up/{i:03d}.png')
run = lambda a: subprocess.run(['ffmpeg', '-y', '-loglevel', 'error'] + a, check=True)
enc = ['-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '14', '-preset', 'slow']
run(['-framerate', '30', '-i', f'{d}/up/%03d.png'] + enc + [f'{D}/puppet_motion_plain.mp4'])
run(['-framerate', '30', '-i', f'{d}/full/%03d.png'] + enc + [f'{D}/puppet_motion_plain_full.mp4'])
# camp: frames at their own size, no upscaling
run(['-framerate', '30', '-i', f'{d}/80_puppet_%03d.jpg'] + enc + [f'{D}/puppet_idle.mp4'])
fr = [Image.open(f).convert('RGB') for f in sorted(glob.glob(d + '/80_puppet_*.jpg'))]
fr[0].save(f'{D}/puppet_idle.webp', save_all=True, append_images=fr[1:], duration=33, loop=0, quality=85, method=4)
for n in ('puppet_motion_plain.mp4', 'puppet_motion_plain_full.mp4', 'puppet_idle.mp4', 'puppet_idle.webp'):
    print(n, round(os.path.getsize(f'{D}/{n}') / 1e6, 2), 'MB')
