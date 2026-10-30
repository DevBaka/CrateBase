"""Per-user dependency installer; no global Python/PATH or NI data changes."""
import argparse, hashlib, json, os, shutil, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BASE = Path(os.environ['LOCALAPPDATA']) / 'Cratebase'
LAB = BASE / 'traktor-lab'
SCRIPTS = ROOT / 'experiments/TraktorLiveStemsLab/scripts'


def run(*args, capture=False):
    result = subprocess.run([str(a) for a in args], check=True, text=True,
                            stdout=subprocess.PIPE if capture else None)
    return result.stdout.strip() if capture else None


def environment(uv, folder, requirements, probe, index=None):
    python = folder / 'Scripts/python.exe'
    stamp = folder / 'cratebase-requirements.json'
    identity = json.dumps([requirements, index])
    if not python.exists():
        run(uv, '--no-config', 'venv', '--python', '3.11', '--managed-python', folder)
    # A copied virtualenv may point at an interpreter absent on this PC.
    try:
        run(python, '-I', '-c', 'import sys; assert sys.version_info[:2] == (3,11)', capture=True)
    except (OSError, subprocess.CalledProcessError) as error:
        raise RuntimeError(f'Ungueltige kopierte Python-Umgebung: {folder}. '
                           'Diesen Laufzeitordner umbenennen und Starter erneut ausfuehren.') from error
    if stamp.exists() and stamp.read_text() == identity:
        try:
            run(python, '-I', '-c', probe, capture=True)
            return python
        except subprocess.CalledProcessError:
            pass
    command = [uv, '--no-config', 'pip', 'install', '--python', python]
    if index:
        run(*command, '--index-url', index, *requirements[0])
        run(*command, *requirements[1])
    else:
        run(*command, *requirements)
    run(python, '-I', '-c', probe)
    stamp.write_text(identity)
    return python


def choose_backend(requested):
    if requested != 'auto':
        return requested
    query = ('Get-CimInstance Win32_VideoController | '
             'Select-Object -ExpandProperty PNPDeviceID')
    adapters = run('powershell.exe', '-NoProfile', '-Command', query, capture=True).upper()
    if 'VEN_10DE' in adapters:
        return 'cuda'
    if 'VEN_8086' in adapters or 'VEN_1002' in adapters:
        return 'directml'
    return 'cpu'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--target', choices=['desktop', 'beatport', 'live', 'sync'], required=True)
    parser.add_argument('--backend', choices=['auto', 'cuda', 'directml', 'cpu'], default='auto')
    parser.add_argument('--uv', type=Path, required=True)
    args = parser.parse_args()
    LAB.mkdir(parents=True, exist_ok=True)
    if args.target in ('desktop', 'beatport'):
        python = environment(args.uv, BASE / 'bootstrap/media-python',
                             ['imageio-ffmpeg==0.6.0'], 'import imageio_ffmpeg')
    else:
        requirements = ['psutil==7.2.2']
        probe = 'import psutil, tkinter'
        if args.target == 'live':
            requirements += ['frida==17.24.3', 'numpy==2.4.6', 'soundfile==0.14.0',
                             'ziglang==0.16.0', 'imageio-ffmpeg==0.6.0']
            probe += ', frida, numpy, soundfile, ziglang, imageio_ffmpeg'
        python = environment(args.uv, LAB / 'python', requirements, probe)
    if args.target != 'sync' and not (BASE / 'tools/ffmpeg.exe').exists():
        executable = run(python, '-I', '-c', 'import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())', capture=True)
        (BASE / 'tools').mkdir(exist_ok=True)
        shutil.copy2(executable, BASE / 'tools/ffmpeg.exe')
    if args.target != 'live':
        return
    run(python, SCRIPTS / 'setup_portable_profile.py')
    backend = choose_backend(args.backend)
    print('Stem-Backend:', backend, flush=True)
    common = ['demucs==4.0.1', 'numpy==1.26.4', 'soundfile==0.13.1']
    if backend == 'directml':
        wheels = ['torch==2.4.1', 'torchaudio==2.4.1', 'torchvision==0.19.1']
        common += ['torch-directml==0.2.5.dev240914']
        probe = 'import torch, torchaudio, demucs, torch_directml; assert torch_directml.device_count()>0'
        index = 'https://download.pytorch.org/whl/cpu'
    else:
        wheels = ['torch==2.5.1', 'torchaudio==2.5.1']
        probe = 'import torch, torchaudio, demucs'
        if backend == 'cuda':
            probe += '; assert torch.cuda.is_available(), "CUDA-Treiber nicht verfuegbar"'
        index = 'https://download.pytorch.org/whl/' + ('cu124' if backend == 'cuda' else 'cpu')
    inference = environment(args.uv, LAB / (backend + '-python'), [wheels, common], probe, index)
    native = ROOT / 'experiments/TraktorLiveStemsLab/native/live_bridge.c'
    digest = hashlib.sha256(native.read_bytes()).hexdigest()[:16]
    dll = LAB / 'native' / ('live_bridge-' + digest + '.dll')
    dll.parent.mkdir(exist_ok=True)
    if not dll.exists():
        zig = run(python, '-I', '-c', 'import ziglang; from pathlib import Path; print(Path(ziglang.__file__).parent/"zig.exe")', capture=True)
        temporary = dll.with_suffix('.building.dll')
        run(zig, 'cc', '-w', '-O2', '-shared', native, '-o', temporary, '-lkernel32')
        temporary.replace(dll)
    # Validate the actual accelerator before declaring setup complete.
    os.environ['TORCH_HOME'] = str(LAB / 'models')
    run(inference, SCRIPTS / 'check_stem_backend.py', '--backend', backend)
    manifest = {'backend': backend, 'inference_python': str(inference), 'native_bridge': str(dll)}
    temporary = LAB / 'runtime-install.partial'
    temporary.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    temporary.replace(LAB / 'runtime-install.json')
    print('Einrichtung abgeschlossen.', flush=True)


if __name__ == '__main__':
    main()
