"""Small private carriers prepared on demand; source audio is read, never copied or edited."""
import math,re,subprocess,threading,uuid
from pathlib import Path
from add_live_tracks import identify,lab_root
from create_carrier import create

_lock=threading.Lock()
SUPPORTED={'.mp3','.flac','.wav','.aif','.aiff','.m4a','.mp4','.ogg'}

def probe_duration(source):
    ffmpeg=lab_root().parent/'tools'/'ffmpeg.exe'
    probe=subprocess.run([str(ffmpeg),'-nostdin','-hide_banner','-i',str(source),
                          '-t','0','-f','null','-'],capture_output=True,timeout=15,
                         creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
    match=re.search(rb'Duration: (\d+):(\d+):(\d+\.\d+)',probe.stderr)
    if not match:raise ValueError('Audiodauer nicht lesbar: '+source.name)
    h,m,s=map(float,match.groups());return h*3600+m*60+s

def prepare_dynamic(source,seconds):
    source=Path(source).resolve()
    if source.suffix.lower() not in SUPPORTED or source.name.lower().endswith('.stem.mp4'):
        raise ValueError('Kein unterstützter normaler Audiotrack')
    if not source.is_file():raise FileNotFoundError(source)
    seconds=float(seconds)
    if not math.isfinite(seconds) or seconds<=0:seconds=probe_duration(source)
    if not math.isfinite(seconds) or not 0<seconds<=3600:
        raise ValueError('Trackdauer noch nicht verfügbar oder länger als eine Stunde')
    lab=lab_root();identity=identify(source)[:24]
    carrier=lab/'dynamic-carriers'/f'{identity}.stem.mp4'
    with _lock:
        if not carrier.exists():
            partial=carrier.with_name(carrier.name+'.'+uuid.uuid4().hex+'.partial')
            try:create(partial,round(seconds*44100));partial.replace(carrier)
            finally:partial.unlink(missing_ok=True)
    return {'source':str(source),'original_source':str(source),'carrier':str(carrier),
            'cache':str(lab/'cache'/f'{identity}.cache'),'identity':identity,
            'reference_prefix':str(lab/'cache'/f'{identity}-native'),
            'title':source.stem,'duration_seconds':seconds,'dynamic':True}
