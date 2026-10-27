import hashlib,json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import xml.etree.ElementTree as ET
import add_live_tracks as target

class PreparationTests(unittest.TestCase):
    def test_metadata_cues_and_sources_preserved_with_deduplication(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);lab=root/'lab';lab.mkdir();source=root/'Über – Test.mp3';source.write_bytes(b'private fixture')
            before=hashlib.sha256(source.read_bytes()).hexdigest()
            nml=ET.Element('NML');collection=ET.SubElement(nml,'COLLECTION');entry=ET.SubElement(collection,'ENTRY',TITLE='Test',ARTIST='DJ',AUDIO_ID='retained only in original')
            target.set_location(entry,source);ET.SubElement(entry,'INFO',PLAYTIME_FLOAT='90',FLAGS='64');ET.SubElement(entry,'CUE_V2',NAME='Original cue',START='12345')
            original=root/'original.nml';ET.ElementTree(nml).write(original);original_before=original.read_bytes()
            def carrier(path,frames):path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'carrier')
            with patch.object(target,'lab_root',return_value=lab),patch.object(target,'create',side_effect=carrier):
                first=target.prepare([source],original);second=target.prepare([source],original)
            self.assertEqual(len(first),1);self.assertEqual(second,[])
            prepared=ET.parse(lab/'collection-live-test.nml');added=prepared.find('COLLECTION/ENTRY')
            self.assertEqual(added.get('TITLE'),'Test');self.assertEqual(added.find('CUE_V2').get('START'),'12345')
            self.assertTrue(added.get('AUDIO_ID'));self.assertEqual(added.find('INFO').get('FLAGS'),'64')
            self.assertEqual(target.location_path(added),Path(first[0]['source']))
            self.assertEqual(target.predict_sidecar(added.get('AUDIO_ID')).filename,Path(first[0]['carrier']).name)
            self.assertTrue(Path(first[0]['source']).is_relative_to(lab));self.assertTrue(Path(first[0]['carrier']).is_relative_to(lab))
            self.assertEqual(before,hashlib.sha256(source.read_bytes()).hexdigest());self.assertEqual(original_before,original.read_bytes())
            self.assertEqual(len(json.loads((lab/'live-tracks.json').read_text())),1)

if __name__=='__main__':unittest.main()
