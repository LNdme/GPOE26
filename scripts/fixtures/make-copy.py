import random, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter
random.seed(7)
W,H=1400,1800
img=Image.new('RGB',(W,H),(246,243,232))
d=ImageDraw.Draw(img)
for y in range(160,H,64): d.line([(60,y),(W-40,y)],fill=(170,195,225),width=2)
d.line([(150,0),(150,H)],fill=(225,140,140),width=3)
import glob
cands=[f for f in glob.glob('/usr/share/fonts/**/*.ttf',recursive=True) if 'Oblique' in f or 'Italic' in f]
font=ImageFont.truetype(sorted(cands)[0],46)
lines=sys.argv[2].split('|')
y=105
for line in lines:
    x=180+random.randint(-6,6)
    for ch in line:
        d.text((x,y+random.randint(-3,3)),ch,font=font,fill=(25,45,140))
        x+=font.getlength(ch)+random.uniform(-1,1.5)
    y+=128 if line=='' else 64
img=img.rotate(-1.5,expand=False,fillcolor=(60,60,60),resample=Image.BICUBIC).filter(ImageFilter.GaussianBlur(0.6))
img.save(sys.argv[1],quality=85)
print(sorted(cands)[0])
