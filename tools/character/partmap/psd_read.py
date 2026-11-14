"""Minimal PSD layer reader (8-bit RGB, raw or RLE channels; groups ignored), no outside libraries.
read_layers(path) -> W, H, [(name, top, left, bottom, right, {channel id: 2D uint8}, opacity, visible)]"""
import struct, numpy as np

def unpackbits(data, n):
    out = bytearray(); i = 0
    while len(out) < n and i < len(data):
        c = data[i]; i += 1
        if c < 128: out += data[i:i + c + 1]; i += c + 1
        elif c > 128: out += bytes([data[i]]) * (257 - c); i += 1
    return bytes(out[:n])

def read_layers(path):
    b = open(path, 'rb').read()
    sig, ver, ch, H, W, depth, mode = struct.unpack('>4sH6xHIIHH', b[:26])
    assert sig == b'8BPS' and depth == 8, (sig, depth)
    big = ver == 2
    p = 26
    n = struct.unpack('>I', b[p:p + 4])[0]; p += 4 + n          # colour mode data
    n = struct.unpack('>I', b[p:p + 4])[0]; p += 4 + n          # image resources
    lmlen = struct.unpack('>Q' if big else '>I', b[p:p + (8 if big else 4)])[0]; p += 8 if big else 4
    lilen = struct.unpack('>Q' if big else '>I', b[p:p + (8 if big else 4)])[0]; p += 8 if big else 4
    cnt = abs(struct.unpack('>h', b[p:p + 2])[0]); p += 2
    recs = []
    for _ in range(cnt):
        t, l, bo, r, nc = struct.unpack('>iiiiH', b[p:p + 18]); p += 18
        cl = []
        for k in range(nc):
            if big: cid, ln = struct.unpack('>hQ', b[p:p + 10]); p += 10
            else: cid, ln = struct.unpack('>hI', b[p:p + 6]); p += 6
            cl.append((cid, ln))
        p += 4 + 4                                   # 8BIM, blend key
        op, clip, flags, _f = struct.unpack('>BBBB', b[p:p + 4]); p += 4
        el = struct.unpack('>I', b[p:p + 4])[0]; ex = b[p + 4:p + 4 + el]; p += 4 + el
        q = 0; ml = struct.unpack('>I', ex[q:q + 4])[0]; q += 4 + ml
        bl = struct.unpack('>I', ex[q:q + 4])[0]; q += 4 + bl
        nl = ex[q]; name = ex[q + 1:q + 1 + nl].decode('latin1')
        j = ex.find(b'luni')
        if j >= 0:
            un = struct.unpack('>I', ex[j + 8:j + 12])[0]; name = ex[j + 12:j + 12 + 2 * un].decode('utf-16-be')
        recs.append([name, t, l, bo, r, cl, op, not (flags & 2)])
    out = []
    for name, t, l, bo, r, cl, op, vis in recs:
        h, w = bo - t, r - l; chans = {}
        for cid, ln in cl:
            d = b[p:p + ln]; p += ln
            comp = struct.unpack('>H', d[:2])[0]
            if h <= 0 or w <= 0: continue
            if cid < -1: continue                       # user mask etc.
            if comp == 0: plane = np.frombuffer(d[2:2 + h * w], np.uint8).reshape(h, w)
            elif comp == 1:
                cnts = struct.unpack('>%dH' % h, d[2:2 + 2 * h]) if not big else struct.unpack('>%dI' % h, d[2:2 + 4 * h])
                q = 2 + (2 if not big else 4) * h; rows = []
                for c in cnts: rows.append(np.frombuffer(unpackbits(d[q:q + c], w), np.uint8)); q += c
                plane = np.stack(rows)
            else: raise SystemExit('zip-compressed channel not supported')
            chans[cid] = plane
        out.append((name, t, l, bo, r, chans, op, vis))
    return W, H, out
