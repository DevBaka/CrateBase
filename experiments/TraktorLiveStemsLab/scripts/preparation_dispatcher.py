"""Bounded control-thread preparation: exactly one reply, including a timeout."""
import threading
from concurrent.futures import ThreadPoolExecutor

class PreparationDispatcher:
    def __init__(self,prepare,timeout=2):
        self.prepare=prepare;self.timeout=timeout;self.slots=threading.BoundedSemaphore(2)
        self.pool=ThreadPoolExecutor(max_workers=2,thread_name_prefix='CarrierPrepare')

    def submit(self,payload,reply):
        if not self.slots.acquire(blocking=False):
            reply({'error':'Stem-Vorbereitung ausgelastet; normaler Track wird geladen'});return
        lock=threading.Lock();finished=False
        def complete(value):
            nonlocal finished
            with lock:
                if finished:return
                finished=True
            try:reply(value)
            except Exception:pass # The owned Traktor session may already have ended.
        timer=threading.Timer(self.timeout,lambda:complete({'error':'Stem-Vorbereitung dauert zu lange; normaler Track wird geladen'}));timer.daemon=True;timer.start()
        def run():
            try:complete({'track':self.prepare(payload['source'],payload['seconds'])})
            except Exception as error:complete({'error':str(error)})
            finally:timer.cancel();self.slots.release()
        self.pool.submit(run)

    def close(self):self.pool.shutdown(wait=False)
