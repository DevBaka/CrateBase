import tempfile,unittest
from pathlib import Path
from unittest.mock import Mock,patch
import gpu_service
from live_settings import read

class ServiceControlTests(unittest.TestCase):
    def test_invalid_settings_do_not_change_service_or_disk(self):
        with tempfile.TemporaryDirectory() as folder,patch.object(gpu_service,'root',return_value=Path(folder)):
            service=gpu_service.Service()
            try:
                self.assertTrue(service.dispatch({'command':'settings','values':{'enabled':False}})['ok'])
                path=Path(folder)/'live-stems-settings.json';before=path.read_bytes()
                for values in [{'enabled':'false'},{'memory_budget_mb':None},{'max_active_tracks':True},{'precision':'unknown'}]:
                    self.assertFalse(service.dispatch({'command':'settings','values':values})['ok'])
                    self.assertFalse(service.settings['enabled']);self.assertEqual(path.read_bytes(),before)
                self.assertFalse(service.dispatch([])['ok'])
                self.assertFalse(read(path)['enabled'])
            finally:service.pool.shutdown(wait=True)

    def test_failed_separation_can_be_retried_without_restarting_service(self):
        with tempfile.TemporaryDirectory() as folder,patch.object(gpu_service,'root',return_value=Path(folder)):
            service=gpu_service.Service();original_pool=service.pool;service.pool=Mock();service.phase='ready'
            source=Path(folder)/'test.flac';source.write_bytes(b'test source')
            try:
                submitted=service.dispatch({'command':'submit','source':str(source)})
                identity=submitted['id'];service.jobs[identity].update(phase='error',error='simulated GPU failure')
                self.assertFalse(service.dispatch({'command':'submit','source':str(source)})['ok'])
                result=service.dispatch({'command':'submit','source':str(source),'retry_failed':True})
                self.assertTrue(result['ok']);self.assertFalse(result['existing'])
                self.assertEqual(service.pool.submit.call_count,2)
            finally:original_pool.shutdown(wait=True)

if __name__=='__main__':unittest.main()
