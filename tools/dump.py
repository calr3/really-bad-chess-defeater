"""Write a C#-like declaration dump (fields with offsets, methods with addresses)."""
import sys
import struct
import os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from meta import Metadata
from binary import Binary

meta_path, so_path, out_path = sys.argv[1:4]
images = sys.argv[4].split(',') if len(sys.argv) > 4 else ['Assembly-CSharp.dll']
m = Metadata(meta_path)
b = Binary(so_path, m)
img = m.image_of_type()
print('modules found:', len(b.modules), 'Assembly-CSharp' in str(b.modules.keys()), file=sys.stderr)

FIELD_ACCESS = {1: 'private', 2: 'private protected', 3: 'internal', 4: 'protected',
                5: 'protected internal', 6: 'public'}


def default_value(fi, tname):
    if fi not in m.fieldDefaults:
        return None
    _t, di = m.fieldDefaults[fi]
    if di < 0:
        return None
    o = m.default_blob(di)
    d = m.d
    fmt = {'bool': '<?', 'byte': '<B', 'sbyte': '<b', 'short': '<h', 'ushort': '<H',
           'char': '<H', 'float': '<f', 'double': '<d', 'long': '<q', 'ulong': '<Q'}.get(tname)
    if tname in ('int', 'uint'):
        # v29+: compressed int encoding
        return str(read_compressed(d, o, tname == 'int'))
    if fmt:
        return repr(struct.unpack_from(fmt, d, o)[0])
    if tname == 'string':
        ln, o2 = read_compressed_raw(d, o)
        if ln == 0xFFFFFFFF:
            return 'null'
        return repr(d[o2:o2 + ln].decode('utf8', 'replace'))
    # enums etc.: show raw compressed int
    return f'(raw 0x{d[o]:02x})'


def read_compressed_raw(d, o):
    b0 = d[o]
    if b0 & 0x80 == 0:
        return b0, o + 1
    if b0 & 0xC0 == 0x80:
        return ((b0 & 0x3F) << 8) | d[o + 1], o + 2
    if b0 & 0xE0 == 0xC0:
        return ((b0 & 0x1F) << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3], o + 4
    if b0 == 0xF0:
        return struct.unpack_from('<I', d, o + 1)[0], o + 5
    if b0 == 0xFE:
        return 0xFFFFFFFE, o + 1
    if b0 == 0xFF:
        return 0xFFFFFFFF, o + 1
    raise ValueError(hex(b0))


def read_compressed(d, o, signed):
    v, _ = read_compressed_raw(d, o)
    if not signed:
        return v
    if v == 0xFFFFFFFF:
        return -2147483648
    return -(v >> 1) - 1 if v & 1 else v >> 1


with open(out_path, 'w') as out:
    for ti, t in enumerate(m.typeDefs):
        if img.get(ti) not in images:
            continue
        name = b.typedef_fullname(ti)
        parent = b.type_name(b.type_at(t.parentIndex)) if t.parentIndex >= 0 else ''
        kind = 'enum' if (t.bitfield >> 1) & 1 else 'struct' if t.bitfield & 1 else \
            'interface' if t.flags & 0x20 else 'class'
        out.write(f'\n// TypeDefIndex {ti}\n{kind} {name}' + (f' : {parent}' if parent else '') + '\n{\n')
        for i in range(t.field_count):
            fi = t.fieldStart + i
            fname, ftype, ftok = m.fields[fi]
            tva = b.type_at(ftype)
            tn = b.type_name(tva)
            attrs = b.u32(tva + 8) & 0xffff
            static = 'static ' if attrs & 0x10 else ''
            const = 'const ' if attrs & 0x40 else ''
            fo = b.field_offset(ti, i)
            under = tn
            kind_ = (b.u32(tva + 8) >> 16) & 0xff
            if kind_ == 0x11:
                etd = m.typeDefs[b.ptr(tva)]
                if (etd.bitfield >> 1) & 1:
                    under = b.type_name(b.type_at(etd.elementTypeIndex))
            dv = default_value(fi, under)
            out.write(f'    {FIELD_ACCESS.get(attrs & 7, "")} {static}{const}{tn} {m.str(fname)}'
                      + (f' = {dv}' if dv is not None else '') + ';'
                      + (f' // 0x{fo:x}' if fo is not None and not const else '') + '\n')
        for i in range(t.method_count):
            md = m.methods[t.methodStart + i]
            ret = b.type_name(b.type_at(md.returnType))
            ps = []
            for p in range(md.parameterCount):
                pn, ptok, pt = m.params[md.parameterStart + p]
                ps.append(f'{b.type_name(b.type_at(pt))} {m.str(pn)}')
            addr = b.method_ptr(img[ti], md.token)
            static = 'static ' if md.flags & 0x10 else ''
            virt = 'virtual ' if md.flags & 0x40 else ''
            out.write(f'    // RVA 0x{addr:x}\n    {static}{virt}{ret} {m.str(md.nameIndex)}({", ".join(ps)});\n')
        out.write('}\n')
