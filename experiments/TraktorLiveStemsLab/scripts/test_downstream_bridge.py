"""Exercise stale native prefetch replacement at the final cache-read boundary."""
import ctypes as C,os,unittest
os.environ.setdefault('LAB_LIVE_DLL','live_bridge_v3.dll')
import numpy as np
import test_live_bridge as base
from stem_cache import RATE

class DownstreamTests(base.LiveBridgeTests):
    def test_stale_prefetch_forward_reverse_seek_and_bypass(self):
        d=self.dll
        FN=C.CFUNCTYPE(C.c_uint64,C.c_void_p,C.c_uint,C.POINTER(C.c_float),C.POINTER(C.c_float),C.POINTER(C.c_uint))
        d.LabSetCacheReadOriginal.argtypes=[C.c_void_p];d.LabRegisterDownstream.argtypes=[C.c_void_p,C.c_void_p]
        d.LabUnregisterDownstream.argtypes=[C.c_void_p]
        d.LabReadDownstream.argtypes=FN._argtypes_;d.LabReadDownstream.restype=C.c_uint64
        index,cache=self.cache(RATE*2)
        t=np.arange(cache.frames,dtype=np.float32)/RATE
        waves=np.stack([.1*np.sin(2*np.pi*frequency*t) for frequency in (120,240,480,960)])
        cache.store_original(np.full((2,cache.frames),.8,dtype=np.float32))
        source,_,_,_,delete,_=self.reader(index,stem=3)
        obj=C.create_string_buffer(0x250);primary=C.create_string_buffer(0x100)
        address=C.addressof(obj);p=C.addressof(primary);C.c_void_p.from_address(address+0x10).value=p
        @FN
        def stale(ptr,frames,left,right,actual):
            # Native ring contains the original/4 from before GPU publication.
            for output in (left,right):np.ctypeslib.as_array(output,shape=(frames,))[:]=.2
            position=C.c_uint.from_address(p+0xac);direction=-1 if C.c_ubyte.from_address(p+0xb4).value else 1
            position.value=position.value+direction*frames
            if actual:actual[0]=frames
            return 0
        self.keep.append((obj,primary,stale));d.LabSetCacheReadOriginal(C.cast(stale,C.c_void_p))
        self.assertEqual(d.LabRegisterDownstream(address,source),0)
        left=(C.c_float*2048)();right=(C.c_float*2048)();actual=C.c_uint()
        C.c_uint.from_address(p+0xac).value=10000
        d.LabReadDownstream(address,2048,left,right,C.byref(actual))
        np.testing.assert_allclose(np.ctypeslib.as_array(left),.2,atol=1e-7)
        cache.publish(0,np.repeat(waves[:,None,:],2,axis=1))
        for reverse in (False,True):
            C.c_uint.from_address(p+0xac).value=20000;C.c_ubyte.from_address(p+0xb4).value=int(reverse)
            d.LabReadDownstream(address,2048,left,right,C.byref(actual))
            indices=20000+(-1 if reverse else 1)*np.arange(2048)
            np.testing.assert_allclose(np.ctypeslib.as_array(left)[500:],waves[2,indices][500:],atol=1e-7)
            self.assertEqual(actual.value,2048)
        d.LabSetEnabled(0);d.LabReadDownstream(address,2048,left,right,C.byref(actual))
        np.testing.assert_allclose(np.ctypeslib.as_array(left)[500:],.2,atol=1e-7)
        d.LabSetEnabled(1);d.LabUnregisterDownstream(address);delete(source,0)

if __name__=='__main__':unittest.main()
