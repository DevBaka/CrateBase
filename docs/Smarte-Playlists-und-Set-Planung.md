# Smarte Playlists, lokale KI und Set-Planung

Stand: 25.10.2026. Windows-Prototyp; bestehende Library, Recorder und Android-Server bleiben erhalten.

## Einstieg

1. Den aktuellen Release-Build starten: `src/Cratebase.App.Desktop/bin/Release/net8.0-windows10.0.19041.0/Cratebase.App.Desktop.exe`.
2. In der Library oben links **+ Smart** wählen. Quelle und Regeln festlegen, z. B. Club = Ja, Trash = Nein, Genre enthält Techno, BPM mindestens 135. Alle Regeln (UND) oder mindestens eine Regel (ODER) sind möglich. **Vorschau** zeigt die Trefferzahl.
3. Gespeicherte Smart-Playlists stehen im linken Browser. Rechtsklick: Regeln bearbeiten, löschen oder **als normale Playlist speichern**. Letzteres erstellt einen Snapshot für den vorhandenen NML-Export. Smart-Playlists selbst sind dynamisch und keine Drag-and-Drop-Ziele.
4. Neben dem Logo **Set & Transition Planning** wählen. Links befindet sich ein eigener Browser für Collection, Playlist-Hierarchie, smarte Playlists und Festplattenordner. Quellen auswählen, darin suchen und mehrere Tracks direkt auf die gewünschte Stelle im Set ziehen. Doppelklick oder **Auswahl anhängen** übernimmt Tracks ebenfalls. Vorhandene Set-Tracks lassen sich per Drag & Drop umsortieren; ihre Marker bleiben erhalten. Die Vorschlagsfunktionen verwenden Quelle und Suche dieses Browsers. Metadatenregeln können über eine smarte Playlist als Quelle angewendet werden.

**Ordner wählen …** öffnet auch bisher nicht gescannte Musikordner. Beim Öffnen werden die Audiodateien dieses Ordners eingelesen; Unterordner lassen sich im Baum aufklappen. Der Browser hat eigene Such- und Scrollzustände und lädt Trackseiten bei Bedarf nach. Browser- und Set-Breite sowie die Höhe des Quellenbaums sind über Trennlinien verschiebbar. KI-Einstellungen sind unter **Set-Vorschläge** einklappbar.

Zusätzliche Filter einer Trackliste bleiben wirksam. Wer weniger Treffer als erwartet sieht, sollte deshalb auch den Filter-Button der betreffenden Liste prüfen.

## Set-Vorschläge

**Regelbasiert vorschlagen** berücksichtigt BPM-Abstand, harmonische Kompatibilität, Genre-Familien und Artist-Wiederholungen. Camelot, Open Key und gebräuchliche Tonartnamen werden normalisiert. Trash, fehlende Dateien und Tracks ohne bekannte Dauer werden ausgeschlossen. Fehlende BPM/Keys werden als Unsicherheit ausgewiesen. Ohne Freigabe von Genre-Wechseln bleibt ein Vorschlag innerhalb einer Genre-Familie; beispielsweise werden Techno-Untergenres gemeinsam betrachtet.

**Lokale KI vorschlagen** ergänzt musikalisches Thema, Künstlerkontext und eine gewünschte Genre-Reise. Vorlagen:

- Dark to Hard: Goth → EBM → Aggrotech → Dark Techno → Hard Techno.
- Melancholischer Hardstyle: emotionale/depressive Tracks und passende Wasted-Penguinz-Klassiker.
- House / Techno oder frei formulierbares Thema.

Die harte BPM-Grenze gilt auch für KI-Vorschläge. Ein Vorschlag mit unbekannten, doppelten oder unzulässigen Track-IDs wird verworfen. Ein sprachlich plausibler Text ersetzt keine Hörprüfung: Stimmung, Masteringqualität, Energie und Phrasen werden derzeit **nicht aus dem Audio analysiert**. Das Modell kann bei unbekannten Künstlern oder schlecht getaggten Dateien falsch liegen.

Auch große Collections können direkt als Quelle dienen. Regelbasierte Vorschläge berücksichtigen die gesamte Quelle. Für die lokale KI werden die Treffer aus der Datenbank gelesen und automatisch bis zu **250 Kandidaten** anhand von Thema, Genres, Künstlern, Titeln, Kommentaren und Pfaden vorausgewählt; Gleichstände werden zufällig aufgelöst. Die Oberfläche nennt Gesamtzahl und Vorauswahl. Anschließend folgen höchstens **fünf KI-Vorauswahl-Durchläufe plus die finale Reihenfolge** aus höchstens 50 Finalisten. Diese Metadaten-Heuristik ersetzt keine Audioanalyse und kann passende Alternativen auslassen. Laufzeit, Verarbeitungsschritt und eingehende Antwortzeichen werden alle zwei Sekunden angezeigt. Ein einzelner Durchlauf hat einschließlich Modellstart ein Zeitlimit von drei Minuten; der gesamte Auftrag lässt sich abbrechen.

Die abschaltbare Option **Zuerst passende Genre-Ordner** erkennt bekannte Genre-Begriffe aus Thema und Genre-Reise. Sie grenzt auf passende gespeicherte Ordnerpfade oder Genre-Tags ein, beispielsweise Hardtekk oder Techno, bevor die KI eingesetzt wird. Wenn keine passenden Genres erkannt werden oder keine Tracks passen, bleibt die gesamte Quelle verfügbar. Dies ist eine lokale Metadaten-Heuristik, keine zusätzliche KI-Anfrage und kein erneuter Dateiscan.

Über **KI-Vorauswahl (Anzahl)** neben den Set-Vorschlägen öffnet sich eine separate Liste. Zunächst zeigt sie die Metadaten-Kandidaten, nach jedem KI-Durchlauf die bisher gewählten Tracks und zuletzt den fertigen Vorschlag. Doppelklick oder **Vorhören** startet einen ausgewählten Track; Play/Pause, Stop und Positionsregler bleiben während der Berechnung benutzbar. Die Zwischenliste verändert den gespeicherten Set-Plan nicht.

Mit **Musikordner indexieren …** links im Browser lässt sich ein gewählter Ordner einschließlich Unterordner dauerhaft in die vorhandene SQLite-Datenbank einlesen. Der Scan läuft im Hintergrund, zeigt Fortschritt und lässt sich über **Auftrag abbrechen** stoppen. Er liest Dateien und Metadaten, verändert keine Musikdateien und überspringt bei späteren Scans unveränderte Dateien. Die anschließenden Vorschläge und Suchen verwenden die Datenbank. Bereits importierte oder gescannte Tracks sind dort schon enthalten.

Die gewünschte Dauer ist eine Zielgröße für ungekürzte Tracks, keine exakte Mix-Laufzeit. Übergänge, tatsächliche Abspielabschnitte und musikalische Eignung werden anschließend manuell geprüft. Neue Vorschläge erzeugen eigene Set-Pläne und überschreiben bestehende Pläne nicht.

## Lokale Laufzeit

Unter **KI & Referenzen → Lokale KI einrichten** lädt Cratebase llama.cpp b11869 (Vulkan/CPU) und das offizielle Qwen3-4B-Q4_K_M-Modell herunter, zusammen etwa 2,54 GB. Keine separate Ollama-Installation. Die Downloads werden gegen festgelegte SHA-256-Werte geprüft. Die Laufzeit startet versteckt, ausschließlich auf einer lokalen Loopback-Adresse mit zufälligem Zugriffsschlüssel. Nach einem Planungsauftrag beendet die Oberfläche den Modellprozess, damit Grafikspeicher wieder frei wird. Ein CPU-Modus ist wählbar.

Auf dem Entwicklungsrechner mit Ryzen 9 5900X, RTX 3060 Ti 8 GB und 64 GB RAM sind Laufzeit und Modell bereits unter `%LOCALAPPDATA%\Cratebase\ai` installiert und ein tatsächlicher GPU-Inferenztest ist bestanden. Die Installation ist benutzerspezifisch und wird nicht ins Git-Repository aufgenommen.

Modellinferenz bleibt lokal. Bei aktivierter **öffentlicher Set-Inspiration** verlässt der Suchtext den Rechner für die Online-Recherche. Library-Audiodateien werden dabei nicht hochgeladen.

## Set-Inspiration

Öffentliche Set-Beschreibungen werden über eine app-eigene, festgelegte yt-dlp-Laufzeit abgefragt. Die automatische Suche verwendet YouTube; einzelne öffentliche YouTube-, SoundCloud- und Mixcloud-Links werden ebenfalls unterstützt. Es werden nur Metadaten/Beschreibungen gelesen, keine Audio-/Videodateien geladen, keine Browser-Cookies übernommen und keine Zugangsbeschränkungen umgangen. Spotify-Anmeldung und Spotify-API sind noch nicht angebunden.

Die drei besprochenen Referenzen sind im ersten neuen Plan hinterlegt:

- [Kinky Techno 2025](https://www.youtube.com/watch?v=lqAxGUNf4ZQ)
- [Synth Pop & Darkwave Mix in Brooklyn | Georgi](https://www.youtube.com/watch?v=P6zJFGMcWdY)
- [Klanglos live @ Space Monki, Zürich](https://www.youtube.com/watch?v=3b7hz1howpo)

Unter **KI & Referenzen → Set-Referenzen** können Links, Beschreibungen und eigene Tracklisten gespeichert werden. **Beschreibungen abrufen** liest die eingetragenen Links. Beschreibungen enthalten nicht immer eine Trackliste; die Funktion behauptet deshalb keine automatische Erkennung aller gespielten Songs. Blockierte/fehlgeschlagene Abrufe werden angezeigt. Referenzen dienen der Auswahl eigener Tracks, nicht dem Hinzufügen fremder Audiodateien.

## Übergänge planen und vorhören

Eine Zeile auswählen: Deck A zeigt den ausgehenden Track, Deck B seinen Nachfolger. Vorhandene Traktor-Stripes werden bevorzugt; andernfalls dient der Cratebase-Waveform-Cache bzw. die RGB-Erzeugung als Quelle.

- **Transition-Start (Play)**: Beginn des für die Vorschau verwendeten Ausschnitts.
- **Basswechsel**: geplanter Wechselpunkt; in der Vorschau wird der A-Marker relativ zu Play A verwendet.
- **Fade-out**: Ende des Ausschnitts in A.
- **Vorhören / Pause / Fortsetzen** spielt den jeweiligen Deck-Track. **↶ Start** beginnt am Transition-Start-Marker. Deck-Wechsel stoppen das vorherige Vorhören. Doppelklick auf eine Set-Zeile startet bzw. pausiert Deck A.
- Waveform-Klick spult die Wiedergabe, ohne Marker zu verändern.
- Die farbigen Start-/Bass-/Out-Linien bzw. ihre Griffe lassen sich mit der Maus ziehen. Beim Loslassen werden die Marker im Set gespeichert. Die Reihenfolge Start ≤ Basswechsel < Fade-out bleibt erhalten. Sekundenwerte sind weiterhin für Feinkorrekturen verfügbar.
- **Marker übernehmen** speichert die Marker im Set-Plan. Graue Linien zeigen importierte Traktor-Cues, farbige Linien die eigenen Planungsmarker.
- **Kurzen Basswechsel vorschlagen** setzt in A einen 16-Beat-Ausschnitt mit Basswechsel vier Beats vor Schluss. Das ist eine BPM-basierte Ausgangshilfe, keine erkannte Phrase oder ein erkannter Beatgrid.
- **Übergang rendern & vorhören** berechnet eine temporäre Stereo-WAV. B beginnt an Play B, wird auf das bekannte Tempo von A angepasst, hohe Frequenzen werden überblendet, der Bass wechselt bei 180 Hz innerhalb 60 ms. Ein Limiter begrenzt Spitzen. Vor der Wiedergabe wird der Library-Player pausiert. **Vorschau Play/Pause** steuert die Vorschau, **Vorschau stoppen** beendet sie. Waveforms werden unabhängig vom Vorhören geladen; eine laufende RGB-Berechnung blockiert die Play-Tasten nicht.

Die Vorschau ist auf 1–120 Sekunden und Tempoverhältnisse von 0,8–1,25 begrenzt. Sie ist ein Offline-Audition-Werkzeug, kein samplegenauer Live-DJ-Mixer. Es fehlen automatische Phrasenanalyse, ein Beatgrid-Editor und ein durchgehender Set-Render. **Als Playlist speichern** übernimmt Reihenfolge und Referenzen auf die Originaltracks; Planungsmarker werden nicht als Traktor-Hotcues exportiert. Bestehende Traktor-Cues werden weder ersetzt noch beschrieben.

## Stem-Generator

Im Library-Header **Stems** wählen. Markierte Tracks oder die gesamte aktive Quelle samt Filtern können verarbeitet werden. Ein separater Ausgabeordner ist frei wählbar und wird gespeichert; Standard: `%LOCALAPPDATA%\Cratebase\stems`.

Cratebase richtet eine private Python-/PyTorch-CPU-/Demucs-Umgebung ein, ohne systemweites Python oder Traktor zu ändern. Rund 400 MB zusätzliche Downloads einschließlich HTDemucs-Modell. Die Berechnung läuft seriell im Hintergrund mit niedrigerer Prozesspriorität und Abbruchmöglichkeit. Originale bleiben erhalten. Bestehende Ausgabedateien werden übersprungen, Fehler in einen Bericht im Zielordner geschrieben. Laufende unvollständige Dateien werden nicht unter dem endgültigen Namen veröffentlicht.

Der Container enthält Original-Mix plus Drums/Bass/Other/Vocals als fünf Stereo-AAC-Streams mit 256 kbit/s pro Stream, NI-Stem-Manifest Version 1, separaten Trackrollen und `.stem.mp4`-Endung. Das ist kein bloß umbenanntes MP4. Er wird anschließend in Traktor importiert und analysiert; es wird kein Eintrag in der originalen Traktor-Collection automatisch umgeschrieben.

**Prüfstand:** tatsächliche CPU-Separation und Container-Struktur mit Testaudio geprüft. Der praktische Import samt vier Einzelreglern in Traktor 4.5.1 und die klangliche Qualität an Clubmusik sind noch offen. Vor einem großen Batch zuerst einen Track exportieren und in einer gesicherten Testcollection prüfen. GPU-Separation, Zuordnung zu vorhandenen Traktor-Originaleinträgen und echte Live-Stems im laufenden Traktor-Deck sind nicht implementiert; siehe [Machbarkeitsbericht](Traktor-Live-Stems-Machbarkeit.md).

## Technische Prüfung

`dotnet build tests/Cratebase.IntegrationTests -c Release`

Anschließend die gebaute IntegrationTests-DLL mit folgenden Schaltern ausführen:

- `--planning-checks`: dynamische SQL-Regeln, AND/OR, Quellenbegrenzung, Sortierung/Pagination, ID-Validierung, Plan-Persistenz, echte FFmpeg-Vorschau und Abbruch.
- `--planning-checks --real-ai`: zusätzlich tatsächliche lokale GPU-Inferenz mit Fixture-Tracks.
- `--planning-ui-checks`: eigener Browser, Playlist-Inhalte und Suche, tatsächliche Drop-Ereignisse, Play/Pause, Waveform-Seek, Mausziehen aller drei Marker, Erhalt importierter Cues, Umsortieren ohne Marker-Verlust, Playlist-Publikation, Erhalt alter Pläne und gerenderte UI.
- `--planning-native-playback-checks`: tatsächliches Abspielen, Pausieren, Fortsetzen und Stoppen mit LibVLC und einer sehr leisen Audio-Fixture.
- `--stem-checks --real-stems`: tatsächliche private CPU-Separation und Stem-Verpackung mit synthetischem Testaudio.
- `--inspiration-check <URL oder Suchtext>`: öffentlicher Metadatenabruf; Netzwerk erforderlich.
- Bestehende Recorder-, Server-, NML-, Waveform- und 100.000-Track-Regressionen bleiben verfügbar. Für Server-Tests `CRATEBASE_TEST_FFMPEG` auf den tatsächlichen Encoder setzen.

Alle Daten-/Audiotests verwenden isolierte temporäre Fixtures. Keine nächtliche Massenanalyse der echten Collection wurde gestartet.

## Verwendete Laufzeiten und Quellen

- [llama.cpp, Server-Dokumentation und Lizenz](https://github.com/ggml-org/llama.cpp/tree/b11869) – MIT; mitgelieferte Komponenten behalten eigene Lizenzen.
- [Offizielles Qwen3-4B-GGUF](https://huggingface.co/Qwen/Qwen3-4B-GGUF) – Apache-2.0, Revision `bc640142c66e1fdd12af0bd68f40445458f3869b`.
- [Demucs](https://github.com/facebookresearch/demucs), [PyTorch](https://pytorch.org/), [uv](https://docs.astral.sh/uv/), [yt-dlp](https://github.com/yt-dlp/yt-dlp). Originalpakete, Wheel-Lizenztexte und Binär-Komponentenlizenzen beachten; nicht sämtliche Laufzeiten stehen unter derselben Lizenz wie Cratebase.
- [NI: Stem-Dateien](https://docs.native-instruments.com/ni-tech-manuals/traktor-play-user-guide/en/working-with-stems), [Stempeg NI-Container-Verfahren](https://github.com/faroit/stempeg), Mixxx-Stem-Fixtures und -Importer im lokalen Repository als Formatvergleich.
