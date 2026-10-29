"""Prepare isolated settings/registry copies; never edit installed Traktor or user collection."""
import hashlib
import json
import os
import shutil
import winreg
from pathlib import Path
from profile_config import directory as profile_directory

lab=Path(os.environ["LOCALAPPDATA"])/"Cratebase"/"traktor-lab"
original=Path(os.environ["USERPROFILE"])/"Documents"/"Native Instruments"/"Traktor 4.5.1"
install=Path(r"C:\Program Files\Native Instruments\Traktor Pro 4")
root=profile_directory(lab)
root.mkdir(parents=True,exist_ok=True)


def clone_registry(source,dest):
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,source) as incoming,winreg.CreateKey(winreg.HKEY_CURRENT_USER,dest) as outgoing:
        subkeys,values,_=winreg.QueryInfoKey(incoming)
        for i in range(values):
            name,value,kind=winreg.EnumValue(incoming,i)
            if any(term in name.lower() for term in ("serial","license","activation")):continue
            if name=="RootDirectory":value=str(root)
            winreg.SetValueEx(outgoing,name,0,kind,value)
        for i in range(subkeys):
            name=winreg.EnumKey(incoming,i)
            if any(term in name.lower() for term in ("serial","license","activation")):continue
            clone_registry(source+"\\"+name,dest+"\\"+name)


def main():
    snapshots=[]
    for name in ("collection.nml","Traktor Settings.tsi","Settings.tsi"):
        source=original/name
        snapshots.append({"path":str(source),"sha256":hashlib.sha256(source.read_bytes()).hexdigest(),"size":source.stat().st_size})
        if name!="collection.nml" and not (root/name).exists():shutil.copy2(source,root/name)
    # Preserve UI/controller configuration but use a separate empty test collection.
    if not (root/"collection.nml").exists():
        (root/"collection.nml").write_text('<?xml version="1.0" encoding="UTF-8"?><NML VERSION="19"><HEAD COMPANY="Native Instruments" PROGRAM="Traktor"/><COLLECTION ENTRIES="0"/><PLAYLISTS><NODE TYPE="FOLDER" NAME="$ROOT"><SUBNODES COUNT="0"/></NODE></PLAYLISTS></NML>',encoding="utf-8")
    for folder in ("Stripes","Transients","History","Settings","Logs","Stems"):(root/folder).mkdir(exist_ok=True)
    for key in ("Traktor Pro 4","Traktor Pro 4 4.5.1"):
        clone_registry("Software\\Native Instruments\\"+key,"Software\\Cratebase\\TraktorLiveStemsLab\\"+key)
    clone_registry("Software\\Classes\\traktor","Software\\Cratebase\\TraktorLiveStemsLab\\Classes\\traktor")
    shutil.copytree(install/"Resources64",lab/"binaries"/"Resources64",dirs_exist_ok=True)
    for name in ("libbeatport.dll","crashpad_handler.exe"):shutil.copy2(install/name,lab/"binaries"/name)
    (lab/"original-data-baseline.json").write_text(json.dumps(snapshots,indent=2),encoding="utf-8")
    previous=json.loads((lab/'sandbox.json').read_text(encoding='utf-8')) if (lab/'sandbox.json').exists() else {}
    config={"lab":str(lab),"original_documents_ni":str(original.parent),"lab_documents_ni":str(lab/'user/Documents/Native Instruments'),"original_local_ni":str(Path(os.environ["LOCALAPPDATA"])/"Native Instruments"),"lab_local_ni":str(lab/"user"/"Local"/"Native Instruments"),"original_roaming_ni":str(Path(os.environ["APPDATA"])/"Native Instruments"),"lab_roaming_ni":str(lab/"user"/"Roaming"/"Native Instruments")}
    for field in ('profile_directory','legacy_profile_directory'):
        if field in previous:config[field]=previous[field]
    (lab/"sandbox.json").write_text(json.dumps(config,indent=2),encoding="utf-8")
    print("Sandbox prepared:",root)
    print("Original collection/settings hashes recorded; originals remain unmodified.")


if __name__=="__main__":main()
