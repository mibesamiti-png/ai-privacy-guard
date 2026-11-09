"""Non-explicit synthetic adult illustration fixtures, never user or scraped photos.
Deterministic PNG + BGRA pairs for the same pixel data used by C# ONNX.
"""
from PIL import Image, ImageDraw
from pathlib import Path
import argparse, hashlib, json

W,H=480,640

def background(theme):
    im=Image.new('RGB',(W,H))
    p=im.load()
    for y in range(H):
        for x in range(W):
            if theme=='night': color=(18+int(y*.02),19+int(y*.022),28+int(y*.03))
            elif theme=='beach': color=(138+int(y*.07),194-int(y*.045),221-int(y*.05))
            elif theme=='pattern': color=(50+(x//24%2)*60,50+(y//28%2)*60,100+((x+y)//22%2)*65)
            else: color=(194-int(y*.07),204-int(y*.08),211-int(y*.07))
            p[x,y]=tuple(max(0,min(255,c)) for c in color)
    return im

def adult(draw,center=240,top=65,scale=1,garment='dress',night=False,motion=False):
    def px(v): return int(round(center+(v-240)*scale))
    def py(v): return int(round(top+(v-65)*scale))
    def rect(a,b,c,d,fill):draw.rectangle((px(a),py(b),px(c),py(d)),fill=fill)
    def ellipse(a,b,c,d,fill):draw.ellipse((px(a),py(b),px(c),py(d)),fill=fill)
    def poly(coords,fill):draw.polygon([(px(x),py(y)) for x,y in coords],fill=fill)
    skin=tuple(int(c*(.5 if night else 1)) for c in (203,157,118))
    poly([(207,407),(230,410),(224,556),(204,559)],skin)
    poly([(249,407),(270,406),(278,557),(256,559)],skin)
    rect(193,550,232,578,(47,41,49));rect(252,550,291,578,(47,41,49))
    poly([(183,224),(191,222),(184,397),(166,397)],skin)
    poly([(289,221),(296,224),(311,397),(295,397)],skin)
    ellipse(204,80,278,170,skin);ellipse(195,71,282,126,(77,50,39))
    rect(221,158,257,190,skin)
    if garment=='swimsuit':
        poly([(194,187),(285,187),(276,322),(258,345),(240,325),(222,345),(204,323)],(45,89,155))
        poly([(204,198),(222,186),(226,269),(207,288)],(24,72,138))
        poly([(254,186),(273,196),(270,290),(250,266)],(24,72,138))
    else:
        poly([(195,189),(285,187),(293,322),(183,322)],(145,62,80) if garment=='dress' else (43,62,90))
        if garment=='dress':
            if motion:
                poly([(192,306),(281,307),(332,406),(143,423)],(148,61,85))
                poly([(216,375),(269,375),(276,402),(207,401)],(221,224,237))
            else:poly([(184,307),(295,306),(319,448),(159,448)],(145,62,80))
        else:
            poly([(186,310),(242,309),(229,474),(196,482)],(63,73,104))
            poly([(245,310),(294,309),(285,476),(253,477)],(63,73,104))

def save(root,name,im,description):
    folder=root/'inputs';folder.mkdir(parents=True,exist_ok=True)
    png=folder/(name+'.png');raw=folder/(name+'.bgra')
    im.save(png);raw.write_bytes(im.convert('RGBA').tobytes('raw','BGRA'))
    return dict(id=name,width=im.width,height=im.height,description=description,
                source='fully procedural generated non-explicit illustration',
                png='inputs/'+png.name,bgra='inputs/'+raw.name,
                png_sha256=hashlib.sha256(png.read_bytes()).hexdigest())

def create(root):
    root.mkdir(parents=True,exist_ok=True);cases=[]
    for name,gray in [('01_black_control',0),('02_gray_control',90)]:
        cases.append(save(root,name,Image.new('RGB',(W,H),(gray,gray,gray)),
                     'Blank control - zero positive detections expected'))
    configs=[
        ('03_clothed_dress','dress','studio',False,'Opaque dress on illustrated adult mannequin'),
        ('04_clothed_trousers','trousers','studio',False,'Clothed adult illustrated mannequin'),
        ('05_swimsuit_mannequin','swimsuit','beach',False,'Opaque one-piece swimsuit; non-explicit illustration'),
        ('06_skirt_movement','dress','studio',True,'Moving skirt with shorts; NOT a genital-exposure ground truth'),
        ('07_dark_clothing','dress','night',False,'Low light fully clothed adult illustration')]
    for name,garment,theme,motion,description in configs:
        im=background(theme);adult(ImageDraw.Draw(im),garment=garment,
                                   night=theme=='night',motion=motion)
        cases.append(save(root,name,im,description))
    im=background('pattern');draw=ImageDraw.Draw(im)
    adult(draw,center=155,top=120,scale=.75,garment='dress')
    adult(draw,center=340,top=120,scale=.75,garment='trousers')
    cases.append(save(root,'08_multiple_figures',im,'Two clothed illustrations on patterned background'))
    # Optional CC0 licensed photograph of an adult marble statue, cropped to
    # the sculpture. This tests whether the model emits any candidate on
    # a real photograph; it is NOT a ground-truth people/exposure benchmark.
    from urllib.request import Request, urlopen
    from io import BytesIO
    source_url = "https://upload.wikimedia.org/wikipedia/commons/4/43/Michelangelo%27s_David_%288292252317%29.jpg"
    try:
        request = Request(source_url, headers={'User-Agent': 'AIPrivacyGuard-OpenTest/1.0 (public CC0 sculpture evaluation)'})
        with urlopen(request, timeout=35) as response:
            source_bytes = response.read(3 * 1024 * 1024)
        full = Image.open(BytesIO(source_bytes)).convert('RGB')
        left, top, right, bottom = int(full.width*.52), int(full.height*.05), int(full.width*.82), int(full.height*.98)
        cropped = full.crop((left, top, right, bottom))
        cropped.thumbnail((480, 640), Image.Resampling.LANCZOS)
        item = save(root, '09_cc0_david_marble_statue', cropped,
                    'Photograph of Michelangelo David adult marble statue: CC0, Mustang Joe; cropped from source')
        item['source'] = 'Wikimedia Commons (Mustang Joe), CC0-1.0'
        item['source_url'] = 'https://commons.wikimedia.org/wiki/File:Michelangelo%27s_David_(8292252317).jpg'
        item['license'] = 'CC0-1.0'
        cases.append(item)
        print('Downloaded licensed adult marble statue CC0 photo for a real-image probe.')
    except Exception as ex:
        print('CC0 photo could not be fetched; retaining eight synthetic cases:', type(ex).__name__)

    data=dict(schema_version=1,
              purpose='Synthetic regression tests plus optional CC0 marble sculpture photograph; NOT a validated exposure benchmark',
              labels='Eight generated clothed-figure illustrations, plus one optional CC0 adult classical sculpture photograph',
              cases=cases)
    (root/'fixtures.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'Created {len(cases)} exact-pixel PNG/BGRA fixture pairs in {root}')

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--output',default='dist/nudenet-visual-eval')
    args=parser.parse_args()
    create(Path(args.output))
