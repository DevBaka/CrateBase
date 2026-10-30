import json,secrets,tempfile,unittest,http.client,hmac,hashlib,time,socket,threading
from pathlib import Path
from network_sync import Profile,Endpoint,portable,digest,Discovery
from profile_lock import hold,ProfileBusy


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

    def test_shared_gate_blocks_writer(self):
        self.a.gate=Path(self.tmp.name)/'gate'
        with hold(self.a.gate):
            with self.assertRaises(ProfileBusy):self.a.write('blocked.dat',b'data',None)
        self.a.write('blocked.dat',b'data',None)
        self.assertEqual((self.a.root/'blocked.dat').read_bytes(),b'data')

    def test_replay_and_expected_revision_are_authenticated(self):
        path='/file?path=test.dat';data=b'payload';nonce=str(time.time())+':test'
        headers={'X-Cratebase-Nonce':nonce,'X-Cratebase-Body':digest(data),'X-Cratebase-Auth':self.left.signature('PUT',path+'\n',nonce,data)}
        def send(values):
            connection=http.client.HTTPConnection('127.0.0.1',self.port,timeout=3)
            try:
                connection.request('PUT',path,body=data,headers=values);response=connection.getresponse();response.read();return response.status
            finally:connection.close()
        self.assertEqual(send({**headers,'X-Cratebase-Expected':'tampered'}),403)
        self.assertEqual(send(headers),200)
        self.assertEqual(send(headers),403)

    def test_discovery_ignores_bad_packets_and_recovers(self):
        discovery=Discovery(self.right,0,self.port)
        udp_port=discovery.socket.getsockname()[1]
        sender=socket.socket(socket.AF_INET,socket.SOCK_DGRAM)
        try:
            for value in ([],None,{'node':[]}):sender.sendto(json.dumps(value).encode(),('127.0.0.1',udp_port))
            sender.sendto(json.dumps({'cratebase':1,'group':self.right.group,'node':self.left.node,'name':'Local peer','port':self.port}).encode(),('127.0.0.1',udp_port))
            deadline=time.monotonic()+2
            while time.monotonic()<deadline and not discovery.snapshot():time.sleep(.02)
            self.assertEqual(discovery.snapshot()[0]['name'],'Local peer')
        finally:discovery.close();sender.close()


if __name__=='__main__':unittest.main()
