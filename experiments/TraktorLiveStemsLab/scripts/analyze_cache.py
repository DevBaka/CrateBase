"""Objective cache continuity and reconstruction measurements; writes listening excerpts."""
import argparse,json,math
from pathlib import Path
import numpy as np
import soundfile as sf
from stem_cache import StemCache,BLOCK,RATE

p=argparse.ArgumentParser();p.add_argument('cache',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
cache=StemCache(a.cache);a.output.mkdir(parents=True,exist_ok=True)
stems=cache.data.transpose(1,2,0,3).reshape(4,2,-1)[:,:,:cache.frames].copy()
original=cache.original.copy();mix=stems.sum(0);error=original-mix
power=lambda x:float(np.mean(x.astype(np.float64)**2))
metrics={'frames':cache.frames,'all_blocks_ready':bool(np.all(cache.ready)),'finite':bool(np.isfinite(stems).all()),'peak':float(np.max(np.abs(stems))),'reconstruction_snr_db':10*math.log10(power(original)/max(power(error),1e-20)),'stems':[]}
hop=172*BLOCK;boundaries=np.arange(hop,cache.frames,hop)
for i,name in enumerate(('drums','bass','other','vocals')):
    derivative=np.abs(np.diff(stems[i],axis=1))
    boundary_steps=derivative[:,boundaries-1] if len(boundaries) else np.zeros((2,0))
    metrics['stems'].append({'name':name,'rms':math.sqrt(power(stems[i])),'peak':float(np.max(np.abs(stems[i]))),'derivative_p999':float(np.quantile(derivative,.999)),'max_hop_boundary_step':float(np.max(boundary_steps)) if boundary_steps.size else 0})
    sf.write(a.output/(name+'.wav'),stems[i,:,:RATE*20].T*.5,RATE,subtype='PCM_24')
sf.write(a.output/'original.wav',original[:,:RATE*20].T*.5,RATE,subtype='PCM_24');sf.write(a.output/'stem-sum.wav',mix[:,:RATE*20].T*.5,RATE,subtype='PCM_24')
(a.output/'metrics.json').write_text(json.dumps(metrics,indent=2));print(json.dumps(metrics,indent=2));cache.close()
