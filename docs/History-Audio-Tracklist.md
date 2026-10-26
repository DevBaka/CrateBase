# Tracklist aus Traktor-History und vorhandener Aufnahme

Im Workspace **Set Recorder** öffnet **History + Aufnahme analysieren …** einen separaten Prüfbereich. Eine Aufnahme darf auch aus OBS, einem externen Recorder oder einer anderen Anwendung stammen. Es wird die **erste Audiospur** ausgewertet; das Video wird nicht neu gerendert.

1. History-NML und vorhandene Audio-/Videodatei auswählen.
2. **Audio-Abgleich starten**. Die History liefert Originaldateipfade und Tracknamen. NML-Datumsfelder wie LAST_PLAYED sind keine Einsatzzeiten in der Aufnahme und werden nicht als solche ausgegeben.
3. Zeitstempel, Prüfstatus und mögliche Überlagerungen kontrollieren. Per Doppelklick oder **Treffer in Aufnahme vorhören** startet die durchgehende Originalaufnahme drei Sekunden vor dem Treffer. Startzeiten sind editierbar. Die Spaltenbreiten lassen sich ziehen.
4. Nur starke Treffer sind zunächst ausgewählt und sichtbar. **Unsichere Treffer anzeigen** blendet schwächere Kandidaten zur manuellen Prüfung ein. Nicht nachgewiesene Tracks, fehlende Dateien und Fehler bleiben im vollständigen JSON-Bericht erhalten.
5. YouTube-Text kopieren oder Text samt Prüfbericht speichern. Optional lassen sich längere Überlagerungen als **mögliche Live-Mashups** beschriften. Die Auswahl kann außerdem in die Recorder-Tracklist übernommen werden; vorhandene Marker bleiben erhalten. Dort sind auch die Zeiten als MM:SS bzw. H:MM:SS editierbar.

Die Oberfläche prüft außerdem die grundlegenden [YouTube-Kapitelvorgaben](https://support.google.com/youtube/answer/9884579?hl=de): erster Zeitstempel 00:00, mindestens drei Einträge, mindestens zehn Sekunden pro Kapitel. Zeitlich eng benachbarte Einsätze werden deshalb nicht heimlich verschoben; gegebenenfalls erscheint ein Hinweis. Überlappende Abschnitte desselben Tracks werden im Text zusammengefasst; getrennte spätere Einsätze bleiben erhalten. Der Prüfbericht enthält die ursprünglichen Einzelabschnitte.

Die optionale Kurzfassung kürzt lange Tracknamen mit sichtbaren Auslassungszeichen auf insgesamt höchstens 4.500 UTF-8-Bytes; die vollständige Fassung wird zusätzlich gespeichert. Das lässt Reserve gegenüber dem dokumentierten [5.000-Byte-Limit der YouTube-API](https://developers.google.com/youtube/data/reference/rest/v3/videos#snippet.description). Es werden dafür keine Tracks oder Zeitstempel weggelassen. Bei extrem vielen Einträgen, bei denen selbst gekürzte Namen nicht passen, wird die Überschreitung gemeldet.

## Was die Analyse leistet und was offen bleibt

Überlappende Treffer desselben Interpreten und Titels sowie Fragmente mit höchstens 20 Sekunden Lücke werden bereits in der Prüftabelle zusammengefasst, auch bei gleichnamigen Dateikopien. Spätere erneute Einsätze bleiben getrennt. Schwache Fragmente verschieben einen starken Anfang nicht nach vorne. Die Spalte **Treffer** zeigt die zusammengefassten Belege; `RawMatches` im JSON erhält die ursprünglichen Einzelabschnitte. Bereits gespeicherte Berichte werden beim Öffnen ebenfalls zusammengefasst; anschließend gespeicherte Zeitkorrekturen und Auswahlen bleiben erhalten.

## Aufnahme vorhören

Unter der Tabelle bleibt ein Player mit Play/Pause, Stop, Lautstärke, ±5 Sekunden und vorherigem/nächstem ausgewählten Trackmarker sichtbar. Ein Klick in die Timeline springt an die entsprechende Stelle; beim Darüberfahren erscheinen Zeit und Trackname. Die Marker zeigen erkannte Trackanfänge, keine gemessenen Faderbewegungen.

MP4-, MKV- und MOV-Aufnahmen werden unter Windows mit Bild wiedergegeben. Das Video lässt sich rechts neben der Tabelle einklappen oder als **Pop-out** öffnen. Schließen des Pop-outs dockt es wieder an. Andocken und Einklappen erhalten die laufende Wiedergabe. Bei Audioaufnahmen wie MP3, FLAC und WAV erzeugt Cratebase eine RGB-Waveform für die Timeline; der Cache bleibt im privaten Cratebase-Verzeichnis. Die Wiedergabe nutzt die Originaldatei und benötigt keinen vorab gerenderten Vorhörausschnitt.

Die lokale Erkennung vergleicht spektrale Landmarken nach dem Prinzip von [Wang, ISMIR 2003](https://www.princeton.edu/~cuff/ele301/files/Wang03-shazam.pdf). Eine Tempo-Suche gleicht konstante Änderungen zwischen 0,75× und 1,25× ab, wenn die Tonhöhe durch Keylock erhalten bleibt. Mehrere Referenztracks werden unabhängig gesucht, damit echte Überlagerungen nicht durch eine erzwungene Ein-Track-Zuordnung verschwinden.

**Es gibt keine Garantie einer fehlerfreien Tracklist.** Angezeigte Anfangszeiten sind früheste belastbare Audio-Belege, keine Messung von Fader- oder Cue-Betätigungen. Maskierte Intros, kurze Einsätze, Effekte, starke Tempo-Rampen, Pitchänderungen ohne Keylock, identische Samples und Remixe mit gemeinsamen Passagen können ungenaue, fehlende oder falsche Treffer erzeugen. Ein nicht gefundener History-Eintrag beweist weder „nie gespielt“ noch „abgebrochener Übergang“. Eine Überlagerung beweist keinen beabsichtigten Mashup; auch normale Übergänge können lange überlappen. Solche Stellen gehören in die Hörprüfung.

„Stark“ ist eine konservative Heuristik: mindestens 30 verschiedene Aufnahme-Sekunden mit passenden Landmarken und mindestens 60 % belegte Sekunden im Abschnitt. Das ist keine kalibrierte Trefferwahrscheinlichkeit. Schwächere Kandidaten bleiben einblendbar, werden aber nicht automatisch ausgewählt. Tracks ohne starken Treffer erscheinen zusätzlich unter „Nicht nachgewiesen“, auch wenn es schwache Kandidaten gibt. Gleichnamige Trackkopien werden beim Beschriftungsexport nicht als eigener Mashup behandelt.

## Laufzeit und Datenschutz

Alle Audiodaten bleiben lokal. FFmpeg dekodiert die Dateien; ein in Cratebase verwaltetes Python-/NumPy-Modul erstellt Fingerabdrücke und gleicht sie ab. Eine vorhandene private Stem-Laufzeit wird wiederverwendet, andernfalls richtet Cratebase eine kleine eigene Python-/NumPy-Umgebung ein. Eine globale Python- oder Ollama-Installation ist nicht erforderlich. Beim erstmaligen Einrichten werden Laufzeit und Pakete heruntergeladen, keine Musik hochgeladen.

Cache, Zwischenstände, interne Berichte und Vorhördateien liegen unter `%LOCALAPPDATA%\Cratebase\history-analysis`. Fingerabdruck- und Treffer-Caches berücksichtigen Quellpfad, Dateigröße, Änderungszeit und Algorithmusversion; bereits abgeschlossene Track-Prüfungen werden bei Wiederholung wiederverwendet. Musik-, Video- und Traktor-Dateien werden nicht verändert. Abbruch bzw. Schließen beendet den Analyseprozess samt FFmpeg-Kindprozessen.

Prüfung: `dotnet run --project tests/Cratebase.IntegrationTests -c Release -- --history-checks` testet bekannte Cue-Einstiege, Tempoänderung bei Keylock, Überlagerung und einen in der History enthaltenen, aber nicht abgespielten Track mit synthetischen Audiodateien.
