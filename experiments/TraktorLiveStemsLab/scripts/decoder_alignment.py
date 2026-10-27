"""Measure native-decoder/FFmpeg offset from a short PCM reference, never guess by extension."""
import math
import numpy as np

def measure_offset(native,decoded,expected_start=0,max_lag=8192):
    native=np.asarray(native,dtype=np.float64).reshape(-1)
    if len(native)<1024:return {'verified':False,'reason':'reference_too_short'}
    # Exclude clipping from the offset decision and remove DC.
    reference=native-native.mean();energy=float(np.dot(reference,reference))
    if energy<1e-8:return {'verified':False,'reason':'silent_reference'}
    start=max(0,expected_start-max_lag);end=min(len(decoded),expected_start+len(reference)+max_lag)
    candidate=np.asarray(decoded[start:end],dtype=np.float64)
    if len(candidate)<len(reference):return {'verified':False,'reason':'source_too_short'}
    size=1<<(len(candidate)+len(reference)-1).bit_length()
    correlation=np.fft.irfft(np.fft.rfft(candidate,size)*np.conj(np.fft.rfft(reference,size)),size)
    valid=len(candidate)-len(reference)+1;scores=correlation[:valid]
    cumulative=np.concatenate(([0.],np.cumsum(candidate*candidate)))
    local_energy=cumulative[len(reference):]-cumulative[:-len(reference)]
    normalized=scores/np.sqrt(np.maximum(local_energy*energy,1e-30))
    best=int(np.argmax(normalized));confidence=float(normalized[best]);offset=start+best-expected_start
    # Correlation must be strong; a silent intro is not enough evidence to switch audio.
    return {'verified':confidence>.97,'offset_samples':offset,'confidence':confidence,'reference_samples':len(reference)}

def align_audio(decoded,frames,offset):
    """Output position zero corresponds to native position zero; tail is zero-padded."""
    output=np.zeros((2,frames),dtype=np.float32)
    source_start=max(0,offset);target_start=max(0,-offset)
    count=min(decoded.shape[1]-source_start,frames-target_start)
    if count>0:output[:,target_start:target_start+count]=decoded[:,source_start:source_start+count]
    return output
