"""Compare actual samples returned by native Traktor readers with the CUDA cache."""
import argparse,json
from pathlib import Path
import numpy as np
from stem_cache import StemCache
p=argparse.ArgumentParser();p.add_argument('log',type=Path);p.add_argument('cache',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
cache=StemCache(a.cache);rows=[];complete=False
for line in a.log.read_text().splitlines():
    message=json.loads(line)
    if message.get('type')=='error':raise RuntimeError(message)
    payload=message.get('payload',{})
    if payload.get('type')=='native_audio_probe_complete':complete=True
    if payload.get('type')!='native_probe_block':continue
    stem=payload['stem']-1;block=payload['block'];actual=np.array(payload['samples'],dtype=np.float32)
    expected=cache.data[block,stem,0,:len(actual)]
    difference=float(np.max(np.abs(actual-expected)));assert difference==0,(stem,block,difference)
    assert payload['frames']==1024
    rows.append({'stem':stem+1,'block':block,'samples':len(actual),'max_absolute_error':difference,'native_read_with_seek_ms':payload['elapsed_ms']})
assert complete and len(rows)==20 and {r['stem'] for r in rows}=={1,2,3,4}
report={'native_process':'sandbox-owned Traktor Pro 4.5.1.21','passed':True,'sample_comparisons':sum(r['samples'] for r in rows),'reads':rows,'scope':'Native audio readers, not yet deck/output/controller validation'}
a.output.write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='reads'},indent=2));cache.close()
