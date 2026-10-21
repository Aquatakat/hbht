"""A view of the combat struct and the grid on the interpreter's RAM (Sim), of a combatant record (Cmb), and the hooks that let a Python
function stand in for a MIPS one."""
import struct, math
import numpy as np
from cpu import *
from rig import fresh

RNG = 0x800d8088                  # the main random stream
A_SET = 0x4e64                    # the combat struct's target sets, A and B
B_SET = 0x4f0c

def s8(v): v&=0xff; return v-256 if v&0x80 else v
def u8(v): return v&0xff
def s16(v): v&=0xffff; return v-65536 if v&0x8000 else v
def f32(v): return float(np.float32(v))
DX=[0,1,1,1,0,-1,-1,-1]; DZ=[-1,-1,0,1,1,1,0,-1]

class Sim:
    def __init__(s,cpu):
        s.c=cpu
        s.m=cpu.rd32(0x800e0bf0)
        s.g=s.m+0x3c
    # --- memory helpers
    def rd8(s,a): return s.c.rd8(a)
    def rd16(s,a): return s.c.rd16(a)
    def rd32(s,a): return s.c.rd32(a)
    def wr8(s,a,v): s.c.wr8(a,v)
    def fl(s,a): return struct.unpack('>f',struct.pack('>I',s.c.rd32(a)))[0]
    def setfl(s,a,v): s.c.wr32(a,struct.unpack('>I',struct.pack('>f',v))[0])
    def call(s,addr,args=(),**kw): return s.c.run(addr,list(args),**kw)
    # --- grid
    def width(s): return s.rd8(s.g+0x4e22)
    def depth(s): return s.rd8(s.g+0x4e23)
    def diag(s): return s.rd8(s.g+0x4e26)
    def cell(s,x,z): return s.rd8(s.g+u8(x)*100+u8(z))
    def setcell(s,x,z,v): s.wr8(s.g+u8(x)*100+u8(z),v)
    def mark(s,x,z): return s.rd8(s.g+10000+s8(x)*100+s8(z))
    def setmark(s,x,z,v): s.wr8(s.g+10000+s8(x)*100+s8(z),v)
    def is_outside(s,x,z,fp):
        x=s8(x); z=s8(z); fp&=0xff
        for zz in range(z,z+fp):
            for xx in range(x,x+fp):
                if xx<0 or zz<0 or xx>=s.width() or zz>=s.depth(): return True
        return False
    def is_occupied(s,x,z,fp):
        x=u8(x); z=u8(z); fp&=0xff
        zz=z
        while zz<z+fp:
            xx=x
            while xx<x+fp:
                if not s.is_outside(xx,zz,1) and s.cell(xx,zz)&0x80: return True
                xx=(xx+1)&0xff
                if xx==0 and x+fp>256: break
            zz=(zz+1)&0xff
            if zz==0 and z+fp>256: break
        return False
    def is_free(s,x,z,fp): return (not s.is_outside(x,z,fp)) and (not s.is_occupied(x,z,fp))
    def hdiff(s,x0,z0,x1,z1):
        if s.is_outside(x0,z0,1) or s.is_outside(x1,z1,1): return 0x7f
        return s8((s.cell(x1,z1)&0x7f)-(s.cell(x0,z0)&0x7f))
    def is_gentle(s,x0,z0,x1,z1): return s.hdiff(x0,z0,x1,z1)<3
    def occupy(s,x,z,fp,on=True):
        fp&=0xff
        if s.is_outside(x,z,fp): return
        for zz in range(u8(z),u8(z)+fp):
            for xx in range(u8(x),u8(x)+fp):
                v=s.cell(xx,zz); s.setcell(xx,zz,(v|0x80) if on else (v&0x7f))
    def vacate(s,x,z,fp): s.occupy(x,z,fp,False)
    @staticmethod
    def dist(x0,z0,x1,z1):
        a=s16((x0&0xff)-(x1&0xff)); b=s16((z0&0xff)-(z1&0xff))
        d2=s16(a*a+b*b)
        f=float(np.sqrt(np.float32(d2)))
        if f>=2147483648.0: f-=2147483648.0
        return int(f)&0xffff
    @staticmethod
    def distsq(x0,z0,x1,z1):
        a=s16((x0&0xff)-(x1&0xff)); b=s16((z0&0xff)-(z1&0xff))
        return s16(a*a+b*b)&0xffff
    @staticmethod
    def direction_to(x,z,tx,tz):
        x&=0xff;z&=0xff;tx&=0xff;tz&=0xff
        if x<tx: return 3 if tz>z else (2 if tz==z else 1)
        if tx<x: return 5 if tz>z else (6 if tz==z else 7)
        return 4 if tz>z else 0
    def step_dir(s,d,x,z): return (s8(x+DX[d&0xff]), s8(z+DZ[d&0xff]))
    def neighbours(s,x,z,gx,gz,fp):
        buf=[];none=True
        for d in range(8):
            nx,nz=s8(x+DX[d]),s8(z+DZ[d])
            e=[0xffff,0]
            if s.is_free(nx,nz,fp) and s.mark(nx,nz)==0 and s.is_gentle(x,z,nx,nz):
                none=False; e=[s.distsq(nx,nz,gx&0xff,gz&0xff),d]
            buf.append(e)
        return none,buf
    @staticmethod
    def best(buf):
        b=0;v=buf[0][0]
        for i in range(1,8):
            if buf[i][0]<v: v=buf[i][0]; b=i
        return b
    def find_path(s,sx,sz,gx,gz,fp):
        """ArenaGrid.FindPath: returns (len, path). Needs RefillWithBodies afterwards (done by caller via call)"""
        W=s.width()
        for xx in range(W):
            for zz in range(s.depth()): s.setmark(xx,zz,0)   # memset(rows..., depth)
        s.vacate(sx,sz,fp); s.vacate(gx,gz,fp)
        cx,cz=s8(sx),s8(sz); ln=0; path=[]
        gx,gz=u8(gx),u8(gz)
        while True:
            if (cx&0xff)==gx and (cz&0xff)==gz: break
            if ln>=s.diag(): break
            none,buf=s.neighbours(cx,cz,gx,gz,fp)
            if not none:
                b=s.best(buf); d=buf[b][1]
                cx,cz=s.step_dir(d,cx,cz)
                if ln<len(path): path[ln]=(cx&0xff,cz&0xff)
                else: path.append((cx&0xff,cz&0xff))
                ln+=1
                s.setmark(cx,cz,1)
            elif ln==0: ln=s.diag()
            else: ln-=1
        return ln,path

# ---- hooks: let a Python function stand in for a MIPS function (at its entry)
_orig_step = CPU.step
def _step(s):
    h = getattr(s, 'hooks', None)
    if h and s.pc in h:
        r = h[s.pc](s)
        s.setr(2, r if r is not None else 0)
        s.pc = s.getr(31) & M32
        s.steps += 1
        return
    _orig_step(s)
CPU.step = _step

F=np.float32
def vnorm(x,z):
    """Vec2.Normalize 0x800a11ec: len = max(sqrtf(x*x+z*z), 1e-6); (x/len, z/len)"""
    x=F(x); z=F(z)
    l=np.sqrt(F(x*x)+F(z*z)).astype(F)
    if l<F(9.999999974752427e-07): l=F(9.999999974752427e-07)
    return F(x/l), F(z/l)

def cell_of(v):
    v=float(F(v))
    if v<=0.0: return (-int(0.5-v))&0xff
    return int(v+0.5)&0xff

BODIES=0x800d9488
class Cmb:
    """view of a combatant record"""
    def __init__(s,S,p): s.S=S; s.p=p
    @property
    def x(s): return s.S.fl(s.p)
    @property
    def z(s): return s.S.fl(s.p+4)
    @property
    def ox(s): return s.S.fl(s.p+8)
    @property
    def oz(s): return s.S.fl(s.p+12)
    def cell(s): return cell_of(s.x),cell_of(s.z)
    def oldcell(s): return cell_of(s.ox),cell_of(s.oz)
    radius=property(lambda s:s.S.rd8(s.p+0x21))
    fp=property(lambda s:s.S.rd8(s.p+0x23))
    slot=property(lambda s:s.S.rd8(s.p+0x20))
    flags=property(lambda s:s.S.rd16(s.p+0x2c))
    ai=property(lambda s:s.S.rd32(s.p+0x44))
    ent=property(lambda s:s.S.rd32(s.p+0x40))
    def setpos(s,x,z): s.S.setfl(s.p,float(x)); s.S.setfl(s.p+4,float(z))
    def setflag(s,b,on=True):
        v=s.flags; s.S.c.wr16(s.p+0x2c,(v|b) if on else (v&~b))
    def face_dir(s,d):
        x,z=vnorm(DX[d&0x7f if False else d],DZ[d])
        s.S.setfl(s.p+0x18,float(x)); s.S.setfl(s.p+0x1c,float(z))
    def body(s): return s.S.rd32(BODIES+4*s.slot)


def tset_rebuild(S, which, cp): S.c.run(0x80069264, [S.m + which, cp])     # TargetSet.Rebuild(set, combatant)
