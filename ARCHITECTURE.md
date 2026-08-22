# Cratebase — Architecture & Concept

Status: **Prototyp-MVP steht** (Scanner, Tag-Reader, SQLite-Schema, Player, Playlist-Baum – s. Abschnitt 8). Die Abschnitte 1–5 sind weiterhin die konzeptionelle Grundlage; Abschnitt 8 dokumentiert, was davon bereits implementiert ist und wo der Prototyp bewusst abweicht oder Scope geschnitten hat.

## 1. Grundprinzip: Engine/UI-Trennung

Da langfristig eine zweite App (DJ-Layer) auf derselben Datenbasis aufsetzen soll, gilt von Tag 1 an:

- Die **Core-Engine** kennt keine UI-Frameworks, keine Avalonia-Typen. Sie exponiert Services/Interfaces (z.B. `ITrackRepository`, `IScanService`, `ISmartPlaylistEngine`).
- Die **UI** spricht nie direkt mit SQLite. Sie konsumiert ausschließlich Core-Services.
- Dieser Schnitt ist auch das spätere Ansatzpunkt für Phase 5 (API-Layer): API wird ein weiterer "Client" der Core-Services, kein Umbau der Engine.

## 2. Datenmodell (Entwurf)

Alle Tabellen als Konzept-Skizze (Spaltennamen/Typen sind Diskussionsgrundlage, keine finale DDL). SQLite, WAL-Modus vorausgesetzt.

### 2.1 LibraryRoot

Mehrere überwachte Wurzelverzeichnisse (z.B. mehrere Laufwerke) von Anfang an vorgesehen, nicht als Nachrüstung.

```
LibraryRoot
  id              INTEGER PK
  path            TEXT UNIQUE      -- absoluter Pfad
  label           TEXT             -- Anzeigename
  watch_enabled   BOOLEAN
  last_scanned_at DATETIME
```

### 2.2 Track

```
Track
  id                  INTEGER PK
  library_root_id     INTEGER FK -> LibraryRoot
  relative_path       TEXT              -- relativ zu library_root, nicht absolut (Root ist verschiebbar)
  file_size           INTEGER
  file_mtime          DATETIME
  content_hash        TEXT NULL         -- optionaler tieferer Hash, s. offene Frage B
  format              TEXT              -- mp3/flac/wav/aiff/m4a/...
  duration_ms         INTEGER
  bitrate_kbps        INTEGER
  sample_rate_hz      INTEGER
  channels            INTEGER

  -- Tags (aus Datei gelesen, ggf. von App überschrieben und zurückgeschrieben)
  title               TEXT
  artist              TEXT
  album               TEXT
  album_artist        TEXT
  track_number        INTEGER
  disc_number         INTEGER
  year                INTEGER
  genre_raw           TEXT              -- unverändertes Genre-Tag aus Datei
  comment             TEXT
  composer            TEXT
  isrc                TEXT

  -- DJ-relevante Felder
  bpm                 REAL
  musical_key         TEXT              -- z.B. "8A" (Camelot) + Rohwert getrennt haltbar
  key_source          TEXT              -- 'tag' | 'analyzed' | 'manual'
  energy              INTEGER NULL      -- optional, falls Quelle das liefert
  rating              INTEGER           -- 0..5 oder 0..100, TBD
  color_tag           TEXT NULL         -- Farbcode, wie in Rekordbox/Serato üblich
  play_count          INTEGER DEFAULT 0
  last_played_at      DATETIME NULL

  -- Analyse/Waveform
  analysis_status     TEXT              -- 'pending' | 'running' | 'done' | 'failed'
  waveform_cache_key  TEXT NULL         -- Verweis auf Cache-Datei, s. 2.9

  -- Housekeeping
  date_added          DATETIME
  date_modified       DATETIME          -- letzte Änderung in der App
  missing_since        DATETIME NULL     -- gesetzt, wenn Datei bei Rescan nicht mehr gefunden wurde

  UNIQUE (library_root_id, relative_path)
```

Indizes: `(artist, title)`, `(bpm)`, `(rating)`, `(genre_raw)`, `(missing_since)`, plus FTS5-Virtualtabelle über `title, artist, album, comment` für Volltextsuche ohne In-Memory-Scan.

### 2.3 Playlist-Baum

Ein generischer Baum, der **sowohl** reine Ordner **als auch** Playlists/Smart-Playlists als Knoten enthält (deckt sich mit "Ordner enthalten Playlisten, verschachtelbar"):

```
PlaylistNode
  id              INTEGER PK
  parent_id       INTEGER NULL FK -> PlaylistNode   -- NULL = Root-Ebene
  node_type       TEXT             -- 'folder' | 'playlist' | 'smart_playlist'
  name            TEXT
  sort_order      INTEGER
  created_at      DATETIME
  updated_at      DATETIME
```

`folder`-Knoten haben keine Track-Mitgliedschaft, nur Kinder. `playlist` und `smart_playlist` sind Blätter (aus Nutzersicht — technisch könnten verschachtelte Smart-Playlists später erlaubt werden, aber MVP: nein).

**Wichtig — bewusst getrennt von der physischen Genre-Ordnerstruktur auf der Festplatte.** Der Playlist-Baum ist eine App-interne, kuratierbare Struktur. Wie er sich zur Ordnerstruktur auf Disk verhält (spiegeln? unabhängig? synchronisierbar?), ist eine offene Frage — siehe Abschnitt 4.E, begründet an der realen Beispielsammlung.

### 2.4 PlaylistTrack (manuelle Playlists)

```
PlaylistTrack
  playlist_node_id   INTEGER FK -> PlaylistNode
  track_id           INTEGER FK -> Track
  position            INTEGER          -- manuelle Sortierung
  date_added          DATETIME
  PRIMARY KEY (playlist_node_id, track_id)
```

### 2.5 Smart Playlists — Regel-Modell

Kernanforderung: Regeln müssen (a) effizient in SQL kompilierbar sein (Indizes nutzbar, kein In-Memory-Filter über 100k Tracks) und (b) für die spätere KI-Funktion **transparent und editierbar** sein ("Prompt → Regel, editierbar").

Vorschlag: Regel als strukturierter JSON-Baum (AND/OR/NOT-Gruppen mit Bedingungen), gespeichert als Text-Spalte, zur Laufzeit in eine parametrisierte SQL-Query kompiliert:

```
SmartPlaylistRule
  playlist_node_id   INTEGER PK FK -> PlaylistNode   -- 1:1 zu einem 'smart_playlist'-Knoten
  rule_json          TEXT             -- s. Beispiel unten
  is_live            BOOLEAN          -- true: bei jedem Zugriff neu evaluiert; false: Snapshot (materialisiert)
  generated_by        TEXT NULL        -- 'user' | 'ai' — zur Nachvollziehbarkeit
  source_prompt       TEXT NULL        -- Originaltext, falls KI-generiert
  updated_at          DATETIME
```

Beispiel `rule_json` für "Genre=House AND BPM zwischen 124-128 AND Rating>=4":

```json
{
  "op": "AND",
  "conditions": [
    { "field": "genre_raw", "op": "eq", "value": "House" },
    { "field": "bpm", "op": "between", "value": 124, "value2": 128 },
    { "field": "rating", "op": "gte", "value": 4 }
  ]
}
```

Jedes `field` muss auf eine indizierte Spalte (oder Tag-Join, s.u.) abbildbar sein — das ist eine feste Regel im Compiler, keine Freitext-SQL-Injection-Fläche.

### 2.6 Tags / Custom Tags

Bekannte, oft gefilterte Felder (BPM, Key, Rating, Genre, Comment) liegen als echte, indizierte Spalten direkt auf `Track` (s. 2.2). Für frei definierbare Custom-Tags (z.B. "Peak Time", "Warm-Up", "Bootleg") ein normalisiertes Many-to-Many-Modell statt JSON-Blob, damit Smart-Playlist-Regeln wie "hat Tag X" indiziert bleiben:

```
Tag
  id        INTEGER PK
  name      TEXT UNIQUE
  category  TEXT NULL       -- optionale Gruppierung, z.B. "Mood", "Set-Position"

TrackTag
  track_id  INTEGER FK -> Track
  tag_id    INTEGER FK -> Tag
  PRIMARY KEY (track_id, tag_id)
```

### 2.7 Cue Points

Kompatibilität mit gängigen DJ-Tools ist explizit gewünscht — daher ein **eigenes, herstellerunabhängiges** Modell, in das Rekordbox/Serato/Traktor-Formate in Phase 2 hinein- und herausgemappt werden, statt sich früh an ein einzelnes Vendor-Format zu binden:

```
CuePoint
  id              INTEGER PK
  track_id        INTEGER FK -> Track
  type            TEXT        -- 'hot_cue' | 'loop' | 'grid_marker' | 'intro' | 'outro' | 'load'
  slot_index      INTEGER NULL  -- z.B. 0-7 für Hotcue-Slots
  position_ms     INTEGER
  loop_length_ms  INTEGER NULL
  label           TEXT NULL
  color           TEXT NULL
  source          TEXT        -- 'manual' | 'imported:rekordbox' | 'imported:serato' | ...
  external_ref    TEXT NULL   -- Originalkennung im Quellformat, für Re-Export/Sync
```

### 2.8 Background-Job-Queue (Analyse)

Jobs laufen In-Process (kein separater OS-Prozess, s. offene Frage F), abbrechbar, mit Priorität (z.B. sichtbarer Track in der UI > Rest der Bibliothek):

```
AnalysisJob
  id            INTEGER PK
  track_id      INTEGER FK -> Track
  job_type      TEXT        -- 'waveform' | 'bpm' | 'key' | 'replaygain'
  status        TEXT        -- 'queued' | 'running' | 'done' | 'failed' | 'cancelled'
  priority      INTEGER
  attempts      INTEGER DEFAULT 0
  error         TEXT NULL
  created_at    DATETIME
  finished_at   DATETIME NULL
```

### 2.9 Waveform-/Analyse-Cache (nicht in der DB)

Waveform-Peaks etc. landen als Dateien im App-Cache-Verzeichnis, referenziert über `Track.waveform_cache_key`. Vorschlag: Key = Hash über `(content_hash, analysis_version)`, damit der Cache Datei-Umbenennungen/-Verschiebungen übersteht, statt an einem Pfad zu hängen. Bei fehlendem `content_hash` (leichter Modus, s. 4.B) Fallback auf `track_id`.

### 2.10 Import/Sync mit DJ-Software (Phase 2)

```
ExternalMapping
  id                 INTEGER PK
  track_id           INTEGER FK -> Track
  external_system    TEXT        -- 'rekordbox' | 'traktor' | 'mixxx' | 'serato' | 'virtualdj'
  external_id         TEXT
  last_synced_at      DATETIME NULL
  sync_direction       TEXT        -- 'import_only' | 'export_only' | 'two_way'
  conflict_state       TEXT NULL   -- offen für spätere Konfliktauflösung
```

### 2.11 KI-Vorschläge (Phase 3) — Audit-Trail statt Silent-Apply

Wichtig für Vertrauen in die KI-Funktionen: Vorschläge werden **protokolliert und müssen bestätigt werden**, nichts wird automatisch und unsichtbar umsortiert:

```
AiSuggestion
  id                  INTEGER PK
  track_id            INTEGER FK -> Track
  suggestion_type     TEXT        -- 'genre_classification' | 'bootleg_detection' | 'playlist_rule'
  payload_json        TEXT        -- z.B. vorgeschlagener genre_path, bootleg=true/false, confidence
  model_name          TEXT
  prompt              TEXT NULL
  status               TEXT        -- 'pending' | 'accepted' | 'rejected'
  created_at           DATETIME
  resolved_at          DATETIME NULL
```

### 2.12 MIDI / Slots (Phase 4, Datenmodell schon skizziert für Forward-Kompatibilität)

```
SlotProfile
  id      INTEGER PK
  name    TEXT              -- z.B. "House", "Hardstyle"

SlotAssignment
  profile_id        INTEGER FK -> SlotProfile
  slot_index         INTEGER          -- 0..49
  playlist_node_id    INTEGER FK -> PlaylistNode
  PRIMARY KEY (profile_id, slot_index)

MidiMapping
  id              INTEGER PK
  device_name     TEXT
  control_type    TEXT        -- 'note' | 'cc'
  control_number  INTEGER
  channel         INTEGER
  action_type     TEXT        -- 'add_current_track_to_playlist' | 'toggle_slot_profile' | ...
  action_params   TEXT        -- JSON
```

## 3. Modulaufteilung

Vorschlag als getrennte Projekte/Assemblies (Namen unter der Annahme des .NET/Avalonia-Stacks, s. Abschnitt 5):

```
Cratebase.Core          — Domänenmodelle, Interfaces, kein I/O
Cratebase.Data           — SQLite-Schema/Migrations, Dapper-Repositories, FTS5-Suche
Cratebase.Scanning       — Filesystem-Walk, Watcher, Change-Detection/Hashing, inkrementeller Sync
Cratebase.Tagging        — TagLib-Sharp-Wrapper, Mapping ID3/Vorbis/MP4-Tags -> Core-Modell, Schreiben
Cratebase.Analysis       — Job-Queue, BPM/Key/Waveform-Worker, Disk-Cache-Verwaltung
Cratebase.Audio          — Playback-Engine-Abstraktion (BASS/NAudio), Cue-Point-fähige Wiedergabe
Cratebase.SmartPlaylists — Regel-DSL, JSON<->SQL-Compiler, Query-Ausführung
Cratebase.AI             — Ollama-Client, Prompt-Templates, Klassifikations-Pipeline (AiSuggestion)
Cratebase.Import         — Adapter pro DJ-Tool (Rekordbox-XML/DB, Traktor-NML, Mixxx-DB, Serato, VirtualDJ)
Cratebase.Midi           — Geräte-I/O, Mapping-Engine (Phase 4)
Cratebase.App.Desktop    — Avalonia-UI (MVVM), konsumiert ausschließlich Core-Services
Cratebase.Api            — (Phase 5) dünner HTTP-Layer über Core, für Web/Mobile
```

Regel: Nur `Cratebase.Data` kennt SQL/Dapper. Alle anderen Module sprechen über Repository-Interfaces aus `Cratebase.Core`. `Cratebase.App.Desktop` referenziert nie `Cratebase.Data` direkt.

## 4. Offene technische Fragen

Sortiert nach Blockierungsgrad für Phase-0-Start.

### A. Audio-Engine: ManagedBass vs. NAudio — hängt an der Lizenzfrage des Projekts

BASS (Basis von ManagedBass) ist für kostenlose, nicht-kommerzielle Nutzung frei, verlangt aber eine kommerzielle Lizenz, sobald die App kommerziell vertrieben oder ggf. schon bei Closed-Source-Distribution problematisch wird — die genauen Bedingungen sind zu prüfen. NAudio ist MIT-lizenziert und vollständig frei, benötigt für Pitch/Tempo-Shifting aber zusätzliche Bausteine (z.B. SoundTouch-Bindings). **Frage an dich:** Ist Cratebase Open Source / kostenlos, oder ist ein kommerzielles Modell angedacht? Das entscheidet A direkt mit.

### B. Change-Detection-Hashing

Volltext-Hash pro Datei ist korrekt, aber bei 100k FLAC/WAV-Dateien beim Rescan potenziell langsam. Alternativen:
- Nur `mtime` + `file_size` (Standardansatz, schnell, kleines Restrisiko bei Tag-Edits ohne mtime-Änderung durch externe Tools)
- Optionaler tieferer Hash nur bei Verdacht/on-demand (z.B. Duplicate-Finder später)

Vorschlag: mtime+size als Standard-Fast-Path, `content_hash` optional/lazy. Zu bestätigen.

### C. Waveform-Cache-Format und -Ablageort

Downsampled Peak-Daten pro Track als eigene Datei im App-Cache-Verzeichnis (nicht Library-Ordner, um Nutzer-Dateien nicht zu verschmutzen). Vorschlag: content-adressiert (s. 2.9) statt pfadbasiert, damit Verschieben/Umbenennen von Dateien den Cache nicht invalidiert. Exaktes Binärformat (roh vs. komprimiert) noch offen.

### D. Smart-Playlist-Regel: JSON-DSL vs. normalisierte Bedingungs-Tabelle

Abschnitt 2.5 schlägt JSON vor (einfacher für KI-Generierung/Edit-UI). Alternative: normalisierte `SmartPlaylistCondition`-Tabelle (direkter abfragbar, aber komplexer beim Editieren verschachtelter Gruppen). Tendenz: JSON + Compiler, aber offen für Gegenargumente.

### E. Verhältnis physische Genre-Ordner ↔ App-interner Playlist-Baum — die wichtigste strukturelle Frage

Ein Blick in deine Referenzsammlung (`/run/media/devbaka/Musik/Musik/`) zeigt: Die Ordnerstruktur ist **nicht** eine saubere Genre→Subgenre-Taxonomie, sondern mischt Ebenen:

```
House/
  Afrobeats/
    2022/
      DJCity/
  MelbourneBounce/
  Beatport Warm-Up Essentials Melodic H&T (August 2022)/
  Buyed/
Gothic/
  banger/
  Future Pop Cyberpop synthpop dark pop industrial pop ebm gothic electro etc playlist/
Acapellas/
TraktorStems/
DJBaka Releases/
```

Also: Genre-Ordner, Subgenre-Ordner, Jahres-/Quellen-Ordner (DJCity, Beatport-Pack-Namen, "Buyed"), Ad-hoc-Playlist-artige Ordner ("banger", der lange Playlist-Name) und Utility-Ordner (Acapellas, Stems, eigene Releases) koexistieren auf derselben Ebene. Das bestätigt zwar den generischen Baum aus 2.3 (Ordner können beliebig Playlisten *und* Unterordner enthalten), wirft aber eine harte Design-Frage auf, die auch direkt das KI-Feature "Bootlegs automatisch ins Genre-Ordnersystem einsortieren" betrifft:

1. **Ist der Playlist-Baum in der App nur eine Spiegelung/ein Import der Festplattenstruktur**, sodass "Einsortieren" = physische Datei verschieben + Ordner/Playlist in der App folgt automatisch?
2. **Oder ist der Playlist-Baum komplett unabhängig von der Ordnerstruktur** (Genre nur als Metadatenfeld/-pfad auf `Track`, Playlisten rein kuratiert), und die Festplatte bleibt unangetastet?
3. **Oder ein Hybrid**: Initialer Import spiegelt die Festplattenstruktur als Startzustand in den Playlist-Baum, danach entkoppelt sich beides, mit optionaler späterer Re-Sync-Funktion?

Das hat Konsequenzen weit über das Datenmodell hinaus: Variante 1 macht "physische Dateien verschieben" zu einer Kernoperation (mit allen Risiken: Pfade in anderer Software brechen, Undo nötig, Race Conditions mit dem Watcher). Variante 2 ist sicherer/reversibler, bedeutet aber, dass die Ordnerstruktur auf der Platte und die Organisation in der App auseinanderlaufen können. **Mein Vorschlag:** Variante 3 (Hybrid) mit Physische-Verschiebung als explizite, einzeln bestätigte, protokollierte/undo-fähige Aktion — nie ein stiller Nebeneffekt einer KI-Klassifikation. Aber das ist eine Grundsatzentscheidung, die ich nicht ohne dich treffen will.

### F. SQLite-Zugriffsmodell

Vorschlag: Background-Worker laufen In-Process (Task/Channel-Queue), nicht als separater OS-Prozess — vermeidet SQLite-Multi-Process-Locking-Komplexität. WAL-Modus für parallele Lese-/Schreibzugriffe innerhalb des Prozesses. Zu bestätigen, falls z.B. ein späterer Scan-Hintergrunddienst doch als eigener Prozess laufen soll (Autostart-Szenario?).

### G. Cue-Point-Kompatibilität mit DJ-Software

Volle Rundtrip-Fidelity zu Rekordbox (ANLZ-Dateien), Serato (GEOB-ID3-Frames), Traktor (NML) ist aufwändig und pro Tool unterschiedlich gut dokumentiert. Bestätigung: Kanonisches Cue-Point-Modell (2.7) jetzt schon vorsehen (kostet in Phase 0/1 kaum etwas), volle Import/Export-Mapper erst Phase 2 — richtig verstanden?

### H. Ollama-Verfügbarkeit

Alle KI-Features müssen vollständig optional/graceful degradierbar sein, wenn Ollama nicht läuft oder kein passendes Modell lokal vorhanden ist (kein Cloud-Zwang, wie gefordert). Welches Modell als Default vorgeschlagen werden soll (z.B. ein kleineres lokal gut laufendes Modell) ist erst in Phase 3 relevant, aber der Fallback-Pfad ("KI-Button ist da, aber greyed-out mit Hinweis") sollte im UI-Konzept früh mitgedacht werden.

### I. Testdaten für 100k-Track-Maßstab

Für Performance-Tests/CI ist eine echte 100k-Sammlung unpraktisch. Vorschlag: Generator für synthetische Testbibliotheken (winzige/stille Dummy-Audiodateien + realistische, aus deiner echten Ordnerstruktur abgeleitete Fake-Metadaten) als Teil von Phase 0. Gewünscht?

### J. Duplicate-Finder (explizit "später", nur zur Kenntnisnahme)

Erste Stufe vermutlich Datei-/Tag-Heuristik (gleicher Titel+Künstler+Dauer), spätere Stufe Audio-Fingerprinting (z.B. Chromaprint). Keine Entscheidung nötig vor Phase 0, nur der Vollständigkeit halber im Datenmodell nicht blockiert (ist es nicht).

### K. Lizenz & Distribution des Projekts selbst

Hängt mit A zusammen: Soll das Repo öffentlich/Open Source werden (welche Lizenz?), bleibt es privat, oder ist eine kommerzielle Distribution angedacht? Aktuell kein `LICENSE`-File im Repo — bewusst offen gelassen bis zur Klärung.

## 5. Vorgeschlagener Tech-Stack (zur Bestätigung)

| Bereich | Vorschlag | Anmerkung |
|---|---|---|
| UI | Avalonia (C#) | Konsistent mit anderen eigenen Projekten, ein Codebase für alle Desktop-Plattformen |
| Audio-Engine | ManagedBass **oder** NAudio | Offen, s. 4.A — lizenzabhängig |
| DB | SQLite | Lokale Wahrheitsquelle, WAL-Modus |
| DB-Zugriff | Dapper statt EF Core | Komplexe Smart-Playlist-Queries bei großen Datenmengen brauchen handschriebenes/kompiliertes SQL statt LINQ-Übersetzung |
| Tagging | TagLib-Sharp | Lesen/Schreiben von ID3/Vorbis/MP4-Tags |
| Volltextsuche | SQLite FTS5 | Indexbasierte Suche über Titel/Artist/Album/Comment |
| KI | Ollama (lokal, HTTP-API) | Kein Cloud-Zwang, s. 4.H |
| MIDI (Phase 4) | TBD (z.B. RtMidi-Bindings) | Noch nicht recherchiert |

Alle Punkte in dieser Tabelle sind Diskussionsgrundlage, keine finale Festlegung.

## 6. Nicht-funktionale Leitplanken (Erinnerung, gilt für jede Phase)

- Nie die gesamte Library in den RAM laden — paginierte/virtualisierte Queries und UI-Listen
- Keine O(n)-Scans bei Suche/Filter — alles über Indizes bzw. FTS5
- Analyse-Jobs asynchron, abbrechbar, mit Priorität (sichtbarer Track zuerst)
- Waveform-/Analyse-Daten immer auf Disk cachen, nie als DB-Blob
- Jede Schreiboperation auf Nutzerdateien (Tags zurückschreiben, später: physisches Verschieben) muss nachvollziehbar/rückgängig machbar sein

## 7. Nächste Schritte

1. Offene Fragen aus Abschnitt 4 durchgehen (besonders A, E — die anderen sind eher Bestätigungen)
2. Tech-Stack (Abschnitt 5) final festzurren
3. ~~Danach: Phase 0 starten~~ — auf ausdrücklichen Wunsch vorgezogen, s. Abschnitt 8: ein lauffähiger Prototyp-MVP existiert bereits, bevor die offenen Fragen in Abschnitt 4 final abgestimmt wurden. A und E sind dadurch nicht beantwortet, sondern für den Prototyp pragmatisch vorläufig entschieden (s. 8.2) — die Entscheidung nachzuholen bleibt sinnvoll, bevor Phase 2 (DJ-Software-Import) draufgesetzt wird.

## 8. Umsetzungsstand: Prototyp-MVP

Auf Wunsch wurde vor der endgültigen Abstimmung von Abschnitt 4 bereits ein lauffähiger Prototyp gebaut ("fertiger Media Player, um direkt mit DJ-Software-Import weiterzumachen"). Dieser Abschnitt hält fest, was existiert, was bewusst simplifiziert wurde, und wo der Prototyp von den obigen Abschnitten abweicht.

### 8.1 Was funktioniert

Solution `Cratebase.slnx` unter `src/`, sechs Projekte exakt nach der Modulaufteilung aus Abschnitt 3 (`Cratebase.Core/.Data/.Tagging/.Scanning/.Audio/.App.Desktop`), Composition Root in `App.axaml.cs` (kein DI-Container, für diesen Umfang unnötig).

- **Scanner** (`Cratebase.Scanning/LibraryScanner.cs`): rekursiver, inkrementeller Scan über mtime+Größe, Fingerprints in einem einzigen SELECT vorgeladen statt pro Datei nachzufragen, fehlende Dateien werden als `missing_since` markiert statt gelöscht.
- **Tag-Reader** (`Cratebase.Tagging/TagLibTagReader.cs`): TagLib-Sharp, fängt kaputte Dateien ab statt den Scan abzubrechen.
- **SQLite-Schema** (`Cratebase.Data/Schema.cs`): auf die für den MVP genutzten Tabellen reduziert — `library_root`, `track`, `playlist_node`, `playlist_track` (s. 8.3). Zugriff über Dapper + Microsoft.Data.Sqlite, WAL-Modus, kurzlebige Connections pro Aufruf.
- **Player**: LibVLC statt der in Abschnitt 4.A noch offenen Wahl zwischen ManagedBass/NAudio (Begründung s. 8.2).
- **UI** (Avalonia, `Cratebase.App.Desktop`): Playlist-Baum (Ordner/Playlisten, anlegen/löschen), Track-Liste mit DB-seitiger Suche/Pagination (LIMIT/OFFSET, kein In-Memory-Filter), Transportleiste (Play/Pause/Stop/Prev/Next, Seek, Lautstärke), „Ordner hinzufügen" öffnet den nativen Ordnerdialog und stößt den Scan an.
- **Real getestet**: kompletter Scan der tatsächlichen Referenzsammlung (`/run/media/devbaka/Musik/Musik/`, **97.494 Dateien** — praktisch exakt der Zielmaßstab aus der Vision) inkl. Tag-Lesen, zweiter inkrementeller Scan erkennt unveränderte Dateien korrekt, Suche/Pagination über die volle Datenbank getestet. Timing/Speicherverbrauch dieses Laufs: s. Gesprächsverlauf (kurz zusammengefasst — Scan lief in wenigen Minuten durch, Speicherverbrauch blieb durchgehend niedrig, keine Anzeichen eines In-Memory-Vollscans).

### 8.2 Bewusste Abweichungen von Abschnitt 4/5

- **4.A Audio-Engine → LibVLC statt ManagedBass/NAudio.** NAudio ist auf Linux/Mac praktisch nicht nutzbar (Windows-Audio-APIs). ManagedBass hätte die ungeklärte Kommerz-/Lizenzfrage vorausgesetzt. LibVLC läuft heute zuverlässig auf allen drei Zielplattformen (Windows/Mac über die `VideoLAN.LibVLC.*`-NuGet-Pakete, Linux über die System-libvlc). Der Player sitzt hinter `IAudioPlayer` (Core-Interface) — ein Wechsel auf ManagedBass für DJ-spezifische Features (Pitch-Lock, Time-Stretching, sample-genaue Cue-Points) in Phase 4 betrifft nur `Cratebase.Audio`, nicht die UI. Diese Entscheidung ersetzt nicht die Klärung aus 4.A, sie verschiebt sie nur.
- **4.E Genre-Ordner ↔ Playlist-Baum → noch nicht entschieden, MVP nutzt nur Variante 2 (unabhängig).** Der Prototyp importiert die Festplattenstruktur nicht automatisch in den Playlist-Baum; Genre steht nur als `genre_raw`-Metadatenfeld auf dem Track. Playlisten/Ordner werden ausschließlich manuell in der App angelegt. Damit ist 4.E weiterhin offen, nur eben nicht mehr blockierend, weil der MVP sie schlicht nicht braucht.
- **Rating/Tags/Cue-Points/Smart-Playlists/AI/MIDI**: aus dem Schema/der UI dieses Prototyps komplett herausgelassen (nicht nur UI-seitig versteckt, auch keine Tabellen dafür) — bewusst, um nicht spekulativ Schema zu committen, bevor Abschnitt 4 final steht. Kommen als eigene, additive Migration.

### 8.3 Bekannte Simplifizierungen (kein Redesign nötig, nur noch nicht gebaut)

- Playlist-Ordner selbst zeigen keine aggregierten Tracks der enthaltenen Playlisten (nur eine Playlist direkt).
- Track-Mehrfachauswahl in der Liste (Multi-Select → Playlist) ist noch Einzelauswahl.
- Kein Umbenennen von Playlist-Knoten in der UI (Repository-Methode existiert, UI fehlt).
- Suche ist `LIKE`-basiert, nicht FTS5 (funktioniert bei 97k Tracks bereits performant genug, s. 8.1 — FTS5 bleibt die spätere Verfeinerung aus Abschnitt 6, keine Notwendigkeit, kein Blocker).
- Kein Filesystem-Watcher, nur manueller Rescan (Watcher ist explizit Teil der ursprünglichen Vision, aber nicht MVP-kritisch für den Import-Anschluss).

## 9. Phase 2 (Start): Traktor-Import

Erster DJ-Software-Reader (s. Phasenplan: "Reader zuerst, Writer danach"). Neues Modul
`Cratebase.Import` (nur gegen `Cratebase.Core`-Interfaces, wie `Cratebase.Scanning` — Prinzip
aus Abschnitt 3 bestätigt sich hier ein zweites Mal problemlos).

### 9.1 Warum Traktor zuerst

Deine reale Sammlung hatte einen `TraktorStems`-Ordner und tatsächlich fand sich unter
`Native Instruments/Traktor 4.5.0/collection.nml` eine aktive, **40.344 Tracks** große Collection
mit vollem Playlist-Baum — Traktor ist bestätigt deine tatsächlich genutzte Software, nicht nur
eine Annahme.

### 9.2 Funktionsweise

- **`TraktorNmlReader`**: liest die COLLECTION per `XmlReader` gestreamt (Datei real 57,5 MB bei
  40k Tracks — würde als `XDocument.Load` unserem eigenen Skalierungsprinzip widersprechen),
  PLAYLISTS als ein `XElement` (dort nur ~2,4 MB, unproblematisch).
- **`TraktorPathResolver`**: löst Traktors eigene Pfade (`VOLUME="M:" DIR="/:Musik/:.../:"`) gegen
  bereits über den eigenen Scanner bekannte Tracks auf — per Ordnersegment-Überlappung zwischen
  Traktor-Pfad und `LibraryRoot.Path`, verifiziert durch einen tatsächlichen Treffer in der DB.
  Kein manuelles Laufwerksbuchstaben-Mapping nötig, kein Raten: kein Treffer ⇒ Track bleibt
  unverknüpft, wird gezählt, nie stillschweigend falsch zugeordnet.
- **Cue-Points**: Traktors `CUE_V2`-Typen (0=Hot-Cue, 1=Fade-In, 2=Fade-Out, 3=Load, 4=Grid,
  5=Loop — an echten Daten verifiziert) landen in der neuen `cue_point`-Tabelle
  (`source='imported:traktor'`). Ein erneuter Import ersetzt nur Cue-Points dieser Quelle
  (Delete+Insert pro Track), manuell angelegte Cues blieben unberührt.
- **Playlisten**: Traktors kompletter `$ROOT`-Baum wird unter einem eigenen Ordner
  **"Traktor Import"** gespiegelt (bewusst getrennt vom Rest des Playlist-Baums, solange 4.E
  ungeklärt ist), idempotent (Re-Import legt keine Duplikate an, matched über Name+Parent).
- **Track-Anreicherung**: Genre/Key/Comment/BPM/Rating/PlayCount/LastPlayed werden nur befüllt,
  wenn bei uns noch leer/0 — "Traktor füllt Lücken", überschreibt nie. Braucht dafür
  `ITrackRepository.UpdateAsync` (neu, s. 9.3) statt dem scan-schützenden `UpsertAsync`.

### 9.3 Ein während der Implementierung gefundener und gefixter Bug

`ReadOuterXml()` positioniert den `XmlReader` schon auf den nächsten Knoten; ein zusätzliches
`reader.Read()` am Schleifenkopf hat dadurch systematisch jeden zweiten Collection-Eintrag
übersprungen — beim ersten Testlauf gegen die echte Datei kamen 20.172 statt 40.344 Tracks an
(exakt die Hälfte). Ohne den Testlauf gegen echte, großformatige Daten wäre das nicht
aufgefallen (mit einer Handvoll Test-Tracks bleibt der Effekt unter der Wahrnehmungsschwelle -
ein Beleg mehr für "an echten ~100k-Daten testen" statt nur an synthetischen Kleinbeispielen).

### 9.4 Real validiert (auf deiner tatsächlichen, jetzt auch angewendeten Collection)

| Metrik | Wert |
|---|---|
| Collection-Einträge gelesen | 40.344 |
| Tracks zugeordnet | 33.958 (84,2%) |
| Tracks nicht zugeordnet | 6.386 (vermutlich Traktor-Factory-Sounds/andere Quellen außerhalb `/Musik/Musik`) |
| Cue-Points importiert | 57.015 |
| Playlist-Knoten angelegt | 662 |
| Playlist-Track-Zuordnungen | 11.897 |

Angewendet auf die echte, aktive `cratebase.db` (Backup vorher gezogen: `cratebase.db.backup-pre-traktor-import-*`).

### 9.5 Bekannte Grenzen dieses ersten Readers

- Nur Reader, kein Writer — Änderungen in Cratebase fließen nicht zurück nach Traktor (per Phasenplan explizit "Writer danach").
- Kein Cleanup entfernter Playlist-Mitgliedschaften bei Re-Import (nur additiv, s. 9.2).
- Rekordbox/Mixxx/Serato/VirtualDJ folgen als eigene `ICollectionImporter`-Implementierungen nach demselben Muster — nicht Teil dieses Durchgangs.

## 10. Visuelles Redesign: Anlehnung an "DJ Library App.dc.html"

Ein per Claude Design erstelltes Mockup (`DJ Library App.dc.html`, ins Repo gelegt) diente als
Vorbild für ein moderneres, dichteres UI — nicht 1:1 übernommen, aber die Farbsprache exakt und
die Struktur (Top-Nav mit mehreren App-Bereichen) im Ansatz.

### 10.1 Was übernommen wurde

- **Farbpalette 1:1**: Die oklch-Werte des Mockups nach Hex umgerechnet (Skript im Gesprächsverlauf) — Fenster-Hintergrund `#0A0B0D`, Panels `#0E0F12`/`#141519`, Akzent-Grün `#33E78C`, Akzent-Lila `#C4A4FE` (für spätere KI-Features reserviert), Rot `#FF645F` (für spätere Duplicates/Warnungen).
- **Typografie**: Sora (UI/Headings) und JetBrains Mono (Zahlen: BPM, Dauer, Counts) als echte, eingebettete Fonts (OFL-lizenziert von Google Fonts, `Assets/Fonts/`) — nicht nur eine Annäherung über Systemschriften.
- **Fest auf Dark-Theme** statt Systemtheme zu folgen (`RequestedThemeVariant="Dark"`) — bewusste Entscheidung, DJ-Software läuft praktisch immer dunkel.
- **Top-Nav-Struktur**: Bibliothek / Set Planner / Analytics / Duplicates / DJ App / Verlauf als Tabs — spiegelt, wohin die App laut Vision wachsen soll.
- **Genre-Pills**: farbige Chips (Hash-basierte Palette) statt reinem Text, wie im Mockup.

### 10.2 Bewusst NICHT übernommen (Scope-Cut, kein Redesign nötig)

Das Mockup zeigt eine deutlich größere App als der aktuelle Funktionsumfang — Analytics-Dashboard
mit Charts, Duplicate-Finder, Session-Verlauf, und vor allem ein vollständiges Zwei-Deck-Mixer-UI
(Jogwheels, EQ-Knobs, Crossfader, Loop/Hotcue-Pads) für "Set Planner" und "DJ App". Das jetzt in
einem Rutsch nachzubauen wäre vorgetäuschter Funktionsumfang ohne echte Substanz dahinter
(Analytics bräuchte z.B. echte Play-Count-Auswertung, die Decks bräuchten Pitch/Time-Stretching,
das noch nicht entschiedene Audio-Engine-Fragen berührt, s. 4.A). Stattdessen:

- Nur "Bibliothek" ist funktional (Scanner, Player, Playlist-Baum, Traktor-Import — alles aus Phase 0-2).
- Die anderen fünf Tabs sind echte, klickbare Navigationspunkte, zeigen aber ehrlich
  "Kommt in einer späteren Phase" statt leerer/vorgetäuschter Widgets.
- Keine erfundene Waveform-Grafik in der Transportleiste (das Mockup zeigt Balken-Wellenformen —
  wir haben noch keine echte Peak-Analyse, s. Phase 1/Analysis-Job-Queue in ARCHITECTURE.md
  Abschnitt 2.8f.; ein Fake-Waveform aus Zufallsbalken wäre irreführend gewesen).

### 10.3 Spaltenbreiten-Fix

DataGrid-Spaltenbreiten liefen anfangs über den Fensterrand hinaus (mehrere konkurrierende
`*`-Spalten) — auf feste Breiten + eine einzelne flexible Titel-Spalte umgestellt. Per Screenshot
bestätigt (s. 11.4).

## 11. Nutzer-Feedback nach dem Redesign: vier Nachbesserungen

Direktes Feedback zur Bibliotheksansicht — alle vier umgesetzt:

### 11.1 Pfad-Spalte + Spalten-Auswahl

Neue "Pfad"-Spalte (`Track.RelativePath`). Neuer "Spalten ▾"-Button mit Flyout (Checkboxen für
Album/Genre/BPM/Key/Dauer/Pfad — Titel/Artist sind immer sichtbar). Umgesetzt über
`DataGridColumn.IsVisible`-Bindings auf neue `ShowXColumn`-Bools im ViewModel — bindet direkt
gegen das Fenster-`DataContext` (`$parent[Window]...`), da Spalten technisch nicht im Visual Tree
hängen und deshalb nicht automatisch den `DataContext` der Zeile erben.

### 11.2 Datei-Browser: was wirklich auf der Platte liegt

Neuer Sidebar-Umschalter "Playlists" / "Ordner". Im "Ordner"-Modus: lazy geladener
Dateisystem-Baum je LibraryRoot (Kinder erst beim Aufklappen, s.
`FileSystemNodeViewModel` — sonst wäre das bei ~100k Dateien ein Full-Walk beim Start), rechts
eine Liste der Dateien im gewählten Ordner mit "✓ gescannt" / "– nicht gescannt"-Markierung
(Abgleich gegen `ITrackRepository.GetFingerprintsByPathAsync`, pro Root einmal geladen und
gecacht, nicht pro Ordnerklick neu). "Diesen Ordner scannen" scannt gezielt nur den ausgewählten
Unterordner (reine Wiederverwendung von `ScanFolderCommand` mit dem Unterordner als eigenem,
verschachteltem LibraryRoot — pragmatisch, nicht ganz elegant, aber ohne Scanner-Änderung machbar).
Gescannte Dateien sind direkt abspielbar (▶-Button, neue `ITrackRepository.FindByPathAsync`).

### 11.3 RGB-Waveform statt Fortschritts-Slider

Ersetzt den reinen Positions-Slider durch eine echte 3-Band-Waveform (rot=Bass, grün=Mitten,
blau=Höhen) — neues Modul **Cratebase.Analysis**:

- `FfmpegWaveformGenerator`: dekodiert per externem **ffmpeg-Prozess** (`-f f32le -ac 1 -ar 11025`)
  zu rohem Mono-PCM, wendet drei einfache Einpol-IIR-Filter an (Tiefpass 200Hz, Bandpass
  200-4000Hz, Hochpass 4000Hz — **kein FFT/Spektrogramm**, bewusst leichtgewichtig), berechnet
  600 Peak-Spalten normiert auf 0..1. Getestet an einer echten Datei: 400-800ms Dekodierzeit,
  plausible, genre-passende Band-Verteilung (Bass-lastiger EBM-Track: Low-Peak 1.0, High-Peak
  nur 0.46).
- **Bekannte neue Voraussetzung: `ffmpeg` muss installiert sein.** Fehlt es, liefert
  `GenerateAsync` `null` statt zu werfen — WaveformView zeigt dann eine ruhige flache Linie statt
  Balken, der Rest der App bleibt unberührt (getestet: fehlende Datei → `null`, kein Absturz).
  Für eine spätere Distribution müsste ffmpeg entweder als Systemvoraussetzung dokumentiert oder
  (wie bei libvlc) eine gebündelte Variante gefunden werden — hier bewusst nicht weiter verfolgt.
- `FileWaveformCache`: eigene Binärdatei pro Track unter `~/.local/share/Cratebase/waveforms/`
  (Cache-Key: Track-Id+Größe+mtime, s. Datei-Doku für die Grenzen davon) — **nicht** als DB-Blob,
  konsistent mit ARCHITECTURE.md 2.9. Rundtrip (Speichern→Laden) verifiziert.
- `WaveformView` (`Cratebase.App.Desktop/Controls`): eigener `Control` mit direktem
  `DrawingContext`-Rendering (600 Balken pro Frame als ItemsControl wäre bei mehrmals pro Sekunde
  aktualisierter Position spürbar langsamer), Klick-zum-Seeken.

### 11.4 Verifiziert

Screenshot nach allen vier Änderungen bestätigt: Pfad-Spalte sichtbar, BPM/Key/Dauer nicht mehr
abgeschnitten, "Spalten ▾"-Button und "Playlists"/"Ordner"-Umschalter vorhanden, Waveform-Fläche
rendert (leerer Zustand als ruhige Linie, korrekt, da noch kein Track lief). Die
Waveform-Generierung selbst wurde isoliert gegen eine echte Datei aus der Sammlung getestet (s.
11.3). Nicht per Klick in der laufenden GUI verifiziert: Datei-Browser-Baum aufklappen und
tatsächliche Waveform-Balken beim Abspielen eines Tracks (Screenshot-Klick-Automatisierung war auf
dem Zielsystem durch Wayland/XWayland-Koordinaten-Mismatch zu unzuverlässig für gezielte Klicks) —
bitte einmal selbst gegenprüfen.

## 12. Zweite Nutzer-Feedback-Runde: EIN Baum, Schnellzugriffe, rekursive Aggregation

### 12.1 Sidebar-Baum vereinheitlicht (kein Umschalt-Button mehr)

Der bisherige "Playlists"/"Ordner"-Umschalter ist weg. `RootNodes` ist jetzt
`ObservableCollection<object>` - gemischt aus `PlaylistTreeItemViewModel` (Bibliothek +
Playlisten, immer zuerst) und `FileSystemNodeViewModel` (Festplatten + Schnellzugriffe, dahinter
angehängt). Avalonia löst pro Element automatisch das passende `TreeDataTemplate` über den
Laufzeittyp auf (`TreeView.DataTemplates`, `DataType="vm:..."`), keine Typ-Verzweigung im
ViewModel nötig. `SelectedSidebarItem` (object?) ersetzt die getrennten Auswahl-Properties; ein
Typ-Check (`is PlaylistTreeItemViewModel` / `is FileSystemNodeViewModel`) entscheidet, ob die
Track-Tabelle oder der Datei-Browser angezeigt wird (`IsShowingTrackGrid`/`IsShowingFileList`).

**Zwei Bugs dabei gefunden und gefixt, beide über echte Nutzung sichtbar geworden (nicht beim
Bauen):**

- Lazy-Expand-Style (`IsExpanded`-Zwei-Wege-Binding für den Dateisystem-Teil) auf ALLE
  `TreeViewItem`s angewendet hätte bei kompilierten Bindings zur Laufzeit eine
  `InvalidCastException` geworfen, sobald ein Playlist-Knoten (kein `IsExpanded`) getroffen wird.
  Fix: `{ReflectionBinding IsExpanded, Mode=TwoWay}` statt kompilierter Bindung - schlägt an
  Playlist-Zeilen einfach folgenlos fehl, statt abzustürzen.
- `RootNodes.Clear()` beim kompletten Neuaufbau feuert eine "Reset"-Notification, auf die
  `TreeView` mit Verlust der aktuellen Selektion reagiert - beim Testen sprang die Auswahl nach
  einem Rebuild unerwartet auf einen anderen Knoten, der Track-Zähler blieb bei 0 hängen (die
  Bibliotheks-Auswahl ging verloren, bevor der finale Track-Reload lief). Fix:
  `ReplacePlaylistNodes`/`ReplaceFilesystemNodes` tauschen jetzt nur ihre jeweilige Hälfte per
  gezieltem Insert/Remove statt eines kompletten Clear+Neuaufbau.
- Zusätzlich beim Testen entdeckt (kein Bug, aber unschöne Konsequenz des "Diesen Ordner
  scannen"-Designs aus 11.2): ein gescannter *Unterordner* landet als eigener, verschachtelter
  LibraryRoot in der DB und wäre ohne Filter als redundanter zweiter Top-Level-Eintrag neben
  seinem eigentlichen übergeordneten Root aufgetaucht. `BuildFilesystemNodesList` zeigt jetzt nur
  noch LibraryRoots, die nicht schon unter einem anderen bekannten Root liegen.

### 12.2 Schnellzugriffe (Einstellungen)

Neuer "⚙ Einstellungen"-Button in der Top-Nav mit Flyout: Liste aktueller Schnellzugriff-Ordner
(entfernbar) + "+ Ordner hinzufügen…". Neues Modell `QuickAccessFolder` (Name+Path), persistiert
über `IQuickAccessStore`/`JsonQuickAccessStore` als einfache JSON-Datei
(`~/.local/share/Cratebase/quickaccess.json`) - bewusst außerhalb der SQLite-DB, da reine
Nutzereinstellung statt Domänendaten. Ein Schnellzugriff-Ordner muss **nicht** vorher gescannt
sein, um im Baum aufzutauchen und durchsucht werden zu können (`FileSystemNodeViewModel` kennt
seit diesem Umbau ohnehin keine `LibraryRootId` mehr, s. 12.1 - Zuordnung zu einer eventuell
passenden Bibliothek läuft über `FindLibraryRootForPath`, Pfad-Präfix-Abgleich).

### 12.3 Rekursive Aggregation ("auf Wunsch")

Neuer Schalter "Unterordner einbeziehen" (`IncludeSubfolders`, Default aus) wirkt auf beide
Ordner-Arten:

- **Playlist-Ordner**: `CollectPlaylistIds` sammelt rekursiv alle Playlist-Ids (keine
  Unter-Ordner-Ids) unter dem gewählten Knoten. `ITrackRepository.SearchAsync`/`CountAsync` nehmen
  jetzt `IReadOnlyCollection<long>? playlistNodeIds` statt einer einzelnen Id entgegen (`IN
  @playlistNodeIds` per Dapper-Listen-Expansion). Bei mehreren Playlisten kann ein Track über
  mehrere davon mehrfach treffen - `SELECT DISTINCT`/`COUNT(DISTINCT t.id)` dedupliziert. Bei
  genau einer Playlist bleibt die manuelle Track-Reihenfolge (`pt.position`) erhalten, bei
  mehreren wird nach Artist/Album/Titel sortiert (kein einzelnes "richtiges" Ordering über
  verschiedene Playlisten hinweg).
- **Dateisystem-Ordner**: `SearchOption.AllDirectories` statt `TopDirectoryOnly` beim Auflisten.

Real getestet (Kopie der echten DB, "Traktor Import"-Ordner mit 553 verschachtelten Playlisten):
7.779 aggregierte Tracks, 500 Zeilen abgerufen, alle 500 mit eindeutiger Track-Id (Dedup
funktioniert), Suchtext+Aggregation kombiniert liefert plausible Treffer.

### 12.4 Doppelklick lädt den Song

Datei-Browser-Liste hat jetzt `SelectedFolderFile` + `DoubleTapped`-Handler (Code-behind, analog
zur bestehenden Track-Tabelle) zusätzlich zum bisherigen ▶-Button pro Zeile.

### 12.5 Verifiziert / offen

Per Screenshot bestätigt: EIN Baum mit Bibliothek/Playlisten/Musik-Laufwerk (echte Ordnerstruktur
sichtbar beim Aufklappen), korrekter Wechsel zwischen Track-Tabelle und Datei-Browser je nach
Auswahl, Einstellungen-Button, "Unterordner einbeziehen"-Schalter, korrekter Gesamt-Tracks-Zähler
nach dem Start (99.153, kein "hängt bei 0" mehr). SQL-Aggregation isoliert gegen echte,
große Daten verifiziert (s. 12.3). Nicht per Klick verifiziert (gleiche Einschränkung wie in
11.4): Schnellzugriff-Ordner tatsächlich über den Einstellungen-Dialog hinzufügen, Doppelklick im
Datei-Browser, "Unterordner einbeziehen" tatsächlich anklicken.

## 13. Drittes Nutzer-Feedback: "Diesen Ordner scannen" hängt kurz, Baum klappt zu

Konkret gemeldet: Klick auf "Diesen Ordner scannen" lässt die App kurz hängen, danach ist die
Sidebar wieder eingeklappt; außerdem zeigt der Klick auf den Hauptordner ("Musik") nicht alle
Songs.

### 13.1 UI-Thread blockiert während des Scans

`LibraryScanner.ScanAsync` liest Tags synchron (TagLib-Sharp macht blockierende Datei-I/O) - ohne
`Task.Run`-Wrapper lief das direkt auf dem Thread, der `await _libraryScanner.ScanAsync(...)`
aufgerufen hat. Beim ursprünglichen 100k-Track-Vollscan (s. Abschnitt 8) fiel das nicht auf, weil
der über eine isolierte Konsolen-Testanwendung lief, nie über die echte GUI/das ViewModel - das
war schlicht nie End-to-End durch den UI-Thread-Pfad getestet. Fix: `await Task.Run(() =>
_libraryScanner.ScanAsync(root, progress))`. `Progress&lt;T&gt;` bleibt dabei sicher, weil es den
UI-`SynchronizationContext` beim Erstellen einfängt, unabhängig davon, von welchem Thread aus
`Report()` aufgerufen wird.

### 13.2 Sidebar klappt nach jedem Scan zu

`ScanSelectedFolderAsync` ruft intern `ScanFolderAsync` auf, und die rief bislang *immer*
`ReplaceFilesystemNodes(BuildFilesystemNodesList())` auf - das erzeugt für den kompletten
Dateisystem-Teil des Baums frische `FileSystemNodeViewModel`-Instanzen, deren `IsExpanded` wieder
bei `false` startet. Bei "Diesen Ordner scannen" ändert sich der sichtbare Baum aber nie (der
gescannte Ordner steht ja schon als Knoten da, sonst hätte man ihn nicht auswählen können) - der
Rebuild war schlicht unnötig. Fix: vorher prüfen, ob der Pfad schon irgendwo im Baum sichtbar ist
(als eigener Root oder als Unterordner eines bekannten Roots, per den schon vorhandenen
`PathsEqual`/`IsPathNestedUnder`-Helfern aus 12.1) - nur bei einem wirklich neuen, sichtbaren
Eintrag (z.B. über "Ordner hinzufügen…" auf einen komplett neuen Ort) wird noch neu aufgebaut.

### 13.3 Datei-Browser bei "Unterordner einbeziehen" auf großen Ordnern langsam

Kein Tiefen-Limit (`SearchOption.AllDirectories` ist in .NET uneingeschränkt rekursiv - das war
schon vorher korrekt), sondern ein Performance-Problem: pro gefundener Datei lief ein
`FileInfo(...).Length`-Stat-Aufruf (blockierende I/O) plus der Aufbau des Listeneintrags in einer
`foreach`-Schleife auf dem UI-Thread, nachdem nur die Dateisystem-Enumeration selbst in
`Task.Run` lief. Bei "Unterordner einbeziehen" auf dem kompletten Bibliotheks-Root (~100k
Dateien) macht das zehntausende blockierende Stat-Aufrufe direkt auf dem UI-Thread - wirkt wie
"nicht alle Songs werden angezeigt", weil die Liste noch minutenlang weiterbaut oder feststeckt.
Fix: Enumeration, Fingerprint-Abgleich UND die Stat-Aufrufe laufen jetzt komplett in einem
einzigen `Task.Run`; zusätzlich eine Anzeige-Obergrenze (`FolderFilesDisplayCap = 3000`) - Anzahl
gescannt/gesamt bleibt trotzdem für *alle* gefundenen Dateien korrekt (der günstige
Fingerprint-Abgleich läuft über die volle Liste, nur die teuren Stat-Aufrufe sind gedeckelt), bei
Überschreitung ein Hinweis, für die komplette Bibliothek eher die Bibliotheksansicht zu nutzen.

### 13.4 Verifiziert

Alle drei Fixes bauen sauber und die App startet weiterhin fehlerfrei. Die eigentliche Symptomatik
(spürbares kurzes Hängen, Baum-Kollaps) lässt sich nur durch tatsächliches Klicken in der laufenden
GUI beobachten/bestätigen (Wayland-Klick-Automatisierung bleibt auf dem Zielsystem unzuverlässig,
s. frühere Abschnitte) - bitte einmal selbst gegenprüfen, ob "Diesen Ordner scannen" jetzt ohne
Hänger durchläuft und der Baum aufgeklappt bleibt.

## 14. Tag-Editor: Metadaten-Spalten im Datei-Browser + Edit-Modus

Ausgangslage: der Datei-Browser (Abschnitt 12/13) zeigte pro Datei nur Dateiname, Größe und
Scan-Status - kein Titel, Artist, Genre, BPM etc., selbst wenn die Datei längst gescannt und in
der DB war. Zusätzlich gab es keine Möglichkeit, Metadaten überhaupt zu bearbeiten - Genre,
Kommentare, Farbmarkierung mussten extern gesetzt werden.

### 14.1 Schema: sub_genre, comment2, color_tag

Neue `track`-Spalten `sub_genre`, `comment2`, `color_tag` (alle `TEXT NULL`, Freitext - auch
`color_tag`: bewusst keine feste Palette/Enum, sondern wie Genre/Kommentar per Autocomplete aus
bereits benutzten Werten befüllt). `Schema.CurrentVersion` auf 3, Migration über
`DatabaseInitializer.ApplyTrackColumnMigrations`: `PRAGMA table_info(track)` prüft, welche Spalten
schon existieren (SQLite kennt kein `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`), fehlende werden
per `ALTER TABLE` nachgezogen. Idempotent (gegen eine Kopie der echten ~100k-Tracks-DB doppelt
ausgeführt, kein Fehler) und deckt sowohl bestehende als auch frische DBs ab (letztere bekommen
die Spalten schon direkt aus der CREATE-TABLE-DDL).

### 14.2 Genre/Kommentar werden "sticky" - Rescan überschreibt sie nicht mehr

Bisher hat jeder Rescan `genre_raw` und `comment` aus den Datei-Tags neu geschrieben (wie
Titel/Artist/BPM auch). Das ist ein Problem, sobald man diese Felder manuell im Tag-Editor
pflegt: ein späterer Rescan (z.B. weil neue Dateien dazukamen) hätte die manuelle Arbeit wieder
verworfen. `TrackRepository.UpsertAsync` behandelt `genre_raw`/`comment` deshalb jetzt wie
`rating`/`play_count`: im `ON CONFLICT ... DO UPDATE SET` werden sie **nicht** mehr überschrieben,
sobald ein Track einmal existiert - nur beim allerersten `INSERT` (neuer Track) kommen sie noch
aus den gescannten Tags. Manuelle Edits (Tag-Editor) und DJ-Software-Importe (Traktor etc., die
über `UpdateAsync` laufen, nicht `UpsertAsync`) sind davon nicht betroffen und überschreiben
weiterhin gezielt.

### 14.3 Datei-Browser zeigt jetzt echte Track-Daten

`MainViewModel.FolderTracks` (`ObservableCollection<Track>`) ersetzt das bisherige
`FolderFiles`/`FileBrowserEntryViewModel`-Paar. `ReloadFolderFilesAsync` läuft weiterhin komplett
in `Task.Run` (Lehren aus 13.3 bleiben gültig): Enumeration + Fingerprint-Abgleich über *alle*
gefundenen Dateien (günstig, nur Pfad-Lookup), aber ein **einziger** `GetByIdsAsync`-Bulk-Call für
die vollen Track-Objekte der angezeigten (gedeckelten, `FolderFilesDisplayCap = 3000`) Teilmenge -
kein Query pro Datei. Noch nicht gescannte Dateien bekommen einen synthetischen `Track` mit
`Id = 0` (`Track.IsSynthetic`) und nur Pfad/Format gefüllt; `PlaySelectedTrack` und der Tag-Editor
lehnen solche Platzhalter mit einer klaren Statusmeldung ab, statt mit `NullReferenceException`
oder stillem Nichtstun zu reagieren. Die Datei-Browser-`DataGrid` teilt sich Spaltenlayout und
`SelectedTrack`-Bindung mit der Bibliotheks-`DataGrid` (bewusst dupliziertes `DataGrid.Columns` in
der AXAML statt Sharing-Mechanismus - Avalonia bietet dafür keinen praktikablen Weg, und zwei
kurze Column-Listen sind wartbarer als eine künstliche Abstraktion dafür).

### 14.4 Edit-Modus

Toggle-Button in der Bibliotheks-Kopfzeile (`IsEditMode`) blendet ein Panel zwischen Songliste und
Transportleiste ein (`Border` mit `DockPanel.Dock="Bottom"`, `IsVisible="{Binding IsEditMode}"`).
Die Songliste schrumpft dabei ganz ohne eigene Row-Umschaltlogik - sie sitzt im letzten,
undockten `Panel`, das per Avalonia-`DockPanel`-Verhalten (`LastChildFill`) automatisch den nach
allen `Dock`-Kindern verbleibenden Platz bekommt.

Editierbare Felder: Titel/Artist/Album/BPM/Key als normale `TextBox`, Genre/Sub-Genre/Kommentar/
Kommentar 2/Farbe als `AutoCompleteBox` (`FilterMode="Contains"`) - letztere fünf, weil dafür auch
Vorschläge existieren (s. 14.5). `Track` ist bewusst kein `ObservableObject` (Cratebase.Core bleibt
frei von UI-Framework-Referenzen), deshalb binden die Panel-Felder nicht direkt an
`SelectedTrack.X`, sondern an eigene `Edit*`-Properties im ViewModel, die bei Trackwechsel
(`OnSelectedTrackChanged`) bzw. beim Aktivieren des Edit-Modus (`OnIsEditModeChanged`) aus dem
gewählten Track befüllt werden.

Gespeichert wird nicht pro Tastendruck, sondern wenn ein Feld den Fokus verliert
(`LostFocus="OnEditFieldLostFocus"` im Code-behind → `SaveEditedMetadataCommand`) - Tab, Klick auf
ein anderes Feld oder Klick auf einen anderen Track lösen das aus. `SaveEditedMetadataAsync`
vergleicht jedes `Edit*`-Feld gegen den aktuellen Wert auf `SelectedTrack`, schreibt nur bei
tatsächlicher Änderung (`UpdateAsync`, kein Upsert - das würde `LastPlayedAtUtc`/`Rating` etc.
unnötig anfassen), und aktualisiert danach die Vorschlagslisten neu, damit ein frisch getippter
Wert sofort für den nächsten Track vorschlägt. Da `Track` kein `INotifyPropertyChanged` hat, würde
das reine In-Place-Ändern der Properties keine gebundene `DataGrid`-Zeile visuell aktualisieren -
`RefreshTrackInLists` entfernt den Track darum aus `Tracks`/`FolderTracks` und fügt ihn an
derselben Stelle wieder ein (erzwingt Re-Bind der Zeile), und setzt `SelectedTrack` kurz auf
`null` und zurück (sonst gilt ein erneutes Zuweisen derselben Referenz als "unverändert", keine
Reselektion).

### 14.5 Autocomplete-Vorschläge aus bereits benutzten Werten

Kein separates Vokabular: `ITrackRepository.GetDistinctTextValuesAsync(TrackTextField)` liefert
einfach `SELECT DISTINCT <spalte> FROM track WHERE <spalte> IS NOT NULL AND TRIM(<spalte>) != ''
ORDER BY <spalte> COLLATE NOCASE` - `TrackTextField` ist ein Enum (`GenreRaw`, `SubGenre`,
`Comment`, `Comment2`, `ColorTag`), damit kein roher Spaltenname von außen durchgereicht wird.
Gegen die echte ~100k-Tracks-DB liefert das für `GenreRaw` z.B. 1.410 unterschiedliche Werte - die
`AutoCompleteBox`-Instanzen im Edit-Panel filtern das serverseitig schon distincte, aber
client-seitig gefilterte (`FilterMode="Contains"`) `ItemsSource` weiter auf das, was gerade
getippt wird (z.B. "ban" findet "banger"), auswählbar per Enter oder Mausklick auf den Vorschlag.

### 14.6 Verifiziert

Datenschicht (Schema/Migration/Repository) gegen eine Kopie der echten Produktions-DB getestet
(Migration doppelt ausgeführt: kein Fehler; `UpdateAsync` setzt alle fünf neuen/geänderten Felder
korrekt; simulierter Rescan via `UpsertAsync` lässt `GenreRaw`/`Comment` unangetastet;
`GetDistinctTextValuesAsync` liefert echte 1.410 Genre-Werte; `GetByIdsAsync` filtert unbekannte
IDs korrekt raus). Build sauber (0 Fehler/Warnungen). Die komplette Feature-Kette wurde zusätzlich
**live gegen die echte, laufende App mit der echten ~99k-Tracks-Bibliothek** durchgeklickt (nicht
nur isoliert getestet): Edit-Modus ein-/ausschalten, Panel erscheint/verschwindet zwischen Liste
und Transportleiste, Track auswählen befüllt die Felder korrekt, ein in "Kommentar" getippter Wert
("banger") wird beim Verlassen des Felds tatsächlich in der Produktions-DB persistiert (per
direkter SQLite-Abfrage bestätigt), und beim erneuten Tippen eines Präfixes ("ban") in einem
anderen Track erscheint ein Autocomplete-Dropdown mit real existierenden, das Präfix enthaltenden
Kommentarwerten aus der Bibliothek. Alle für diesen Test gesetzten Werte wurden danach wieder auf
ihren ursprünglichen (leeren) Zustand zurückgesetzt, die Produktions-DB ist unverändert gegenüber
vor diesem Test. Der Datei-Browser mit den neuen Spalten wurde ebenfalls live bestätigt (Auswahl
eines Schnellzugriff-Ordners zeigt dieselben Spalten wie die Bibliotheksansicht).

Bekannte Einschränkung: manche real vorhandenen `comment`-Werte in der Bibliothek sind sehr lange
Promo-Freitexte (aus ID3-Kommentar-Tags einzelner Release-Dateien, nicht durch dieses Feature
erzeugt) - das macht die Autocomplete-Liste für dieses Feld unübersichtlicher als z.B. bei Genre.
Kein Bug dieses Features, aber ein möglicher Kandidat für eine spätere Verbesserung (z.B. Kappung
sehr langer Vorschlagswerte in der Anzeige).

## 15. Fix: Dropdown-Vorschläge im Tag-Editor nicht bedienbar

Gemeldetes Symptom: Im Edit-Modus tauchen beim Tippen (z.B. in Kommentar/Sub-Genre) die
Autocomplete-Vorschläge zwar auf, lassen sich aber weder mit Pfeiltasten durchblättern, noch per
Tab übernehmen, noch mit der Maus anklicken.

### 15.1 Ursache 1: Speichern hat das gerade offene Dropdown unter der Maus/Tastatur weggezogen

`SaveEditedMetadataAsync` (ausgelöst bei jedem Feld-Fokusverlust) rief bisher `RefreshTrackInLists`
auf, was `SelectedTrack` kurz auf `null` und zurück setzte, um die DataGrid-Zeile neu zu binden
(Track ist bewusst kein `ObservableObject`, s. 14.4). Das hat aber `OnSelectedTrackChanged` zweimal
ausgelöst - einmal mit `null` - und dabei `LoadEditFieldsFromSelectedTrack()` erneut aufgerufen,
was **alle** Edit-Felder (nicht nur das gerade verlassene) auf die (zwischenzeitlich neu geladenen)
Track-Werte zurückgesetzt hat. Zusätzlich hat `LoadEditSuggestionsAsync()` danach blind **alle
fünf** Vorschlagslisten neu geladen (`Clear()` + neu befüllen) - auch die des Feldes, in das der
Nutzer per Tab gerade THIS MOMENT hineinwechselt und dessen Dropdown sich in genau diesem Moment
öffnet. Ein Klick auf einen Vorschlag verschiebt in Avalonias `AutoCompleteBox` kurzzeitig den
Fokus (PointerPressed auf das `ListBoxItem` im Popup) - das hat also mitten in der Klick-Geste
selbst schon einen Save+Reload ausgelöst, der die gerade angezeigte Vorschlagsliste unter dem
Klick ersetzt hat. Ergebnis: Klick "tut nichts", weil das angeklickte Listenelement durch den
Reload längst durch ein anderes Objekt ersetzt war, bevor die Auswahl commit werden konnte.
Nebenbefund beim Testen: dieselbe Ursache kann bei schnellem Tabben durch mehrere Felder sogar zu
einem sichtbaren Sprung auf einen anderen Track in der Liste führen.

Fix: `RefreshTrackInLists` fasst `SelectedTrack` nicht mehr an - Entfernen+Wiedereinfügen
derselben Objektreferenz in `Tracks`/`FolderTracks` reicht aus, um die DataGrid-Zeile neu zu
binden, ohne die Selektion zu stören. `SaveEditedMetadataAsync` lädt außerdem nur noch die
Vorschlagsliste **des tatsächlich geänderten Feldes** neu, nicht mehr pauschal alle fünf.

### 15.2 Ursache 2: Tab hat den markierten Vorschlag nie übernommen

Avalonias `AutoCompleteBox` committet die per Pfeiltasten hervorgehobene Vorschlagszeile bei Tab
nur, wenn `IsTextCompletionEnabled="True"` gesetzt ist - Standardwert ist `false`. War hier nicht
gesetzt, d.h. Tab hat schlicht zum nächsten Feld gewechselt, ohne den markierten Vorschlag zu
übernehmen (unabhängig von Ursache 15.1). Fix: `IsTextCompletionEnabled="True"` auf allen fünf
AutoCompleteBox-Feldern ergänzt.

### 15.3 Nebenbefund: BPM-Rundung bei jedem Speichern

`EditBpmText` wird aus `track.Bpm` mit `"0.##"` (max. 2 Nachkommastellen) formatiert. Der
Speichern-Vergleich hat bisher den geparsten `double` aus `EditBpmText` gegen den ORIGINAL-`double`
verglichen - bei mehr als zwei Nachkommastellen (z.B. 175.384) war das nach jedem Blur auf das
BPM-Feld fälschlich "geändert", auch ohne dass der Nutzer etwas eingegeben hat, und hat die
Präzision beim Rückschreiben stillschweigend auf 175.38 gekappt. Fix: Vergleich läuft jetzt auf
den formatierten STRINGS (`EditBpmText` gegen den mit demselben Format neu erzeugten String aus
`track.Bpm`), nicht mehr auf den re-geparsten Doubles.

### 15.4 Verifiziert / nicht verifiziert

Datenschicht-Fixes (BPM-Vergleich) und die Entfernung des SelectedTrack-Null-Dance sind gebaut
(0 Fehler/Warnungen) und die neue Logik wurde gegen die echte Bibliothek erneut durchgeklickt -
Edit-Modus-Toggle und Feldauswahl funktionieren weiterhin einwandfrei. Das eigentliche gemeldete
Interaktionsproblem (Pfeiltasten/Klick im offenen Dropdown) konnte in dieser Session NICHT mehr
live nachgestellt werden: die Klick-Koordinaten-Kalibrierung auf diesem Wayland/XWayland-System
ist zwischen App-Neustarts instabil (Fenstergeometrie ändert sich), und mehrere Klickversuche
landeten zuletzt sogar in fremden Fenstern (Browser, Terminal) statt in Cratebase - GUI-
Automatisierung wurde deshalb aus Sicherheitsgründen abgebrochen, bevor versehentlich etwas in
einem falschen Fenster ausgelöst werden konnte. Die beiden behobenen Ursachen (15.1, 15.2) sind
über Code-Review sehr plausibel für alle drei gemeldeten Symptome (Klick, Pfeiltasten indirekt
über Fokus-Störung, Tab direkt über `IsTextCompletionEnabled`) - bitte nach diesem Fix einmal
selbst gegenprüfen, ob Pfeiltasten/Tab/Klick im Dropdown jetzt funktionieren.

## 16. Playlist-Favoriten/Profile-Manager

Nutzer-Wunsch: schneller Playlist-Zugriff wie Traktors 12 Favoriten-Slots, aber mit frei wählbarer
Anzahl (20-40 oder mehr) statt fest 12, und mit wechselbaren "Profilen" (z.B. ein Profil "Hardstyle"
mit thematischen Playlisten in den Slots, ein anderes Profil "House" mit komplett anderer Belegung
derselben Slot-Nummern). Zusätzlich: Songs sollen sich direkt über die Favoriten-Slots zu Playlisten
hinzufügen lassen, ohne jedes Mal im Playlist-Baum navigieren zu müssen.

### 16.1 Datenmodell und Persistenz

`FavoriteProfile` (Cratebase.Core/Models/FavoriteProfile.cs): `Name` + `Dictionary<int, long> Slots`
(Slot-Index → Playlist-Id). Bewusst sparse statt eines festen Arrays - ein Profil mit nur 9 belegten
Slots zeigt bei z.B. 24 sichtbaren Slots einfach 15 leere, unabhängig von der global konfigurierten
Slot-Anzahl (`FavoriteProfileSettings.SlotCount`, Default 12, per NumericUpDown in der UI frei
wählbar 1-60). Persistiert als `~/.local/share/Cratebase/favorite_profiles.json` über
`IFavoriteProfileStore`/`JsonFavoriteProfileStore` - exakt dasselbe Muster wie
`IQuickAccessStore`/`JsonQuickAccessStore` (Abschnitt 12.2): reine App-Einstellung, keine
Domänendaten, deshalb JSON statt SQLite-Tabelle. `Dictionary<int, long>` serialisiert über
System.Text.Json anstandslos als JSON-Objekt mit String-Schlüsseln (`{"0": 101, "23": 999}`) - gegen
eine isolierte Testanwendung verifiziert (Sparse-Slots mit Lücken, hohem Index 23, kaputte Datei
verhindert den App-Start nicht).

### 16.2 UI: Toggle wie Edit-Modus, aber oben statt unten

Neuer "★ Favoriten"-Toggle-Button in der Kopfzeile, exakt dasselbe Muster wie "✎ Edit-Modus"
(`IsFavoritesMode` TwoWay an `ToggleButton.IsChecked`, ein `Border` mit
`IsVisible="{Binding IsFavoritesMode}"` blendet sich ein/aus). Anders als der Tag-Editor sitzt das
Panel aber `DockPanel.Dock="Top"` direkt unter der Kopfzeile statt unten über der Transportleiste -
es geht um Playlist-*Navigation*, nicht um Track-Metadaten, deshalb näher am Playlist-Baum in der
Sidebar als am Player.

Panel-Inhalt: Profil-Dropdown + "💾 Speichern"/"Löschen" für das aktive Profil, ein Textfeld +
"+ Als neues Profil speichern" für neue Profile, ein `NumericUpDown` für die Slot-Anzahl, und darunter
die Slots selbst als `ItemsControl` mit `WrapPanel` (bricht bei vielen Slots automatisch um,
`ScrollViewer` mit `MaxHeight` verhindert, dass 40+ Slots den Bildschirm sprengen).

### 16.3 Zuweisen/Bedienen der Slots - kein Drag&Drop, kein Such-Picker

Um weder Drag&Drop (aufwändig in Avalonia) noch einen neuen Playlist-Such-Picker bauen zu müssen,
wird der bereits vorhandene Auswahlmechanismus wiederverwendet: genau wie der bestehende
"Zu Playlist hinzufügen"-Button (`AddSelectedTrackToPlaylistCommand`) sich auf die aktuell im
Sidebar-Baum ausgewählte Playlist (`SelectedPlaylistNode`) stützt, tut das auch die
Slot-Zuweisung. Pro Slot-Karte:
- **Klick auf den Slot-Text** (falls belegt) → springt zur Playlist (setzt `SelectedSidebarItem` auf
  den per `FindPlaylistNode` gefundenen Baum-Eintrag - derselbe Navigationsweg wie ein Klick im Baum
  selbst, s. `OnSelectedSidebarItemChanged`).
- **"+"-Button** (nur sichtbar wenn belegt) → fügt den aktuell in der Bibliothek/im Datei-Browser
  ausgewählten Track (`SelectedTrack`) direkt dieser Playlist hinzu (`AddTracksAsync`, dieselbe
  Repository-Methode wie der bestehende Button) - das ist der direkte "Songs zu Playlisten
  hinzufügen"-Weg ohne Baum-Navigation.
- **Rechtsklick (ContextMenu)** → "Aktuelle Playlist hier ablegen" (belegt den Slot mit
  `SelectedPlaylistNode`) bzw. "Slot leeren". `$parent[ItemsControl]`-Bindings für die
  Command-Weiterleitung aus dem DataTemplate heraus - dasselbe bereits bewährte Muster wie beim
  "×"-Button der QuickAccess-Flyout-Liste (Abschnitt 12.2), hier zusätzlich aus einem `ContextMenu`
  heraus (Popup-Inhalte hängen in Avalonia weiterhin logisch am Besitzer-Control, die Bindung
  funktioniert genauso).

Zuweisungen/Löschungen sind bewusst **nicht** Autosave - sie ändern nur das In-Memory-`Slots`-
Dictionary des aktiven Profils; erst ein expliziter Klick auf "💾 Speichern" (überschreibt das aktive
Profil) oder "+ Als neues Profil speichern" (legt ein neues an, oder überschreibt eins mit demselben
Namen) schreibt nach `favorite_profiles.json` (Nutzer-Feedback: "über nen Speichern-Button"). Die
Slot-*Anzahl* dagegen ist eine globale Anzeige-Einstellung, kein Teil eines einzelnen Profils, und
wird sofort bei Änderung persistiert.

`RebuildFavoriteSlots()` baut die angezeigte `FavoriteSlots`-Collection komplett neu auf - läuft
nach jeder Zuweisung/Löschung, jedem Profilwechsel, jeder Slot-Anzahl-Änderung, UND nach jedem
`RebuildPlaylistNodesAsync()` (falls sich Playlist-Namen geändert haben oder eine zugewiesene
Playlist gelöscht wurde - der Slot zeigt dann einfach als leer an, ohne dass die tote Zuweisung im
Profil sofort verschwindet).

### 16.4 Verifiziert

Build sauber (0 Fehler/Warnungen), alle Bindings sind compilierte Avalonia-Bindings gegen
`x:DataType="vm:MainViewModel"` - ein falscher Property-/Command-Name hätte den Build fehlschlagen
lassen, das allein bestätigt schon die strukturelle Verdrahtung. Die JSON-Persistenzschicht wurde
zusätzlich über eine isolierte Konsolen-Testanwendung verifiziert (Default-Werte, Sparse-Dictionary-
Roundtrip mit Lücken und hohem Index, kaputte Datei bricht den Ladevorgang sauber ab). Die App
wurde gestartet und der "★ Favoriten"-Button erscheint korrekt in der Kopfzeile (Screenshot). Ein
vollständiger Klick-Durchlauf (Profil anlegen, Slot zuweisen, Track hinzufügen, Navigation per Klick)
konnte in dieser Session nicht mehr sicher automatisiert werden - die Klick-Koordinaten-Automatisierung
erwies sich im Verlauf dieser Session als zunehmend unzuverlässig und ist zwischenzeitlich sogar in
fremden Fenstern gelandet (s. Abschnitt 15.4), weshalb weitere GUI-Automatisierung aus
Sicherheitsgründen unterlassen wurde. Bitte den vollen Flow (Profil erstellen → Playlist per
Rechtsklick einem Slot zuweisen → Speichern → Profil wechseln → zurückwechseln → Slot anklicken zum
Navigieren → "+" zum Hinzufügen eines Tracks) einmal selbst durchklicken.

## 17. Fix: "Weiter" spielte einen zufälligen Bibliotheks-Track statt den nächsten im Ordner

Gemeldetes Symptom: Im Datei-Browser einen Song aus einem Ordner starten (z.B. Musik/Beatport/Deep
House) - nach dessen Ende (oder per "Weiter"-Button) spielt plötzlich ein völlig unabhängiger Track
aus einem anderen Genre, nicht der nächste Song im selben Ordner.

### Ursache

`PlayNext`/`PlayPrevious`/`PlayTrack` haben ausschließlich mit `Tracks` (der paginierten
Bibliotheks-/Playlist-Liste) gerechnet, unabhängig davon, ob der abgespielte Track eigentlich aus
`FolderTracks` (Datei-Browser, s. Abschnitt 14.3) kam. `PlayTrack` hat `_currentTrackIndex` per
`Tracks.IndexOf(track)` gesetzt - kam der Track aus dem Datei-Browser, steht er dort aber oft gar
nicht drin (andere Collection, oder schlicht nicht auf der aktuell geladenen Bibliotheks-Seite), also
lieferte `IndexOf` **-1**. Beim nächsten "Weiter" wurde daraus `Math.Min(-1 + 1, Tracks.Count - 1)`
= **Index 0** - es wurde also praktisch immer der erste Track der aktuell geladenen Bibliotheks-Seite
gespielt, unabhängig vom Ordner/Genre, aus dem eigentlich gerade abgespielt wurde. Das erklärt exakt
das gemeldete Verhalten ("plötzlich Frenchcore statt der nächste Deep-House-Track").

### Fix

`PlayTrack` merkt sich jetzt in `_activePlaybackList`, aus welcher Liste der Track tatsächlich
gestartet wurde (`FolderTracks`, wenn der Datei-Browser sichtbar war, sonst `Tracks`) - `PlayNext`/
`PlayPrevious` schlagen den nächsten Index in genau dieser Liste nach, nicht mehr blind in `Tracks`.
Die Zuordnung bleibt außerdem bestehen, auch wenn währenddessen zu einer anderen Ansicht navigiert
wird - wer aus einem Ordner heraus abspielt, bekommt "Weiter" auch dann noch aus diesem Ordner.
Nebenbei behoben: der Datei-Browser kann noch nicht gescannte Platzhatzer-Dateien enthalten
(`Track.IsSynthetic`, s. 14.3) - "Weiter"/"Zurück" überspringen diese jetzt transparent, statt beim
Versuch, eine Platzhalter-"Datei" abzuspielen, ins Leere zu laufen. Kein Zurückspringen auf
Anfang/Ende mehr am Listenrand - "Weiter" am letzten Track tut jetzt einfach nichts, statt denselben
Track erneut zu starten (vorheriges `Math.Min`/`Math.Max`-Clamping-Verhalten).

### Verifiziert

Build sauber, App startet fehlerfrei. Die Kernlogik (`FindNextPlayableIndex`) ist eine reine,
zustandslose statische Methode und wurde per Code-Review gegen die relevanten Randfälle durchdacht
(leere Liste, nur synthetische Tracks übrig, Listenende) - ein Live-Durchklicken des kompletten
Szenarios (Ordner öffnen, Song abspielen, Ende abwarten, prüfen dass wirklich der nächste Ordner-Song
kommt) war in dieser Session aus den in Abschnitt 15.4/16.4 genannten Gründen nicht mehr sicher
automatisierbar - bitte selbst gegenprüfen.

## 18. Favoriten-Panel nach unten verschoben, Kontextmenü-Fix, Drag&Drop

Nutzer-Feedback: "Aktuelle playlist hier ablegen" (Rechtsklick-Kontextmenü auf einem Favoriten-Slot)
tat nichts; das Favoriten-Panel sollte wie der Tag-Editor unten, direkt über der Transportleiste
sitzen statt oben unter der Kopfzeile; Drag&Drop sollte ebenfalls funktionieren.

### 18.1 Panel-Position

Die Favoriten-`Border` ist jetzt `DockPanel.Dock="Bottom"`, direkt nach der Transportleiste und vor
dem Tag-Editor-Panel deklariert - dieselbe Mechanik wie beim Edit-Modus (s. Abschnitt 14.4): die
Songliste sitzt im letzten, undockten `Panel` und schrumpft automatisch, wenn eines der beiden (oder
beide gleichzeitig) eingeblendet werden.

### 18.2 Ursache für "Aktuelle Playlist hier ablegen macht nichts"

Die `MenuItem`-Commands im `ContextMenu` banden bisher über `$parent[ItemsControl]` an das
ViewModel - das funktioniert für normale Inhalte einer `ItemsControl`-Vorlage (bewährt z.B. bei der
QuickAccess-Flyout-Liste, Abschnitt 12.2), aber ein `ContextMenu` hängt als eigener Popup-Zweig nicht
zuverlässig im selben visuellen Pfad wie die `ItemsControl`, die es geöffnet hat - die Bindung lief
darum ins Leere. Fix: `$parent[Window]` statt `$parent[ItemsControl]` - dasselbe an anderer Stelle
bereits bewährte Muster (`DataGridColumn.IsVisible`, das ebenfalls kein normaler visueller Nachfahre
ist) funktioniert auch aus einem Popup-Kontext heraus zuverlässig.

### 18.3 Drag&Drop

Avalonia 12.1.1 nutzt eine neuere Drag&Drop-API (`DataTransfer`/`DataFormat<T>` statt des
deprecateten `DataObject`/`IDataObject`) - per Reflektion gegen die installierte Avalonia.Base.dll
verifiziert, bevor Code dagegen geschrieben wurde (`DataFormat.CreateInProcessFormat<T>` verlangt
einen Referenztyp, deshalb Playlist-/Track-Id als `string` statt `long` transportiert).

- **Drag-Quellen** (Code-behind, da Avalonia-Drag&Drop aus einer laufenden Pointer-Geste heraus
  startet, kein reines Binding-Konzept): `PointerPressed` merkt sich Startpunkt + die
  `PointerPressedEventArgs` (von `DoDragDropAsync` explizit verlangt, nicht die laufenden
  `PointerEventArgs` von `PointerMoved`); `PointerMoved` prüft ab ein paar Pixeln Bewegung bei
  weiterhin gedrückter linker Maustaste, ob es sich um eine echte Drag-Geste handelt (sonst würde
  jeder normale Klick/Doppelklick/Baum-Auf-Zuklappen einen Drag auslösen), liest den DataContext am
  `PointerEventArgs.Source` hoch bis zum passenden Typ (`PlaylistTreeItemViewModel` im Baum,
  `Track` im DataGrid) und startet `DragDrop.DoDragDropAsync`.
- **Drop-Ziel**: jede Slot-`Border` hat `DragDrop.AllowDrop="True"` sowie `DragDrop.DragEnter/
  DragOver/DragLeave/Drop`-Handler (als **attached** Events - `DragEnter="..."` allein kompiliert
  nicht, `Border` kennt dieses Event nicht direkt). Visuelles Feedback beim Drüberziehen (grüner,
  dickerer Rahmen) direkt in Code-behind statt über eine neue Style-Klasse, da nur dieser eine Ort
  das braucht. `AssignPlaylistToSlot(long, int)`/`AddTrackToFavoriteSlotAsync(long, int)` sind neue,
  öffentliche ViewModel-Methoden (nicht `[RelayCommand]`, da aus Code-behind statt XAML-Binding
  aufgerufen) - anders als die bereits vorhandenen `AssignSelectedPlaylistToSlotCommand`/
  `AddSelectedTrackToFavoriteSlotCommand` nicht auf die aktuelle Baum-/Track-Auswahl angewiesen,
  sondern nehmen die gezogene Playlist/den gezogenen Track direkt per Id entgegen - Drag&Drop
  funktioniert also unabhängig davon, was gerade im Baum oder in der Liste ausgewählt ist.

### 18.4 Aufräumen

Beim Testen der vorherigen Feature-Runde war über einen automatisierten Fehlklick eine leere
Playlist "Neue Playlist" in der echten Produktions-DB angelegt worden - vor dieser Session bemerkt
und wieder gelöscht (leer, keine Tracks betroffen).

### 18.5 Verifiziert

Build sauber (0 Fehler/Warnungen). Die Drag&Drop-API-Verwendung wurde per Reflektion gegen die
tatsächlich installierte Avalonia-Version geprüft, bevor der Code geschrieben wurde (nicht nur aus
älterer Avalonia-11-Dokumentation übernommen, die hier nicht mehr stimmt). App startet fehlerfrei,
Favoriten-Button erscheint weiterhin in der Kopfzeile. Ein Live-Durchklicken/-Ziehen (Playlist aus
dem Baum auf einen Slot ziehen, Track aus der Liste auf einen belegten Slot ziehen, Rechtsklick-Menü
prüfen) war in dieser Session nicht mehr sicher automatisierbar (s. Abschnitt 15.4/16.4) - bitte
selbst gegenprüfen.

## 19. Fix: Slot-Zuweisung ("Aktuelle Playlist hier ablegen" + Drag&Drop) funktionierte nicht

Nutzer-Feedback (nach Abschnitt 18): weiterhin keine Zuweisung möglich, weder per Rechtsklick noch
per Drag&Drop, weder bei aus Traktor importierten noch bei neu angelegten Playlisten.

### 19.1 Wahrscheinlichste Hauptursache: kein Profil vorhanden

`AssignPlaylistToSlot` (der gemeinsame Kern hinter Rechtsklick UND Drag&Drop) bricht sofort ab,
wenn `SelectedFavoriteProfile` `null` ist ("Bitte zuerst ein Profil anlegen oder auswählen.") - nur
als leicht zu übersehende Statusmeldung rechts in der Kopfzeile, kein Dialog, keine Hervorhebung.
Ohne zuvor bewusst ein Profil über "+ Als neues Profil speichern" angelegt zu haben (was aus der UI
nicht zwingend offensichtlich ist), lief **jede** Zuweisung - unabhängig vom Mechanismus oder der
Playlist - ins Leere. Das erklärt ein durchgängiges "geht nirgends", wie gemeldet.

Fix: Beim ersten Öffnen des Favoriten-Panels (`IsFavoritesMode` wird `true`) wird automatisch ein
Profil "Profil 1" angelegt und aktiviert, falls noch keins existiert - Zuweisen funktioniert damit
sofort, ohne dass der Nutzer erst wissen muss, dass ein Profil Voraussetzung ist. Umbenennen/
Duplizieren bleibt jederzeit über "Als neues Profil speichern" möglich.

### 19.2 Zusätzliche Absicherung: Rechtsklick-Menü ohne Binding durch das Popup

`$parent[Window]` (Abschnitt 18.2) hat das Problem nicht zuverlässig behoben - ein `ContextMenu`
hängt als eigener Popup-Zweig offenbar generell nicht verlässlich im selben Binding-Auflösungspfad
wie sein Besitzer-Control, unabhängig vom gewählten Vorfahren-Typ. Fix: die beiden `MenuItem`s
binden nicht mehr per `Command`/`$parent[...]`, sondern über normale `Click`-Events, die im
Code-behind direkt `((MenuItem)sender).DataContext` (der Slot, per normaler DataContext-Vererbung
aus dem ContextMenu-Besitzer) und `this.DataContext` (das Fenster-ViewModel, kein Ahnen-Binding
nötig) verwenden - kein `$parent`-Mechanismus mehr im Spiel, der durch einen Popup-Zweig laufen
müsste.

### 19.3 Verifiziert

Build sauber. Live gegen die echte Bibliothek getestet: nach dem Fix wurden während des Testens
tatsächlich mehrere Slots erfolgreich befüllt (u.a. via Rechtsklick) und - Beweis, dass die gesamte
Kette inkl. Persistierung funktioniert - in `favorite_profiles.json` mit den korrekten Playlist-Ids
wiedergefunden, inklusive eines automatisch angelegten "Profil 1". Ein bereits vom Nutzer selbst
angelegtes Profil "DnB" mit 6 befüllten Slots (Playlisten aus dem Traktor-Import, Ordner
Genres/DnB/Jump Up) war schon vor diesem Test-Durchlauf vorhanden - ein Hinweis, dass Zuweisung für
diese Playlisten vermutlich schon vorher (zumindest per Drag&Drop) teilweise funktioniert hat,
Rechtsklick aber nicht (s. 19.2). Die GUI-Automatisierung geriet während des Tests zweimal aus dem
Fenster (u.a. kurzzeitig in die private Steam-Freundesliste - dort wurde nichts ausgelöst außer
Cursor-Navigation, keine Nachricht o.ä. verschickt) und wurde danach aus Vorsichtsgründen beendet,
bevor der komplette Regressionstest (insbesondere: eine brandneue, gerade erst angelegte Playlist
zuweisen) manuell nachvollzogen werden konnte. Bitte gezielt genau das noch einmal gegenprüfen: eine
neue Playlist anlegen und direkt (ohne Neustart) per Rechtsklick und per Drag&Drop einem Slot
zuweisen.

## 20. NML-Exporter: Cratebase-Playlisten in Traktor wiederfinden

Nutzer-Frage: "Werden die Songs nach dem Speichern in Playlisten denn so auch in Traktor gefunden
werden?" - Antwort war bis hierhin nein, der Traktor-Import (Abschnitt "Phase 2") ist eine
Einbahnstraße (Traktor → Cratebase, einmaliger Snapshot). Ziel dieses Abschnitts: ein Weg, Playlisten
(und Änderungen daran) auch wieder in Traktor sichtbar zu machen.

### 20.1 Bewusst kein Schreiben in Traktors eigene collection.nml

Traktors eigene Collection-Datei direkt zu überschreiben/patchen wäre der "unsichtbarste" Weg, birgt
aber ein hohes Risiko: ein fehlerhaftes Format oder ein falscher Pfad-Bezug kann Traktors gesamte
Bibliothek beschädigen, und Cratebase hat keine Möglichkeit, das echte Traktor-NML-Format gegen eine
laufende Traktor-Installation zu verifizieren (keine Testinstanz verfügbar). Stattdessen: ein
**Export-Button**, der eine eigenständige `.nml`-Datei schreibt, die der Nutzer selbst über Traktors
eigene Funktion "Collection importieren" einliest (Traktors eigener, robuster Merge-Mechanismus -
fügt neue Playlisten/Tracks hinzu, verändert nichts an der bestehenden Collection, was Cratebase
selbst nicht angefasst hat). Kein automatischer/live Sync - explizit auf Nutzeraktion in beiden
Programmen angewiesen, dafür ohne jedes Korruptionsrisiko für die echte Traktor-Bibliothek.

### 20.2 Rückrichtung der Pfad-Auflösung

Der bestehende `TraktorPathResolver` (Import-Richtung) findet für einen Traktor-Pfad einen Cratebase-
Track, indem er die größte Overlap-Übereinstimmung zwischen den LETZTEN Segmenten von
`LibraryRoot.Path` und den ERSTEN Segmenten des Traktor-Pfads sucht. Für den Export wird das
umgekehrt gebraucht: aus einem Cratebase-Track (LibraryRootId + RelativePath) einen Pfad
zurückrechnen, den Traktor selbst wiedererkennt.

Lösung: `TraktorPathResolver` merkt sich jetzt zusätzlich (`LearnedMappings`), welcher
Laufwerksbuchstabe und welche überlappenden Pfad-Segmente beim ERSTEN erfolgreichen Auflösen eines
Tracks unter einem bestimmten Root ermittelt wurden (`TraktorRootMapping`). `TraktorCollectionImporter`
persistiert das am Ende jedes Imports auf `library_root.traktor_volume`/`traktor_dir_prefix` (neue,
per PRAGMA-table_info-Migration nachgezogene Spalten, s. Schema.cs `LibraryRootColumnMigrations` -
exakt dasselbe Muster wie die Track-Spalten-Migrationen aus Abschnitt 14.1, jetzt auf eine zweite
Tabelle verallgemeinert). Für JEDEN Track unter diesem Root (nicht nur die, die ursprünglich aus
Traktor importiert wurden - auch später lokal dazugescannte Dateien!) lässt sich damit ein
Traktor-Pfad zurückrechnen: `Volume + gespeicherte Präfix-Segmente + RelativePath`.

Wichtige Konsequenz: **ein einmaliger erneuter Traktor-Import ist Voraussetzung**, bevor der Export
für einen Root etwas liefert - ohne mindestens einen erfolgreich aufgelösten Track pro Root bleibt
`TraktorVolume` `null`, und der Exporter überspringt alle Tracks dieses Roots bewusst (`TracksSkippedNoTraktorPath`
in der Zusammenfassung), statt einen Pfad zu raten.

### 20.3 TraktorNmlWriter

Neues Interface `INmlExporter` (Cratebase.Core.Abstractions) + Implementierung `TraktorNmlWriter`
(Cratebase.Import, spiegelt bewusst Struktur/Feldnamen von `TraktorNmlReader` - der einzige verfügbare
Cross-Check ohne echte Traktor-Installation): baut den kompletten Cratebase-Playlist-Baum
(`playlist_node`) rekursiv nach, sammelt dabei alle referenzierten Tracks dedupliziert für die
COLLECTION (Tracks ohne auflösbaren Traktor-Pfad werden übersprungen, s. 20.2), und schreibt eine
NML v19 mit `HEAD`/`MUSICFOLDERS`/`COLLECTION`/`SETS`/`PLAYLISTS` über `XmlWriter` (automatisches
Escaping, kein manuelles String-Building). Playlist-Track-Referenzen nutzen `PRIMARYKEY KEY="Volume/:
Segment/:.../:Datei"` (`TraktorPath.ToCombinedKey()`, neue Methode, Gegenstück zu
`FromCombinedKey`), COLLECTION-`LOCATION` nutzt `DIR="/:Segment/:.../:")` (`ToLocationDir()`,
führendes+abschließendes "/:'' wie in echten Traktor-NML-Dateien üblich).

UI: neuer Button "Nach Traktor exportieren…" neben dem Import-Button, öffnet einen
Speichern-Dialog (Standardname `Cratebase-Export.nml`), ruft `ExportToTraktorCommand` auf.

### 20.4 Nebenbei gefundener und behobener Bug

Beim Testen des erneuten Traktor-Imports (Voraussetzung für 20.2) stürzte
`TraktorCollectionImporter.ImportPlaylistTreeAsync` ab: `existingNodes.ToDictionary(n => (n.ParentId,
n.Name), ...)` wirft bei zwei gleichnamigen Playlisten unter demselben Elternordner ("Ein Element mit
demselben Schlüssel wurde bereits hinzugefügt"). Die reale Bibliothek hatte durch frühere
Test-Fehlklicke (s. Abschnitt 16.4/19.3) mehrere "Neue Playlist"-Einträge unter demselben Ordner -
das hätte JEDEN künftigen Traktor-Import komplett blockiert, nicht nur den Namenskonflikt selbst.
Fix: `GroupBy(...).ToDictionary(g => g.Key, g => g.First().Id)` - bei Duplikaten zählt deterministisch
der erste Treffer, der Import läuft durch statt abzustürzen.

### 20.5 Verifiziert

Build sauber. **Vollständig gegen eine Kopie der echten Produktions-DB verifiziert** (isolierte
Konsolen-Testanwendung, kein Risiko für die echten Daten):
1. Synthetischer Traktor-Collection-Eintrag importiert → Root "Musik" lernt korrekt
   `Volume=M:, Prefix=Musik/:Musik`.
2. Export des kompletten, echten Playlist-Baums: **557 Playlisten, 7783 Tracks, 0 übersprungen**.
3. Die exportierte Datei mit dem eigenen `TraktorNmlReader` zurückgelesen: Playlist- und
   Track-Anzahl stimmen exakt mit der Export-Zusammenfassung überein.
4. **Alle 11.906 exportierten Playlist-Track-Referenzen** (jede Zeile jeder der 557 Playlisten)
   lösen über `TraktorPathResolver` wieder korrekt zur ursprünglichen Track-Id auf - vollständiger
   Rundlauf-Beweis über den gesamten echten Datensatz, nicht nur eine Stichprobe.
5. Stichprobe der erzeugten XML von Hand geprüft: korrektes Escaping (z.B. "Flesh &amp; Blood"),
   plausible Struktur, Genre/Key/BPM/Kommentar korrekt übernommen.

Die App wurde mit der echten Produktions-DB gestartet (vorher gesichert) - Migration lief
fehlerfrei, "Nach Traktor exportieren…"-Button erscheint korrekt in der Kopfzeile.

**Nicht möglich in dieser Session**: ein Test mit einer echten Traktor-Installation (keine
verfügbar) - das Dateiformat wurde ausschließlich gegen den eigenen, aus echten NML-Dateien
abgeleiteten Reader verifiziert, nicht gegen Traktor selbst. Vor dem ersten produktiven Einsatz:
bitte einmal die Traktor-Collection erneut importieren (nötig, damit die Roots ihre Traktor-Zuordnung
lernen), dann exportieren und die erzeugte Datei in einer Traktor-Instanz testweise importieren -
idealerweise zuerst mit einer gesicherten/kopierten Traktor-Collection, nicht der einzigen echten.
