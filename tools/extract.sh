#!/bin/sh
# Regenerate the analysis outputs in build/ from your own copy of the APK (baseapk.apk).
# Needs: python3 with pyelftools and capstone (pip install pyelftools capstone), unzip.
set -e
cd "$(dirname "$0")/.."
sha256sum -c apk.sha256
mkdir -p build/x
unzip -q -o baseapk.apk lib/x86_64/libil2cpp.so assets/bin/Data/Managed/Metadata/global-metadata.dat -d build/x
META=build/x/assets/bin/Data/Managed/Metadata/global-metadata.dat
SO=build/x/lib/x86_64/libil2cpp.so
python3 -I tools/dump.py "$META" "$SO" build/dump_acs.cs
python3 -I tools/disasm.py "$META" "$SO" build/cg.asm \
    cgEngine. cgBoard. cgValueModifiers. cgMoveGenerator. cgMoveSet. cgSimpleMove. cgGlobal. \
    cgCastlingMove. cgEnPassantMove.
python3 -I tools/disasm.py "$META" "$SO" build/script.asm cgChessBoardScript
python3 -I tools/pid.py "$META" F80A9B08=Bishop:64:sb F18B1630=Rook:64:sb 778F558A=Knight:64:sb \
    E7A0A28B=Pawn:64:sb 225FBC69=Queen:64:sb 5AB889CB=DefaultStart:64:sb > build/tables.txt
echo "outputs in build/"
