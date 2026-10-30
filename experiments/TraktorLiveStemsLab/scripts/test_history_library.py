import tempfile,unittest,xml.etree.ElementTree as ET
from pathlib import Path
from test_collection_store import CollectionTests,NML
from collection_store import ensure_collection,private_path
from history_library import import_histories

class HistoryTests(unittest.TestCase):
    def test_import_once_preserves_archives_and_native_deletion(self):
        with tempfile.TemporaryDirectory() as directory:
            lab,source=CollectionTests().setup_paths(Path(directory));ensure_collection(lab)
            folder=source.parent/'History';folder.mkdir();archive=folder/'history.nml';archive.write_bytes(NML)
            self.assertEqual(import_histories(lab)['imported'],1);self.assertEqual(archive.read_bytes(),NML)
            tree=ET.parse(private_path(lab));sessions=tree.find('.//NODE[@NAME="Session Histories"]/SUBNODES')
            self.assertEqual(len(sessions),1);self.assertEqual(sessions[0].find('PLAYLIST').get('TYPE'),'LIST')
            self.assertEqual(import_histories(lab)['imported'],0)
            sessions.remove(sessions[0]);tree.write(private_path(lab))
            self.assertEqual(import_histories(lab)['imported'],0)
            self.assertEqual(len(ET.parse(private_path(lab)).find('.//NODE[@NAME="Session Histories"]/SUBNODES')),0)

    def test_invalid_file_does_not_block_others(self):
        with tempfile.TemporaryDirectory() as directory:
            lab,source=CollectionTests().setup_paths(Path(directory));ensure_collection(lab)
            folder=source.parent/'History';folder.mkdir();(folder/'bad.nml').write_bytes(b'<NML>');(folder/'good.nml').write_bytes(NML)
            result=import_histories(lab);self.assertEqual(result['imported'],1);self.assertEqual(len(result['errors']),1);self.assertEqual(source.read_bytes(),NML)

if __name__=='__main__':unittest.main()
