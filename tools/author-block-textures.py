#!/usr/bin/env python3
"""Cuts the vanilla block textures (mods/vanilla/textures/blocks/*.png) from the artist's source pictures.

Usage: tools/author-block-textures.py SOURCE_DIR [OUT_DIR]   (default OUT_DIR: mods/vanilla/textures/blocks)
SOURCE_DIR holds one <material>.jpg per block material (aluminum.jpg, wood.jpg, ...). Needs Python 3 with numpy
and Pillow. Deterministic: the same sources give the same pixels.

One texture covers one 0.25 m block face and repeats on the next block, so each picture is cut to a whole number
of its pattern repeats (measured by autocorrelation), resized to 512 px and blended across its edges so it tiles.
See-through materials get alpha (glass, armored glass); the net is drawn as a cut-out lattice because its source
is not regular enough to tile; scrap metal is drawn as framed plates; the path light gets a glow map of its lit strips.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

N = 512

# How each source is cut. px/py: approximate pattern repeat in source pixels, nx/ny: repeats per block face.
# size/w/h: a plain square (or w x h) crop for patternless pictures. feather (or fx/fy): fraction of the edge
# blended so the cut tiles; 0 leaves that axis as it is. at: crop corner.
CUTS = {
    "aluminum": {"px": 194.5, "py": 195, "nx": 2, "ny": 2},
    "armored_glass": {"w": 1168, "h": 784, "fx": 0, "fy": 0},  # the whole picture already tiles
    "barrier": {"px": 173, "py": 159, "nx": 2, "ny": 2},  # top rows: the bottom of the source is smeared
    "brick": {"px": 194.5, "py": 130, "nx": 2, "ny": 3},
    "cardboard": {"px": 390, "py": 390, "nx": 1, "ny": 1},
    "carpet": {"size": 400, "feather": 0.3},
    "concrete": {"size": 512, "feather": 0.3},
    "diamond_plate": {"px": 131, "py": 124, "nx": 3, "ny": 3},
    "glass": {"px": 196, "py": 196, "nx": 2, "ny": 2},
    "hull_plate": {"w": 579, "h": 579, "fx": 0.08, "fy": 0.15},
    "insulation": {"size": 392, "feather": 0.3},
    "metal": {"px": 434, "py": 390, "nx": 1, "ny": 1},
    "path_light": {"px": 195, "py": 196, "nx": 2, "ny": 2},
    "plaster": {"size": 512, "feather": 0.3},
    "rusted_metal": {"size": 512, "feather": 0.3},
    "scrap_wood": {"px": 293, "py": 336, "nx": 1, "ny": 1},
    "tile": {"px": 83.4, "py": 78.5, "nx": 4, "ny": 4},
    "wood": {"px": 581, "py": 336, "nx": 1, "ny": 1},
}


def autocorrelation(gray):
    a = gray - gray.mean()
    f = np.fft.fft2(a)
    ac = np.real(np.fft.ifft2(f * np.conj(f)))
    return ac / ac[0, 0]


def refine(profile, period, limit):
    """The repeat measured precisely: the autocorrelation peak near the largest multiple that fits, divided back."""
    n = max(1, int(limit // period))
    centre = int(round(n * period))
    width = max(3, n * 2)
    i = int(np.argmax(profile[centre - width:centre + width + 1])) + centre - width
    y0, y1, y2 = profile[i - 1], profile[i], profile[i + 1]
    curve = y0 - 2 * y1 + y2
    return (i + (0.5 * (y0 - y2) / curve if curve else 0)) / n


def smoothstep(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def seamless(a, fx, fy):
    """Blends the picture with itself shifted by half, weighted towards the shifted copy at the edges, so it wraps."""
    s = a.shape[0]
    def ramp(f):
        if f == 0:
            return np.ones(s)
        return smoothstep(np.minimum(np.arange(s), s - 1 - np.arange(s)) / (s * f), 0, 1)
    shifted = np.roll(a, (s // 2 if fy else 0, s // 2 if fx else 0), axis=(0, 1))
    w = np.minimum(ramp(fy)[:, None], ramp(fx)[None, :])[..., None]
    return a * w + shifted * (1 - w)


def cut(source, c):
    im = Image.open(source).convert("RGB")
    if "px" in c:
        ac = autocorrelation(np.asarray(im.convert("L"), np.float64))
        w = refine(ac[0, :], c["px"], im.width / 2) * c["nx"]
        h = refine(ac[:, 0], c["py"], im.height / 2) * c["ny"]
    else:
        w, h = c.get("w", c.get("size")), c.get("h", c.get("size"))
    x, y = c.get("at", (0, 0))
    a = np.asarray(im.resize((N, N), Image.LANCZOS, box=(x, y, x + w, y + h)), np.float64)
    feather = c.get("feather", 0.15)
    return seamless(a, c.get("fx", feather), c.get("fy", feather))


def save(path, rgb, alpha=None):
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    if alpha is None:
        Image.fromarray(rgb, "RGB").save(path, optimize=True)
    else:
        a = np.clip(alpha * 255, 0, 255).astype(np.uint8)
        Image.fromarray(np.dstack([rgb, a]), "RGBA").save(path, optimize=True)


def value_noise(cells, seed, octaves=4):
    """Smooth noise in 0..1: bicubic-upscaled random grids, each octave twice as fine and half as strong."""
    r = np.random.default_rng(seed)
    out, amp, total = np.zeros((N, N)), 1.0, 0.0
    for o in range(octaves):
        c = cells * 2 ** o
        g = Image.fromarray((r.random((c, c)) * 255).astype(np.uint8)).resize((N, N), Image.BICUBIC)
        out += amp * np.asarray(g, np.float64) / 255
        total += amp
        amp *= 0.5
    return out / total


def scrap_metal():
    """Orange painted plates (one wide, one tall, two small) bolted into a gunmetal frame, worn to steel at the
    edges, with rust, scratches and dents. Drawn rather than cut so the frame closes on every block face."""
    ss = smoothstep
    noise = value_noise
    y, x = np.mgrid[0:N, 0:N] + 0.5
    F=18  # frame width
    G=3   # seam half-gap
    # plates (x0,y0,x1,y1) inside the frame, like the reference: big top-left, tall right, two bottom-left
    m=F
    plates=[(m,m,318,262),(318,m,N-m,N-m),(m,262,150,N-m),(150,262,318,N-m)]
    paint=np.zeros((N,N,3)); height=np.zeros((N,N)); inside=np.zeros((N,N)); steelmask=np.zeros((N,N))
    n_chip=noise(10,1,5); n_chip2=noise(40,2,3); n_dirt=noise(4,3,4); n_rust=noise(14,4,5)
    base_cols=[np.array([172,74,34]),np.array([184,84,38]),np.array([164,68,32]),np.array([178,80,36])]
    for i,(x0,y0,x1,y1) in enumerate(plates):
        a0,b0,a1,b1=x0+G,y0+G,x1-G,y1-G
        d=np.minimum.reduce([x-a0,a1-x,y-b0,b1-y])  # distance to plate edge
        msk=(d>0).astype(float)
        # brushed streaks along the plate's long side
        horiz=(a1-a0)>=(b1-b0)
        r=np.random.default_rng(20+i)
        line=r.random(N*2)
        streak=np.asarray(Image.fromarray((line[:N]*255).astype(np.uint8)[None,:].repeat(N,0)).filter(ImageFilter.GaussianBlur(0.7)),np.float64)/255
        streak=streak.T if horiz else streak
        col=base_cols[i]*(0.94+0.12*r.random())
        p=col[None,None,:]*(0.95+0.07*streak[...,None])*(0.8+0.35*n_dirt[...,None])
        # bevel: raised plate, lit from top-left
        bev=ss(d,0,7)
        lightx=np.where(x-a0<a1-x,1,-1); lighty=np.where(y-b0<b1-y,1,-1)
        edge_light=np.where(np.minimum(x-a0,y-b0)<np.minimum(a1-x,b1-y),1.25,0.7)
        shade=bev+(1-bev)*edge_light
        # worn edges: chips where paint is gone showing steel, more near edges
        wear=ss(n_chip*0.6+n_chip2*0.4 + 0.28*(1-ss(d,0,14)) ,0.76,0.79)
        steel=np.array([112,110,108])*(0.8+0.4*streak[...,None]) * (0.85+0.3*n_chip2[...,None])
        rust=ss(n_rust+0.18*(1-ss(d,0,30)),0.70,0.90)
        rustcol=np.array([92,44,24])*(0.8+0.4*n_chip2[...,None])
        q=p*(1-wear[...,None])+steel*wear[...,None]
        q=q*(1-0.85*rust[...,None])+rustcol*0.85*rust[...,None]
        grime=1-0.35*(1-ss(d,0,22))*(0.6+0.4*n_chip2)
        sheen=1+0.12*np.exp(-(((x-a0)/(a1-a0)-0.35)**2+((y-b0)/(b1-b0)-0.3)**2)/0.08)
        q=q*(shade*grime*sheen)[...,None]
        paint=paint*(1-msk[...,None])+q*msk[...,None]
        inside=np.maximum(inside,msk)
        # bolts at plate corners
        for cx,cy in [(a0+13,b0+13),(a1-13,b0+13),(a0+13,b1-13),(a1-13,b1-13)]:
            rr=np.hypot(x-cx,y-cy)
            bolt=ss(7.5-rr,0,1.2)
            lit=np.clip(1.05-((x-cx)+(y-cy))/12,0.5,1.5)
            boltc=np.array([120,118,116])*lit[...,None]
            ring=ss(9.5-rr,0,1)*(1-bolt)*0.5
            paint=paint*(1-ring[...,None])
            paint=paint*(1-bolt[...,None])+boltc*bolt[...,None]
    # frame: gunmetal steel with bevel
    fd=np.minimum.reduce([x,N-x,y,N-y])
    frame=(fd<F).astype(float)
    nf=noise(16,7,4); gun=np.array([80,80,82])*(0.8+0.35*nf[...,None]); fr=ss(noise(12,8,4),0.6,0.8); gun=gun*(1-0.5*fr[...,None])+np.array([105,60,36])*0.5*fr[...,None]
    fl=np.where(np.minimum(x,y)<np.minimum(N-x,N-y),1.25,0.75)
    fb=ss(fd,0,4); fb2=ss(F-fd,0,3)
    fshade=(fb*fb2+(1-fb*fb2)*fl)
    gunc=gun*fshade[...,None]
    seam=np.array([28,26,26])
    out=np.where(frame[...,None]>0,gunc,paint*inside[...,None]+seam*(1-inside[...,None]))
    # grime in the seams
    out=out*(0.92+0.08*noise(8,9,3)[...,None])

    # scratches: thin bright lines through the paint, showing steel
    sc=Image.new('L',(N,N),0); dr=ImageDraw.Draw(sc); r=np.random.default_rng(31)
    for k in range(22):
        x0,y0=r.uniform(20,N-20,2); ang=r.uniform(0,np.pi); L=r.uniform(15,70)
        pts=[(x0+t*L*np.cos(ang)+r.normal(0,0.6),y0+t*L*np.sin(ang)+r.normal(0,0.6)) for t in np.linspace(0,1,8)]
        dr.line(pts,fill=int(r.uniform(60,150)),width=1)
    scr=np.asarray(sc.filter(ImageFilter.GaussianBlur(0.5)),np.float64)/255*inside
    out=out*(1-scr[...,None])+np.array([128,122,116])*scr[...,None]
    # dents: soft dark blotch with a lit rim
    for k in range(5):
        cx,cy=r.uniform(60,N-60,2); rad=r.uniform(14,30)
        rr=np.hypot(x-cx,y-cy)/rad
        dent=np.exp(-rr**2*2)*inside
        rim=np.exp(-((rr-0.9)**2)*18)*inside*np.clip(((cx-x)+(cy-y))/rad,0,1)
        out=out*(1-0.22*dent[...,None])*(1+0.18*rim[...,None])
    return out


def net():
    """Rope netting: two sets of diagonal strands, woven over and under, with clear gaps (alpha 0)."""
    period = N / 6
    half = 7.5
    y, x = np.mgrid[0:N, 0:N] + 0.5
    def distance(u):
        return np.abs(((u + period / 2) % period) - period / 2) / np.sqrt(2)
    def strand(d):
        return np.clip(half + 0.5 - d, 0, 1), np.sqrt(np.clip(1 - (d / half) ** 2, 0, 1))
    a1, h1 = strand(distance(x + y))
    a2, h2 = strand(distance(x - y))
    first_on_top = (np.floor((x + y) / period) + np.floor((x - y) / period)) % 2 == 0
    shade = np.where(a1 * a2 > 0, np.where(first_on_top, h1, h2), np.maximum(h1 * a1, h2 * a2))
    rng = np.random.default_rng(7)
    grain = np.asarray(Image.fromarray((rng.random((N // 4, N // 4)) * 255).astype(np.uint8)).resize((N, N), Image.BICUBIC), np.float64) / 255
    twist = 0.5 + 0.5 * np.sin(np.where(a1 >= a2, x - y, x + y) * 0.55)
    base = 55 + 60 * shade + 18 * twist * shade + 20 * (grain - 0.5)
    return np.dstack([base * 0.98, base * 0.97, base * 0.92]), np.maximum(a1, a2)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    src = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mods", "vanilla", "textures", "blocks")
    os.makedirs(out, exist_ok=True)
    for name, c in CUTS.items():
        a = cut(os.path.join(src, f"{name}.jpg"), c)
        lum = a.mean(axis=2)
        if name == "armored_glass":
            # Frame solid, panes see-through (multiplied by the part's opacity).
            save(os.path.join(out, f"{name}.png"), a, 0.45 + 0.55 * (1 - smoothstep(lum, 160, 190)))
        elif name == "glass":
            save(os.path.join(out, f"{name}.png"), a, 0.75 + 0.25 * smoothstep(lum, 170, 235))
        else:
            save(os.path.join(out, f"{name}.png"), a)
        if name == "path_light":
            # Glow map: the yellow strips (red and green well above blue).
            strips = smoothstep((a[..., 0] + a[..., 1]) / 2 - a[..., 2], 60, 200) * 255
            save(os.path.join(out, "path_light_glow.png"), np.dstack([strips] * 3))
        print(name)
    save(os.path.join(out, "scrap_metal.png"), scrap_metal())
    print("scrap_metal")
    save(os.path.join(out, "net.png"), *net())
    print("net")


if __name__ == "__main__":
    main()
