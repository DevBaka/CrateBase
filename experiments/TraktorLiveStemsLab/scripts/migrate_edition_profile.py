"""Copy the existing private profile into Documents/Native Instruments.

Never merges into an existing directory, changes the installed Traktor keys,
or removes the old private profile. Activate the copy only after validation.
"""
import hashlib
import json
import os
import shutil
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path
from live_settings import atomic_write
from collection_store import summarize
from profile_config import EDITION_NAME, directory


def migrate(lab, is_running=lambda: False):
    lab=Path(lab).resolve()
    if is_running():raise RuntimeError('Traktor vor der Profilübernahme schließen')
    config_path=lab/'sandbox.json'
    config=json.loads(config_path.read_text(encoding='utf-8'))
    source=directory(lab).resolve()
    target=(Path(config['original_documents_ni'])/EDITION_NAME).resolve()
    if source==target:return target
    if target.exists():raise FileExistsError('Zielordner existiert bereits: '+str(target))
    temporary=target.with_name(target.name+'.preparing-'+uuid.uuid4().hex[:8])
    shutil.copytree(source,temporary)
    # Copying must not lose any collection entries/cues or source files.
    original_hash=hashlib.sha256((source/'collection.nml').read_bytes()).digest()
    if hashlib.sha256((temporary/'collection.nml').read_bytes()).digest()!=original_hash:
        raise RuntimeError('Collection-Kopie stimmt nicht überein')
    summarize(temporary/'collection.nml')
    for name in ('Traktor Settings.tsi','Settings.tsi'):
        path=temporary/name
        if not path.exists():continue
        tree=ET.parse(path)
        for entry in tree.iter('Entry'):
            value=entry.get('Value')
            if value and str(source).lower() in value.lower():
                # Path spelling in copied settings is case-preserving.
                import re
                entry.set('Value',re.sub(re.escape(str(source)),lambda _:str(target),value,flags=re.I))
            if entry.get('Name')=='Browser.Dir.Root':entry.set('Value',str(target))
        tree.write(path,encoding='utf-8',xml_declaration=True)
    if is_running():raise RuntimeError('Traktor wurde gestartet; Profil wurde nicht aktiviert')
    temporary.rename(target)
    # Only the private redirected registry is updated, never the installed app.
    if os.name=='nt':
        import winreg
        def update(key_path):
            try:key=winreg.OpenKey(winreg.HKEY_CURRENT_USER,key_path,0,winreg.KEY_READ|winreg.KEY_WRITE)
            except FileNotFoundError:return
            with key:
                children,values,_=winreg.QueryInfoKey(key)
                for i in range(values):
                    name,value,kind=winreg.EnumValue(key,i)
                    if name=='RootDirectory':winreg.SetValueEx(key,name,0,kind,str(target))
                    elif isinstance(value,str) and str(source) in value:winreg.SetValueEx(key,name,0,kind,value.replace(str(source),str(target)))
                for i in range(children):update(key_path+'\\'+winreg.EnumKey(key,i))
        update(r'Software\Cratebase\TraktorLiveStemsLab')
    config['legacy_profile_directory']=str(source)
    config['profile_directory']=str(target)
    atomic_write(config_path,config)
    (target/'Cratebase Edition.txt').write_text(
        'Traktor 4.5 Cratebase Edition\n\nCollection, Einstellungen, History, Stripes und Transients dieser Edition.\n'
        'Start über den Cratebase-Live-Stems-Starter.\n'
        'Die normale Traktor-Installation verwendet weiterhin ihren eigenen Versionsordner.\n',encoding='utf-8')
    return target


if __name__=='__main__':
    import psutil
    lab=Path(os.environ['LOCALAPPDATA'])/'Cratebase/traktor-lab'
    print(migrate(lab,lambda:any((p.info['name'] or '').lower()=='traktor pro 4.exe' for p in psutil.process_iter(['name']))))
