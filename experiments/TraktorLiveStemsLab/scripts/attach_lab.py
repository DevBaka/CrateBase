"""Attach an additional experiment only to the active, sandbox-owned copied process."""
import argparse,json,os,time
from pathlib import Path
import frida,psutil

p=argparse.ArgumentParser();p.add_argument('script',type=Path);p.add_argument('--seconds',type=int,default=120);a=p.parse_args()
lab=Path(os.environ['LOCALAPPDATA'])/'Cratebase'/'traktor-lab';active=json.loads((lab/'active-session.json').read_text())
expected=(lab/'binaries'/'Traktor Pro 4.exe').resolve()
if Path(psutil.Process(active['pid']).exe()).resolve()!=expected:raise RuntimeError('Not the owned lab executable')
session=frida.attach(active['pid']);output=(lab/'logs'/f'extra-{a.script.stem}-{time.strftime("%H%M%S")}.jsonl').open('w')
def message(m,d):
    output.write(json.dumps(m)+'\n');output.flush()
    if m.get('type')=='error':print(m,flush=True)
script=session.create_script(a.script.read_text());script.on('message',message);script.load();print('Attached',a.script.name,'log',output.name,flush=True)
try:
    end=time.monotonic()+a.seconds
    while time.monotonic()<end and not session.is_detached:time.sleep(.5)
except KeyboardInterrupt:pass
finally:
    if not session.is_detached:script.unload();session.detach()
    output.close()
