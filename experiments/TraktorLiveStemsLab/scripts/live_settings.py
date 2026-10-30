"""Validated settings and atomic publication shared by panel, launcher and GPU service."""
import json, os, time, uuid, math
from pathlib import Path

DEFAULTS={'enabled':True,'gpu_index':0,'precision':'fp16','memory_budget_mb':4096,'max_active_tracks':4,'backend':'auto','render_rate':0.0}

def validate(values):
    result={**DEFAULTS,**values}
    if type(result['enabled']) is not bool:raise ValueError('Aktivierung muss ein boolescher Wert sein')
    if result['precision'] not in ('fp16','fp32'):raise ValueError('Ungültige GPU-Genauigkeit')
    if result['backend'] not in ('auto','cuda','directml','cpu'):raise ValueError('Ungültiges Stem-Backend')
    rate=result['render_rate']
    if type(rate) not in (int,float) or not math.isfinite(rate) or not 0<=rate<=32:raise ValueError('Rendergeschwindigkeit: 0 (Maximum) bis 32 Audiosekunden pro Sekunde')
    for name,low,high in [('gpu_index',0,31),('memory_budget_mb',1024,6144),('max_active_tracks',1,4)]:
        if type(result[name]) is not int or not low<=result[name]<=high:raise ValueError('Ungültige Einstellung: '+name)
    return result

def read(path):
    path=Path(path)
    for attempt in range(40):
        try:return validate(json.loads(path.read_text(encoding='utf-8')))
        except FileNotFoundError:return DEFAULTS.copy()
        except PermissionError:
            if attempt==39:raise
            time.sleep(.005)

def atomic_write(path,value):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    temp=path.with_name(path.name+'.'+uuid.uuid4().hex+'.tmp')
    try:
        with temp.open('x',encoding='utf-8') as stream:
            json.dump(value,stream,ensure_ascii=False,indent=2,allow_nan=False)
            stream.flush();os.fsync(stream.fileno())
        # Windows readers/antivirus may briefly deny a replace while holding
        # the destination open. Keep the complete old file until retry succeeds.
        for attempt in range(40):
            try:temp.replace(path);break
            except PermissionError:
                if attempt==39:raise
                time.sleep(.025)
    finally:temp.unlink(missing_ok=True)

def save(path,values):
    values=validate(values);atomic_write(path,values);return values
