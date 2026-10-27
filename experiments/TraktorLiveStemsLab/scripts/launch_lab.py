"""Launch only the copied executable, inject data redirection, log, and preserve original state."""
import argparse
import hashlib
import json
import os
import signal
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import frida


def main():
    parser=argparse.ArgumentParser();parser.add_argument("--seconds",type=int,default=1800);parser.add_argument("--trace",type=Path);parser.add_argument('--live',action='store_true');args=parser.parse_args()
    lab=Path(os.environ["LOCALAPPDATA"])/"Cratebase"/"traktor-lab";binary=lab/"binaries"/"Traktor Pro 4.exe"
    expected="1b4db54ab51d63e1f662971c74b3f37e75df7c19291844f5ac7ec8ccc04373ba"
    if hashlib.sha256(binary.read_bytes()).hexdigest()!=expected:raise RuntimeError("Lab binary differs from verified 4.5.1 baseline")
    import psutil
    if any(p.info['name'] and p.info['name'].lower()=='traktor pro 4.exe' for p in psutil.process_iter(['name'])):
        raise RuntimeError('Close the existing Traktor process before starting the isolated lab')
    baseline=json.loads((lab/'original-data-baseline.json').read_text(encoding='utf-8'))
    session_hashes={item['path']:hashlib.sha256(Path(item['path']).read_bytes()).hexdigest() for item in baseline}
    pool=ThreadPoolExecutor(max_workers=1,thread_name_prefix='GPURequest')
    def submit_generation(payload):
        from gpu_service import request
        try:print('GPU request:',json.dumps(request({'command':'submit',**{k:v for k,v in payload.items() if k!='type'}})),flush=True)
        except Exception as error:print('GPU request failed; original fallback remains:',str(error),flush=True)
    config=json.loads((lab/"sandbox.json").read_text(encoding="utf-8"));script_path=Path(__file__).with_name("sandbox.js")
    device=frida.get_local_device();pid=device.spawn([str(binary)],cwd=str(binary.parent));session=device.attach(pid)
    logs=lab/"logs";logs.mkdir(exist_ok=True);output=(logs/(time.strftime("sandbox-%Y%m%d-%H%M%S")+".jsonl")).open("w",encoding="utf-8")
    ready=False;live_ready=False;alive=True
    def message(message,data):
        nonlocal ready,live_ready
        output.write(json.dumps(message,ensure_ascii=False)+"\n");output.flush()
        if message.get("type")=="error":print("INSTRUMENTATION ERROR:",message.get("stack",message),flush=True)
        elif message.get("payload",{}).get("type")=="sandbox_ready":ready=True;print("Sandbox hooks ready, PID",pid,flush=True)
        elif message.get('payload',{}).get('type')=='generation_requested':pool.submit(submit_generation,message['payload'])
        elif message.get('payload',{}).get('type')=='prepare_dynamic_track':
            payload=message['payload']
            # This request occurs on a browser/load control thread, never an audio callback.
            try:
                from dynamic_tracks import prepare_dynamic
                track=prepare_dynamic(payload['source'],payload['seconds'])
                extra.post({'type':'dynamic_prepared:'+str(payload['request_id']),'track':track})
            except Exception as error:
                extra.post({'type':'dynamic_prepared:'+str(payload['request_id']),'error':str(error)})
        elif message.get('payload',{}).get('type') in ('live_session_ready','live_reader_registered','live_error'):
            if message['payload']['type']=='live_session_ready':live_ready=True
            print(json.dumps(message['payload']),flush=True)
    try:
        script=session.create_script(script_path.read_text(encoding="utf-8").replace("LAB_CONFIG",json.dumps(config)));script.on("message",message);script.load()
        for _ in range(20):
            if ready:break
            time.sleep(.05)
    except Exception:
        device.kill(pid);raise
    if not ready:device.kill(pid);raise RuntimeError("Sandbox initialization not acknowledged; process not resumed")
    extra=None
    if args.trace:extra=session.create_script(args.trace.read_text(encoding="utf-8"));extra.on("message",message);extra.load()
    if args.live:
        manifest=json.loads((lab/'live-tracks.json').read_text(encoding='utf-8'))
        settings_file=lab/'live-stems-settings.json'
        live_settings=json.loads(settings_file.read_text(encoding='utf-8')) if settings_file.exists() else {}
        live_config={'tracks':manifest,'enabled':live_settings.get('enabled',True),'dynamic_loading':True,'dll':str(Path(__file__).resolve().parent.parent/'native'/'build'/'live_bridge_v4.dll')}
        extra=session.create_script(Path(__file__).with_name('live_session.js').read_text(encoding='utf-8').replace('LIVE_CONFIG',json.dumps(live_config)))
        extra.on('message',message);extra.load()
        for _ in range(40):
            if live_ready:break
            time.sleep(.05)
        if not live_ready:
            device.kill(pid)
            raise RuntimeError('Live-Stem-Anbindung nicht bereit; Testkopie wurde nicht gestartet. Siehe Instrumentierungsprotokoll.')
    session.on("detached",lambda *args:print("Lab process detached:",args[0] if args else "",flush=True))
    (lab/"active-session.json").write_text(json.dumps({"pid":pid,"binary":str(binary),"log":output.name}),encoding="utf-8")
    device.resume(pid);print("Lab resumed; original collection protected. Log:",output.name,flush=True)
    try:
        deadline=time.monotonic()+args.seconds
        settings_stamp=None
        while time.monotonic()<deadline:
            if session.is_detached:break
            if args.live and settings_file.exists():
                stamp=settings_file.stat().st_mtime_ns
                if stamp!=settings_stamp:
                    try:
                        current_settings=json.loads(settings_file.read_text(encoding='utf-8'))
                        extra.post({'type':'live_settings','enabled':bool(current_settings.get('enabled',True))})
                        settings_stamp=stamp
                    except (OSError,ValueError):pass
            time.sleep(.5)
    except KeyboardInterrupt:pass
    finally:
        try:device.kill(pid)
        except (frida.ProcessNotFoundError,frida.InvalidOperationError):pass
        try:session.detach()
        except frida.InvalidOperationError:pass
        output.close()
        pool.shutdown(wait=False,cancel_futures=True)
        for path,expected_hash in session_hashes.items():
            current=hashlib.sha256(Path(path).read_bytes()).hexdigest()
            if current!=expected_hash:raise RuntimeError("Original data changed during lab session: "+path)
        print("Verified: original collection and settings hashes unchanged.",flush=True)


if __name__=="__main__":main()
