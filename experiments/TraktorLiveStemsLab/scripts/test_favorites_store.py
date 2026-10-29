import json
import tempfile
import unittest
from pathlib import Path
import favorites_store as store


class FavoritesStoreTests(unittest.TestCase):
    def test_persistence_unicode_and_backup(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary)
            self.assertEqual(store.read(root), ['']*12)
            slots=['$PLAYLIST/tGenres/tHärter']+['']*11
            store.save(root,slots)
            self.assertEqual(store.read(root),slots)
            changed=slots.copy();changed[11]='$EXPLORER/tM:/tMusik/tTechno'
            store.save(root,changed)
            self.assertEqual(store.read(root),changed)
            self.assertEqual(json.loads((root/'additional-favorites.previous.json').read_text(encoding='utf-8'))['slots'],slots)

    def test_invalid_update_preserves_targets(self):
        with tempfile.TemporaryDirectory() as root:
            slots=['$PLAYLIST/tA']+['']*11
            store.save(root,slots)
            for invalid in ([],['']*24,[None]*12,['bad\0path']+['']*11):
                with self.assertRaises(ValueError):store.save(root,invalid)
                self.assertEqual(store.read(root),slots)


if __name__=='__main__':unittest.main()
