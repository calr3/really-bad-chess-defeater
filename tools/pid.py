import sys, os, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from meta import Metadata
m = Metadata(sys.argv[1])
want = {p[:8]: p for p in sys.argv[2:]}
for ti, t in enumerate(m.typeDefs):
    if m.str(t.nameIndex) != '<PrivateImplementationDetails>': continue
    for i in range(t.field_count):
        fi = t.fieldStart + i
        n = m.str(m.fields[fi][0])
        for k, label in [(a, b) for a, b in want.items()]:
            if n.startswith(k.split('=')[0]):
                tdi, di = m.fieldDefaults[fi]
                o = m.default_blob(di)
                lab, ln, typ = label.split('=')[1].split(':')
                ln = int(ln)
                fmt = {'sb': 'b', 'i': 'i', 'b': 'B'}[typ]
                vals = struct.unpack_from('<%d%s' % (ln, fmt), m.d, o)
                print(lab, ti)
                w = 8 if ln == 64 else 16
                for r in range(0, ln, w): print('   ', ' '.join('%4d' % v for v in vals[r:r + w]))
