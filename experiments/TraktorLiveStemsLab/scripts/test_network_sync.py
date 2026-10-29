import json,secrets,tempfile,unittest
from pathlib import Path
from network_sync import Profile,Endpoint,portable


class NetworkSyncTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();root=Path(self.tmp.name)
        self.a=Profile(root/'a',root/'state-a');self.b=Profile(root/'b',root/'state-b')
        self.a.root.mkdir();self.b.root.mkdir();key=secrets.token_bytes(32)
        self.left=Endpoint(self.a,key);self.right=Endpoint(self.b,key);self.port=self.right.start('127.0.0.1',0)
    def tearDown(self):self.right.close();self.tmp.cleanup()
    def sync(self):return self.left.sync('127.0.0.1',self.port)
    def test_new_files_updates_conflicts_and_missing(self):
        (self.a.root/'track.dat').write_bytes(b'first')
        self.assertEqual(self.sync()['copied'],1)
        self.assertEqual((self.b.root/'track.dat').read_bytes(),b'first')
        (self.b.root/'track.dat').write_bytes(b'updated')
        self.assertEqual(self.sync()['copied'],1)
        self.assertEqual((self.a.root/'track.dat').read_bytes(),b'updated')
        (self.a.root/'track.dat').write_bytes(b'local edit');(self.b.root/'track.dat').write_bytes(b'remote edit')
        self.assertEqual(len(self.sync()['conflicts']),1)
        self.assertEqual((self.a.root/'track.dat').read_bytes(),b'local edit')
        (self.b.root/'track.dat').unlink()
        self.assertEqual(len(self.sync()['conflicts']),1)
        self.assertTrue((self.a.root/'track.dat').exists())
    def test_busy_authentication_and_path_escape(self):
        (self.a.root/'file').write_bytes(b'data');self.b.busy=lambda:True
        with self.assertRaises(RuntimeError):self.sync()
        self.b.busy=lambda:False
        wrong=Endpoint(self.a,secrets.token_bytes(32))
        with self.assertRaises(ValueError):wrong.sync('127.0.0.1',self.port)
        for name in ('../escape','C:/escape','folder\\escape','/absolute','bad.','sub/../../escape'):
            with self.assertRaises(ValueError):self.a.path(name)
        self.assertFalse((self.b.root/'file').exists())
    def test_tsi_retains_device_setup(self):
        def tsi(device,value):return f'<Root><Entry Name="Audio.DeviceName.Win" Value="{device}"/><Entry Name="Browser.Dir.Root" Value="{device}"/><Entry Name="Custom" Value="{value}"/></Root>'.encode()
        (self.a.root/'Settings.tsi').write_bytes(tsi('PC A','one'))
        (self.b.root/'Settings.tsi').write_bytes(tsi('PC B','one'))
        self.assertEqual(self.sync()['conflicts'],[])
        (self.a.root/'Settings.tsi').write_bytes(tsi('PC A','two'))
        self.assertEqual(self.sync()['copied'],1)
        saved=(self.b.root/'Settings.tsi').read_bytes()
        self.assertIn(b'PC B',saved);self.assertIn(b'two',saved)
        self.assertEqual(portable(saved,'Settings.tsi'),portable(tsi('PC A','two'),'Settings.tsi'))
    def test_cas_and_invalid_xml_keep_existing_file(self):
        p=self.a.root/'collection.nml';p.write_bytes(b'<NML/>');before=p.read_bytes()
        with self.assertRaises(RuntimeError):self.a.write('collection.nml',b'<NML/>','wrong revision')
        with self.assertRaises(Exception):self.a.write('collection.nml',b'broken',self.a.revision('collection.nml'))
        self.assertEqual(p.read_bytes(),before)


if __name__=='__main__':unittest.main()
