# Cratebase

Lokaler Musik-Manager & DJ-Collection-Tool. Plattformunabhängige Desktop-App (Linux/Windows/Mac) für die eigene Musiksammlung — von "lokales Spotify" für Normalnutzer bis zum Collection-Manager auf Lexicon/Mixo-Niveau für DJs, mit Fokus auf sehr große Bibliotheken (Referenzgröße: ~100.000 Tracks).

> **Status: Prototyp-MVP.** Scanner, Tag-Reader, SQLite-Schema und ein lauffähiger Media Player mit Playlist-Baum existieren (`src/`), gebaut vor der finalen Abstimmung einiger offener Fragen aus ARCHITECTURE.md Abschnitt 4 — s. dort Abschnitt 8 für den genauen Stand und bewusste Abweichungen.

## Starten

```
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"   # falls dotnet nicht schon im PATH ist
dotnet run --project src/Cratebase.App.Desktop/Cratebase.App.Desktop.csproj
```

Der Repo-Ordner heißt aktuell noch `vision` (Arbeitsordner) — Umbenennung auf `cratebase` kann jederzeit unabhängig vom Code erfolgen (lokal per `mv`, auf GitHub per Rename), ist aber nicht zeitkritisch.

## Vision

Zwei Zielgruppen, eine Engine:

1. **Normale Nutzer** — lokale Musiksammlung verwalten und abspielen, ohne Streaming-Zwang.
2. **DJs / Musik-Nerds** — Collection-Management (Smart Playlists, BPM/Key-Analyse, Hotcues, Tags), später DJ-Workflow (MIDI, Sync mit DJ-Software).

Langfristig denkbare Aufteilung: ein solider Media Player + Collection Manager als Fundament, darauf aufbauend eine DJ-Software mit derselben Engine, aber eigener UI-Schicht. Deshalb ist die Kernregel für die Architektur: **Engine und UI sind von Anfang an getrennt.**

## Harter Constraint: Skalierung

Alles muss für ~100.000 Tracks performant sein:

- Lokale SQLite-Datenbank als Wahrheitsquelle, kein Neu-Scan des Dateisystems bei jedem Start
- Inkrementelles Scannen (Filesystem-Watcher + mtime/Hash-Vergleich)
- Waveform-/BPM-Analyse als asynchrone, abbrechbare Background-Job-Queue, Cache auf Disk (nicht als Blob in der DB)
- Suche/Filter/Smart-Playlist-Queries über Indizes, nicht In-Memory-Filtering

## Phasenplan

| Phase | Inhalt |
|---|---|
| 0 | Kern-Engine: Scanner, Tag-Reader/Writer, SQLite-Schema |
| 1 | MVP: Bibliotheksansicht, Player mit Waveform, Tag-Editor, regelbasierte Smart Playlists |
| 2 | Import/Export/Sync mit DJ-Software (Rekordbox, Traktor, Mixxx zuerst) |
| 3 | KI-gestützte Smart Playlists via Ollama (Regel-Generierung + Genre-/Bootleg-Klassifikation) |
| 4 | DJ-Layer: Hotcues, MIDI-Mapping, Slot-Presets |
| 5 | Cloud/Mobile/Web |

Details, Datenmodell, Modulaufteilung und offene Fragen: siehe [ARCHITECTURE.md](ARCHITECTURE.md).

## Referenz-Musiksammlung

Die bestehende Ordnerstruktur unter `/run/media/devbaka/Musik/Musik/` dient als Vorbild für Genre-/Ordnerhierarchie (z.B. `House/MelbourneBounce/...`, `Gothic/Death Rock/...`). Sie ist bewusst nicht als Vorbild für eine *App-interne* starre Taxonomie zu verstehen — siehe die Diskussion dazu in ARCHITECTURE.md unter "Offene Fragen".
