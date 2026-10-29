"""Authenticated local JSON pipe messages with bounded authentication/read waits."""
import time
from multiprocessing.connection import answer_challenge,deliver_challenge

class DeadlineConnection:
    def __init__(self,connection,timeout=3):self.connection=connection;self.deadline=time.monotonic()+timeout
    def recv_bytes(self,maxlength=None):
        remaining=max(0,self.deadline-time.monotonic())
        if not self.connection.poll(remaining):raise TimeoutError('GPU-Dienst antwortet nicht rechtzeitig')
        return self.connection.recv_bytes(maxlength)
    def send_bytes(self,data):return self.connection.send_bytes(data)

def connect(address,key,timeout=3):
    from multiprocessing.connection import PipeClient
    connection=PipeClient(address)
    try:
        pipe=DeadlineConnection(connection,timeout)
        answer_challenge(pipe,key);deliver_challenge(pipe,key)
        return connection,pipe
    except BaseException:connection.close();raise

def authenticate_server(connection,key,timeout=3):
    pipe=DeadlineConnection(connection,timeout)
    deliver_challenge(pipe,key);answer_challenge(pipe,key)
    return pipe
