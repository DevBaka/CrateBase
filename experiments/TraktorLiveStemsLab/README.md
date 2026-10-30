# Private Traktor Live Stems Lab

Started 2026-10-27 00:36:43 UTC (01:36:43 Europe/Berlin). User-requested investigation window: eight hours, ending 08:36:43 UTC / 09:36:43 Europe/Berlin.

User renewed the working window at 05:48:21 UTC for three hours, until 08:48:21 UTC / 09:48:21 Berlin.

## Testversion starten

Im Repository `start-traktor-live-stems.cmd` öffnen. Das separate Kontrollfenster startet den privaten CUDA-Dienst und anschließend die instrumentierte Traktor-Testkopie. Ein bereits laufendes Traktor muss zuvor geschlossen sein. Beim Start bis zu zwei Minuten auf die vollständige Ansicht warten.

- `Traktor mit Live Stems starten`: startet Erweiterung und private Collection. Eine vorhandene private Collection wird beim Start erhalten.
- Beim ersten Start mit der Collection-Anbindung wird die originale `Traktor 4.5.1/collection.nml` vollständig und unverändert in den privaten Arbeitsbereich kopiert; die alte kleine Testcollection wird gesichert. Danach bleiben Änderungen in dieser privaten Collection erhalten. Unter **Traktor-Collection · private Kopie** lassen sich die Quelldatei wählen und mit **Original erneut übernehmen** die neuesten Originaldaten neu kopieren. Dafür muss Traktor geschlossen sein. Jede Ersetzung sichert die vorherige private Collection unter `collection-backups/`. Änderungen werden nicht automatisch in die originale Collection zurückgeschrieben.
- Normale Audiodateien aus Ordnerbrowser, Collection oder Playlisten mit dem üblichen Ladebefehl laden. Bei aktivierter Erweiterung wird automatisch ein natives Stem-Deck gewählt. Ein vorheriges Hinzufügen zur Testplaylist ist nicht erforderlich.
- Unter **GPU und Leistung** schaltet **Live Stems aktivieren** die automatische Deck-Auswahl und native Audio-Ersetzung ein/aus. Die Einstellung wird gespeichert und ohne Traktor-Neustart übernommen. **Traktor: … bestätigt** stammt aus der laufenden Mod-Sitzung; der GPU-Status wird separat angezeigt.
- Deaktiviert werden neue Songs als normale Tracks geladen. Schon geladene Stem-Decks werden nicht während der Wiedergabe ausgetauscht: Ihre vier Spuren liefern jeweils ein Viertel des Originalmixes. Mutes, Filter, Effekte und Fader bleiben wirksam; für den gesamten Originalmix müssen alle vier Spuren eingeschaltet sein.
- Temporäre Stem-Container und CUDA-Caches liegen ausschließlich im privaten Arbeitsbereich. Musikdateien werden dafür nicht verändert oder vollständig kopiert; es sind keine fertigen portablen Stem-Exporte.
- Genauigkeit, GPU-Speicherbudget und Zahl gleichzeitiger Aufträge sind einstellbar. Ein geändertes Speicherbudget benötigt einen Neustart des GPU-Dienstes; bestehende Cache-Ergebnisse behalten ihre Berechnungsqualität.
- Der Ausgangspegel ist für Tests leise eingestellt. Audiogerät und Routing können in der privaten Traktor-Kopie normal eingestellt werden.

Die Laufzeiten sind auf diesem PC privat eingerichtet; das Startskript ist kein allgemeiner Installer. Das Kontrollfenster ist separat und ersetzt keinen nativen Preferences-Tab. Ohne den Mod-Launcher oder bei einer anderen Traktor-Buildversion ist die Erweiterung nicht aktiv.

## Stabilitätsstand vom 29.10.2026

### Favoriten-Erweiterung vom 30.10.2026

Der Starter lädt die zweite native Favoritenreihe automatisch mit. Schriftquelle,
Symbolabstand und einzeilige Begrenzung werden von den ursprünglichen Controls
übernommen. Die zusätzlichen Ziele bleiben in `additional-favorites.json` erhalten.
Die Controller-Manager-Parameter „Favorites Select“ und „Append to favorite“
besitzen jetzt Werte bis „Favorite 24“. Erweiterte Werte werden auf die zweite
Zielbank übersetzt, bevor ein nativer Zwölf-Slot-Handler aufgerufen wird.
Die Änderung prüft drei buildgebundene Instruktionsfolgen und betrifft nur den
privaten laufenden Prozess, nicht die installierte EXE.

Im laufenden Traktor geprüft: Beschriftung mit Symbolabstand nach vollständiger
Neuzeichnung; natives Append-Dropdown mit Favorite 13–24. Ein Test mit physischem
MIDI-Eingabegerät wurde hierbei nicht durchgeführt (Generic MIDI In-Port: n/a).

Die Edition verwendet jetzt einen eigenen Versionsordner unter
`C:\Users\DJBaka\Documents\Native Instruments\Traktor 4.5 Cratebase Edition`.
Dort liegen Collection, TSI-Einstellungen, History, Stripes und Transients.
`sandbox.json` speichert diesen Ort als `profile_directory`; Launcher und
Kontrollfenster beziehen ihren Collection-Pfad daraus. Die Pfadumleitung nimmt
diesen Ordner ausdrücklich aus der allgemeinen NI-Umleitung aus. Der bisherige
private Profilordner bleibt als Sicherung bestehen. GPU-Laufzeiten und temporäre
Stem-Caches liegen weiterhin unter LocalAppData/Cratebase.

Kein Nachweis hundertprozentiger Ausfallsicherheit oder einer Freigabe für unbeaufsichtigte Club-Sets. Die Änderung instrumentiert die konkrete Traktor-Buildversion 4.5.1.21. Mehrstündige Tests mit S4 MK3, tatsächlichem Club-Audiogerät/ASIO, allen benötigten Mappings, vier spielenden Decks und hoher GPU-Last stehen aus.

Geprüft: native 44,1/48/96-kHz-Verarbeitung, Vorwärts-/Rückwärtslesen, Sprünge, Cache-Isolation und Leser-Wiederverwendung; 400 Umschalt-/Sprungzyklen mit vier Spuren sowie Umschalten aus einem zweiten Thread während der Reads. Einstellungen werden validiert und atomar gespeichert. Tests prüfen paralleles Schreiben/Lesen, ungültige Werte, begrenzte Vorbereitungsaufträge, Zeitüberschreitungen und authentifizierte Pipe-Kommunikation nach einem hängenden Client.

Praktischer Test: normale FLAC-Ladevorgänge aus dem Ordnerbrowser, deaktiviert als normaler Track und nach Aktivierung als Stem-Deck; die bereits laufende Deck-Wiedergabe blieb aktiv. Bei gestopptem GPU-Dienst funktionierten sowohl fertig gecachte Stems als auch der Originalmix-Fallback eines ungecachten Tracks. Nach Wiederanlauf und erneutem Laden wurden alle 8.705 Blöcke des zweiten Testtracks erzeugt; die vier nachgelagerten nativen Leser lieferten anschließend getrennte GPU-Signale mit nichtnull Energie. Das ist kein Nachweis für einen CUDA-Treiberabsturz oder jedes Audio-Routing.

Die Dateivorbereitung antwortet nach spätestens zwei Sekunden mit einem Fehler/Fallback, wenn sie nicht fertig wird. Neue GPU-Anfragen erhalten begrenzte Authentifizierungs-/Antwortwartezeiten. Bei einer fehlgeschlagenen Anfrage bleibt der Originalmix-Fallback verfügbar; nach Wiederherstellung des Dienstes kann erneutes Laden einen neuen Auftrag auslösen. Es gibt beim normalen Start kein automatisches Ende nach zwölf Stunden mehr.

18 automatisierte Prüfungen bestanden: `test_live_settings.py`, `test_pipe_transport.py`, `test_gpu_service_control.py`, `test_dynamic_tracks.py`, `test_add_live_tracks.py`, `test_downstream_bridge.py`. Native Tests mit `LAB_LIVE_DLL=live_bridge_v4.dll` und der privaten Python-Laufzeit ausführen. Python-Quellen zusätzlich mit `compileall` geprüft.

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

