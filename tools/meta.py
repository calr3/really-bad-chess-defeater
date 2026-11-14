"""Minimal IL2CPP global-metadata.dat (v31) reader."""
import struct

HDR_NAMES = ['stringLiteral', 'stringLiteralData', 'string', 'events', 'properties', 'methods',
             'parameterDefaultValues', 'fieldDefaultValues', 'fieldAndParameterDefaultValueData',
             'fieldMarshaledSizes', 'parameters', 'fields', 'genericParameters',
             'genericParameterConstraints', 'genericContainers', 'nestedTypes', 'interfaces',
             'vtableMethods', 'interfaceOffsets', 'typeDefinitions', 'images', 'assemblies',
             'fieldRefs', 'referencedAssemblies', 'attributeData', 'attributeDataRange',
             'unresolvedVirtualCallParameterTypes', 'unresolvedVirtualCallParameterRanges',
             'windowsRuntimeTypeNames', 'windowsRuntimeStrings', 'exportedTypeDefinitions']


class TypeDef:
    FMT = '<16i8H2I'
    SIZE = struct.calcsize(FMT)

    def __init__(self, t):
        (self.nameIndex, self.namespaceIndex, self.byvalTypeIndex, self.declaringTypeIndex,
         self.parentIndex, self.elementTypeIndex, self.genericContainerIndex, self.flags,
         self.fieldStart, self.methodStart, self.eventStart, self.propertyStart,
         self.nestedTypesStart, self.interfacesStart, self.vtableStart, self.interfaceOffsetsStart,
         self.method_count, self.property_count, self.field_count, self.event_count,
         self.nested_type_count, self.vtable_count, self.interfaces_count,
         self.interface_offsets_count, self.bitfield, self.token) = t


class MethodDef:
    FMT = '<7i4H'
    SIZE = struct.calcsize(FMT)

    def __init__(self, t):
        (self.nameIndex, self.declaringType, self.returnType, self.returnParameterToken,
         self.parameterStart, self.genericContainerIndex, self.token, self.flags, self.iflags,
         self.slot, self.parameterCount) = t


class Metadata:
    def __init__(self, path):
        d = self.d = open(path, 'rb').read()
        sanity, self.version = struct.unpack_from('<Ii', d, 0)
        assert sanity == 0xFAB11BAF and self.version == 31, hex(sanity)
        self.h = {}
        for i, n in enumerate(HDR_NAMES):
            self.h[n] = struct.unpack_from('<Ii', d, 8 + 8 * i)
        self.typeDefs = self._arr('typeDefinitions', TypeDef)
        self.methods = self._arr('methods', MethodDef)
        self.fields = self._tuples('fields', '<iiI')          # name, typeIndex, token
        self.params = self._tuples('parameters', '<iIi')      # name, token, typeIndex
        self.fieldDefaults = {f: (t, di) for f, t, di in self._tuples('fieldDefaultValues', '<3i')}
        self.paramDefaults = {p: (t, di) for p, t, di in self._tuples('parameterDefaultValues', '<3i')}
        self.nested = self._tuples('nestedTypes', '<i')
        self.images = self._tuples('images', '<Ii i I i I i I i I')
        self.lits = self._tuples('stringLiteral', '<Ii')
        self.genericContainers = self._tuples('genericContainers', '<4i')
        self.genericParams = self._tuples('genericParameters', '<iIhhHH')

    def _tuples(self, name, fmt):
        off, size = self.h[name]
        fmt = fmt.replace(' ', '')
        sz = struct.calcsize(fmt)
        assert size % sz == 0, (name, size, sz)
        return [struct.unpack_from(fmt, self.d, off + i * sz) for i in range(size // sz)]

    def _arr(self, name, cls):
        return [cls(t) for t in self._tuples(name, cls.FMT)]

    def str(self, idx):
        off = self.h['string'][0] + idx
        return self.d[off:self.d.index(b'\0', off)].decode('utf8', 'replace')

    def literal(self, i):
        ln, di = self.lits[i]
        off = self.h['stringLiteralData'][0] + di
        return self.d[off:off + ln].decode('utf8', 'replace')

    def default_blob(self, dataIndex):
        return self.h['fieldAndParameterDefaultValueData'][0] + dataIndex

    def type_name(self, ti):
        t = self.typeDefs[ti]
        n = self.str(t.nameIndex)
        if t.declaringTypeIndex >= 0:
            # declaringTypeIndex is a *type* index (Il2CppType), resolved by caller if needed
            pass
        ns = self.str(t.namespaceIndex)
        return (ns + '.' if ns else '') + n

    def image_of_type(self):
        """Map typeDef index -> image name."""
        out = {}
        for (nameIdx, asm, tstart, tcount, *_rest) in self.images:
            nm = self.str(nameIdx)
            for i in range(tstart, tstart + tcount):
                out[i] = nm
        return out
