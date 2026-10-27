"""Lifecycle, cache isolation, native fallback, resampling and enable-switch tests."""
import ctypes as C,os,time,unittest
from pathlib import Path
import numpy as np
from stem_cache import StemCache,RATE,BLOCK

class LiveBridgeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dll=C.WinDLL(str(Path(__file__).resolve().parents[1]/'native'/'build'/os.environ.get('LAB_LIVE_DLL','live_bridge_v1.dll')))
        d=cls.dll;d.LabCreateCache.argtypes=[C.c_wchar_p,C.c_uint64,C.c_uint];d.LabAddCache.argtypes=[C.c_wchar_p]
        d.LabRegisterSource.argtypes=[C.c_void_p,C.c_uint,C.c_uint,C.c_uint,C.c_uint]
        d.LabStatistics.argtypes=[C.c_uint,C.c_uint];d.LabStatistics.restype=C.c_uint64
        cls.keep=[];cls.counter=0
    def cache(self,frames=RATE*2):
        type(self).counter+=1;path=Path(os.environ['LOCALAPPDATA'])/'Cratebase'/'traktor-lab'/'fixtures'/f'live-test-{os.getpid()}-{self.counter}.cache'
        index=self.dll.LabCreateCache(str(path),frames,RATE);self.assertGreaterEqual(index,0)
        cache=StemCache(path);self.assertEqual(cache.frames,frames);self.assertEqual(self.dll.LabAddCache(str(path)),index)
        self.keep.append(cache);return index,cache
    def reader(self,index,stem=1,rate=RATE,mix=True):
        READ=C.CFUNCTYPE(C.c_int64,C.c_void_p,C.POINTER(C.POINTER(C.c_float)));DESTROY=C.CFUNCTYPE(C.c_void_p,C.c_void_p,C.c_uint)
        obj=C.create_string_buffer(0xf8);address=C.addressof(obj);destroyed=[]
        @READ
        def original(ptr,out):
            for ch in range(2):np.ctypeslib.as_array(out[ch],shape=(BLOCK,))[:]=.8
            C.c_int64.from_address(ptr+0xe0).value+=BLOCK;return BLOCK
        @DESTROY
        def destroy(ptr,flags):destroyed.append(flags);return ptr
        table=(C.c_void_p*19)();table[0]=0x1234;table[1]=C.cast(destroy,C.c_void_p).value;table[18]=C.cast(original,C.c_void_p).value
        C.c_void_p.from_address(address).value=C.addressof(table)+8
        self.assertEqual(self.dll.LabRegisterSource(address,stem,index,rate,int(mix)),0)
        left=(C.c_float*BLOCK)();right=(C.c_float*BLOCK)();out=(C.POINTER(C.c_float)*2)(left,right)
        vtable=C.c_void_p.from_address(address).value;read=READ(C.c_void_p.from_address(vtable+17*8).value);delete=DESTROY(C.c_void_p.from_address(vtable).value)
        self.keep.append((obj,original,destroy,table,left,right,out,read,delete))
        return address,read,out,np.ctypeslib.as_array(left),delete,destroyed
    def test_fallback_then_gpu_and_disable_with_crossfade(self):
        index,cache=self.cache();ptr,read,out,left,delete,destroyed=self.reader(index)
        read(ptr,out);np.testing.assert_allclose(left,.2,rtol=0,atol=1e-7)
        cache.store_original(np.full((2,cache.frames),.8,dtype=np.float32));cache.publish(0,np.full((4,2,cache.frames),.4,dtype=np.float32))
        C.c_int64.from_address(ptr+0xe0).value=0;read(ptr,out)
        self.assertAlmostEqual(float(left[0]),.2,places=6);self.assertAlmostEqual(float(left[500]),.4,places=6)
        self.dll.LabSetEnabled(0);read(ptr,out);self.assertAlmostEqual(float(left[0]),.4,places=6);self.assertAlmostEqual(float(left[500]),.2,places=6)
        self.dll.LabSetEnabled(1);delete(ptr,7);self.assertEqual(destroyed,[7])
    def test_samplerates_unaligned_reads_and_cache_isolation(self):
        index,cache=self.cache();t=np.arange(cache.frames,dtype=np.float64)/RATE
        signal=(.2*np.sin(2*np.pi*1000*t)).astype(np.float32);stems=np.broadcast_to(signal,(4,2,cache.frames)).copy();cache.publish(0,stems)
        for rate in (44100,48000,96000):
            ptr,read,out,left,delete,destroyed=self.reader(index,rate=rate)
            C.c_int64.from_address(ptr+0xe0).value=rate//2+117;read(ptr,out)
            positions=(rate//2+117+np.arange(BLOCK))/rate
            expected=.2*np.sin(2*np.pi*1000*positions)
            self.assertLess(float(np.max(np.abs(left-expected))),.00015)
            delete(ptr,0);self.assertEqual(destroyed,[0])
        other,cache2=self.cache();cache2.publish(0,np.full((4,2,cache2.frames),.7,dtype=np.float32))
        ptr,read,out,left,delete,_=self.reader(other);read(ptr,out);np.testing.assert_allclose(left,.7,rtol=0,atol=1e-7);delete(ptr,0)
    def test_reader_slots_reuse_after_native_destruction(self):
        index,cache=self.cache(BLOCK*2);cache.publish(0,np.zeros((4,2,cache.frames),dtype=np.float32))
        before=self.dll.LabReaderCount()
        for i in range(300):
            ptr,read,out,left,delete,destroyed=self.reader(index);read(ptr,out);delete(ptr,i);self.assertEqual(destroyed,[i])
        self.assertLessEqual(self.dll.LabReaderCount(),before+1)

if __name__=='__main__':unittest.main()
