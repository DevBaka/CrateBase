"""Local spectral-landmark matching. Input paths are data, never shell commands.

No cloud requests, no source writes. Constellation approach inspired by Wang,
ISMIR 2003; tempo-grid alignment and conservative interval reporting.
"""
import hashlib
import json
import os
import subprocess
import sys
import time
from pathlib import Path

import numpy as np

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

VERSION = 'landmarks-v2'
SR, FFT, HOP = 11025, 2048, 256
STEP = HOP / SR


def progress(message):
    print(json.dumps({'progress': message}, ensure_ascii=False), flush=True)


def fingerprint(path, ffmpeg, cache):
    stat = os.stat(path)
    key = hashlib.sha256(f'{VERSION}|{path}|{stat.st_size}|{stat.st_mtime_ns}'.encode()).hexdigest()
    target = Path(cache) / (key + '.npz')
    if target.exists():
        with np.load(target, allow_pickle=False) as data:
            return data['t'], data['f'], float(data['duration'])
    result = subprocess.run([ffmpeg, '-nostdin', '-v', 'error', '-i', path,
                             '-map', '0:a:0', '-vn', '-ac', '1', '-ar', str(SR),
                             '-f', 's16le', 'pipe:1'], stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, check=False,
                            creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if result.returncode:
        raise RuntimeError(result.stderr.decode('utf-8', errors='replace')[-1200:])
    audio = np.frombuffer(result.stdout, dtype='<i2').astype(np.float32) / 32768
    duration = len(audio) / SR
    del result
    frames = max(0, (len(audio) - FFT) // HOP + 1)
    window = np.hanning(FFT).astype(np.float32)
    times, freqs = [], []
    for begin in range(0, frames, 2048):
        start, end = max(0, begin - 3), min(frames, begin + 2048 + 3)
        snippets = np.lib.stride_tricks.sliding_window_view(audio[start * HOP:(end - 1) * HOP + FFT], FFT)[::HOP]
        mag = np.log1p(np.abs(np.fft.rfft(snippets * window, axis=1))).astype(np.float32)
        # Remove smooth spectral envelope to retain quieter upper-band landmarks
        # through bass swaps and EQ changes.
        pad = np.pad(mag, ((0, 0), (15, 15)), mode='edge')
        sums = np.pad(np.cumsum(pad, axis=1), ((0, 0), (1, 0)))
        mag -= (sums[:, 31:] - sums[:, :-31]) / 31
        maxima = mag.copy()
        for shift in range(1, 5):
            maxima[:, shift:] = np.maximum(maxima[:, shift:], mag[:, :-shift])
            maxima[:, :-shift] = np.maximum(maxima[:, :-shift], mag[:, shift:])
        for shift in range(1, 4):
            maxima[shift:] = np.maximum(maxima[shift:], mag[:-shift])
            maxima[:-shift] = np.maximum(maxima[:-shift], mag[shift:])
        peaks = (mag >= maxima) & (mag > .20)
        peaks[:, :22] = False
        peaks[:, 760:] = False
        scores = np.where(peaks, mag, -100)
        indices = np.argpartition(scores, -4, axis=1)[:, -4:]
        rows = np.repeat(np.arange(len(mag)), 4)
        bins = indices.ravel()
        keep = (scores[rows, bins] > .20) & (rows + start >= begin) & (rows + start < min(frames, begin + 2048))
        # Density control: a few reproducible landmarks per 116 ms are enough.
        # Keeping every local spectral maximum explodes the search index.
        chosen = np.flatnonzero(keep)
        buckets = (rows[chosen] + start) // 5
        ranking = np.lexsort((-scores[rows[chosen], bins[chosen]], buckets))
        sorted_buckets = buckets[ranking]
        group_starts = np.r_[0, np.flatnonzero(np.diff(sorted_buckets)) + 1]
        within_group = np.arange(len(ranking)) - np.repeat(group_starts, np.diff(np.r_[group_starts, len(ranking)]))
        keep[:] = False
        keep[chosen[ranking[within_group < 3]]] = True
        times.append((rows[keep] + start).astype(np.int32))
        freqs.append(bins[keep].astype(np.int16))
    t = np.concatenate(times) if times else np.array([], dtype=np.int32)
    f = np.concatenate(freqs) if freqs else np.array([], dtype=np.int16)
    order = np.argsort(t, kind='stable')
    t, f = t[order], f[order]
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = str(target) + '.partial.npz'
    np.savez_compressed(temporary, t=t, f=f, duration=duration)
    os.replace(temporary, target)
    return t, f, duration


def pairs(t, f):
    anchors, first, second, delta = [], [], [], []
    for i in range(len(t)):
        lo = np.searchsorted(t, t[i] + 7)
        hi = np.searchsorted(t, t[i] + 65)
        if hi <= lo:
            continue
        selected = np.linspace(lo, hi - 1, min(6, hi - lo)).astype(int)
        for j in selected:
            if abs(int(f[i]) - int(f[j])) < 8:
                continue
            anchors.append(t[i]); first.append(f[i]); second.append(f[j]); delta.append(t[j] - t[i])
    return (np.asarray(anchors, dtype=np.int32), np.asarray(first, dtype=np.int32),
            np.asarray(second, dtype=np.int32), np.asarray(delta, dtype=np.int32))


def hashes(first, second, delta):
    return ((first // 3) << 16) | ((second // 3) << 8) | np.clip(np.rint(delta / 3), 1, 255).astype(np.int32)


def recording_index(t, f):
    anchors, first, second, delta = pairs(t, f)
    keys = hashes(first, second, delta)
    order = np.argsort(keys)
    return keys[order], anchors[order] * STEP


def match(t, f, index, max_tempo=.25):
    keys, rec_times = index
    anchors, first, second, delta = pairs(t, f)
    source_times = anchors * STEP
    candidates = []
    # Key-locked time stretching changes landmark time distances, not frequencies.
    for rate in np.arange(1 - max_tempo, 1 + max_tempo + .002, .005):
        query = hashes(first, second, delta / rate)
        left = np.searchsorted(keys, query, side='left')
        right = np.searchsorted(keys, query, side='right')
        counts = right - left
        valid = (counts > 0) & (counts <= 45)
        counts = counts[valid]
        if not len(counts):
            continue
        qids = np.repeat(np.flatnonzero(valid), counts)
        starts = np.repeat(left[valid], counts)
        increments = np.arange(len(qids)) - np.repeat(np.cumsum(counts) - counts, counts)
        rtimes = rec_times[starts + increments]
        stimes = source_times[qids]
        offsets = rtimes - stimes / rate
        buckets, votes = np.unique(np.rint(offsets / 2).astype(int), return_counts=True)
        best = np.argsort(votes)[-8:]
        for bucket, count in zip(buckets[best], votes[best]):
            if count < 30:
                continue
            keep = np.abs(offsets - bucket * 2) < 1.5
            x, y = stimes[keep], rtimes[keep]
            if len(x) < 30 or np.ptp(x) < 16:
                continue
            # Robustly refine the tempo and offset; random recurring drum hashes
            # must agree across distinct reference positions, not just a beat.
            slope, intercept = 1 / rate, bucket * 2.
            for tolerance in (1.2, .7, .35):
                good = np.abs(y - (slope * x + intercept)) < tolerance
                if good.sum() < 25 or np.ptp(x[good]) < 16:
                    break
                slope, intercept = np.polyfit(x[good], y[good], 1)
            good = np.abs(y - (slope * x + intercept)) < .35
            if good.sum() < 30 or not .70 <= slope <= 1.40:
                continue
            supported = np.unique(np.floor(y[good]).astype(int))
            for group in np.split(supported, np.flatnonzero(np.diff(supported) > 10) + 1):
                if len(group) < 12 or group[-1] - group[0] < 18:
                    continue
                start, end = float(group[0]), float(group[-1] + 1)
                density = len(group) / (end - start)
                if density < .25:
                    continue
                candidates.append({'start': max(0, start), 'end': end,
                                   'supportSeconds': int(len(group)), 'density': round(density, 3),
                                   'tempo': round(1 / slope, 5),
                                   'confidence': 'high' if len(group) >= 30 and density >= .60 else 'review'})
    # The same occurrence is detected by several nearby tempo-grid values.
    chosen = []
    for item in sorted(candidates, key=lambda r: r['supportSeconds'], reverse=True):
        if any(min(item['end'], old['end']) - max(item['start'], old['start']) > .5 * min(item['end'] - item['start'], old['end'] - old['start']) for old in chosen):
            continue
        chosen.append(item)
    return sorted(chosen, key=lambda r: r['start'])


def analyze(manifest):
    cache = manifest['cache']
    progress('Aufnahme-Audio dekodieren und spektrale Fingerabdrücke berechnen …')
    t, f, duration = fingerprint(manifest['recording'], manifest['ffmpeg'], cache)
    progress(f'Aufnahme: {duration / 60:.1f} Minuten · {len(t):,} spektrale Landmarken; Suchindex aufbauen …')
    index = recording_index(t, f)
    results, missing, errors = [], [], []
    recstat = os.stat(manifest['recording'])
    recording_key = f"{manifest['recording']}|{recstat.st_size}|{recstat.st_mtime_ns}"
    for number, track in enumerate(manifest['tracks'], 1):
        progress(f"Audio-Abgleich {number}/{len(manifest['tracks'])}: {track['artist']} – {track['title']}")
        if not os.path.isfile(track['path']):
            missing.append(track); continue
        try:
            stat = os.stat(track['path'])
            match_key = hashlib.sha256(f"match-v1|{VERSION}|{recording_key}|{track['path']}|{stat.st_size}|{stat.st_mtime_ns}|{manifest.get('maxTempoChange', .25)}".encode()).hexdigest()
            match_path = Path(cache) / 'matches' / (match_key + '.json')
            if match_path.exists():
                with open(match_path, encoding='utf-8') as cached:
                    found = json.load(cached)
            else:
                st, sf, _ = fingerprint(track['path'], manifest['ffmpeg'], cache)
                found = match(st, sf, index, manifest.get('maxTempoChange', .25))
                match_path.parent.mkdir(parents=True, exist_ok=True)
                temporary = str(match_path) + '.partial'
                with open(temporary, 'w', encoding='utf-8') as cached:
                    json.dump(found, cached)
                os.replace(temporary, match_path)
            for item in found:
                item['confidence'] = 'high' if item['supportSeconds'] >= 30 and item['density'] >= .60 else 'review'
                results.append(dict(track, **item))
            progress(f"{len(found)} mögliche Trefferabschnitte: {track['title']}")
        except Exception as error:
            errors.append({'track': track, 'error': str(error)})
        with open(manifest['output'] + '.checkpoint.json', 'w', encoding='utf-8') as checkpoint:
            json.dump({'completed': number, 'total': len(manifest['tracks']), 'matches': results, 'errors': errors}, checkpoint, ensure_ascii=False)
    results.sort(key=lambda r: r['start'])
    for item in results:
        item['overlaps'] = [other['id'] for other in results if item['confidence'] == 'high' and other['confidence'] == 'high' and other['id'] != item['id'] and
                            min(item['end'], other['end']) - max(item['start'], other['start']) >= 20]
    report = {'version': VERSION, 'recording': manifest['recording'], 'duration': duration,
              'matches': results, 'missing': missing, 'errors': errors,
              'unmatched': [t for t in manifest['tracks'] if not any(r['id'] == t['id'] and r['confidence'] == 'high' for r in results)],
              'limitations': 'Zeitstempel sind früheste belastbare Audio-Treffer, keine garantierten Fader-Einsätze. Tempoänderungen bei Keylock bis ±25%; Pitchänderungen, kurze Einsätze, Effekte und Maskierung können Treffer verhindern. Überlagerungen sind Mashup-Kandidaten, keine Bestätigung. Keine Audiodaten hochgeladen.'}
    temporary = manifest['output'] + '.partial'
    with open(temporary, 'w', encoding='utf-8') as out:
        json.dump(report, out, ensure_ascii=False, indent=2)
    os.replace(temporary, manifest['output'])
    progress(f'Analyse abgeschlossen: {len(results)} Abschnitte; {len(report["unmatched"])} History-Tracks ohne bestätigten Treffer.')


if __name__ == '__main__':
    with open(sys.argv[1], encoding='utf-8-sig') as source:
        analyze(json.load(source))
