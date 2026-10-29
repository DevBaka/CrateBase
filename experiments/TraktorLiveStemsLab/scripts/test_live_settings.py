import json,tempfile,threading,time,unittest
from pathlib import Path
from live_settings import read,save,DEFAULTS
from preparation_dispatcher import PreparationDispatcher

class SettingsTests(unittest.TestCase):
    def test_atomic_switch_persists_and_invalid_values_preserve_previous_file(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'settings.json'
            self.assertTrue(read(path)['enabled'])
            save(path,{**DEFAULTS,'enabled':False});before=path.read_bytes()
            self.assertFalse(read(path)['enabled'])
            for bad in [{'enabled':'false'},{'enabled':0},{'max_active_tracks':0},{'precision':'fast'},{'memory_budget_mb':999999}]:
                with self.assertRaises(ValueError):save(path,{**DEFAULTS,**bad})
                self.assertEqual(path.read_bytes(),before)
            self.assertFalse(list(Path(folder).glob('*.tmp')))

    def test_concurrent_writes_never_expose_partial_json(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'settings.json';save(path,DEFAULTS);errors=[]
            def write(enabled):
                try:
                    for _ in range(30):save(path,{**DEFAULTS,'enabled':enabled})
                except Exception as error:errors.append(error)
            workers=[threading.Thread(target=write,args=(value,)) for value in [True,False]]
            for worker in workers:worker.start()
            while any(w.is_alive() for w in workers):
                try:self.assertIs(type(read(path)['enabled']),bool)
                except Exception as error:errors.append(error)
            for worker in workers:worker.join()
            self.assertEqual(errors,[])

class PreparationTests(unittest.TestCase):
    def test_timeout_replies_once_and_overload_does_not_queue_unbounded_work(self):
        release=threading.Event();replies=[];condition=threading.Condition()
        def prepare(source,seconds):release.wait(2);return {'source':source}
        def reply(value):
            with condition:replies.append(value);condition.notify_all()
        dispatcher=PreparationDispatcher(prepare,timeout=.05)
        try:
            for _ in range(3):dispatcher.submit({'source':'x','seconds':1},reply)
            with condition:self.assertTrue(condition.wait_for(lambda:len(replies)==3,timeout=1))
            self.assertTrue(all('error' in value for value in replies))
            release.set();dispatcher.pool.shutdown(wait=True)
            self.assertEqual(len(replies),3)
        finally:release.set();dispatcher.close()

    def test_success_and_missing_file_return_explicit_results(self):
        replies=[];done=threading.Event()
        def prepare(source,seconds):
            if source=='missing':raise FileNotFoundError('missing audio')
            return {'source':source}
        dispatcher=PreparationDispatcher(prepare)
        try:
            for source in ['ok','missing']:
                done.clear()
                def reply(value):replies.append(value);done.set()
                dispatcher.submit({'source':source,'seconds':1},reply);self.assertTrue(done.wait(1))
            self.assertEqual(replies[0],{'track':{'source':'ok'}})
            self.assertIn('missing audio',replies[1]['error'])
        finally:dispatcher.close()

if __name__=='__main__':unittest.main()
