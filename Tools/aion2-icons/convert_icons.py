"""Converts dumped cooked texture packages (see Tools/aion2-icons) into 128px WebP files."""
import struct,io,json,os,sys
from PIL import Image
RAW=sys.argv[1]; OUT=sys.argv[2]; MAPS=sys.argv[3:]
def dds(w,h,fourcc,data):
    hdr=struct.pack('<4sIIIIIII44sII4sIIIII',b'DDS ',124,0x81007,h,w,len(data),0,1,b'\0'*44,32,4,fourcc,0,0,0,0,0)
    return hdr+data+b'' if False else hdr+struct.pack('<IIIII',0x1000,0,0,0,0)+data
def conv(path):
    d=open(path,'rb').read()
    i=d.rfind(b'PF_'); j=d.find(b'\0',i); fmt=d[i:j].decode()
    w,h,_=struct.unpack('<iii',d[i-16:i-4])
    if fmt=='PF_DXT5': n=w*h; fc=b'DXT5'
    elif fmt=='PF_DXT1': n=w*h//2; fc=b'DXT1'
    else: return None
    im=Image.open(io.BytesIO(dds(w,h,fc,d[len(d)-n-12:len(d)-12]))).convert('RGBA')
    return im.resize((128,128),Image.LANCZOS)
done=0
for m in MAPS:
    kind=os.path.basename(m).split('_')[0]          # item_icons.json -> item
    os.makedirs(f'{OUT}/{kind}',exist_ok=True)
    for name in sorted(set(json.load(open(m)).values())):
        if not os.path.exists(f'{RAW}/{name}.uasset'): continue
        im=conv(f'{RAW}/{name}.uasset')
        if im: im.save(f'{OUT}/{kind}/{name}.webp','WEBP',quality=82,method=6); done+=1
print('written',done)
