# Private Traktor Live Stems Lab

Started 2026-10-27 00:36:43 UTC (01:36:43 Europe/Berlin). User-requested investigation window: eight hours, ending 08:36:43 UTC / 09:36:43 Europe/Berlin.

User renewed the working window at 05:48:21 UTC for three hours, until 08:48:21 UTC / 09:48:21 Berlin.

## Testversion starten

Im Repository `start-traktor-live-stems.cmd` öffnen. Das separate Kontrollfenster startet den privaten CUDA-Dienst und anschließend die instrumentierte Traktor-Testkopie. Ein bereits laufendes Traktor muss zuvor geschlossen sein. Beim Start bis zu zwei Minuten auf die vollständige Ansicht warten.

- `Traktor mit Live Stems starten`: lädt Erweiterung und private Testcollection zusammen.
- In Traktor die Playlist **LAB Live GPU Stems** öffnen. Track per Kontextmenü **Load into Deck → Load as Stem** laden.
- **Live-Trennung aktiv** schaltet die native Ersetzung während der Wiedergabe ein/aus. Ausgeschaltet liefern die vier Spuren jeweils ein Viertel des Originalmixes; sie sind dann keine getrennten Instrumente.
- **Songs hinzufügen** kopiert ausgewählte Originaldateien in den privaten Arbeitsbereich. Verfügbare Traktor-Metadaten und Cues werden in die Testcollection übernommen. Nach einem Neustart über dieses Kontrollfenster liegen die Titel in **Live Stems – hinzugefügt**. Es wird keine abgeschlossene Stem-Datei exportiert.
- Genauigkeit, GPU-Speicherbudget und Zahl gleichzeitiger Aufträge sind einstellbar. Ein geändertes Speicherbudget benötigt einen Neustart des GPU-Dienstes; bestehende Cache-Ergebnisse behalten ihre Berechnungsqualität.
- Der Ausgangspegel ist für Tests leise eingestellt. Audiogerät und Routing können in der privaten Traktor-Kopie normal eingestellt werden.

Die Laufzeiten sind auf diesem PC privat eingerichtet; das Startskript ist kein allgemeiner Installer. Das Kontrollfenster ist separat und ersetzt keinen nativen Preferences-Tab. Ohne den Mod-Launcher oder bei einer anderen Traktor-Buildversion ist die Erweiterung nicht aktiv.

## Verifizierter Zwischenstand, 06:28 UTC

Automatisches Abfangen realer Stem-Deck-Ladevorgänge, private Originaldecoder, inkrementeller CUDA-Cache und native Ersetzung hinter Traktors Vorladepuffer sind implementiert. Zwei echte Tracks laufen in nativen Stem-Decks. Vier getrennte Waveforms werden angezeigt. Der Ausgang wurde über Voicemeeter B1 lokal aufgenommen; Stem-Mutes verändern die tatsächlich ausgegebene Instrumentenspur. Beispiel: Bass-Einzelaufnahme korreliert mit dem erzeugten Bass bei 0,867, gegenüber 0,585 für den Summenmix. Das ist ein Funktionsnachweis, kein Nachweis bitidentischer Ausgabe oder perfekter Trennqualität.

147,7 s AAC: 6,59 s einschließlich Decodierung. 375,1 s MP3: 14,94 s. Residentes HTDemucs auf RTX 3060 Ti, FP16, maximal ungefähr 612 MB gemessene PyTorch-Allokation. Diese Messung umfasst nicht sämtliche Treiber-/Prozessspeicherbelegung.

Native Tests prüfen 44,1/48/96 kHz, Spulen, Rückwärtslesen, veraltete Vorladepuffer, Ein-/Ausschalten, getrennte Caches und Wiederverwendung nach Destruktion. Hardware, insbesondere S4 MK3, ist weiterhin nicht vollständig geprüft. Der Prototyp ist keine neu geschriebene DJ-Software und keine Zusicherung vollständiger Geräte-/Mapping-Kompatibilität.

Goal: investigate GPU-generated live stems in the existing Traktor Pro 4.5.1 audio engine, retaining native UI, effects, mappings, and S4 MK3 behavior. An engine-integrated prototype is the target; a companion/offline exporter alone does not meet it. A complete decompile/rebuild with identical behavior is not assumed feasible.

## Boundaries

- Installed Traktor, user collection, TSI mappings, music and recordings remain unmodified.
- Work on a hash-verified copy; any runtime instrumentation targets lab-launched processes only.
- Do not investigate activation, DRM or licensing bypasses. A normally licensed installation is the prerequisite.
- Never present a synthetic buffer swap, exported file, UI mock or offline separation as working live Traktor integration.
- Native hardware compatibility remains pending until tested with the actual device. Inventory currently detects an F1; S4 MK3 is not enumerated as present.

## Environment

- Original: `C:\Program Files\Native Instruments\Traktor Pro 4\Traktor Pro 4.exe`
- SHA-256 original and lab copy: `1B4DB54AB51D63E1F662971C74B3F37E75DF7C19291844F5AC7EC8CCC04373BA`
- Executable size: 570,408,944 bytes.
- Private binaries, tools, Python environment and Ghidra databases: `%LOCALAPPDATA%\Cratebase\traktor-lab`.
- Existing Mixxx source: `C:\Users\DJBaka\Documents\GitHub\mixxx`, commit `1d1fbaf2c6` with live stem source/cache integration. Read-only baseline.

## Investigation tracks

1. PE inventory, string and RTTI anchors for stem decoders, audio readers, deck loaders and native separation.
2. Ghidra analysis and targeted decompilation; identify real interfaces rather than guessed offsets.
3. Isolated runtime tracing of file loading and stem playback using synthetic fixtures and a copied settings environment.
4. GPU separation benchmark and a seekable, nonblocking stem cache informed by the Mixxx implementation.
5. Attempt the smallest measurable in-deck integration with safe bypass; test load, seek, loop, tempo and resource cleanup.
6. Document verified results, failed paths and remaining work with exact versions and reproducible scripts.

Progress and experiments are recorded in `JOURNAL.md`; raw reports are private and ignored by Git.

