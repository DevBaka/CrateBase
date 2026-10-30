"""Read archived sessions once into editable native playlists; archives untouched."""
import copy,hashlib,json,os,shutil,time,uuid
from pathlib import Path
import xml.etree.ElementTree as ET
from collection_store import private_path,default_source
from live_settings import atomic_write

def key(entry):
    p=entry.find('LOCATION')
    return None if p is None else '/:'.join([p.get('VOLUME','')]+[s for s in p.get('DIR','').split('/:') if s]+[p.get('FILE','')])

def import_histories(lab,is_running=lambda:False):
    lab=Path(lab);target=private_path(lab);journal=lab/'history-library.json'
    if is_running():raise RuntimeError('Traktor vor dem History-Import schließen.')
    state=json.loads(journal.read_text(encoding='utf-8')) if journal.exists() else {}
    folders={default_source(lab).parent/'History',target.parent/'History'}
    native=default_source(lab).parent.parent
    if native.is_dir():folders.update(native.glob('Traktor*/History'))
    pending=[];seen=set(state);errors=[]
    for path in sorted({p.resolve() for folder in folders if folder.is_dir() for p in folder.glob('*.nml')}):
        try:
            raw=path.read_bytes();identity=hashlib.sha256(raw).hexdigest()
            if identity in seen:continue
            history=ET.fromstring(raw)
            if history.find('.//PLAYLISTS//PLAYLIST') is None:raise ValueError('History-Playlist fehlt')
            pending.append((path,identity));seen.add(identity)
        except (OSError,ET.ParseError,ValueError) as error:errors.append({'path':str(path),'error':str(error)})
    if not pending:return {'imported':0,'errors':errors}
    original=target.read_bytes();tree=ET.ElementTree(ET.fromstring(original));root=tree.getroot();collection=root.find('COLLECTION')
    if collection is None:raise ValueError('Collection fehlt')
    known={key(e).casefold() for e in collection if key(e)}
    playlists=root.find('PLAYLISTS')
    if playlists is None:playlists=ET.SubElement(root,'PLAYLISTS')
    browser=playlists.find('NODE')
    if browser is None:browser=ET.SubElement(playlists,'NODE',TYPE='FOLDER',NAME='$ROOT')
    children=browser.find('SUBNODES')
    if children is None:children=ET.SubElement(browser,'SUBNODES')
    folder=next((n for n in children if n.get('TYPE')=='FOLDER' and n.get('NAME')=='Session Histories'),None)
    if folder is None:folder=ET.SubElement(children,'NODE',TYPE='FOLDER',NAME='Session Histories')
    sessions=folder.find('SUBNODES')
    if sessions is None:sessions=ET.SubElement(folder,'SUBNODES')
    imported_count=0
    for path,identity in pending:
        try:raw=path.read_bytes()
        except OSError as error:errors.append({'path':str(path),'error':str(error)});continue
        if hashlib.sha256(raw).hexdigest()!=identity:errors.append({'path':str(path),'error':'Datei während Import geändert'});continue
        history=ET.fromstring(raw);playlist=history.find('.//PLAYLISTS//PLAYLIST')
        for entry in history.findall('COLLECTION/ENTRY'):
            location=key(entry)
            if location and location.casefold() not in known:collection.append(copy.deepcopy(entry));known.add(location.casefold())
        playlist_id=uuid.uuid5(uuid.NAMESPACE_URL,'cratebase-history:'+identity).hex
        if not any(p.get('UUID')==playlist_id for p in root.findall('.//PLAYLIST')):
            node=ET.SubElement(sessions,'NODE',TYPE='PLAYLIST',NAME=path.stem)
            imported=copy.deepcopy(playlist);imported.set('UUID',playlist_id);imported.set('TYPE','LIST');imported.set('ENTRIES',str(len(imported.findall('ENTRY'))));node.append(imported)
        state[identity]={'source':str(path),'playlist_uuid':playlist_id,'imported':time.time()};imported_count+=1
    sessions.set('COUNT',str(len(sessions)));children.set('COUNT',str(len(children)));collection.set('ENTRIES',str(len(collection)))
    temp=target.with_name(target.name+'.'+uuid.uuid4().hex+'.histories')
    try:
        tree.write(temp,encoding='utf-8',xml_declaration=True)
        if is_running() or target.read_bytes()!=original:raise RuntimeError('Collection während History-Import geändert.')
        backups=lab/'collection-backups';backups.mkdir(exist_ok=True)
        shutil.copy2(target,backups/('before-histories-'+uuid.uuid4().hex+'.nml'))
        os.replace(temp,target);atomic_write(journal,state)
    finally:temp.unlink(missing_ok=True)
    return {'imported':imported_count,'errors':errors}
