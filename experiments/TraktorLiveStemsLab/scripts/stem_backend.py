"""CUDA, DirectML (Intel/AMD) and CPU selection for the isolated stem process."""
import importlib.util
import types


def devices():
    import torch
    result=[{'backend':'cpu','gpu_index':0,'name':'CPU'}]
    if torch.cuda.is_available():
        result += [{'backend':'cuda','gpu_index':i,'name':'CUDA · '+torch.cuda.get_device_name(i)} for i in range(torch.cuda.device_count())]
    if importlib.util.find_spec('torch_directml') is not None:
        import torch_directml
        result += [{'backend':'directml','gpu_index':i,'name':'DirectML · '+torch_directml.device_name(i).rstrip('\0')} for i in range(torch_directml.device_count())]
    return result


def directml_spectral_bridge(model,device):
    # HTDemucs already uses this pattern for Apple's MPS: complex STFT/ISTFT
    # stay on CPU, real neural-network tensors live on the accelerator. Unlike
    # an ONNX DFT export this retains torch's original FFT implementation.
    spec,mask,ispec=model._spec,model._mask,model._ispec
    def cpu_spec(self,value,*args,**kwargs):return spec(value.cpu(),*args,**kwargs)
    def cpu_mask(self,spectrum,magnitude,*args,**kwargs):return mask(spectrum,magnitude.cpu(),*args,**kwargs)
    def cpu_ispec(self,value,*args,**kwargs):return ispec(value,*args,**kwargs).to(device)
    model._spec=types.MethodType(cpu_spec,model)
    model._mask=types.MethodType(cpu_mask,model)
    model._ispec=types.MethodType(cpu_ispec,model)
    return model


def load(settings):
    import torch
    from demucs.pretrained import get_model
    choice=settings.get('backend','auto');index=int(settings.get('gpu_index',0))
    if choice=='auto':
        import os
        from pathlib import Path
        from runtime_install import read
        choice=read(Path(os.environ['LOCALAPPDATA'])/'Cratebase/traktor-lab').get('backend','auto')
    torch.set_num_threads(4)
    if choice in ('auto','cuda') and torch.cuda.is_available():
        if not 0<=index<torch.cuda.device_count():raise RuntimeError('Gewählte CUDA-GPU nicht verfügbar. Rechengerät erneut auswählen.')
        device=torch.device('cuda',index)
        torch.cuda.set_device(device)
        total=torch.cuda.get_device_properties(index).total_memory
        torch.cuda.set_per_process_memory_fraction(min(.9,int(settings['memory_budget_mb'])*1024**2/total),index)
        name='CUDA · '+torch.cuda.get_device_name(index)
    elif choice in ('auto','directml') and importlib.util.find_spec('torch_directml') is not None:
        import torch_directml
        if torch_directml.device_count()<1:raise RuntimeError('Kein DirectML/DX12-Grafikgerät gefunden. Grafiktreiber prüfen oder CPU auswählen.')
        if not 0<=index<torch_directml.device_count():raise RuntimeError('Gewählte DirectML-GPU nicht verfügbar. Rechengerät erneut auswählen.')
        device=torch_directml.device(index)
        name='DirectML · '+torch_directml.device_name(index).rstrip('\0')
    elif choice in ('cuda','directml'):
        raise RuntimeError('Gewünschter GPU-Backend fehlt. Den BAT-Starter zur Einrichtung erneut ausführen.')
    else:device=torch.device('cpu');name='CPU · langsamer als GPU'
    model=get_model('htdemucs').models[0].eval().to(device)
    if device.type=='privateuseone':directml_spectral_bridge(model,device)
    return model,device,name


if __name__=='__main__':
    import json
    print(json.dumps(devices(),ensure_ascii=True))
