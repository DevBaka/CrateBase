"""Read-only PE/RTTI inventory; no process attachment or installation mutation."""
import argparse
import hashlib
import json
import re
from pathlib import Path
import pefile


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("binary",type=Path)
    parser.add_argument("output",type=Path)
    args=parser.parse_args()
    args.output.mkdir(parents=True,exist_ok=True)
    data=args.binary.read_bytes()
    pe=pefile.PE(data=data,fast_load=False)
    base=pe.OPTIONAL_HEADER.ImageBase
    sections=[{"name":s.Name.rstrip(b"\0").decode(errors="replace"),"rva":hex(s.VirtualAddress),"virtual_size":s.Misc_VirtualSize,"raw_offset":s.PointerToRawData,"raw_size":s.SizeOfRawData,"flags":hex(s.Characteristics)} for s in pe.sections]
    imports=[]
    for module in getattr(pe,"DIRECTORY_ENTRY_IMPORT",[]):
        imports.append({"dll":module.dll.decode(errors="replace"),"functions":[{"name":imp.name.decode(errors="replace") if imp.name else f"ordinal_{imp.ordinal}","iat":hex(imp.address)} for imp in module.imports]})
    exports=[{"name":e.name.decode(errors="replace") if e.name else None,"rva":hex(e.address),"ordinal":e.ordinal} for e in getattr(getattr(pe,"DIRECTORY_ENTRY_EXPORT",None),"symbols",[])]
    summary={"path":str(args.binary),"sha256":hashlib.sha256(data).hexdigest(),"size":len(data),"base":hex(base),"entry":hex(base+pe.OPTIONAL_HEADER.AddressOfEntryPoint),"sections":sections,"imports":imports,"exports":exports}
    (args.output/"pe_inventory.json").write_text(json.dumps(summary,indent=2),encoding="utf-8")
    interesting=re.compile(r"stem|separat|demux|decoder|audiosource|trackdeck|stemdeck|trackplayer|samplebuffer|audioreader|readframes|cachingreader|readsample|time.?stretch|rubberband|soundtouch|command.?line|settings.?path|root.?dir",re.I)
    records=[]
    for match in re.finditer(rb"[\x20-\x7e]{5,}",data):
        text=match.group().decode("ascii")
        if len(text)>1600 or not interesting.search(text):continue
        offset=match.start()
        try:rva=pe.get_rva_from_offset(offset)
        except pefile.PEFormatError:rva=None
        records.append({"offset":hex(offset),"rva":hex(rva) if rva is not None else None,"va":hex(base+rva) if rva is not None else None,"text":text})
    with (args.output/"audio_anchors.jsonl").open("w",encoding="utf-8") as output:
        for record in records:output.write(json.dumps(record,ensure_ascii=False)+"\n")
    types=[]
    for match in re.finditer(rb"\.\?A[UV][A-Za-z0-9_?$@]{3,}\x00",data):
        text=match.group()[:-1].decode("ascii")
        if interesting.search(text):
            offset=match.start();rva=pe.get_rva_from_offset(offset)
            types.append({"va":hex(base+rva),"offset":hex(offset),"name":text})
    (args.output/"audio_rtti.json").write_text(json.dumps(types,indent=2),encoding="utf-8")
    print(json.dumps({"sha256":summary["sha256"],"base":summary["base"],"sections":sections,"import_modules":[m["dll"] for m in imports],"exports":len(exports),"audio_anchors":len(records),"audio_rtti":len(types)},indent=2))
    print("RTTI audio candidates:")
    for record in types[:180]:print(record["va"],record["name"])


if __name__=="__main__":main()
