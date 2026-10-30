"""First-install numerical accelerator check, cached per device/driver/code."""
import argparse, hashlib, json, os, subprocess, time
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--backend', required=True, choices=['cuda', 'directml', 'cpu'])
    parser.add_argument('--force', action='store_true')
    args = parser.parse_args()
    import torch
    lab = Path(os.environ['LOCALAPPDATA']) / 'Cratebase/traktor-lab'
    driver = subprocess.check_output(['powershell.exe', '-NoProfile', '-Command',
        'Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion | ConvertTo-Json -Compress'], text=True)
    identity = hashlib.sha256((args.backend + torch.__version__ + driver +
        Path(__file__).read_text() + Path(__file__).with_name('stem_backend.py').read_text()).encode()).hexdigest()
    stamp = lab / ('backend-check-' + args.backend + '.json')
    if not args.force and stamp.exists() and json.loads(stamp.read_text()).get('identity') == identity:
        print('GPU-Pruefung bereits bestanden (gleicher Treiber und Backend).', flush=True)
        return
    os.environ['TORCH_HOME'] = str(lab / 'models')
    from stem_backend import load
    model, device, name = load({'backend': args.backend, 'gpu_index': 0, 'memory_budget_mb': 4096})
    print('Pruefe ' + name + ' gegen CPU-Referenz ...', flush=True)
    torch.manual_seed(217)
    # Same 327680-sample inference window used by live playback.
    audio = torch.randn(1, 2, 327680) * .08
    start = time.monotonic()
    with torch.no_grad():
        actual = model(audio.to(device)).cpu()
    elapsed = time.monotonic() - start
    # Separate model avoids retaining the DirectML-bound spectral methods.
    from demucs.pretrained import get_model
    reference_model = get_model('htdemucs').models[0].eval()
    with torch.inference_mode():
        expected = reference_model(audio)
    relative = ((actual - expected).square().mean() / expected.square().mean().clamp_min(1e-12)).sqrt().item()
    if not torch.isfinite(actual).all() or actual.square().mean() < 1e-12 or relative > .02:
        raise RuntimeError(f'GPU-Pruefung fehlgeschlagen: relativer Fehler {relative:.6f}. Kein Live-Start freigegeben.')
    stamp.write_text(json.dumps({'identity': identity, 'device': name,
        'relative_rms_error': relative, 'window_seconds': elapsed}, indent=2))
    print(f'GPU-Pruefung bestanden: {name}; Fehler {relative:.6f}; Fenster {elapsed:.2f}s.', flush=True)


if __name__ == '__main__':
    main()
