"""Additional native browser targets; never truncate or replace legacy TSI slots."""
import json
from pathlib import Path
from live_settings import atomic_write


def validate(slots):
    if not isinstance(slots, list) or len(slots) != 12:
        raise ValueError('Die zweite Favoritenreihe benötigt genau zwölf Ziele')
    if any(not isinstance(value, str) or len(value.encode('utf-8')) > 65536 or '\0' in value for value in slots):
        raise ValueError('Ungültiger Favoritenpfad')
    return list(slots)


def read(lab):
    path = Path(lab) / 'additional-favorites.json'
    return validate(json.loads(path.read_text(encoding='utf-8'))['slots']) if path.exists() else [''] * 12


def save(lab, slots):
    slots = validate(slots)
    path = Path(lab) / 'additional-favorites.json'
    if path.exists():
        previous = json.loads(path.read_text(encoding='utf-8'))
        validate(previous['slots'])
        if previous['slots'] == slots:
            return
        atomic_write(path.with_suffix('.previous.json'), previous)
    atomic_write(path, {'version': 1, 'slots': slots})
