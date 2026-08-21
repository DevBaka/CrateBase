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
