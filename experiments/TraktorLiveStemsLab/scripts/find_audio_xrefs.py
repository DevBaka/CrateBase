"""Bounded RIP-relative code references to audio vtables in the verified lab executable."""
import bisect,json,os,re,struct
from pathlib import Path
import pefile
from capstone import Cs,CS_ARCH_X86,CS_MODE_64

lab=Path(os.environ['LOCALAPPDATA'])/'Cratebase'/'traktor-lab'
data=(lab/'binaries'/'Traktor Pro 4.exe').read_bytes();pe=pefile.PE(data=data,fast_load=True)
base=pe.OPTIONAL_HEADER.ImageBase;text=next(s for s in pe.sections if s.Name.startswith(b'.text'))
code=data[text.PointerToRawData:text.PointerToRawData+text.SizeOfRawData]
report=Path(__file__).resolve().parents[1]/'reports'
functions=json.loads((report/'function_bounds.json').read_text());starts=[f[0] for f in functions]
targets={int(t['vtable_va'],16):t['name'] for t in json.loads((report/'audio_vtables.json').read_text())}
dis=Cs(CS_ARCH_X86,CS_MODE_64)
found=[]
for m in re.finditer(rb'[\x48\x4c]\x8d[\x05\x0d\x15\x1d\x25\x2d\x35\x3d]....',code,re.S):
    rva=text.VirtualAddress+m.start();va=base+rva;target=va+7+struct.unpack_from('<i',m.group(),3)[0]
    if target not in targets:continue
    fi=bisect.bisect_right(starts,rva)-1;f=functions[fi]
    start=f[0] if f[0]<=rva<f[1] else rva
    offset=pe.get_offset_from_rva(start)
    instructions=[f'{a:x}: {mn} {op}' for a,sz,mn,op in dis.disasm_lite(data[offset:offset+min(2000,f[1]-start)],base+start)]
    found.append({'target':targets[target],'instruction':hex(va),'function':hex(base+start),'instructions':instructions})
(report/'audio_constructor_xrefs.json').write_text(json.dumps(found,indent=2))
for f in found:print(f['target'],f['instruction'],'function',f['function'])
