"""Discover this PC's licensed Traktor install; preserve existing edition data."""
import hashlib, json, os, shutil, winreg
from pathlib import Path

SUPPORTED = '1b4db54ab51d63e1f662971c74b3f37e75df7c19291844f5ac7ec8ccc04373ba'


def main():
    lab = Path(os.environ['LOCALAPPDATA']) / 'Cratebase/traktor-lab'
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders') as key:
        documents = Path(os.path.expandvars(winreg.QueryValueEx(key, 'Personal')[0]))
    ni = documents / 'Native Instruments'
    config_file = lab / 'sandbox.json'
    previous = json.loads(config_file.read_text(encoding='utf-8')) if config_file.exists() else {}
    edition = Path(previous.get('profile_directory', ni / 'Traktor 4.5 Cratebase Edition'))
    target = lab / 'binaries/Traktor Pro 4.exe'
    candidates = [Path(os.environ.get('ProgramW6432', r'C:\Program Files')) / 'Native Instruments/Traktor Pro 4/Traktor Pro 4.exe']
    for hive in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
        try:
            with winreg.OpenKey(hive, r'Software\Native Instruments\Traktor Pro 4') as key:
                candidates.insert(0, Path(winreg.QueryValueEx(key, 'InstallDir')[0]) / 'Traktor Pro 4.exe')
        except OSError:
            pass
    installed = next((p for p in candidates if p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest() == SUPPORTED), None)
    if not target.exists() or hashlib.sha256(target.read_bytes()).hexdigest() != SUPPORTED:
        if installed is None:
            raise RuntimeError('Live-Stems benoetigt die lizenzierte Windows-x64-Version Traktor Pro 4.5.1.21. '
                               'Kein kompatibles installiertes Programm gefunden; andere Versionen werden nicht veraendert.')
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(installed.parent, target.parent, dirs_exist_ok=True)
    # Never refresh binaries beneath an already running Traktor process.
    profiles = [p for p in ni.glob('Traktor*') if p != edition and 'Cratebase' not in p.name and (p / 'collection.nml').is_file()]
    source = max(profiles, key=lambda p: (p / 'collection.nml').stat().st_mtime) if profiles else None
    edition.mkdir(parents=True, exist_ok=True)
    if source:
        for name in ('Traktor Settings.tsi', 'Settings.tsi'):
            if (source / name).exists() and not (edition / name).exists():
                shutil.copy2(source / name, edition / name)
    if not (edition / 'Traktor Settings.tsi').exists():
        raise RuntimeError('Traktor einmal normal starten und schliessen, damit seine Einstellungen vorhanden sind.')
    for name in ('logs', 'fixtures', 'stems', 'recordings'):
        (lab / name).mkdir(exist_ok=True)
    for name in ('Stripes', 'Transients', 'History', 'Settings', 'Logs', 'Stems'):
        (edition / name).mkdir(exist_ok=True)
    config = {**previous, 'lab': str(lab), 'profile_directory': str(edition),
        'original_documents_ni': str(ni), 'lab_documents_ni': str(lab / 'user/Documents/Native Instruments'),
        'original_local_ni': str(Path(os.environ['LOCALAPPDATA']) / 'Native Instruments'),
        'lab_local_ni': str(lab / 'user/Local/Native Instruments'),
        'original_roaming_ni': str(Path(os.environ['APPDATA']) / 'Native Instruments'),
        'lab_roaming_ni': str(lab / 'user/Roaming/Native Instruments')}
    if source:
        config['source_collection'] = str(source / 'collection.nml')
    config_file.write_text(json.dumps(config, indent=2), encoding='utf-8')
    from prepare_sandbox import clone_registry
    # clone_registry's destination is obtained from the now-persisted config.
    for name in ('Traktor Pro 4', 'Traktor Pro 4 4.5.1'):
        destination = 'Software\\Cratebase\\TraktorLiveStemsLab\\' + name
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, destination):
                pass
        except FileNotFoundError:
            try:
                clone_registry('Software\\Native Instruments\\' + name, destination)
            except FileNotFoundError:
                pass
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER, destination) as key:
            winreg.SetValueEx(key, 'RootDirectory', 0, winreg.REG_SZ, str(edition))
    classes = r'Software\Cratebase\TraktorLiveStemsLab\Classes\traktor'
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, classes):
            pass
    except FileNotFoundError:
        try:
            clone_registry(r'Software\Classes\traktor', classes)
        except FileNotFoundError:
            pass
    baseline = lab / 'original-data-baseline.json'
    if not baseline.exists():
        baseline.write_text(json.dumps([{'path': str(p), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest(), 'size': p.stat().st_size}
            for p in ([source / n for n in ('collection.nml', 'Traktor Settings.tsi', 'Settings.tsi')] if source else []) if p.exists()], indent=2))
    manifest = lab / 'live-tracks.json'
    if not manifest.exists():
        manifest.write_text('[]')
    from collection_store import import_collection, read_state, summarize, state_path
    from live_settings import atomic_write
    if source and not read_state(lab):
        if (edition / 'collection.nml').exists():
            # An edition copied by Syncthing/LAN may already contain edits.
            # Unknown common ancestor => report conflict, never overwrite it.
            atomic_write(state_path(lab), {'source': str(source / 'collection.nml'),
                'source_sha256': None, 'backup': None, **summarize(edition / 'collection.nml')})
        else:
            import_collection(lab, source / 'collection.nml')


if __name__ == '__main__':
    main()
