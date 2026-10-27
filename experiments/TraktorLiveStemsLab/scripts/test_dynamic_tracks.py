import math,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import dynamic_tracks as target

class DynamicTrackTests(unittest.TestCase):
    def test_private_atomic_carrier_source_preserved_and_reused(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);source=root/'Original ä.flac';source.write_bytes(b'original audio')
            def create(path,frames):
                self.assertTrue(path.name.endswith('.partial'))
                path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(str(frames).encode())
            with patch.object(target,'lab_root',return_value=root/'lab'),patch.object(target,'create',side_effect=create) as writer:
                first=target.prepare_dynamic(source,90);second=target.prepare_dynamic(source,90)
                self.assertEqual(first,second);self.assertEqual(writer.call_count,1)
                self.assertEqual(source.read_bytes(),b'original audio')
                self.assertEqual(Path(first['source']),source)
                self.assertTrue(Path(first['carrier']).is_relative_to(root/'lab'))
                self.assertFalse(list((root/'lab').rglob('*.partial')))
                source.write_bytes(b'changed source audio')
                changed=target.prepare_dynamic(source,90)
                self.assertNotEqual(first['identity'],changed['identity'])

    def test_reject_unknown_duration_and_never_publish_incomplete_carrier(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);source=root/'Track.mp3';source.write_bytes(b'original')
            with patch.object(target,'lab_root',return_value=root/'lab'),patch.object(target,'probe_duration',side_effect=ValueError('unreadable')):
                for duration in [0,-1,math.nan,math.inf,3601]:
                    with self.assertRaises(ValueError):target.prepare_dynamic(source,duration)
                def fail(path,frames):
                    path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'incomplete');raise OSError('disk full')
                with patch.object(target,'create',side_effect=fail):
                    with self.assertRaises(OSError):target.prepare_dynamic(source,90)
                self.assertFalse(list((root/'lab').rglob('*.stem.mp4')))
                self.assertFalse(list((root/'lab').rglob('*.partial')))

    def test_unknown_traktor_duration_is_probed_from_original(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);source=root/'Track.m4a';source.write_bytes(b'original')
            def create(path,frames):
                self.assertEqual(frames,round(309.25*44100))
                path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'carrier')
            with patch.object(target,'lab_root',return_value=root/'lab'),patch.object(target,'probe_duration',return_value=309.25) as probe,patch.object(target,'create',side_effect=create):
                result=target.prepare_dynamic(source,-1)
                self.assertEqual(result['duration_seconds'],309.25)
                probe.assert_called_once_with(source)
                self.assertEqual(source.read_bytes(),b'original')

if __name__=='__main__':unittest.main()
