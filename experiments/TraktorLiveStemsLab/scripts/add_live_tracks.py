"""Prepare user-selected audio for the isolated lab; never modifies source music."""
import argparse,base64,copy,hashlib,json,os,re,shutil,subprocess,uuid
from pathlib import Path
import xml.etree.ElementTree as ET
from create_carrier import create
from prepare_live_tracks import location_path,predict_sidecar

def lab_root():return Path(os.environ['LOCALAPPDATA'])/'Cratebase'/'traktor-lab'
def identify(path):
    info=path.stat()
    return hashlib.sha256((str(path).lower()+':'+str(info.st_mtime_ns)+':'+str(info.st_size)).encode()).hexdigest()
def atomic_json(path,data):
    temp=path.with_suffix('.tmp');temp.write_text(json.dumps(data,indent=2,ensure_ascii=False),encoding='utf-8');temp.replace(path)
def set_location(entry,path):
    loc=entry.find('LOCATION')
    if loc is None:loc=ET.SubElement(entry,'LOCATION')
    directory='/:'+ '/:'.join(path.parent.parts[1:])+'/:'
    loc.attrib.update(DIR=directory,FILE=path.name,VOLUME=path.drive,VOLUMEID='')
    return path.drive+directory+path.name
def prepare(paths,collection=None,onprogress=None):
    lab=lab_root();manifest_path=lab/'live-tracks.json'
    manifest=json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.exists() else []
    selected={str(Path(p).resolve()).casefold():Path(p).resolve() for p in paths}
    metadata={}
    if collection and Path(collection).is_file():
        for _,entry in ET.iterparse(collection,events=('end',)):
            if entry.tag!='ENTRY' or entry.find('LOCATION') is None:continue
            original=location_path(entry)
            if original and str(original.resolve()).casefold() in selected:metadata[str(original.resolve()).casefold()]=copy.deepcopy(entry)
            entry.clear()
    target_nml=lab/'collection-live-test.nml'
    if target_nml.exists():tree=ET.parse(target_nml);nml=tree.getroot();tracks=nml.find('COLLECTION')
    else:
        nml=ET.Element('NML',VERSION='19');ET.SubElement(nml,'HEAD',COMPANY='Native Instruments',PROGRAM='Traktor');tracks=ET.SubElement(nml,'COLLECTION');tree=ET.ElementTree(nml)
    keys=[];added=[]
    for index,(key,original) in enumerate(selected.items()):
        if onprogress:onprogress(f'{index+1}/{len(selected)}: {original.name}')
        if not original.is_file():raise FileNotFoundError(original)
        source_id=identify(original)[:12];private=lab/'inputs'/f'{source_id}-{original.name}';private.parent.mkdir(exist_ok=True)
        if not private.exists():
            partial=private.with_name(private.name+'.copying');shutil.copy2(original,partial);partial.replace(private)
        elif private.stat().st_size!=original.stat().st_size:raise ValueError('Private Kopie unterscheidet sich: '+private.name)
        identity=identify(private)[:24]
        if any(t.get('identity')==identity for t in manifest):continue
        entry=copy.deepcopy(metadata.get(key))
        if entry is None:entry=ET.Element('ENTRY',TITLE=original.stem,ARTIST='')
        info=entry.find('INFO')
        if info is None:info=ET.SubElement(entry,'INFO')
        seconds=float(info.get('PLAYTIME_FLOAT') or info.get('PLAYTIME') or '0')
        if seconds<=0:
            ffmpeg=lab.parent/'tools'/'ffmpeg.exe'
            probe=subprocess.run([str(ffmpeg),'-nostdin','-hide_banner','-i',str(private),'-t','0','-f','null','-'],capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
            match=re.search(rb'Duration: (\d+):(\d+):(\d+\.\d+)',probe.stderr)
            if not match:raise ValueError('Dauer nicht lesbar: '+original.name)
            h,m,s=map(float,match.groups());seconds=h*3600+m*60+s
            info.set('PLAYTIME_FLOAT',str(seconds));info.set('PLAYTIME',str(int(seconds)))
        # Use the same native sidecar route as the verified deck integration.
        # A carrier LOCATION alone is treated as an ordinary track by Traktor.
        audio_id=entry.get('AUDIO_ID')
        try:prediction=predict_sidecar(audio_id or '')
        except (ValueError,TypeError):
            # Private collection identity only, not an asserted audio fingerprint.
            audio_id=base64.b64encode(hashlib.sha512(identity.encode()).digest()*4).decode()
            prediction=predict_sidecar(audio_id)
        entry.set('AUDIO_ID',audio_id)
        carrier=lab/'stems'/f'{prediction.shard:03d}'/prediction.filename
        if not carrier.exists():create(carrier,round(seconds*44100))
        info.set('FLAGS',str(int(info.get('FLAGS','0'))|64))
        location_key=set_location(entry,private);tracks.append(entry);keys.append(location_key)
        item={'source':str(private),'original_source':str(original),'carrier':str(carrier),'cache':str(lab/'cache'/f'{identity}.cache'),'identity':identity,'reference_prefix':str(lab/'cache'/f'{identity}-native'),'title':entry.get('TITLE',original.stem),'artist':entry.get('ARTIST',''),'duration_seconds':seconds,'audio_id':audio_id}
        manifest.append(item);added.append(item)
    tracks.set('ENTRIES',str(len(tracks)))
    if keys:
        playlists=nml.find('PLAYLISTS')
        if playlists is None:playlists=ET.SubElement(nml,'PLAYLISTS')
        root=playlists.find('NODE')
        if root is None:root=ET.SubElement(playlists,'NODE',TYPE='FOLDER',NAME='$ROOT')
        children=root.find('SUBNODES')
        if children is None:children=ET.SubElement(root,'SUBNODES')
        node=ET.SubElement(children,'NODE',TYPE='PLAYLIST',NAME='Live Stems - hinzugefügt')
        playlist=ET.SubElement(node,'PLAYLIST',ENTRIES=str(len(keys)),TYPE='LIST',UUID=uuid.uuid4().hex.upper())
        for key in keys:ET.SubElement(ET.SubElement(playlist,'ENTRY'),'PRIMARYKEY',TYPE='TRACK',KEY=key)
        children.set('COUNT',str(len(children)))
    temp=target_nml.with_suffix('.tmp');tree.write(temp,encoding='utf-8',xml_declaration=True);temp.replace(target_nml);atomic_json(manifest_path,manifest)
    return added

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('files',nargs='+',type=Path);p.add_argument('--collection',type=Path);a=p.parse_args()
    print(json.dumps(prepare(a.files,a.collection,print),indent=2,ensure_ascii=False))
