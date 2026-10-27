"""Synthetic, quiet, known stem signals; no collection imports or real-track changes."""
import json
import os
import struct
import subprocess
from pathlib import Path
import numpy as np
import soundfile as sf


def atoms(data,start,end):
    while start<end:
        size=struct.unpack_from(">I",data,start)[0];kind=data[start+4:start+8].decode("ascii");header=8
        if size==1:size=struct.unpack_from(">Q",data,start+8)[0];header=16
        if size==0:size=end-start
        if size<header or start+size>end:raise ValueError("Invalid atom")
        yield start,size,kind,header
        start+=size


def main():
    lab=Path(os.environ["LOCALAPPDATA"])/"Cratebase"/"traktor-lab";work=lab/"fixtures";work.mkdir(exist_ok=True)
    ffmpeg=Path(os.environ["LOCALAPPDATA"])/"Cratebase"/"tools"/"ffmpeg.exe"
    sr=44100;seconds=90;t=np.arange(sr*seconds,dtype=np.float64)/sr
    stems=[]
    for i,(name,frequency) in enumerate(zip(("Drums","Bass","Other","Vocals"),(220,440,880,1760))):
        envelope=(.25+.75*(np.sin(2*np.pi*(i+1)*t/3)>0)).astype(np.float32)
        signal=.005*np.sin(2*np.pi*frequency*t)*envelope
        stereo=np.column_stack((signal,signal*np.cos(i*.2)))
        sf.write(work/(name+".wav"),stereo,sr,subtype="PCM_24");stems.append(stereo)
    sf.write(work/"Lab Original.wav",sum(stems),sr,subtype="PCM_24")
    output=work/"Lab Four Tones.stem.mp4"
    if not output.exists():
        command=[str(ffmpeg),"-nostdin","-v","error","-n"]
        for source in [work/"Lab Original.wav"]+[work/(name+".wav") for name in ("Drums","Bass","Other","Vocals")]:command.extend(("-i",str(source)))
        for i,name in enumerate(("Master","Drums","Bass","Other","Vocals")):command.extend(("-map",f"{i}:a:0",f"-metadata:s:a:{i}","handler_name="+name,f"-disposition:a:{i}","default" if i==0 else "0"))
        command.extend(("-c:a","aac","-b:a","192k","-ar","44100","-ac","2","-brand","M4A ","-skip_iods","1","-metadata","artist=Cratebase Lab","-metadata","title=Four Known Stems","-f","mp4",str(output)))
        subprocess.run(command,check=True)
        data=bytearray(output.read_bytes());moov=next(a for a in atoms(data,0,len(data)) if a[2]=="moov");children=list(atoms(data,moov[0]+8,moov[0]+moov[1]));tracks=[a for a in children if a[2]=="trak"]
        for i,track in enumerate(tracks):
            tkhd=next(a for a in atoms(data,track[0]+track[3],track[0]+track[1]) if a[2]=="tkhd")
            version=data[tkhd[0]+tkhd[3]];data[tkhd[0]+tkhd[3]+1:tkhd[0]+tkhd[3]+4]=bytes((0,0,3 if i==0 else 2));offset=tkhd[0]+(54 if version==1 else 42);data[offset:offset+2]=b"\0\0"
        manifest=json.dumps({"version":1,"stems":[{"name":name,"color":color} for name,color in zip(("Drums","Bass","Other","Vocals"),("#FF604A","#FFD54A","#33DDAA","#AA77FF"))],"mastering_dsp":{"compressor":{"enabled":False,"ratio":3,"threshold":0,"attack":.003,"release":.3,"input_gain":0,"output_gain":0,"hp_cutoff":300,"dry_wet":50},"limiter":{"enabled":False,"threshold":0,"ceiling":0,"release":.05}}},separators=(",",":")).encode()
        box=struct.pack(">I4s",len(manifest)+8,b"stem")+manifest;udta=next((a for a in children if a[2]=="udta"),None)
        if udta:
            assert udta[0]+udta[1]==len(data);struct.pack_into(">I",data,udta[0],udta[1]+len(box));data.extend(box)
        else:box=struct.pack(">I4s",len(box)+8,b"udta")+box;data.extend(box)
        struct.pack_into(">I",data,moov[0],moov[1]+len(box));output.write_bytes(data)
    print("Fixtures:",work)
    print("Original and four frequency-separated stems are 90 seconds, peak < -34 dBFS.")


if __name__=="__main__":main()
