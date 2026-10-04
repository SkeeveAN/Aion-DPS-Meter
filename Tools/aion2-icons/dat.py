import struct,re
KEY=bytes([0x25,0,0xa8,0,0x7e,0,0x91,0])
def strings(d):
    """all plausible UTF-16 xor-obfuscated FStrings: [(offset, text, end)]"""
    out=[]; p=0; n=len(d)
    while p+8<=n:
        l=struct.unpack_from('<i',d,p)[0]
        if -300<l<-1 and p+4+(-l)*2<=n:
            raw=bytearray(d[p+4:p+4+(-l)*2])
            for i in range(len(raw)): raw[i]^=KEY[i%8]
            try: s=raw.decode('utf-16le')
            except: p+=1; continue
            if all(32<=ord(c)<127 for c in s[:-1]) and len(s)>=4:
                out.append((p,s[:-1],p+4+(-l)*2)); p+=4+(-l)*2; continue
        p+=1
    return out
