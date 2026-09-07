import struct, zlib, math, sys

BG_TOP=(0x2b,0x2d,0x3d); BG_BOT=(0x14,0x15,0x1d); EDGE=(0x4a,0x4c,0x5c)
BLADE_A=(0xe8,0xed,0xf6); BLADE_B=(0x8f,0x9a,0xad)
GOLD_HI=(0xf2,0xd4,0x72); GOLD=(0xc8,0xa8,0x3a); GOLD_LO=(0x8a,0x70,0x24)
RED=(0xd8,0x3a,0x3a)

def cap(px,py,ax,ay,bx,by):
    vx,vy=bx-ax,by-ay; wx,wy=px-ax,py-ay
    L=vx*vx+vy*vy
    t=0.0 if L==0 else max(0.0,min(1.0,(wx*vx+wy*vy)/L))
    return math.hypot(wx-t*vx,wy-t*vy), t

def render(size, ss=4):
    W=size*ss; S=lambda v: v*W
    img=[[(0,0,0,0)]*W for _ in range(W)]
    r=W*0.20
    def inside(x,y):
        cx=min(max(x,r),W-1-r); cy=min(max(y,r),W-1-r)
        return (x-cx)**2+(y-cy)**2 <= r*r
    for y in range(W):
        t=y/(W-1); bg=tuple(int(BG_TOP[i]+(BG_BOT[i]-BG_TOP[i])*t) for i in range(3))
        for x in range(W):
            if inside(x,y):
                near = not (inside(x+ss,y) and inside(x-ss,y) and inside(x,y+ss) and inside(x,y-ss))
                img[y][x]=(*(EDGE if near else bg),255)
    def put(x,y,c):
        if 0<=x<W and 0<=y<W and img[y][x][3]: img[y][x]=(*c,255)

    # ---- Schadens-Schimmer hinter der Klinge (radial, sehr dezent) ----
    gx,gy,gr = S(0.5), S(0.42), S(0.34)
    for y in range(int(gy-gr),int(gy+gr)+1):
        for x in range(int(gx-gr),int(gx+gr)+1):
            if not (0<=x<W and 0<=y<W) or not img[y][x][3]: continue
            d=math.hypot(x-gx,(y-gy)*1.25)
            if d<gr:
                k=(1-d/gr)**2*0.42
                b=img[y][x]
                put(x,y,(int(b[0]+(RED[0]-b[0])*k), int(b[1]+(RED[1]-b[1])*k*0.5), int(b[2]+(RED[2]-b[2])*k*0.5)))

    # ---- Flügel: vier gekrümmte Federn je Seite, überlappend zu einer Flügelform ----
    def disc(cx,cy,rad,col):
        for y in range(int(cy-rad),int(cy+rad)+2):
            for x in range(int(cx-rad),int(cx+rad)+2):
                if (x-cx)**2+(y-cy)**2<=rad*rad: put(x,y,col)
    def feather(sgn, reach, rise, droop, th, col):
        # quadratische Bezier: Basis an der Klinge -> Kontrollpunkt oben -> Spitze aussen
        x0,y0 = S(0.5+sgn*0.065), S(0.585)
        x2,y2 = S(0.5+sgn*(0.065+reach)), S(0.585-rise)
        x1,y1 = S(0.5+sgn*(0.065+reach*0.45)), S(0.585-rise-droop)
        n=90
        for i in range(n+1):
            t=i/n; mt=1-t
            x=mt*mt*x0+2*mt*t*x1+t*t*x2
            y=mt*mt*y0+2*mt*t*y1+t*t*y2
            rad=S(th)*(1.0-0.72*t**1.3)          # zur Spitze schlank auslaufend
            sh=1.0-0.20*t
            disc(x,y,rad,(int(col[0]*sh),int(col[1]*sh),int(col[2]*sh)))
    for sgn in (-1,1):
        for reach,rise,droop,th,col in [
            (0.300,0.070,0.080,0.056,GOLD_LO),
            (0.265,0.155,0.075,0.052,GOLD),
            (0.215,0.230,0.062,0.047,GOLD),
            (0.155,0.290,0.048,0.040,GOLD_HI),
        ]:
            feather(sgn,reach,rise,droop,th,col)

    # ---- Klinge: spitzes Trapez, Mittelgrat hell / Flanke dunkler ----
    tipY,baseY = S(0.115), S(0.665)
    halfBase   = S(0.090)
    for y in range(int(tipY), int(baseY)):
        t=max(0.0,(y-tipY)/(baseY-tipY))
        hw=halfBase*(0.12+0.88*t**0.65)
        for x in range(int(S(0.5)-hw), int(S(0.5)+hw)+1):
            u=(x-S(0.5))/max(hw,1e-6)
            col = BLADE_A if u<0.15 else BLADE_B      # Grat links hell, rechts abgeschattet
            put(x,y,col)

    # ---- Parierstange + Griff + Knauf ----
    for y in range(int(S(0.64)), int(S(0.70))):
        for x in range(int(S(0.30)), int(S(0.70))):
            put(x,y, GOLD if y<S(0.675) else GOLD_LO)
    for y in range(int(S(0.70)), int(S(0.885))):
        for x in range(int(S(0.455)), int(S(0.545))):
            put(x,y, GOLD_LO if (x-S(0.5))>0 else GOLD)
    cx,cy,rr=S(0.5),S(0.875),S(0.052)
    for y in range(int(cy-rr),int(cy+rr)+1):
        for x in range(int(cx-rr),int(cx+rr)+1):
            if (x-cx)**2+(y-cy)**2<=rr*rr: put(x,y, GOLD_HI if (y-cy)<0 else GOLD)

    out=bytearray()
    for y in range(size):
        out.append(0)
        for x in range(size):
            acc=[0,0,0,0]
            for dy in range(ss):
                for dx in range(ss):
                    p=img[y*ss+dy][x*ss+dx]; a=p[3]/255
                    acc[0]+=p[0]*a; acc[1]+=p[1]*a; acc[2]+=p[2]*a; acc[3]+=p[3]
            n=ss*ss; a=acc[3]/n
            if a<1: out+=bytes(4)
            else:
                k=(acc[3]/255)/n
                out+=bytes((min(255,int(acc[0]/n/k)),min(255,int(acc[1]/n/k)),min(255,int(acc[2]/n/k)),int(a)))
    return bytes(out)

def png(size, raw):
    def chunk(tag,data):
        c=tag+data
        return struct.pack(">I",len(data))+c+struct.pack(">I",zlib.crc32(c)&0xffffffff)
    return (b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR",struct.pack(">IIBBBBB",size,size,8,6,0,0,0))
            +chunk(b"IDAT",zlib.compress(raw,9))+chunk(b"IEND",b""))

if __name__=="__main__":
    SC="/tmp/claude-1000/-home-ralf-development-apps-dmg-meter/966880a0-a3f8-472a-af7d-acd74e4f079b/scratchpad/"
    if sys.argv[1:] and sys.argv[1]=="preview":
        for s in (128,32):
            open(SC+f"prev{s}.png","wb").write(png(s,render(s)))
        print("Vorschau geschrieben")
    else:
        sizes=[16,24,32,48,64,128,256]
        images=[png(s,render(s)) for s in sizes]
        head=struct.pack("<HHH",0,1,len(sizes)); off=6+16*len(sizes); dirs=b""
        for s,i in zip(sizes,images):
            dirs+=struct.pack("<BBBBHHII",0 if s==256 else s,0 if s==256 else s,0,0,1,32,len(i),off); off+=len(i)
        open("assets/app/aionsniffer.ico","wb").write(head+dirs+b"".join(images))
        open(SC+"icon256.png","wb").write(images[-1]); open(SC+"icon32.png","wb").write(images[2])
        print("ICO geschrieben")
