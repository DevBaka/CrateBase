"""Bounded direct-call references in the private verified executable."""
import argparse,bisect,json,os,re,struct
from pathlib import Path
import pefile
p=argparse.ArgumentParser();p.add_argument('targets',nargs='+',type=lambda s:int(s,16));p.add_argument('--output',type=Path,required=True);a=p.parse_args()
data=(Path(os.environ['LOCALAPPDATA'])/'Cratebase/traktor-lab/binaries/Traktor Pro 4.exe').read_bytes();pe=pefile.PE(data=data,fast_load=True);base=pe.OPTIONAL_HEADER.ImageBase
s=next(s for s in pe.sections if s.Name.startswith(b'.text'));code=data[s.PointerToRawData:s.PointerToRawData+s.SizeOfRawData]
b=json.loads((Path(__file__).resolve().parents[1]/'reports/function_bounds.json').read_text());starts=[x[0] for x in b];found={}
for m in re.finditer(b'\xe8....',code,re.S):
 r=s.VirtualAddress+m.start();target=r+5+struct.unpack_from('<i',m.group(),1)[0]
 if target in a.targets:
  f=b[bisect.bisect_right(starts,r)-1];found[f[0]]=f;print(hex(target),hex(r),hex(f[0]))
a.output.write_text('\n'.join(f'{base+f[0]:x} {base+f[1]:x}' for f in found.values()))
