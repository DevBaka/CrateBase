import json,tempfile,unittest
from pathlib import Path
from collection_store import import_collection,ensure_collection,private_path,read_state,sync_collection

NML=b'''<?xml version="1.0"?><NML VERSION="19"><COLLECTION ENTRIES="1"><ENTRY TITLE="Original" AUDIO_ID="keep"><LOCATION VOLUME="M:" DIR="/:Musik/:" FILE="track.flac"/><CUE_V2 NAME="Cue" START="12.5"/></ENTRY></COLLECTION><PLAYLISTS><NODE TYPE="FOLDER" NAME="$ROOT"><SUBNODES><NODE TYPE="FOLDER" NAME="Techno"><SUBNODES><NODE TYPE="PLAYLIST" NAME="Club"><PLAYLIST><ENTRY><PRIMARYKEY TYPE="TRACK" KEY="keep"/></ENTRY></PLAYLIST></NODE></SUBNODES></NODE></SUBNODES></NODE></PLAYLISTS><UNKNOWN DATA="preserve"/></NML>'''

class CollectionTests(unittest.TestCase):
    def setup_paths(self,root):
        source=root/'original/Traktor 4.5.1/collection.nml';source.parent.mkdir(parents=True);source.write_bytes(NML)
        lab=root/'lab';lab.mkdir();(lab/'sandbox.json').write_text(json.dumps({'original_documents_ni':str(source.parent.parent)}))
        return lab,source

    def test_real_collection_copied_exactly_original_unchanged_and_legacy_backed_up(self):
        with tempfile.TemporaryDirectory() as folder:
            lab,source=self.setup_paths(Path(folder));target=private_path(lab);target.parent.mkdir(parents=True);target.write_bytes(b'old fixture')
            result=ensure_collection(lab)
            self.assertEqual(result['tracks'],1);self.assertEqual(result['playlists'],1);self.assertEqual(result['folders'],2)
            self.assertEqual(source.read_bytes(),NML);self.assertEqual(target.read_bytes(),NML)
            self.assertEqual(Path(result['backup']).read_bytes(),b'old fixture')
            target.write_bytes(NML.replace(b'Original',b'Private edit'))
            ensure_collection(lab);self.assertIn(b'Private edit',target.read_bytes())
            refreshed=import_collection(lab,source)
            self.assertIn(b'Private edit',Path(refreshed['backup']).read_bytes())
            self.assertEqual(target.read_bytes(),NML)

    def test_invalid_or_running_import_preserves_existing_copy(self):
        with tempfile.TemporaryDirectory() as folder:
            lab,source=self.setup_paths(Path(folder));ensure_collection(lab);before=private_path(lab).read_bytes();state=read_state(lab)
            source.write_bytes(b'<NML>broken')
            with self.assertRaises(Exception):import_collection(lab,source)
            self.assertEqual(private_path(lab).read_bytes(),before);self.assertEqual(read_state(lab),state)
            source.write_bytes(NML)
            with self.assertRaises(RuntimeError):import_collection(lab,source,lambda:True)
            checks=iter([False,True])
            with self.assertRaises(RuntimeError):import_collection(lab,source,lambda:next(checks))
            with self.assertRaises(ValueError):import_collection(lab,private_path(lab))
            self.assertEqual(private_path(lab).read_bytes(),before)
            self.assertFalse(list(lab.rglob('*.importing')))

class CollectionSyncTests(unittest.TestCase):
    def test_both_directions_and_concurrent_changes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);lab=root/'lab';source=root/'collection.nml';source.write_bytes(NML)
            import_collection(lab,source);private=private_path(lab)
            private.write_bytes(NML.replace(b'Original',b'Edition edit'))
            self.assertEqual(sync_collection(lab)['status'],'exported')
            self.assertEqual(private.read_bytes(),source.read_bytes())
            source.write_bytes(NML.replace(b'Original',b'Traktor edit'))
            self.assertEqual(sync_collection(lab)['status'],'imported')
            private.write_bytes(NML.replace(b'Original',b'Both local'))
            source.write_bytes(NML.replace(b'Original',b'Both remote'))
            self.assertEqual(sync_collection(lab)['status'],'conflict')
            self.assertIn(b'Both local',private.read_bytes());self.assertIn(b'Both remote',source.read_bytes())
            self.assertEqual(sync_collection(lab,lambda:True)['status'],'busy')
    def test_broken_nml_is_not_exported(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);lab=root/'lab';source=root/'collection.nml';source.write_bytes(NML)
            import_collection(lab,source);private_path(lab).write_bytes(b'<NML>broken')
            with self.assertRaises(Exception):sync_collection(lab)
            self.assertEqual(source.read_bytes(),NML)

if __name__=='__main__':unittest.main()
