"""Minimal reader for an x86_64 libil2cpp.so (metadata v31)."""
import struct
import bisect
from elftools.elf.elffile import ELFFile
from elftools.elf.relocation import RelocationSection

PRIM = {1: 'void', 2: 'bool', 3: 'char', 4: 'sbyte', 5: 'byte', 6: 'short', 7: 'ushort',
        8: 'int', 9: 'uint', 0xa: 'long', 0xb: 'ulong', 0xc: 'float', 0xd: 'double',
        0xe: 'string', 0x16: 'TypedReference', 0x18: 'IntPtr', 0x19: 'UIntPtr', 0x1c: 'object'}


class Binary:
    def __init__(self, path, meta):
        self.meta = meta
        self.f = open(path, 'rb')
        self.data = self.f.read()
        elf = self.elf = ELFFile(open(path, 'rb'))
        self.segs = [(s['p_vaddr'], s['p_filesz'], s['p_offset']) for s in elf.iter_segments()
                     if s['p_type'] == 'PT_LOAD']
        self.reloc = {}
        dynsym = elf.get_section_by_name('.dynsym')
        for sec in elf.iter_sections():
            if isinstance(sec, RelocationSection):
                for r in sec.iter_relocations():
                    t = r['r_info_type']
                    if t == 8:   # R_X86_64_RELATIVE
                        self.reloc[r['r_offset']] = r['r_addend']
                    elif t in (1, 6) and dynsym is not None:   # R_X86_64_64 / GLOB_DAT
                        sym = dynsym.get_symbol(r['r_info_sym'])
                        self.reloc[r['r_offset']] = sym['st_value'] + r['r_addend']
        self._find_registrations()

    # --- address helpers -------------------------------------------------
    def off(self, va):
        for v, sz, o in self.segs:
            if v <= va < v + sz:
                return o + va - v
        return None

    def va(self, off):
        for v, sz, o in self.segs:
            if o <= off < o + sz:
                return v + off - o
        return None

    def ptr(self, va):
        if va in self.reloc:
            return self.reloc[va]
        o = self.off(va)
        if o is None:
            return 0
        return struct.unpack_from('<Q', self.data, o)[0]

    def u32(self, va):
        return struct.unpack_from('<I', self.data, self.off(va))[0]

    def i32(self, va):
        return struct.unpack_from('<i', self.data, self.off(va))[0]

    def cstr(self, va):
        o = self.off(va)
        return self.data[o:self.data.index(b'\0', o)].decode('utf8', 'replace')

    # --- registration discovery -------------------------------------------
    def _find_registrations(self):
        m = self.meta
        ntd = len(m.typeDefs)
        # MetadataRegistration: ... typesCount, types, methodSpecsCount, methodSpecs,
        # fieldOffsetsCount, fieldOffsets, typeDefinitionsSizesCount, typeDefinitionsSizes, ...
        rel_addrs = sorted(self.reloc)
        cand = []
        for a in rel_addrs:
            if a + 16 in self.reloc and a - 8 not in self.reloc and a + 8 not in self.reloc:
                try:
                    if self.ptr(a - 8) == ntd and self.ptr(a + 8) == ntd:
                        cand.append(a - 8)
                except Exception:
                    pass
        assert len(cand) == 1, cand
        fo = cand[0]                          # address of fieldOffsetsCount
        self.mreg = fo - 8 * 10
        self.fieldOffsetsPtr = self.ptr(fo + 8)
        self.typeSizesPtr = self.ptr(fo + 24)
        self.typesCount = self.ptr(fo - 32)
        self.typesPtr = self.ptr(fo - 24)
        self.genericInstsCount = self.ptr(self.mreg + 16)
        # Code gen modules: find module name strings
        self.modules = {}
        name_va = {}
        for img in m.images:
            nm = m.str(img[0])
            o = self.data.find(nm.encode() + b'\0')
            while o >= 0:
                if self.data[o - 1] == 0:
                    break
                o = self.data.find(nm.encode() + b'\0', o + 1)
            if o >= 0:
                name_va[self.va(o)] = nm
        for a, v in self.reloc.items():
            if v in name_va:
                cnt = self.ptr(a + 8)
                if 0 <= cnt < 1_000_000 and (a + 16) in self.reloc or cnt == 0:
                    self.modules[name_va[v]] = (cnt, self.ptr(a + 16) if cnt else 0)

    def method_ptr(self, image, token):
        cnt, arr = self.modules[image]
        rid = token & 0xFFFFFF
        if rid == 0 or rid > cnt:
            return 0
        return self.ptr(arr + 8 * (rid - 1))

    def field_offset(self, typeIndex, i):
        p = self.ptr(self.fieldOffsetsPtr + 8 * typeIndex)
        if p == 0:
            return None
        return self.i32(p + 4 * i)

    # --- Il2CppType ---------------------------------------------------------
    def type_at(self, typeIndex):
        return self.ptr(self.typesPtr + 8 * typeIndex)

    def type_name(self, tva, depth=0):
        m = self.meta
        data = self.ptr(tva)
        bits = self.u32(tva + 8)
        kind = (bits >> 16) & 0xff
        byref = (bits >> 29) & 1
        s = self._tn(kind, data, depth)
        return ('ref ' if byref else '') + s

    def _tn(self, kind, data, depth):
        m = self.meta
        if kind in PRIM:
            return PRIM[kind]
        if kind in (0x11, 0x12):
            return self.typedef_fullname(data)
        if kind == 0x1d:
            return self.type_name(data, depth + 1) + '[]'
        if kind == 0x14:   # ARRAY
            return self.type_name(self.ptr(data), depth + 1) + '[,]'
        if kind == 0xf:
            return self.type_name(data, depth + 1) + '*'
        if kind in (0x13, 0x1e):
            gp = m.genericParams[data]
            return m.str(gp[1])
        if kind == 0x15:
            gtype = self.ptr(data)
            cls_inst = self.ptr(data + 8)
            base = self.type_name(gtype, depth + 1).split('`')[0]
            argc = self.ptr(cls_inst)
            argv = self.ptr(cls_inst + 8)
            args = [self.type_name(self.ptr(argv + 8 * i), depth + 1) for i in range(argc)]
            return base + '<' + ', '.join(args) + '>'
        return f'?{kind:#x}'

    def typedef_fullname(self, ti):
        m = self.meta
        t = m.typeDefs[ti]
        n = m.str(t.nameIndex)
        if t.declaringTypeIndex >= 0:
            outer = self.type_name(self.type_at(t.declaringTypeIndex))
            return outer + '.' + n
        ns = m.str(t.namespaceIndex)
        return (ns + '.' if ns else '') + n
