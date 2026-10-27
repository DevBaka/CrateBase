"""Private named-pipe GPU service: one resident model, bounded concurrent track jobs.

No TCP listener, firewall rule, system service, or external Ollama installation.
Messages are authenticated JSON bytes, never pickle-deserialized commands.
"""
import argparse,hashlib,json,os,secrets,threading,time,traceback
from concurrent.futures import ThreadPoolExecutor
from multiprocessing.connection import Listener,Client
from pathlib import Path
from types import SimpleNamespace

def root():return Path(os.environ['LOCALAPPDATA'])/'Cratebase'/'traktor-lab'
def connection_info():
    lab=root();lab.mkdir(parents=True,exist_ok=True);key_file=lab/'service-auth.bin'
    if not key_file.exists():
        try:
            with key_file.open('xb') as f:f.write(secrets.token_bytes(32))
        except FileExistsError:pass
    suffix=hashlib.sha256(str(lab).lower().encode()).hexdigest()[:12]
    return r'\\.\pipe\Cratebase.TraktorStems.'+suffix,key_file.read_bytes()
def request(message):
    address,key=connection_info()
    with Client(address,family='AF_PIPE',authkey=key) as pipe:
        pipe.send_bytes(json.dumps(message).encode());return json.loads(pipe.recv_bytes(4*1024*1024))

class Service:
    def __init__(self):
        self.lab=root();self.jobs={};self.lock=threading.RLock();self.model_lock=threading.Lock();self.stop=threading.Event();self.pool=ThreadPoolExecutor(max_workers=4,thread_name_prefix='StemTrack')
        self.settings={'enabled':True,'gpu_index':0,'precision':'fp16','memory_budget_mb':4096,'max_active_tracks':4}
        self.model=None;self.phase='loading';self.error=None
        saved=self.lab/'live-stems-settings.json'
        if saved.exists():self.settings.update(json.loads(saved.read_text()))
    def load_model(self):
        import torch
        from demucs.pretrained import get_model
        os.environ['TORCH_HOME']=str(self.lab/'models');torch.set_num_threads(4)
        if not torch.cuda.is_available():raise RuntimeError('CUDA unavailable')
        index=int(self.settings['gpu_index']);torch.cuda.set_device(index)
        total=torch.cuda.get_device_properties(index).total_memory
        budget=int(self.settings['memory_budget_mb'])*1024*1024
        torch.cuda.set_per_process_memory_fraction(min(.9,budget/total),index)
        self.model=get_model('htdemucs').models[0].eval().to('cuda')
        dummy=torch.zeros((1,2,327680),device='cuda')
        with torch.inference_mode(),torch.autocast('cuda',enabled=self.settings['precision']=='fp16',dtype=torch.float16):self.model(dummy)
        torch.cuda.synchronize();self.gpu=torch.cuda.get_device_name(index);self.phase='ready'
    def snapshot(self):
        with self.lock:return {'phase':self.phase,'gpu':getattr(self,'gpu',None),'settings':self.settings.copy(),'error':self.error,'jobs':{k:{x:v for x,v in j.items() if x not in ('cancel','future')} for k,j in self.jobs.items()}}
    def dispatch(self,message):
        command=message.get('command')
        if command=='status':return self.snapshot()
        if command=='shutdown':self.stop.set();return {'ok':True}
        if command=='cancel':
            with self.lock:
                job=self.jobs.get(message.get('id'))
                if not job:return {'ok':False,'error':'Unknown job'}
                job['cancel'].set();return {'ok':True}
        if command=='settings':
            changed=message.get('values',{});allowed={'enabled','precision','memory_budget_mb','max_active_tracks'}
            if set(changed)-allowed:return {'ok':False,'error':'Unsupported setting'}
            if 'precision' in changed and changed['precision'] not in ('fp16','fp32'):return {'ok':False,'error':'Invalid precision'}
            if 'memory_budget_mb' in changed and not 1024<=int(changed['memory_budget_mb'])<=6144:return {'ok':False,'error':'Memory budget must be 1024–6144 MB'}
            if 'max_active_tracks' in changed and not 1<=int(changed['max_active_tracks'])<=4:return {'ok':False,'error':'Active track limit must be 1–4'}
            with self.lock:self.settings.update(changed);(self.lab/'live-stems-settings.json').write_text(json.dumps(self.settings,indent=2))
            return {'ok':True,'settings':self.settings.copy(),'restart_required':'memory_budget_mb' in changed}
        if command!='submit':return {'ok':False,'error':'Unknown command'}
        if self.phase!='ready' or not self.settings['enabled']:return {'ok':False,'error':'GPU service is not ready or disabled'}
        source=Path(message['source']).resolve()
        if not source.is_file():return {'ok':False,'error':'Source file unavailable'}
        identity=hashlib.sha256((str(source).lower()+':'+str(source.stat().st_mtime_ns)+':'+str(source.stat().st_size)).encode()).hexdigest()[:24]
        with self.lock:
            if identity in self.jobs:return {'ok':True,'id':identity,'cache':self.jobs[identity]['cache'],'existing':True}
            active=sum(j['phase'] not in ('complete','error','cancelled') for j in self.jobs.values())
            if active>=int(self.settings['max_active_tracks']):return {'ok':False,'error':'Active track limit reached; original audio remains available'}
            cache=self.lab/'cache'/f'{identity}.cache';report=self.lab/'cache'/f'{identity}.json';cache.parent.mkdir(exist_ok=True)
            supplied=message.get('cache')
            if supplied:
                cache=Path(supplied).resolve()
                if not cache.is_relative_to(self.lab.resolve()):return {'ok':False,'error':'Cache must stay inside the private lab'}
            reference=message.get('native_reference_prefix')
            if reference and not Path(reference).resolve().is_relative_to(self.lab.resolve()):return {'ok':False,'error':'Native reference must stay inside the private lab'}
            job={'id':identity,'source':str(source),'cache':str(cache),'phase':'queued','cancel':threading.Event(),'submitted':time.time()};self.jobs[identity]=job
            options=SimpleNamespace(source=source,cache=cache,report=report,seconds=message.get('seconds'),precision=self.settings['precision'],throttle=float(message.get('throttle',0)),start_signal=None,native_reference_prefix=Path(reference) if reference else None,reuse_cache=bool(message.get('reuse_cache',False)))
            job['future']=self.pool.submit(self.execute,identity,options)
            return {'ok':True,'id':identity,'cache':str(cache),'existing':False}
    def execute(self,identity,options):
        from gpu_worker import run_job
        job=self.jobs[identity]
        def status(value):
            with self.lock:job.update({k:v for k,v in value.items() if k not in ('windows',)})
        try:
            run_job(options,model=self.model,model_lock=self.model_lock,cancel_event=job['cancel'],onstatus=status)
        except InterruptedError:job.update(phase='cancelled',error='Cancelled')
        except BaseException as error:job.update(phase='error',error=str(error));traceback.print_exc()
    def serve(self):
        address,key=connection_info()
        with Listener(address,family='AF_PIPE',authkey=key) as listener:
            print(json.dumps({'phase':'loading','pipe':address}),flush=True)
            def load():
                try:self.load_model();print(json.dumps({'phase':'ready','gpu':self.gpu}),flush=True)
                except BaseException as error:self.phase='error';self.error=str(error);traceback.print_exc()
            threading.Thread(target=load,name='GPUInitialize',daemon=True).start()
            while not self.stop.is_set():
                try:
                    with listener.accept() as pipe:
                        message=json.loads(pipe.recv_bytes(1024*1024));response=self.dispatch(message);pipe.send_bytes(json.dumps(response).encode())
                except (EOFError,ValueError,KeyError) as error:print('Bad local request:',error,flush=True)
            for job in self.jobs.values():job['cancel'].set()
            self.pool.shutdown(wait=True,cancel_futures=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('command',choices=['serve','status','shutdown','submit']);p.add_argument('source',nargs='?');p.add_argument('--seconds',type=float);p.add_argument('--native-reference-prefix');a=p.parse_args()
    if a.command=='serve':Service().serve()
    else:
        message={'command':a.command}
        if a.source:message.update(source=a.source,seconds=a.seconds,native_reference_prefix=a.native_reference_prefix)
        print(json.dumps(request(message),indent=2))
