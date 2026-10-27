"""Incremental CUDA separation worker. Never runs inside Traktor or its audio thread."""
import argparse,contextlib,json,math,os,struct,subprocess,time
from pathlib import Path
import numpy as np
from stem_cache import StemCache,BLOCK,RATE

def arguments():
    p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('cache',type=Path)
    p.add_argument('--seconds',type=float);p.add_argument('--precision',choices=['fp32','fp16'],default='fp16')
    p.add_argument('--throttle',type=float,default=0);p.add_argument('--start-signal',type=Path)
    p.add_argument('--report',type=Path);p.add_argument('--native-reference-prefix',type=Path)
    return p.parse_args()

def run_job(a,model=None,model_lock=None,cancel_event=None,onstatus=None):
    lab=Path(os.environ['LOCALAPPDATA'])/'Cratebase'/'traktor-lab'
    os.environ.setdefault('TORCH_HOME',str(lab/'models'))
    import torch
    from demucs.pretrained import get_model
    torch.set_num_threads(4)
    if model is not None:torch.cuda.set_device(next(model.parameters()).device)
    if not torch.cuda.is_available():raise RuntimeError('CUDA is not available; no silent CPU fallback')
    report=a.report or a.cache.with_suffix('.json');started=time.perf_counter();metrics={'source':str(a.source),'cache':str(a.cache),'precision':a.precision,'windows':[]}
    def status(phase,**extra):
        metrics.update(phase=phase,elapsed_seconds=time.perf_counter()-started,**extra)
        report.parent.mkdir(parents=True,exist_ok=True);temporary=report.with_suffix('.partial')
        temporary.write_text(json.dumps(metrics,indent=2));temporary.replace(report)
        print(json.dumps({'phase':phase,**extra}),flush=True)
        if onstatus:onstatus(metrics.copy())
    status('decoding')
    command=[str(lab.parent/'tools'/'ffmpeg.exe'),'-nostdin','-v','error','-i',str(a.source)]
    if a.seconds:command+=['-t',str(a.seconds)]
    command+=['-f','f32le','-ar',str(RATE),'-ac','2','pipe:1']
    raw=subprocess.run(command,check=True,capture_output=True).stdout
    audio=np.frombuffer(raw,dtype='<f4').reshape(-1,2).T.copy();del raw
    if not audio.shape[-1] or not np.isfinite(audio).all():raise ValueError('Source audio is empty or nonfinite')
    if a.native_reference_prefix:
        from decoder_alignment import measure_offset,align_audio
        reference_meta=json.loads(a.native_reference_prefix.with_suffix('.json').read_text())
        native_reference=np.fromfile(str(a.native_reference_prefix)+'-left.f32',dtype='<f4')[:65536]
        reference=native_reference
        if reference_meta['rate']!=RATE:
            from torchaudio.functional import resample
            reference=resample(torch.from_numpy(native_reference),reference_meta['rate'],RATE).numpy()
        expected_start=round(reference_meta.get('reference_start_frames',0)*RATE/reference_meta['rate'])
        alignment=measure_offset(reference,audio[0],expected_start);metrics['decoder_alignment']=alignment
        if not alignment['verified']:raise RuntimeError('Native/FFmpeg alignment could not be verified')
        audio=align_audio(audio,audio.shape[-1],alignment['offset_samples'])
        if np.max(np.abs(native_reference*32768-np.round(native_reference*32768)))<1e-3:
            np.clip(audio,-1,32767/32768,out=audio);metrics['native_pcm16_range']=True
    frames=audio.shape[-1]
    if getattr(a,'reuse_cache',False) and a.cache.exists():
        from decoder_alignment import align_audio
        cache=StemCache(a.cache)
        if abs(cache.frames-frames)>max(4096,frames*.01):raise RuntimeError('Native source/cache duration mismatch')
        audio=align_audio(audio,cache.frames,0);frames=cache.frames
    else:cache=StemCache(a.cache,frames)
    if not struct.unpack_from('<i',cache.map,72)[0]:cache.store_original(audio)
    metrics.update(total_frames=frames,duration_seconds=frames/RATE,decode_seconds=time.perf_counter()-started,gpu=torch.cuda.get_device_name(0))
    if bool(np.all(cache.ready)):
        # Published cache blocks are immutable and can still be mapped by decks.
        # Reopening a completed track must not run the whole model again.
        metrics.update(reused_complete_cache=True,total_blocks=cache.blocks)
        status('complete',ready_blocks=cache.blocks,generation_seconds=0)
        cache.close();return
    status('loading_model')
    begin=time.perf_counter()
    if model is None:model=get_model('htdemucs').models[0].eval().to('cuda')
    torch.cuda.synchronize()
    metrics.update(model_load_seconds=time.perf_counter()-begin,sources=model.sources)
    if model.sources!=['drums','bass','other','vocals']:raise RuntimeError('Unexpected model stem order')
    if a.start_signal:
        status('waiting_for_start_signal')
        while not a.start_signal.exists():time.sleep(.1)
    # 7.43 s context, 3.99 s hop; neighboring outputs crossfade over 0.37 s.
    context=327680;hop=172*BLOCK;overlap=8*BLOCK;left=(context-hop)//2
    windows=math.ceil(frames/hop);done=set();accumulator=np.zeros((4,2,frames),dtype=np.float32);weights=np.zeros(frames,dtype=np.float32)
    expected=np.zeros(cache.blocks,dtype=np.int32);completed=np.zeros(cache.blocks,dtype=np.int32)
    ranges=[]
    for index in range(windows):
        start=max(0,index*hop-overlap);end=min(frames,(index+1)*hop+overlap);ranges.append((start,end))
        expected[start//BLOCK:math.ceil(end/BLOCK)]+=1
    generation_begin=time.perf_counter();last_requested=-1
    try:
        while len(done)<windows:
            if cancel_event and cancel_event.is_set():raise InterruptedError('Separation cancelled')
            requested=cache.requested();wanted=(requested*BLOCK)//hop if requested>=0 else -1
            priorities=[wanted,wanted-1,wanted+1] if requested!=last_requested else []
            candidates=[i for i in priorities if 0<=i<windows and i not in done]
            index=candidates[0] if candidates else next(i for i in range(windows) if i not in done)
            if candidates:last_requested=requested
            core=index*hop;context_start=core-left;source_start=max(0,context_start);source_end=min(frames,context_start+context)
            chunk=np.zeros((2,context),dtype=np.float32)
            chunk[:,source_start-context_start:source_end-context_start]=audio[:,source_start:source_end]
            tensor=torch.from_numpy(chunk).unsqueeze(0).to('cuda');reference=tensor.mean(1,keepdim=True);mean=reference.mean();std=reference.std().clamp_min(1e-5)
            normalized=(tensor-mean)/std
            with model_lock or contextlib.nullcontext():
                torch.cuda.synchronize();begin=time.perf_counter()
                with torch.inference_mode(),torch.autocast('cuda',enabled=a.precision=='fp16',dtype=torch.float16):separated=model(normalized)
                separated=(separated.float()*std+mean/4)[0].cpu().numpy();elapsed=time.perf_counter()-begin
            if not np.isfinite(separated).all():raise RuntimeError('Nonfinite GPU output')
            start,end=ranges[index];raw_start=core-overlap;length=hop+2*overlap
            weight=np.ones(length,dtype=np.float32);weight[:2*overlap]=np.linspace(0,1,2*overlap,dtype=np.float32);weight[-2*overlap:]=np.linspace(1,0,2*overlap,dtype=np.float32)
            weight=weight[start-raw_start:end-raw_start]
            accumulator[:,:,start:end]+=separated[:,:,start-context_start:end-context_start]*weight
            weights[start:end]+=weight;done.add(index)
            first=start//BLOCK;last=math.ceil(end/BLOCK);completed[first:last]+=1
            for block in range(first,last):
                if cache.ready[block] or completed[block]!=expected[block]:continue
                block_start=block*BLOCK;block_end=min(frames,block_start+BLOCK)
                result=accumulator[:,:,block_start:block_end]/np.maximum(weights[block_start:block_end],1e-8)
                cache.publish(block_start,result)
            metrics['windows'].append({'index':index,'inference_and_transfer_seconds':elapsed,'ready_blocks':int(cache.ready.sum())})
            status('separating',completed_windows=len(done),total_windows=windows,ready_blocks=int(cache.ready.sum()),total_blocks=cache.blocks,generation_seconds=time.perf_counter()-generation_begin)
            if a.throttle:time.sleep(a.throttle)
        metrics.update(peak_gpu_mb=torch.cuda.max_memory_allocated()/1024**2,peak_stem=float(np.max(np.abs(cache.data))),first_window_seconds=metrics['windows'][0]['inference_and_transfer_seconds'])
        status('complete',ready_blocks=int(cache.ready.sum()),generation_seconds=time.perf_counter()-generation_begin)
    except BaseException:
        struct.pack_into('<i',cache.map,56,1);status('error');raise
    finally:cache.close()

if __name__=='__main__':run_job(arguments())
