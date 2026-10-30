# Cratebase

Gespeicherte Traktor-Histories ansehen und zu Sets zusammenführen:
[Session-Histories](docs/Session-Histories.md).

Neu: eigenständiger [Beatport-Crates-Prototyp in C#/Avalonia](docs/Beatport-Crates.md) mit wiederverwendbarem Kern, Demo, Benutzername-/Passwort-Login, Playlist-Abgleich und Duplikatprüfung. Start: `start-beatport.bat`. Der Live-Zugriff ist noch nicht mit einem echten Konto verifiziert.

Android-Prototyp und eingebauter Streaming-Server: [Einrichtung, APK-Build und Funktionen](docs/Android-und-Server.md).

Neu: [Smarte Playlists, lokale KI, Set-/Transition-Workspace und Stem-Generator](docs/Smarte-Playlists-und-Set-Planung.md). Der Prototyp erhält Traktor-Cues und Originaldateien; eine Live-Stem-Mod für das laufende Traktor-Deck ist [noch nicht implementiert](docs/Traktor-Live-Stems-Machbarkeit.md).

[History-NML mit vorhandener Aufnahme abgleichen und YouTube-Zeitstempel erzeugen](docs/History-Audio-Tracklist.md): im Set Recorder, mit lokaler Audio-Erkennung, Prüfbericht, Vorhören und editierbaren Zeiten.

[Thumbnail- und Titelgenerator](docs/Thumbnail-und-Titel.md): Videoframes auswählen, Texte frei verschieben, Schriftarten/Effekte/Rahmen und Bildkorrekturen bearbeiten, lokale KI für Titel und Bildvarianten nutzen und Thumbnails als PNG exportieren.

Lokaler Musik-Manager & DJ-Collection-Tool. Plattformunabhängige Desktop-App (Linux/Windows/Mac) für die eigene Musiksammlung — von "lokales Spotify" für Normalnutzer bis zum Collection-Manager auf Lexicon/Mixo-Niveau für DJs, mit Fokus auf sehr große Bibliotheken (Referenzgröße: ~100.000 Tracks).

> **Status: Prototyp-MVP.** Scanner, Tag-Reader, SQLite-Schema und ein lauffähiger Media Player mit Playlist-Baum existieren (`src/`), gebaut vor der finalen Abstimmung einiger offener Fragen aus ARCHITECTURE.md Abschnitt 4 — s. dort Abschnitt 8 für den genauen Stand und bewusste Abweichungen.

## Starten

### Neuer Library-Workspace

- **Traktor abgleichen** liest eine NML-Collection ein und öffnet anschließend die Traktor Collection. Die einmal gewählte Quelle wird gespeichert und auch nach einem Neustart wiederverwendet. Rechtsklick auf den Button oder auf die Traktor Collection → **NML-Quelle wechseln …** wählt eine andere Datei.
- Konfliktfreie Änderungen werden direkt übernommen. Bei widersprüchlichen Änderungen erscheint die Abgleichvorschau. Der Import zeigt Fortschritt an und übernimmt auch Track-Zuordnungen alter, leer importierter Playlisten, ohne deren IDs/Favoriten zu ersetzen.
- **Club Collection** lässt sich aufklappen und enthält eigene Playlisten und Gruppen. Rechtsklick → **+ Club-Playlist** / **+ Club-Gruppe**, oder die Collection auswählen und oben **+ Playlist** / **+ Gruppe** verwenden. Bestehende Playlisten lassen sich hineinverschieben. **Nicht einsortiert** zeigt Club-markierte Tracks ohne Zugehörigkeit zu einer Club-Playlist, auch in Untergruppen. Normale Playlisten zählen hierfür nicht. Ein Drop auf eine Club-Playlist markiert den Track zugleich als Club-tauglich; das Entfernen aus einer Playlist löscht diese Markierung nicht.
- **Club exportieren** kopiert die komplette markierte Club-Auswahl in einen wählbaren Zielordner (Standard: `C:\Users\DJBaka\Music\ClubCollection`) und legt `Cratebase-Club-Collection.nml` direkt darin ab. Genre-Ordner liegen unmittelbar darunter: beispielsweise wird `Beatport/Hard Techno/2020` zu `Techno/Hard Techno`. Herkunfts- und Jahresordner entfallen. Die Vorschau zeigt Zuordnungen, Widersprüche und Dateigrößen; Ziel und Genre-Zuordnungen lassen sich ändern und werden gespeichert. Bekannte spezifische Genre-Ordner haben Vorrang vor widersprüchlichen Tags; Unklares bleibt prüfbar.
- Der Dateiexport erhält Audioqualität, Cues und Club-Playlisten. NML-Collection und Playlist-Verweise zeigen auf die exportierten Kopien. Fehlende Quellen müssen verbunden oder ausdrücklich übersprungen werden. Fortschritt, Abbruch, eindeutige Namen bei Kollisionen und überprüfte Wiederverwendung beim erneuten Export sind eingebaut. Originale und ältere Exportdateien werden nicht automatisch gelöscht. Bericht und Kopiermanifest liegen ebenfalls im Zielordner.
- Rechtsklick auf **Club exportieren → Nur Club-NML exportieren …** oder auf **Club Collection → Club Collection als NML exportieren …** exportiert weiterhin ausschließlich die NML mit Original-Dateipfaden. Beide Exportarten enthalten die Club-Hierarchie sowie die Playlisten „Club Collection“ und „Nicht einsortiert“; sie verändern den normalen Traktor-Abgleichstand nicht.
- Die persönliche Bestandsanalyse und der Vorschlag für eine dauerhafte, appübergreifende Ordnerstruktur stehen in [ClubCollection-Sortierung.md](docs/ClubCollection-Sortierung.md).
- Playlisten und Gruppen können per Drag & Drop oder Rechtsklick → **In Gruppe verschieben …** umgehängt werden. IDs, Track-Zuordnungen und Favoriten bleiben erhalten.
- Über **Spalten** je Trackliste lassen sich Metadaten frei auswählen, einschließlich Format und Bitrate. Spaltenköpfe sortieren; Suche kann auf ein Feld beschränkt werden, **Filter** kombiniert mehrere Bedingungen. **Extended** und **Intro** sind unabhängige lokale Markierungen und bleiben bei Scans und Dateikopien erhalten.
- **Ansicht** stellt die UI-Skalierung zwischen 70 und 125 % ein und speichert sie. Kleinere Werte schaffen mehr Platz für Tracks.
- Unter Windows integriert sich der Player in die System-Mediensteuerung: Play/Pause, Weiter und Zurück funktionieren auch im Hintergrund. Windows zeigt Titel und Artist des aktuellen Tracks an; die Queue bleibt beim Navigieren in der Library erhalten.
- Direkt im Player stehen die drei gespeicherten Markierungen in der Reihenfolge **Club → Extended → Intro**. Die Schalter beziehen sich immer auf den geladenen Track und spiegeln Änderungen aus den Listen.
- Nicht erreichbare Dateien werden beim Abspielen dauerhaft als **Datei nicht erreichbar** markiert und automatisch übersprungen, auch mehrere hintereinander. Auch **Vor/Zurück** überspringt sie in der jeweiligen Richtung. Die Browser-Ansicht **Datei nicht erreichbar** sammelt diese Tracks; über **Spalten → Dateistatus** und den gleichnamigen Metadatenfilter lässt sich die Markierung auch in anderen Listen anzeigen und durchsuchen. Tracks, Tags und Playlist-Zuordnungen werden dabei nicht gelöscht. Ist eine Datei später wieder lesbar, entfernt die nächste erfolgreiche Wiedergabe die Markierung. Sind keine weiteren erreichbaren Tracks in der aktuellen Wiedergabeliste vorhanden, endet die Suche ohne Endlosschleife.
- **Medientasten: Wiedergabe / Tags** schaltet pro Tastaturprofil die Hardware-Medientasten um. Im Tag-Modus gilt **Zurück → Club**, **Play/Pause → Extended**, **Weiter → Intro**; jede Taste schaltet die entsprechende Markierung um, ohne Wiedergabe oder Track zu wechseln. Das gilt auch im Hintergrund für Cratebases Windows-Mediensitzung. Die Transportbuttons im Player steuern weiterhin die Wiedergabe. Unter **Tasten** kann auch eine eigene lokale oder globale Kombination für den Moduswechsel festgelegt werden.
- Neben **MIDI** öffnet **Tasten** die Tastaturprofile. Belegungen lassen sich getrennt für **App-Fokus** und **Global** lernen, deaktivieren und löschen; Profile können angelegt, kopiert und gewechselt werden. Rechtsklick auf **Tasten** wechselt direkt das Profil. Verfügbare Aktionen: Transport, Club/Extended/Intro des Player-Tracks, Player-Track zu einem Favoriten-Slot, Recorder-Übergangsmarker und Medientasten-Moduswechsel. Slots verwenden das aktuell ausgewählte Favoritenprofil.
- Eigene globale Hotkeys sind zunächst aus; der Schalter **Global** aktiviert/deaktiviert sie sofort. Sie werden ausschließlich als konkrete Windows-Hotkeys registriert und beim Ausschalten, Profilwechsel und Beenden freigegeben. Konflikte mit bereits registrierten Hotkeys erscheinen am **Tasten**-Button und im Dialog. Globale Kombinationen benötigen mindestens zwei Zusatztasten; F12 und Windows-Kombinationen sind reserviert. Normale, nicht global registrierte Tastenkürzel anderer Apps lassen sich nicht automatisch erkennen – daher seltene Kombinationen wählen, etwa Strg+Alt+Shift+F9. Eigene Fokus-Belegungen pausieren beim Texteingeben und in Dialogen; beide Bereiche haben eigene Aktivierungsschalter. Der Modus für dedizierte Medientasten gilt unabhängig vom Schalter **Global**.
- Neben dem Logo über **Library Workspace → Set Recorder** wechseln. Geräteauswahl, FLAC/WAV/MP3-320-Aufnahme, Stereo-Pegel, manuelle/automatische Übergangsmarkierungen, Tracklist-Editor, Vorschau und Text-/Zwischenablagenexport sind wieder verfügbar. Beim Ansichtswechsel läuft die Aufnahme weiter; **● REC** bleibt sichtbar. Beim Schließen wird die Aufnahme zuerst finalisiert.
- Der Set Recorder benötigt unter Windows einen vollständigen FFmpeg-Build mit DirectShow und dem jeweiligen Audio-Encoder. **FFmpeg auswählen …** speichert einen separaten Pfad für den Recorder; die Waveform-Generierung wird dadurch nicht umgestellt. Fehlen Aufnahmegeräte oder Capture-Unterstützung, erscheint eine Meldung.
- NML-Dateien enthalten Dateiverweise, keine Audiodateien. Für den Laptop müssen die referenzierten Musikdateien ebenfalls erreichbar sein; bei abweichenden Speicherorten sind die Pfade dort neu zuzuordnen.
- Beim Vorhören lädt Cratebase vorhandene Traktor-STRIPE-Dateien über die AUDIO_ID der gespeicherten NML. Unterstützt werden ältere PRTS-Grafiken und Spectrum-Archive aus Traktor 4. Die Traktor-Verzeichnisse werden dabei ausschließlich gelesen.
- Fehlt eine lesbare Stripe, verwendet Cratebase seinen RGB-Cache oder generiert die Waveform automatisch mit FFmpeg. Generierte Peaks liegen ausschließlich unter `%LOCALAPPDATA%\Cratebase\waveforms` (bei `CRATEBASE_DATA_DIR` entsprechend unter dessen `waveforms`-Unterordner). Audio-Dateien, Traktor-Stripes und Traktor-Metadaten werden dabei nicht verändert. Ein erneuter NML-Import ist für die Waveform-Anzeige nicht nötig.
- FFmpeg muss im PATH oder neben der Cratebase-Anwendung liegen. Die Generierung verwendet auch mit eingeschränkten FFmpeg-Builds kompatibles 16-Bit-PCM. Ladezustand, Quelle und mögliche Fehler erscheinen direkt unter der Waveform.

Regressionstests (isolierte temporäre Datenbanken, keine Änderungen an deiner Collection):

```powershell
dotnet run --project tests/Cratebase.IntegrationTests/Cratebase.IntegrationTests.csproj
```

Recorder-Ansichtswechsel mit simuliertem Aufnahmegerät beziehungsweise echte Windows-Mediensitzung mit simuliertem Audioplayer prüfen:

```powershell
dotnet run --project tests/Cratebase.IntegrationTests -c Release -- --recorder-checks
dotnet run --project tests/Cratebase.IntegrationTests -c Release -- --media-checks
dotnet run --project tests/Cratebase.IntegrationTests -c Release -- --keyboard-checks
dotnet run --project tests/Cratebase.IntegrationTests -c Release -- --playback-checks
dotnet run --project tests/Cratebase.IntegrationTests -c Release -- --club-export-checks
```

Diese Prüfungen ersetzen keine Aufnahme mit einem echten Audio-Interface oder einen Test mit physischen Medientasten.

Optional einen vollständigen Import und sämtliche lokalen Playlist-Zuordnungen einer echten NML in einer temporären Testdatenbank prüfen:

```powershell
dotnet run --project tests/Cratebase.IntegrationTests/Cratebase.IntegrationTests.csproj -- --real-nml "C:\Pfad\zur\collection.nml"
```

Voraussetzung: [.NET SDK 8](https://dotnet.microsoft.com/download) installiert.

- **Linux/Mac:** `./start.sh` (oder Doppelklick, falls der Dateimanager das anbietet)
- **Windows:** `start.bat` doppelklicken

Beide Skripte liegen im Repo-Root und starten direkt `Cratebase.App.Desktop`. `start.sh` fällt
automatisch auf `~/.dotnet` zurück, falls `dotnet` nicht im PATH steht (z.B. auf rpm-ostree/
immutable Linux-Distros ohne System-Paket).

Manuell geht's auch so:
```
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
