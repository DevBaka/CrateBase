"""Copy a real Traktor NML into the isolated session without editing its contents."""
import hashlib,json,os,shutil,time,uuid
from pathlib import Path
import xml.etree.ElementTree as ET
from live_settings import atomic_write
from profile_config import directory as profile_directory

def summarize(path):
    tracks=playlists=folders=0;collection=False;first=True
    for event,element in ET.iterparse(path,events=('start','end')):
        if first:
            first=False
            if element.tag!='NML':raise ValueError('Keine Traktor-NML-Datei')
        if event!='end':continue
        if element.tag=='COLLECTION':collection=True
        elif element.tag=='ENTRY':
            if element.find('LOCATION') is not None:tracks+=1
            element.clear()
        elif element.tag=='NODE':
            playlists+=element.get('TYPE')=='PLAYLIST';folders+=element.get('TYPE')=='FOLDER'
    if not collection:raise ValueError('Die NML enthält keine Collection')
    return {'tracks':tracks,'playlists':playlists,'folders':folders}

def private_path(lab):return profile_directory(lab)/'collection.nml'
def state_path(lab):return Path(lab)/'collection-source.json'

def read_state(lab):
    path=state_path(lab)
    return json.loads(path.read_text(encoding='utf-8')) if path.exists() else None

def default_source(lab):
    state=read_state(lab)
    if state:return Path(state['source'])
    config=json.loads((Path(lab)/'sandbox.json').read_text(encoding='utf-8'))
    return Path(config['original_documents_ni'])/'Traktor 4.5.1/collection.nml'

def import_collection(lab,source,is_running=lambda:False):
    lab=Path(lab).resolve();source=Path(source).resolve();target=private_path(lab).resolve()
    if source.is_relative_to(lab) or source.is_relative_to(target.parent):raise ValueError('Bitte die originale Traktor-Collection außerhalb des Testbereichs auswählen')
    if is_running():raise RuntimeError('Traktor bitte vor der Collection-Übernahme schließen')
    if not source.is_file():raise FileNotFoundError('Traktor-Collection nicht gefunden: '+str(source))
    target.parent.mkdir(parents=True,exist_ok=True)
    temporary=target.with_name('collection.'+uuid.uuid4().hex+'.importing')
    before=source.stat()
    try:
        digest=hashlib.sha256()
        with source.open('rb') as incoming,temporary.open('xb') as outgoing:
            while block:=incoming.read(1024*1024):outgoing.write(block);digest.update(block)
            outgoing.flush();os.fsync(outgoing.fileno())
        after=source.stat()
        if (before.st_size,before.st_mtime_ns)!=(after.st_size,after.st_mtime_ns):
            raise RuntimeError('Die Original-Collection wurde während des Kopierens geändert. Bitte erneut versuchen.')
        counts=summarize(temporary)
        if is_running():raise RuntimeError('Traktor wurde inzwischen gestartet; die Collection wurde nicht ersetzt')
        backup=None
        if target.exists():
            backup=lab/'collection-backups'/('collection-'+time.strftime('%Y%m%d-%H%M%S')+'-'+uuid.uuid4().hex[:8]+'.nml')
            backup.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(target,backup)
        temporary.replace(target)
        state={'source':str(source),'imported':time.time(),'source_sha256':digest.hexdigest(),
               'backup':str(backup) if backup else None,**counts}
        atomic_write(state_path(lab),state);return state
    finally:temporary.unlink(missing_ok=True)

def ensure_collection(lab,is_running=lambda:False):
    state=read_state(lab)
    if state and private_path(lab).is_file():return state
    # One-time upgrade from the old tiny fixture collection. Later launches
    # preserve library edits made in the private instance until explicit refresh.
    return import_collection(lab,default_source(lab),is_running)


def sync_collection(lab,is_running=lambda:False):
    """Three-way whole-NML sync; retain both versions on concurrent edits."""
    if is_running():return {'status':'busy'}
    state=read_state(lab)
    if not state:return {'status':'unconfigured'}
    original=Path(state['source']);edition=private_path(lab)
    if not original.is_file() or not edition.is_file():return {'status':'missing'}
    def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
    upstream=digest(original);local=digest(edition);base=state['source_sha256']
    if upstream==local:
        if base!=local:state['source_sha256']=local;atomic_write(state_path(lab),state)
        return {'status':'equal'}
    if upstream!=base and local!=base:
        report={'status':'conflict','original':str(original),'edition':str(edition),'original_sha256':upstream,'edition_sha256':local}
        atomic_write(Path(lab)/'collection-sync-conflict.json',report)
        return report
    source,target,expected=(edition,original,upstream) if upstream==base else (original,edition,local)
    counts=summarize(source)
    temporary=target.with_name(target.name+'.'+uuid.uuid4().hex+'.syncing')
    try:
        shutil.copy2(source,temporary)
        copied=digest(temporary)
        if copied!=digest(source) or is_running() or digest(target)!=expected:
            return {'status':'changed_during_sync'}
        backups=Path(lab)/'collection-backups';backups.mkdir(parents=True,exist_ok=True)
        shutil.copy2(target,backups/('sync-'+time.strftime('%Y%m%d-%H%M%S')+'-'+uuid.uuid4().hex[:8]+'.nml'))
        os.replace(temporary,target)
        state.update(source_sha256=copied,**counts);atomic_write(state_path(lab),state)
        (Path(lab)/'collection-sync-conflict.json').unlink(missing_ok=True)
        return {'status':'exported' if target==original else 'imported',**counts}
    finally:temporary.unlink(missing_ok=True)
