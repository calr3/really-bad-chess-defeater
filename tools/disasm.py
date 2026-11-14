"""Annotated x86_64 disassembly of IL2CPP methods.

usage: disasm.py META SO OUT PATTERN [PATTERN...]
PATTERN matches 'Type.Method' names (substring, or exact with '=' prefix).
"""
import sys
import os
import struct
import bisect
import pickle
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from meta import Metadata
from binary import Binary
import capstone
from capstone import x86_const as X

meta_path, so_path, out_path = sys.argv[1:4]
patterns = sys.argv[4:]
m = Metadata(meta_path)
b = Binary(so_path, m)
img = m.image_of_type()

# ---- method names --------------------------------------------------------
names = {}
method_name = {}


def mdef_name(mi):
    md = m.methods[mi]
    return b.typedef_fullname(md.declaringType) + '.' + m.str(md.nameIndex)


for ti, t in enumerate(m.typeDefs):
    for i in range(t.method_count):
        mi = t.methodStart + i
        md = m.methods[mi]
        a = b.method_ptr(img[ti], md.token)
        nm = mdef_name(mi)
        method_name[mi] = nm
        if a:
            names.setdefault(a, nm)

# generic method instances
mreg = b.mreg
gmt_count, gmt = b.ptr(mreg + 32), b.ptr(mreg + 40)
ms_count, ms = b.ptr(mreg + 64), b.ptr(mreg + 72)
gi_count, gi = b.ptr(mreg + 16), b.ptr(mreg + 24)


def inst_args(idx):
    p = b.ptr(gi + 8 * idx)
    argc = b.ptr(p)
    argv = b.ptr(p + 8)
    return [b.type_name(b.ptr(argv + 8 * i)) for i in range(argc)]


def methodspec_name(si):
    mdi, cls_i, meth_i = struct.unpack_from('<3i', b.data, b.off(ms + 12 * si))
    md = m.methods[mdi]
    tn = b.typedef_fullname(md.declaringType).split('`')[0]
    if cls_i >= 0:
        tn += '<' + ', '.join(inst_args(cls_i)) + '>'
    mn = m.str(md.nameIndex)
    if meth_i >= 0:
        mn += '<' + ', '.join(inst_args(meth_i)) + '>'
    return tn + '.' + mn


# CodeRegistration: locate via codeGenModules array
def is_module(v):
    try:
        return b.cstr(b.ptr(v)).endswith('.dll')
    except Exception:
        return False
acs_mod = next(a for a, v in b.reloc.items()
               if (a + 16) in b.reloc and b.cstr(v) == 'Assembly-CSharp.dll'
               if b.off(v) is not None)
slot = next(a for a, v in b.reloc.items() if v == acs_mod)
p = slot
while (p - 8) in b.reloc and is_module(b.reloc[p - 8]):
    p -= 8
arr_start = p
creg = next(a for a, v in b.reloc.items() if v == arr_start and b.ptr(a - 8) == len(b.modules)) - 128
gmp_count, gmp = b.ptr(creg + 16), b.ptr(creg + 24)
print('codeReg', hex(creg), 'genericMethodPointers', gmp_count, 'table', gmt_count, file=sys.stderr)
for i in range(gmt_count):
    gidx, midx, inv, adj = struct.unpack_from('<4i', b.data, b.off(gmt + 16 * i))
    if 0 <= midx < gmp_count:
        a = b.ptr(gmp + 8 * midx)
        if a and a not in names:
            try:
                names[a] = methodspec_name(gidx)
            except Exception:
                pass

for sym in b.elf.get_section_by_name('.dynsym').iter_symbols():
    if sym['st_value'] and sym['st_info']['type'] == 'STT_FUNC':
        names.setdefault(sym['st_value'], sym.name)

starts = sorted(names)
text_end = 0x15865e0 + 0x1970986


def func_end(a):
    i = bisect.bisect_right(starts, a)
    return starts[i] if i < len(starts) else a + 0x1000


# ---- metadata usage decoding -------------------------------------------------
USAGE = {1: 'TypeInfo', 2: 'Il2CppType', 3: 'MethodDef', 4: 'FieldInfo', 5: 'String', 6: 'MethodRef'}
fieldrefs = m._tuples('fieldRefs', '<2i')


def usage(va):
    o = b.off(va)
    if o is None or va in b.reloc:
        return None
    try:
        v = struct.unpack_from('<Q', b.data, o)[0]
    except struct.error:
        return None
    if not v & 1 or v >> 32:
        return None
    kind = (v >> 29) & 7
    idx = (v & 0x1FFFFFFF) >> 1
    try:
        if kind == 1 or kind == 2:
            return f'{USAGE[kind]}<{b.type_name(b.type_at(idx))}>'
        if kind == 3:
            return f'MethodDef<{method_name.get(idx, idx)}>'
        if kind == 4:
            ti, fi = fieldrefs[idx]
            tva = b.type_at(ti)
            tdi = b.ptr(tva)
            fname = m.str(m.fields[m.typeDefs[tdi].fieldStart + fi][0])
            return f'FieldInfo<{b.type_name(tva)}.{fname}>'
        if kind == 5:
            return 'String<' + repr(m.literal(idx)) + '>'
        if kind == 6:
            return f'MethodRef<{methodspec_name(idx)}>'
    except Exception as e:
        return None
    return None


md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True


def disasm(a, end):
    code = b.data[b.off(a):b.off(end)]
    lines = []
    for ins in md.disasm(code, a):
        note = ''
        if ins.mnemonic in ('call', 'jmp') and ins.operands and ins.operands[0].type == X.X86_OP_IMM:
            tgt = ins.operands[0].imm
            if tgt in names and not (a <= tgt < end):
                note = names[tgt]
            elif not (a <= tgt < end):
                note = f'sub_{tgt:x}'
        for op in ins.operands:
            if op.type == X.X86_OP_MEM and op.mem.base == X.X86_REG_RIP:
                tgt = ins.address + ins.size + op.mem.disp
                u = usage(tgt)
                if u:
                    note = u
                elif tgt in b.reloc:
                    note = names.get(b.reloc[tgt], f'ptr->{b.reloc[tgt]:x}')
                else:
                    note = note or f'[{tgt:x}]'
                    if ins.mnemonic.startswith(('movss', 'movsd', 'cvt', 'mulss', 'addss', 'divss', 'subss', 'ucomiss', 'comiss')):
                        o = b.off(tgt)
                        if o is not None:
                            note += f' = {struct.unpack_from("<f", b.data, o)[0]!r}/{struct.unpack_from("<d", b.data, o)[0]!r}'
        lines.append(f'  {ins.address:08x}  {ins.mnemonic:7s} {ins.op_str}' + (f'    ; {note}' if note else ''))
        if ins.mnemonic in ('ret',) and ins.address + ins.size >= end:
            break
    return lines


with open(out_path, 'w') as out:
    for a in starts:
        nm = names[a]
        if not any((p[1:] == nm) if p.startswith('=') else (p in nm) for p in patterns):
            continue
        end = func_end(a)
        out.write(f'\n==== {nm} @ {a:x} (size {end - a:#x})\n')
        out.write('\n'.join(disasm(a, end)) + '\n')

with open(os.path.join(os.path.dirname(out_path), 'names.pkl'), 'wb') as f:
    pickle.dump(names, f)
