"""Find small audio cache-ring accessors in the private PE (read-only)."""
import json,os,bisect
from pathlib import Path
import pefile,capstone
root=Path(__file__).resolve().parent.parent
lab=Path(os.environ['LOCALAPPDATA'])/'Cratebase'/'traktor-lab'
pe=pefile.PE(str(lab/'binaries'/'Traktor Pro 4.exe'));image=pe.get_memory_mapped_image()
bounds=json.loads((root/'reports'/'function_bounds.json').read_text());starts=[x[0] for x in bounds]
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64);md.detail=True
found={}
for ins in md.disasm(image[0x673000:0x689000],0x140673000):
    for operand in ins.operands:
        if operand.type==capstone.x86.X86_OP_MEM and operand.mem.disp in (0xc8,0xe0,0x94,0xac):
            rva=ins.address-0x140000000;index=bisect.bisect_right(starts,rva)-1
            if index<0:continue
            begin,end,_=bounds[index]
            if begin<=rva<end:found.setdefault(begin,[]).append((hex(ins.address),ins.mnemonic,ins.op_str))
selected={hex(0x140000000+k):v for k,v in found.items() if len(v)>=3}
(root/'reports'/'cache-read-candidates.json').write_text(json.dumps(selected,indent=2))
print('\n'.join(f'{k}: {len(v)}' for k,v in selected.items()))
