"""Versioned memory-mapped cache shared by a GPU worker and the native lab bridge."""
import math,mmap,struct
from pathlib import Path
import numpy as np

BLOCK=1024;RATE=44100;HEADER=struct.Struct('<8sIIIIQQQiiii')
class StemCache:
    def __init__(self,path,frames=None):
        self.path=Path(path)
        if frames is not None:
            self.path.parent.mkdir(parents=True,exist_ok=True)
            blocks=math.ceil(frames/BLOCK);offset=((4096+blocks*4+4095)//4096)*4096
            with self.path.open('xb') as f:
                f.write(HEADER.pack(b'CBSTEM01',1,RATE,BLOCK,8,frames,blocks,offset,0,-1,0,1))
                original_offset=offset+blocks*8*BLOCK*4
                f.write(struct.pack('<Qi',original_offset,0))
                f.truncate(original_offset+frames*2*4)
        self.file=self.path.open('r+b');self.map=mmap.mmap(self.file.fileno(),0)
        magic,version,rate,block,channels,self.frames,self.blocks,self.offset,*_=HEADER.unpack_from(self.map)
        if (magic,version,rate,block,channels)!=(b'CBSTEM01',1,RATE,BLOCK,8):raise ValueError('Incompatible cache')
        self.ready=np.ndarray((self.blocks,),dtype='<i4',buffer=self.map,offset=4096)
        self.data=np.ndarray((self.blocks,4,2,BLOCK),dtype='<f4',buffer=self.map,offset=self.offset)
        self.original_offset=struct.unpack_from('<Q',self.map,64)[0]
        self.original=np.ndarray((2,self.frames),dtype='<f4',buffer=self.map,offset=self.original_offset)
    def store_original(self,audio):
        if audio.shape!=(2,self.frames) or not np.isfinite(audio).all():raise ValueError('Invalid original audio')
        self.original[:]=audio;struct.pack_into('<i',self.map,72,1)
    def publish(self,start,audio):
        """Publish finalized, block-aligned samples. Partial final block is zero-padded."""
        if start%BLOCK:raise ValueError('Unaligned publication')
        if audio.shape[:2]!=(4,2) or not np.isfinite(audio).all():raise ValueError('Invalid stems')
        count=audio.shape[-1]
        for relative in range(0,count,BLOCK):
            index=(start+relative)//BLOCK
            if index>=self.blocks:break
            take=min(BLOCK,count-relative)
            if take<BLOCK and start+count<self.frames:break
            self.data[index].fill(0);self.data[index,:,:,:take]=audio[:,:,relative:relative+take]
            # File-backed shared views are coherent on Windows. Publish only after all planes are written.
            self.ready[index]=1
        struct.pack_into('<i',self.map,48,int(np.count_nonzero(self.ready)))
    def requested(self):return struct.unpack_from('<i',self.map,52)[0]
    def close(self):
        del self.ready,self.data,self.original;self.map.close();self.file.close()

def create_test(path,seconds=90):
    cache=StemCache(path,int(seconds*RATE));t=np.arange(cache.frames,dtype=np.float64)/RATE
    stems=np.empty((4,2,cache.frames),dtype=np.float32)
    for i,frequency in enumerate((330,660,1320,2640)):
        stems[i,0]=.004*np.sin(2*np.pi*frequency*t);stems[i,1]=stems[i,0]
    cache.publish(0,stems);cache.close()
if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser();p.add_argument('path');a=p.parse_args();create_test(a.path);print('Published 90-second synthetic replacement cache')
