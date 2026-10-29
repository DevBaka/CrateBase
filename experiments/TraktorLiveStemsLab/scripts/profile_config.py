"""Location of the edition's writable Traktor configuration and collection."""
import json
from pathlib import Path

EDITION_NAME = 'Traktor 4.5 Cratebase Edition'


def directory(lab):
    lab = Path(lab)
    config = lab / 'sandbox.json'
    if config.exists():
        value = json.loads(config.read_text(encoding='utf-8')).get('profile_directory')
        if value:
            return Path(value)
    return lab / 'user/Documents/Native Instruments/Traktor 4.5.1'
