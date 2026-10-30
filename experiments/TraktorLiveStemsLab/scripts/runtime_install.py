"""Machine-local paths written by the Windows bootstrap."""
import json
from pathlib import Path


def read(lab):
    path = Path(lab) / 'runtime-install.json'
    return json.loads(path.read_text(encoding='utf-8')) if path.exists() else {}


def inference_python(lab):
    return Path(read(lab).get('inference_python', Path(lab) / 'cuda-python/Scripts/python.exe'))


def native_bridge(lab):
    return Path(read(lab).get('native_bridge', Path(__file__).resolve().parent.parent / 'native/build/live_bridge_v4.dll'))
