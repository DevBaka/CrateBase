"""Create a small standards-based silent Stem container as a native deck carrier.

Audio is supplied by cache_bridge, not by this file. Never present a carrier as a
finished stem export. All five AAC tracks share a valid repeated silent payload.
"""
import argparse,json,math,os,struct,subprocess,time
from pathlib import Path
from create_fixtures import atoms

RATE=44100
def box(kind,payload):return struct.pack('>I4s',len(payload)+8,kind.encode())+payload
def full(kind,payload=b'',flags=0):return box(kind,struct.pack('>I',flags)+payload)
def find(data,path):
    start=0;end=len(data)
    for part in path:
        a=next(a for a in atoms(data,start,end) if a[2]==part);start=a[0]+a[3];end=a[0]+a[1]
    return bytes(data[a[0]:end])
def template(lab):
    output=lab/'fixtures'/'carrier-aac-template.m4a';ffmpeg=lab.parent/'tools'/'ffmpeg.exe'
    if not output.exists():
        subprocess.run([str(ffmpeg),'-nostdin','-v','error','-n','-f','lavfi','-i','anullsrc=r=44100:cl=stereo','-t','0.5','-c:a','aac','-b:a','128k',str(output)],check=True)
    data=output.read_bytes();stsz=find(data,['moov','trak','mdia','minf','stbl','stsz']);stco=find(data,['moov','trak','mdia','minf','stbl','stco'])
    fixed,count=struct.unpack_from('>2I',stsz,12)
    sizes=[fixed]*count if fixed else list(struct.unpack_from('>'+str(count)+'I',stsz,20))
    offset=struct.unpack_from('>I',stco,16)[0]+sum(sizes[:4]);payload=data[offset:offset+sizes[4]]
    return find(data,['moov','trak','mdia','minf','stbl','stsd']),payload
def create(output,frames,lab=None):
    lab=lab or Path(os.environ['LOCALAPPDATA'])/'Cratebase'/'traktor-lab'
    if frames<1 or frames>RATE*3600:raise ValueError('Carrier length must be between one frame and one hour')
    stsd,packet=template(lab);samples=math.ceil(frames/1024);duration=samples*1024
    matrix=struct.pack('>9I',0x10000,0,0,0,0x10000,0,0,0,0x40000000)
    ftyp=box('ftyp',b'M4A '+struct.pack('>I',512)+b'M4A isomiso2')
    mdat=box('mdat',packet*samples);chunk_offset=len(ftyp)+8
    mvhd=full('mvhd',struct.pack('>4I',0,0,RATE,duration)+struct.pack('>IHH',0x10000,0x100,0)+b'\0'*8+matrix+b'\0'*24+struct.pack('>I',6))
    tracks=[]
    for index,name in enumerate(('Master','Drums','Bass','Other','Vocals'),1):
        tkhd=full('tkhd',struct.pack('>5I',0,0,index,0,duration)+b'\0'*8+struct.pack('>4H',0,0,0,0)+matrix+b'\0'*8,3 if index==1 else 2)
        mdhd=full('mdhd',struct.pack('>4IHH',0,0,RATE,duration,0x55c4,0))
        hdlr=full('hdlr',b'\0'*4+b'soun'+b'\0'*12+name.encode()+b'\0')
        dinf=box('dinf',full('dref',struct.pack('>I',1)+full('url ',flags=1)))
        stts=full('stts',struct.pack('>3I',1,samples,1024));stsc=full('stsc',struct.pack('>4I',1,1,samples,1))
        stsz=full('stsz',struct.pack('>2I',len(packet),samples));stco=full('stco',struct.pack('>2I',1,chunk_offset))
        stbl=box('stbl',stsd+stts+stsc+stsz+stco)
        minf=box('minf',full('smhd',b'\0'*4)+dinf+stbl)
        tracks.append(box('trak',tkhd+box('mdia',mdhd+hdlr+minf)))
    manifest={'version':1,'stems':[{'name':n,'color':c} for n,c in zip(('Drums','Bass','Other','Vocals'),('#FF604A','#FFD54A','#33DDAA','#AA77FF'))],
              'mastering_dsp':{'compressor':{'enabled':False,'ratio':3,'threshold':0,'attack':.003,'release':.3,'input_gain':0,'output_gain':0,'hp_cutoff':300,'dry_wet':50},'limiter':{'enabled':False,'threshold':0,'ceiling':0,'release':.05}}}
    moov=box('moov',mvhd+b''.join(tracks)+box('udta',box('stem',json.dumps(manifest,separators=(',',':')).encode())))
    output=Path(output);output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('xb') as f:f.write(ftyp+mdat+moov)
    return {'path':str(output),'frames':duration,'seconds':duration/RATE,'bytes':output.stat().st_size,'aac_packet_bytes':len(packet)}
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('output',type=Path);p.add_argument('--seconds',type=float,default=90);a=p.parse_args();begin=time.perf_counter();result=create(a.output,int(a.seconds*RATE));result['creation_seconds']=time.perf_counter()-begin;print(json.dumps(result,indent=2))
