"""Compare a private loopback recording with the generated stems (no microphone)."""
import argparse,json
from pathlib import Path
import numpy as np
import soundfile as sf
from stem_cache import StemCache,RATE

p=argparse.ArgumentParser();p.add_argument('cache',type=Path);p.add_argument('recording',type=Path);a=p.parse_args()
cache=StemCache(a.cache);audio,rate=sf.read(a.recording,dtype='float32',always_2d=True)
if rate!=RATE:raise ValueError('Recording must use cache sample rate')
# Decimation reduces memory while retaining enough detail to distinguish stems.
y=audio[:,0][::4].astype('float64');y-=y.mean();n=len(y);yp=np.dot(y,y)
stems=cache.data.transpose(1,2,0,3).reshape(4,2,-1)[:,:,:cache.frames]
report={'recording':a.recording.name,'rms':float(np.sqrt(np.mean(audio**2))),'matches':[]}
for name,source in zip(('drums','bass','other','vocals','sum'),[*stems[:,0],stems.sum(axis=0)[0]]):
    best={'stem':name,'correlation':0.,'position_seconds':0.}
    for phase in range(4):
        x=source[phase::4].astype('float64');size=1<<(len(x)+n-2).bit_length()
        c=np.fft.irfft(np.fft.rfft(x,size)*np.fft.rfft(y[::-1],size),size)[n-1:len(x)]
        sums=np.concatenate(([0.],np.cumsum(x)));squares=np.concatenate(([0.],np.cumsum(x*x)))
        power=squares[n:]-squares[:-n]-(sums[n:]-sums[:-n])**2/n
        score=c/np.sqrt(np.maximum(power*yp,1e-30));i=int(np.argmax(np.abs(score)))
        if abs(score[i])>abs(best['correlation']):best.update(correlation=float(score[i]),position_seconds=(i*4+phase)/RATE)
    report['matches'].append(best)
print(json.dumps(report,indent=2));a.recording.with_suffix('.verification.json').write_text(json.dumps(report,indent=2));cache.close()
