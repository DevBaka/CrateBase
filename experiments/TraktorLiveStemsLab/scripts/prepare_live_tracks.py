"""Create a private miniature collection with copied inputs and native sidecar carriers.

Only reads the user's collection/music. Does not replace the active private
collection; the launcher installs this prepared NML after stopping its own copy.
"""
import argparse,copy,hashlib,json,os,shutil,sys
from pathlib import Path
import xml.etree.ElementTree as ET
from create_carrier import create
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'vendor'))
from traktor_stem_bridge import predict_sidecar

def location_path(entry):
    loc=entry.find('LOCATION')
    if loc is None:return None
    return Path(loc.get('VOLUME','')+loc.get('DIR','').replace('/:',os.sep)+loc.get('FILE',''))
def main():
    p=argparse.ArgumentParser();p.add_argument('--collection',type=Path,default=Path.home()/'Documents'/'Native Instruments'/'Traktor 4.5.1'/'collection.nml');a=p.parse_args()
    lab=Path(os.environ['LOCALAPPDATA'])/'Cratebase'/'traktor-lab';inputs=lab/'inputs';inputs.mkdir(exist_ok=True)
    wanted='rave all night (original mix).m4a';selected=[];secondary=None
    for _,entry in ET.iterparse(a.collection,events=('end',)):
        if entry.tag!='ENTRY' or entry.find('LOCATION') is None:continue
        path=location_path(entry)
        if path and entry.get('AUDIO_ID') and path.is_file():
            if path.name.lower()==wanted and not any(x.get('AUDIO_ID')==entry.get('AUDIO_ID') for x in selected):selected.insert(0,copy.deepcopy(entry))
            elif secondary is None and path.suffix.lower()=='.mp3' and any(word in str(path).lower() for word in ('hardstyle','hard techno','hardtekk')):secondary=copy.deepcopy(entry)
        entry.clear()
    if secondary is not None:selected.append(secondary)
    if not selected:raise RuntimeError('No suitable existing AUDIO_ID entries found; original collection unchanged')
    nml=ET.Element('NML',VERSION='19');ET.SubElement(nml,'HEAD',COMPANY='Native Instruments',PROGRAM='Traktor')
    collection=ET.SubElement(nml,'COLLECTION',ENTRIES=str(len(selected)));manifest=[];keys=[]
    for index,entry in enumerate(selected):
        source=location_path(entry)
        source_identity=hashlib.sha256((str(source).lower()+':'+str(source.stat().st_mtime_ns)+':'+str(source.stat().st_size)).encode()).hexdigest()[:12]
        target=inputs/f'{source_identity}-{source.name}'
        if not target.exists():shutil.copy2(source,target)
        elif source.stat().st_size!=target.stat().st_size:raise RuntimeError('Private input differs; refusing overwrite')
        loc=entry.find('LOCATION');directory='/:'+ '/:'.join(target.parent.parts[1:])+'/:'
        loc.set('DIR',directory);loc.set('FILE',target.name);loc.set('VOLUME',target.drive);loc.set('VOLUMEID','')
        info=entry.find('INFO')
        if info is None:info=ET.SubElement(entry,'INFO')
        info.set('FLAGS',str(int(info.get('FLAGS','0'))|64))
        seconds=float(info.get('PLAYTIME_FLOAT') or info.get('PLAYTIME') or '0')
        if seconds<=0:raise RuntimeError('Missing native track duration')
        prediction=predict_sidecar(entry.get('AUDIO_ID'));carrier=lab/'stems'/f'{prediction.shard:03d}'/prediction.filename
        if not carrier.exists():create(carrier,round(seconds*44100))
        identity=hashlib.sha256((str(target).lower()+':'+str(target.stat().st_mtime_ns)+':'+str(target.stat().st_size)).encode()).hexdigest()[:24]
        cache=lab/'cache'/f'{identity}.cache';cache.parent.mkdir(exist_ok=True)
        manifest.append({'source':str(target),'carrier':str(carrier),'cache':str(cache),'identity':identity,'reference_prefix':str(lab/'cache'/f'{identity}-native'),'title':entry.get('TITLE'),'artist':entry.get('ARTIST'),'duration_seconds':seconds,'audio_id':entry.get('AUDIO_ID')})
        collection.append(entry);keys.append(target.drive+directory+target.name)
    playlists=ET.SubElement(nml,'PLAYLISTS');node=ET.SubElement(playlists,'NODE',TYPE='FOLDER',NAME='$ROOT');children=ET.SubElement(node,'SUBNODES',COUNT='1');pn=ET.SubElement(children,'NODE',TYPE='PLAYLIST',NAME='LAB Live GPU Stems');playlist=ET.SubElement(pn,'PLAYLIST',ENTRIES=str(len(keys)),TYPE='LIST',UUID='CEED0FABE00000000000000000000002')
    for key in keys:ET.SubElement(ET.SubElement(playlist,'ENTRY'),'PRIMARYKEY',TYPE='TRACK',KEY=key)
    ET.ElementTree(nml).write(lab/'collection-live-test.nml',encoding='utf-8',xml_declaration=True)
    (lab/'live-tracks.json').write_text(json.dumps(manifest,indent=2));print(json.dumps([{k:v for k,v in x.items() if k!='audio_id'} for x in manifest],indent=2))

if __name__=='__main__':main()
