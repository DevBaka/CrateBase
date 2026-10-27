"""Measure native HTDemucs CUDA block inference before attempting any Traktor audio integration."""
import argparse
import json
import os
import subprocess
import time
from pathlib import Path


def main():
    parser=argparse.ArgumentParser();parser.add_argument("source",type=Path);parser.add_argument("output",type=Path);parser.add_argument("--ffmpeg",required=True);parser.add_argument("--seconds",type=float,default=60);args=parser.parse_args()
    import numpy as np
    import torch
    import soundfile as sf
    from demucs.pretrained import get_model
    torch.set_num_threads(4)
    if not torch.cuda.is_available():raise RuntimeError("CUDA unavailable")
    args.output.mkdir(parents=True,exist_ok=True)
    raw=subprocess.run([args.ffmpeg,"-nostdin","-v","error","-i",str(args.source),"-t",str(args.seconds),"-f","f32le","-ar","44100","-ac","2","pipe:1"],check=True,capture_output=True).stdout
    audio=np.frombuffer(raw,dtype="<f4").reshape(-1,2).T.copy();sr=44100
    load=time.perf_counter();model=get_model("htdemucs").models[0].eval().to("cuda");torch.cuda.synchronize();load=time.perf_counter()-load
    print(json.dumps({"gpu":torch.cuda.get_device_name(0),"torch":torch.__version__,"sources":model.sources,"load_seconds":load}),flush=True)
    results=[]
    for seconds in (1.5,3.,6.,10.):
        frames=int(seconds*sr);chunk=torch.from_numpy(audio[:,:frames]).unsqueeze(0).to("cuda");reference=chunk.mean(1,keepdim=True);mean=reference.mean();std=reference.std().clamp_min(1e-5)
        chunk=(chunk-mean)/std
        for precision in ("fp32","amp-fp16"):
            runs=[];last=None
            try:
                torch.cuda.reset_peak_memory_stats()
                for iteration in range(4):
                    torch.cuda.synchronize();start=time.perf_counter()
                    with torch.inference_mode(),torch.autocast("cuda",enabled=precision!="fp32",dtype=torch.float16):output=model(chunk)
                    torch.cuda.synchronize();elapsed=time.perf_counter()-start
                    last=output.float()*std+mean/4
                    if not torch.isfinite(last).all():raise RuntimeError("Nonfinite GPU output")
                    if iteration:runs.append(elapsed)
                result={"seconds":seconds,"precision":precision,"runs":runs,"mean_inference":sum(runs)/len(runs),"real_time_factor":sum(runs)/len(runs)/seconds,"peak_gpu_mb":torch.cuda.max_memory_allocated()/1024**2,"peak_abs":last.abs().max().item()}
                results.append(result);print(json.dumps(result),flush=True)
                if seconds==6 and precision=="fp32":
                    for index,name in enumerate(model.sources):sf.write(args.output/(name+".wav"),last[0,index].cpu().numpy().T,sr,subtype="PCM_24")
                    sf.write(args.output/"original.wav",audio[:,:frames].T,sr,subtype="PCM_24")
            except (RuntimeError,torch.cuda.OutOfMemoryError) as error:
                result={"seconds":seconds,"precision":precision,"error":str(error)};results.append(result);print(json.dumps(result),flush=True);torch.cuda.empty_cache()
    (args.output/"benchmark.json").write_text(json.dumps({"source":str(args.source),"torch":torch.__version__,"gpu":torch.cuda.get_device_name(0),"load_seconds":load,"results":results},indent=2),encoding="utf-8")


if __name__=="__main__":main()
