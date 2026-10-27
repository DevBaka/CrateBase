"""Recover selected MSVC x64 RTTI vtables and exception-directory function bounds."""
import argparse
import bisect
import json
import re
import struct
from pathlib import Path
import numpy as np
import pefile
from capstone import Cs,CS_ARCH_X86,CS_MODE_64


def main():
    p=argparse.ArgumentParser();p.add_argument("binary",type=Path);p.add_argument("output",type=Path);args=p.parse_args()
    data=args.binary.read_bytes();pe=pefile.PE(data=data,fast_load=True);base=pe.OPTIONAL_HEADER.ImageBase
    text=next(s for s in pe.sections if s.Name.startswith(b".text"))
    rdata=next(s for s in pe.sections if s.Name.startswith(b".rdata"))
    code_start=base+text.VirtualAddress;code_end=code_start+text.Misc_VirtualSize
    patterns=re.compile(r"StemMixdown|TrackPlayer@@|TrackDeck@@|StemFileWriter|StemSeparation|AudioStream|TrackFile|MP4|Mp4|TimeStretchingModule|PlayerRouting@@|StemChain@",re.I)
    types={}
    for match in re.finditer(rb"\.\?A[UV][A-Za-z0-9_?$@]{3,}\x00",data):
        name=match.group()[:-1].decode("ascii")
        if patterns.search(name) and len(name)<180:
            types[pe.get_rva_from_offset(match.start())-16]=name
    pdata=next(s for s in pe.sections if s.Name.startswith(b".pdata"))
    functions=[struct.unpack_from("<III",data,pdata.PointerToRawData+i) for i in range(0,pdata.Misc_VirtualSize-11,12)]
    starts=[f[0] for f in functions]
    words=np.frombuffer(data,dtype="<u4",count=rdata.SizeOfRawData//4,offset=rdata.PointerToRawData)
    locators=[];chunk=1024*1024
    for begin in range(0,len(words)-6,chunk):
        end=min(len(words)-6,begin+chunk)
        indices=np.where((words[begin:end]==1)&(words[begin+5:end+5]==rdata.VirtualAddress+np.arange(begin,end,dtype=np.uint32)*4))[0]+begin
        for i in indices.tolist():
            type_rva=int(words[i+3])
            if type_rva in types:locators.append({"name":types[type_rva],"type_rva":hex(type_rva),"locator_rva":rdata.VirtualAddress+i*4,"offset":int(words[i+1])})
    dis=Cs(CS_ARCH_X86,CS_MODE_64);results=[]
    for locator in locators:
        needle=struct.pack("<Q",base+locator["locator_rva"]);offset=0
        while True:
            offset=data.find(needle,offset)
            if offset<0:break
            table=offset+8;entries=[]
            for index in range(150):
                pointer=struct.unpack_from("<Q",data,table+index*8)[0]
                if not code_start<=pointer<code_end:break
                rva=pointer-base;fi=bisect.bisect_right(starts,rva)-1;bounds=functions[fi] if fi>=0 and functions[fi][0]<=rva<functions[fi][1] else None
                file_offset=pe.get_offset_from_rva(rva)
                instructions=[f"{address:x}: {mnemonic} {operands}" for address,size,mnemonic,operands in dis.disasm_lite(data[file_offset:file_offset+100],pointer)][:15]
                entries.append({"index":index,"va":hex(pointer),"rva":hex(rva),"function_bounds":[hex(base+bounds[0]),hex(base+bounds[1])] if bounds else None,"instructions":instructions})
            if entries:results.append({**locator,"locator_rva":hex(locator["locator_rva"]),"vtable_va":hex(base+pe.get_rva_from_offset(table)),"entries":entries})
            offset+=8
    args.output.mkdir(parents=True,exist_ok=True)
    (args.output/"audio_vtables.json").write_text(json.dumps(results,indent=2),encoding="utf-8")
    (args.output/"function_bounds.json").write_text(json.dumps(functions),encoding="utf-8")
    for table in results:
        print(table["name"],table["vtable_va"],len(table["entries"]),"entries", "this-offset",table["offset"])
        for entry in table["entries"][:12]:print(" ",entry["index"],entry["va"],entry["instructions"][:3])


if __name__=="__main__":main()
