# Übergabe: MSI Claw A1M / Intel Arc

Stand: 31.10.2026. GitHub: https://github.com/DevBaka/CrateBase.git
Der zuletzt geprüfte gepushte Stand ist `99ad551` (History-Erweiterungen).
Diese Übergabedatei wurde danach lokal angelegt; bei Bedarf separat übertragen.

## Aktueller Auftrag

Auf dem MSI Claw startet Traktor, **Live Stems funktionieren dort aber noch nicht**.
Die tatsächliche Ursache ist unbekannt. Direkt auf dem Claw GPU-Dienst, DirectML,
native Anbindung und Audiopfad prüfen und reparieren. Die NVIDIA-Tests auf dem
Haupt-PC gelten nicht als Nachweis für den Claw. Keine erneute Feature-Planung nötig.

## Projekt und Start

- Windows-Desktop-App: Avalonia/.NET 8, SQLite, LibVLC; deutsche Oberfläche.
- Vier gewünschte Einstiege: `start.bat`, `start-traktor-live-stems.bat`,
  `start-beatport.bat`, `start-traktor-sync.bat`.
- Gemeinsamer Installer: `scripts/bootstrap-windows.ps1`, `scripts/setup_windows.py`.
- Traktor-Erweiterung: `experiments/TraktorLiveStemsLab/scripts/`.
- Hooks sind ausschließlich für Traktor Pro **4.5.1.21 / Windows x64** geprüft.
  Programm-SHA256: `1b4db54ab51d63e1f662971c74b3f37e75df7c19291844f5ac7ec8ccc04373ba`.
  Kein anderer Build darf mit denselben Hook-Adressen gestartet werden.
- Installiertes Traktor bleibt unverändert. Private Programm-/Laufzeitkopien liegen
  unter `%LOCALAPPDATA%\Cratebase\traktor-lab`.
- Eigenes Profil: `Dokumente\Native Instruments\Traktor 4.5 Cratebase Edition`.

## Intel-Arc-Pfad und Diagnose

- Intel/AMD → PyTorch DirectML, NVIDIA → CUDA; CPU ist separat wählbar.
- DirectML: `torch-directml==0.2.5.dev240914`, Torch/Torchaudio 2.4.1 CPU-Wheels,
  Demucs 4.0.1. Neuronales Netz auf GPU, komplexe FFT/ISTFT auf CPU.
- `torch.no_grad()` verwenden: `inference_mode()` verursachte unter DirectML Fehler.
- Laufzeitmanifest: `traktor-lab\runtime-install.json` enthält Backend,
  Inferenz-Python und den pro PC kompilierten nativen DLL-Pfad.
- Einstellungen: `live-stems-settings.json`; Steuerfenster `live_control.py`.
- In `stem_backend.py` werden Geräte aufgelistet und das Modell geladen.
- `check_stem_backend.py --backend directml --force` prüft einen vollständigen
  Audioblock gegen die CPU-Referenz. Mit `directml-python\Scripts\python.exe`
  starten, nicht mit dem kleinen GUI-Python oder System-Python.
- `gpu_service.py` stellt den lokalen Named-Pipe-Dienst; `gpu_worker.py` rendert
  inkrementell. `launch_lab.py` startet/instrumentiert die private Traktor-Kopie.
- Zuerst die tatsächlichen Protokolle lesen:
  `%LOCALAPPDATA%\Cratebase\bootstrap\setup-live.log` und
  `%LOCALAPPDATA%\Cratebase\traktor-lab\logs\` (GPU-Service/Live-Launch/JSONL).
- Prüfen: richtiger Prozess/Build, erfolgreicher Hook-Start, enabled=true,
  tatsächlich Intel Arc im Dienst, Generation-Aufträge und fertige Cache-Blöcke.
  Fehlender Ton bzw. fehlende Stems nicht automatisch dem Modell zuschreiben.
- Traktor braucht häufig **1–2 Minuten** bis zur fertigen Vollbildansicht.

## Bereits implementierte Funktionen / Schutz

- Normales Laden soll bei aktivierten Live Stems automatisch Stem-Decks nutzen.
- »Stem Settings« liegt im Cratebase-Steuerfenster, noch nicht im nativen Preferences-
  Dialog: CPU/GPU-Gerät, Renderlimit (Audiosekunden/Echtzeitsekunde; 0=Maximum),
  Präzision, Budget, aktive Aufträge. Gerätewechsel nur bei geschlossenem Traktor.
- Bei Needle Search/Beatjump in fehlende Blöcke bleibt der Originalmix verfügbar;
  Ziel und Überlappungsnachbarn werden priorisiert. Mutes/Filter bleiben wirksam.
- Native Favoriten: 24 Slots auf zwei Reihen, MIDI-Werte bis 24.
- Collection: zuletzt ca. 40.456 Tracks / 554 Playlisten, keine kleine Testcollection
  darüberkopieren. Collection-Sync hat Konflikt- und Schrumpfschutz.
- History: beim nächsten Editionsstart »Session Histories« als normale Playlisten;
  Cratebase Set-Planer → »Session-Histories …« zum Ansehen/Mergen/Prüfen.
  Siehe `docs/Session-Histories.md`.

## Wichtiger behobener Installer-Fehler

In `fix4` löschte eine Bereinigung rekursiv Pfade aus optionalen Umgebungsvariablen.
Bei fehlendem Wert wurde `Path('')` zum Projektordner. Ein Test traf den Checkout.
Der versionierte Stand, Git-Remote und `main` wurden wiederhergestellt; die Löschlogik
ist entfernt. **Keine rekursive Bereinigung von `UV_PYTHON_INSTALL_DIR`, `UV_CACHE_DIR`
oder ungeprüften Pfaden wieder einbauen.** Nur einen geprüften defekten venv unter
Cratebase in `.broken-*` umbenennen und neu anlegen. Gemeinsame Interpreter können
von anderen Umgebungen benutzt werden. Regressionstest: `scripts/test_setup_windows.py`.

## Prüfungen

- Haupt-PC: Ryzen 5900X, 64 GB RAM, RTX 3060 Ti 8 GB. CUDA und DirectML dort geprüft;
  DirectML-Stem-Worker hat Audio vollständig gerendert. Claw noch nicht bestätigt.
- `python -m unittest discover -s scripts -p 'test_*.py'`
- Lab-Tests in `experiments/TraktorLiveStemsLab/scripts/test_*.py`; einige ältere native
  Tests benötigen zusätzliche, ignorierte DLL-Builds. Produktiv-DLL liegt im Manifest.
- `dotnet run --project tests/Cratebase.IntegrationTests -c Release -- --history-library-checks`
- `--history-library-audit`: 140 reale Histories / 6.570 Einträge, ohne Lesefehler.

Keine Original-Collection, Musikdateien, Controller-Mappings oder aktiven Decks
überschreiben. Nicht »100 % club-stabil« behaupten, bevor reale Audiotests mit
Sprung, mehreren Decks, Gerätewahl und längerer Wiedergabe auf dem Claw bestanden sind.
