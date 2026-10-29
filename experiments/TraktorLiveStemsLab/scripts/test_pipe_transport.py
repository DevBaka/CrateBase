import json,os,threading,unittest,uuid
from multiprocessing.connection import Listener,PipeClient
from pipe_transport import connect,authenticate_server,DeadlineConnection

@unittest.skipUnless(os.name=='nt','Windows pipe integration')
class PipeTests(unittest.TestCase):
    def test_stalled_authentication_is_closed_and_next_client_succeeds(self):
        address=r'\\.\pipe\Cratebase.TransportTest.'+uuid.uuid4().hex;key=os.urandom(32)
        timed_out=threading.Event();errors=[]
        with Listener(address,family='AF_PIPE',authkey=None) as listener:
            def serve():
                try:
                    with listener.accept() as connection:
                        try:authenticate_server(connection,key,timeout=.1)
                        except TimeoutError:timed_out.set()
                    with listener.accept() as connection:
                        pipe=authenticate_server(connection,key,timeout=1)
                        value=json.loads(pipe.recv_bytes(1024));pipe.send_bytes(json.dumps({'echo':value}).encode())
                except BaseException as error:errors.append(error)
            worker=threading.Thread(target=serve,daemon=True);worker.start()
            with PipeClient(address):self.assertTrue(timed_out.wait(2))
            connection,pipe=connect(address,key,timeout=1)
            with connection:
                pipe.send_bytes(b'{"enabled":false}')
                self.assertEqual(json.loads(pipe.recv_bytes(1024)),{'echo':{'enabled':False}})
            worker.join(2);self.assertFalse(worker.is_alive());self.assertEqual(errors,[])

    def test_unresponsive_reply_has_a_deadline(self):
        from unittest.mock import Mock
        connection=Mock();connection.poll.return_value=False
        with self.assertRaises(TimeoutError):DeadlineConnection(connection,timeout=.01).recv_bytes(1024)
        connection.recv_bytes.assert_not_called()

if __name__=='__main__':unittest.main()
