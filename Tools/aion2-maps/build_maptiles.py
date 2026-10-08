"""Builds the transparent map tiles in native resolution: python3 maptiles.py"""
import struct,io,os,json,collections,sys
from PIL import Image,ImageFilter,ImageChops,ImageDraw
OUT='/mnt/c/Users/ralf/Desktop/aion2/Maps'
def dds(w,h,fc,data):
    hdr=struct.pack('<4sIIIIIII44sII4sIIIII',b'DDS ',124,0x81007,h,w,len(data),0,1,b'\0'*44,32,4,fc,0,0,0,0,0)
    return hdr+struct.pack('<IIIII',0x1000,0,0,0,0)+data
def load(p):
    d=open(p,'rb').read()
    i=d.rfind(b'PF_');j=d.find(b'\0',i);fmt=d[i:j].decode();w,h,_=struct.unpack('<iii',d[i-16:i-4])
    bpp=0.5 if fmt=='PF_DXT1' else 1.0;fc=b'DXT1' if fmt=='PF_DXT1' else b'DXT5'
    ub=p[:-7]+'.ubulk'
    if os.path.exists(ub):
        data=open(ub,'rb').read();dim=w
        while dim>4 and int(dim*dim*bpp)>len(data): dim//=2
        data=data[:int(dim*dim*bpp)];w=h=dim
    else:
        n=int(w*h*bpp);data=d[len(d)-n-12:len(d)-12]
    return Image.open(io.BytesIO(dds(w,h,fc,data))).convert('RGB')
def landmask(comp,W,vmin=150,hmin=128,minhalo=350,erode=0,band=0):
    hsv=comp.convert('HSV');H,S,V=hsv.split();h=H.load();s=S.load();v=V.load()
    m=Image.new('L',(W,W));mp=m.load()
    for y in range(W):
        for x in range(W):
            hh,ss,vv=h[x,y],s[x,y],v[x,y]
            halo=(128<=hh<=168 and ss<125)
            if (ss>68 and not halo) or vv<125: mp[x,y]=255
    m=m.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(5))       # thin lines, text, ornaments
    m=m.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.MinFilter(9))       # small gaps
    out=m.copy();ImageDraw.floodfill(out,(0,0),128,thresh=0);mp=m.load();op=out.load()
    for y in range(W):
        for x in range(W):
            if op[x,y]!=128: mp[x,y]=255                                        # enclosed holes (rivers, lakes, pale ground) are land
    seen=bytearray(W*W);keep=Image.new('L',(W,W));kp=keep.load()
    for y0 in range(W):
        for x0 in range(W):
            if mp[x0,y0]==255 and not seen[y0*W+x0]:
                q=collections.deque([(x0,y0)]);seen[y0*W+x0]=1;comp_px=[]
                while q:
                    x,y=q.popleft();comp_px.append((x,y))
                    for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                        nx,ny=x+dx,y+dy
                        if 0<=nx<W and 0<=ny<W and mp[nx,ny]==255 and not seen[ny*W+nx]:
                            seen[ny*W+nx]=1;q.append((nx,ny))
                cx=sum(c[0] for c in comp_px)/len(comp_px);cy=sum(c[1] for c in comp_px)/len(comp_px)
                if len(comp_px)>=1000 and not (cx<W*0.215 and cy<W*0.215):
                    for x,y in comp_px: kp[x,y]=255
    # the light blue-grey sea mist between islands and in bays: connected areas of it are not land (small lakes and rivers stay)
    hp=Image.new('L',(W,W));hq=hp.load()
    for y in range(W):
        for x in range(W):
            if kp[x,y]==255 and hmin<=h[x,y]<=168 and 8<s[x,y]<135 and v[x,y]>vmin: hq[x,y]=255
    if band:
        interior=keep.filter(ImageFilter.MinFilter(2*band+1)).load()
        for y in range(W):
            for x in range(W):
                if hq[x,y]==255 and not interior[x,y]: kp[x,y]=0
        hq=Image.new('L',(W,W)).load()          # the component rule below has nothing left to do
    seen2=bytearray(W*W)
    for y0 in range(W):
        for x0 in range(W):
            if hq[x0,y0]==255 and not seen2[y0*W+x0]:
                q=collections.deque([(x0,y0)]);seen2[y0*W+x0]=1;px=[]
                while q:
                    x,y=q.popleft();px.append((x,y))
                    for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                        nx,ny=x+dx,y+dy
                        if 0<=nx<W and 0<=ny<W and hq[nx,ny]==255 and not seen2[ny*W+nx]:
                            seen2[ny*W+nx]=1;q.append((nx,ny))
                if len(px)>=minhalo:
                    for x,y in px: kp[x,y]=0
    if erode: keep=keep.filter(ImageFilter.MinFilter(2*erode+1))          # the thin blue-grey rim around the coast
    return keep
def build(name,dirn,prefix,grid,vmin=150,hmin=128,minhalo=350,erode=0,band=0):
    first=load(f'{dirn}/{prefix}_00_00.uasset');T=first.size[0]
    W=2048;t=W//grid
    comp=Image.new('RGB',(W,W));tiles={}
    for rr in range(grid):
        for cc in range(grid):
            im=load(f'{dirn}/{prefix}_{rr:02d}_{cc:02d}.uasset');tiles[(rr,cc)]=im
            comp.paste(im.resize((t,t),Image.LANCZOS),(rr*t,cc*t))
    mask=landmask(comp,W,vmin,hmin,minhalo,erode,band)
    d=f'{OUT}/{name}_tiles';os.makedirs(d,exist_ok=True);kept=0
    for (rr,cc),im in tiles.items():
        x0,y0=rr*t,cc*t;mg=3
        region=mask.crop((x0-mg,y0-mg,x0+t+mg,y0+t+mg))
        if region.getbbox() is None: continue
        big=region.resize(((t+2*mg)*(T//t),(t+2*mg)*(T//t)),Image.BICUBIC)
        k=mg*(T//t);a=big.crop((k,k,k+T,k+T)).point(lambda x:255 if x>127 else 0).filter(ImageFilter.GaussianBlur(max(2,T/400)))
        if a.getbbox() is None: continue
        rgba=im.convert('RGBA');rgba.putalpha(a);rgba.save(f'{d}/{rr}_{cc}.webp','WEBP',quality=88,method=4);kept+=1
    json.dump({'tile':T,'grid':grid,'x':'file RR = column, CC = row'},open(f'{d}/info.json','w'))
    ov=comp.resize((4096,4096),Image.LANCZOS) if False else None
    full=comp.convert('RGBA');full.putalpha(mask.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(1.5)));full=full.resize((4096,4096),Image.LANCZOS)
    full.save(f'{OUT}/Karte_{name}.png')
    size=sum(os.path.getsize(f'{d}/{f}') for f in os.listdir(d))//1024//1024
    print(name,'tiles with land',kept,'of',grid*grid,'tile px',T,'->',size,'MB',flush=True)
    bg=Image.new('RGBA',(4096,4096),(30,40,44,255));bg.alpha_composite(full);bg=bg.convert('RGB');bg.thumbnail((1000,1000));bg.save(f'trans_{name}.jpg',quality=85)
which=sys.argv[1:] or ['Verteron','Altgard','Abyss']
cfg={'Verteron':('/mnt/d/tmp_aion2_work/maptiles2','World_L_A',8,95,100,350,1,28),'Altgard':('/mnt/d/tmp_aion2_work/maptiles3','World_D_A',8,95,100,350,1,28),'Abyss':('/mnt/d/tmp_aion2_work/maptiles3','Abyss_Reshanta_A',4,70,92,40,3)}
for n in which: build(n,*cfg[n])
