"""Exercise the compiled native callback in an owned mock process, without Traktor."""
import ctypes as C,os,struct,tempfile,unittest
from pathlib import Path
import numpy as np
from stem_cache import StemCache,BLOCK,RATE

class NativeBridgeTests(unittest.TestCase):
    def test_cached_planes_unready_fallback_seek_and_vtable(self):
        dllpath=Path(__file__).resolve().parents[1]/'native'/'build'/os.environ.get('LAB_BRIDGE_DLL','cache_bridge.dll')
        dll=C.WinDLL(str(dllpath));dll.LabConfigure.argtypes=[C.c_wchar_p];dll.LabRegister.argtypes=[C.c_void_p,C.c_uint]
        dll.LabStatistics.argtypes=[C.c_uint,C.c_uint];dll.LabStatistics.restype=C.c_uint64
        # The DLL intentionally owns its mapping until process exit; keep this file in the private lab.
        path=Path(os.environ['LOCALAPPDATA'])/'Cratebase'/'traktor-lab'/'fixtures'/f'native-test-{os.getpid()}.cache'
        cache=StemCache(path,BLOCK*3)
        audio=np.empty((4,2,BLOCK*3),dtype=np.float32)
        for s in range(4):
            for channel in range(2):audio[s,channel]=np.arange(BLOCK*3,dtype=np.float32)*1e-6+(s*2+channel)*.01
        cache.publish(0,audio);self.assertEqual(dll.LabConfigure(str(path)),0)
        READ=C.CFUNCTYPE(C.c_int64,C.c_void_p,C.POINTER(C.POINTER(C.c_float)))
        @READ
        def original(self,output):
            for ch in range(2):np.ctypeslib.as_array(output[ch],shape=(BLOCK,))[:]=-.25
            position=C.c_int64.from_address(self+0xe0);position.value+=BLOCK
            return BLOCK
        table=(C.c_void_p*19)();table[0]=0x12345678;table[18]=C.cast(original,C.c_void_p).value
        objects=[]
        for stem in range(1,5):
            obj=C.create_string_buffer(0xf8);objects.append(obj);ptr=C.addressof(obj)
            C.c_void_p.from_address(ptr).value=C.addressof(table)+8
            self.assertEqual(dll.LabRegister(ptr,stem),0)
            self.assertEqual(dll.LabRegister(ptr,stem),0)
            vtable=C.c_void_p.from_address(ptr).value
            self.assertEqual(C.c_void_p.from_address(vtable-8).value,0x12345678)
            callback=READ(C.c_void_p.from_address(vtable+17*8).value)
            left=(C.c_float*BLOCK)();right=(C.c_float*BLOCK)();out=(C.POINTER(C.c_float)*2)(left,right)
            self.assertEqual(callback(ptr,out),BLOCK)
            np.testing.assert_array_equal(np.ctypeslib.as_array(left),audio[stem-1,0,:BLOCK])
            np.testing.assert_array_equal(np.ctypeslib.as_array(right),audio[stem-1,1,:BLOCK])
            C.c_int64.from_address(ptr+0xe0).value=BLOCK*2
            callback(ptr,out)
            np.testing.assert_array_equal(np.ctypeslib.as_array(left),audio[stem-1,0,2*BLOCK:])
            cache.ready[1]=0;C.c_int64.from_address(ptr+0xe0).value=BLOCK
            callback(ptr,out);self.assertTrue(np.all(np.ctypeslib.as_array(left)==-.25))
            self.assertEqual(cache.requested(),1);cache.ready[1]=1
            self.assertEqual(dll.LabStatistics(stem-1,2),3)
            self.assertEqual(dll.LabStatistics(stem-1,3),2)
            self.assertEqual(dll.LabStatistics(stem-1,4),1)
        self.assertEqual(dll.LabReaderCount(),4);cache.close()

if __name__=='__main__':unittest.main()
