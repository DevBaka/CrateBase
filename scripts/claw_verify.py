"""Bounded on-device verification/start helper for the MSI Claw repair."""
import argparse,hashlib,json,os,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SCRIPTS=ROOT/'experiments/TraktorLiveStemsLab/scripts'
LAB=Path(os.environ['LOCALAPPDATA'])/'Cratebase/traktor-lab'
sys.path.insert(0,str(SCRIPTS))

def status():
 import psutil
 print('Traktor:',[(p.pid,p.info['exe']) for p in psutil.process_iter(['name','exe']) if (p.info['name'] or '').lower()=='traktor pro 4.exe'])
 from gpu_service import request
 try:
  state=request({'command':'status'})
  print('Service:',json.dumps({k:state.get(k) for k in ('phase','gpu','error','inference_python')},ensure_ascii=True))
  print('Jobs:',json.dumps([{k:j.get(k) for k in ('source','phase','ready_blocks','total_blocks','render_audio_seconds_per_second')} for j in state.get('jobs',{}).values()]))
 except OSError as e:print('Service unavailable:',e)
 logs=sorted((LAB/'logs').glob('sandbox-*.jsonl'),key=lambda p:p.stat().st_mtime)
 if logs:
  print('Log:',logs[-1])
  rows=[json.loads(line) for line in logs[-1].read_text(encoding='utf-8').splitlines() if line.strip()]
  events=[r.get('payload',{}) for r in rows if r.get('type')=='send']
  stats=[r for r in events if r.get('type')=='live_stats']
  print('Audio:',json.dumps(stats[-1] if stats else {}))
  errors=[r for r in events if r.get('type')=='live_error']
  print('Errors:',json.dumps(errors[-5:]))

def verify():
 manifest=json.loads((LAB/'runtime-install.json').read_text())
 digest=hashlib.sha256((SCRIPTS.parent/'native/live_bridge.c').read_bytes()).hexdigest()[:16]
 assert Path(manifest['native_bridge']).name==f'live_bridge-{digest}.dll','DLL/source mismatch'
 print('DLL/source identity:',digest,flush=True)
 node=Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'
 subprocess.run([str(node),'--check',str(SCRIPTS/'live_session.js')],check=True)
 subprocess.run([str(node),'--test',str(SCRIPTS/'test_live_associations.js')],check=True)
 for name in ['test_live_bridge.py','test_downstream_bridge.py','test_live_settings.py','test_gpu_service_control.py','test_dynamic_tracks.py','test_render_priority.py']:
  subprocess.run([sys.executable,'-m','unittest','discover','-s',str(SCRIPTS),'-p',name],cwd=ROOT,check=True)

def start():
 import psutil
 assert not any((p.info['name'] or '').lower()=='traktor pro 4.exe' for p in psutil.process_iter(['name'])),'Close Traktor normally first; active decks are never terminated'
 from gpu_service import request
 assert request({'command':'status'})['phase']=='ready','GPU service not ready'
 with (LAB/'logs/claw-fixed-launch-out.log').open('a',encoding='utf-8') as out,(LAB/'logs/claw-fixed-launch-err.log').open('a',encoding='utf-8') as err:
  process=subprocess.Popen([sys.executable,str(SCRIPTS/'launch_lab.py'),'--live','--favorites'],cwd=ROOT,stdin=subprocess.DEVNULL,stdout=out,stderr=err,creationflags=subprocess.CREATE_NO_WINDOW)
 print('Launcher:',process.pid)

if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('action',choices=['status','verify','start']);args=parser.parse_args()
 globals()[args.action]()
