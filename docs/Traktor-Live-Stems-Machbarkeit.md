# Traktor 4.5.1: Live-Stems – aktueller Befund

Geprüft am 25.10.2026. Installiert: `Traktor Pro 4.exe`, Version `4.5.1 21`. Die Installation wurde ausschließlich gelesen. Keine DLL, QML-Datei, NML oder Traktor-Einstellung wurde verändert.

## Was die Mixxx-Arbeit tatsächlich ermöglicht

Im lokalen Repository `C:\Users\DJBaka\Documents\GitHub\mixxx` untersucht:

- Benutzer-Commit `1d1fbaf2c64aa3de38fd50a2a90a6bae29406b80`, „feature: live stems“.
- `src/engine/stemseparation/istemseparationbackend.h`: blockweise Stereo-Separation auf vier Stereo-Stems.
- `livestemseparationengine.cpp`: asynchrone Initialisierung, Cache und Hintergrundarbeit.
- `onnxstemseparationbackend.cpp`: native Inferenz und Provider-Auswahl.
- `src/sources/soundsourcelivestemseparation.cpp` und die Änderungen am Caching Reader: der entscheidende Eingriff in den tatsächlichen Audio-Leseweg.
- `tools/export_htdemucs_onnx.py`: HTDemucs-Export, Eingabe `[1,2,N]`, Ausgabe `[1,4,2,N]`, Rollen Drums/Bass/Other/Vocals.

Die Architektur ist eine sinnvolle Grundlage für Cratebase-eigene Separation. Sie funktioniert in Mixxx, weil Engine und Audioquellen im Quelltext geändert und neu kompiliert werden können. Der Commit selbst nennt noch Stottern/Stopps, CPU-Probleme und nicht funktionierende GPU-Separation. Daraus lässt sich keine belastbare Zusage ableiten, dieselbe Technik in Traktors laufendes Deck einzusetzen.

## Was Traktor bereits bietet

Native Instruments dokumentiert die Hintergrundgenerierung von Stems, auch während ein Track spielt. Anschließend kann die erzeugte Stem-Version geladen werden. Das ist von fortlaufender blockweiser Separation mit sofortiger Einzelsteuerung des bereits spielenden Decks zu unterscheiden.

Quellen: [NI: How to mix stems](https://www.native-instruments.com/blogs/blog/how-to-mix-stems), [Traktor-Pro-4-Handbuch](https://www.native-instruments.com/fileadmin/ni_media/downloads/manuals/traktor/Traktor-Pro-4-Manual-English-170724.pdf), [Preferences / Stem-Dateiverzeichnis](https://docs.native-instruments.com/ni-tech-manuals/traktor-pro-manual/en/preferences).

## Screen-Mods und mögliche Brücke

Die installierten QML-Dateien für S4 MK3, S8 und weitere Controller enthalten Anzeige- und Controllerlogik für Stem-Decks. Die gezielte Suche nach Separation, PCM-/Audiobufferzugriff und Stem-Dateiladung hat keinen verwendbaren öffentlichen DSP-Einspeisepunkt ergeben. Das beweist nicht, dass intern keiner existiert; es ist aber kein belastbarer Ausgangspunkt für eine stabile Live-Mod.

Die bekannten [S4-Screen-Mods](https://github.com/kokernutz/traktor-kontrol-screens), [QML-Mod-Unterlagen](https://github.com/lsmith77/traktor-kontrol-qml) und [traktor-api-client](https://github.com/ErikMinekus/traktor-api-client) eignen sich für Anzeigen, Controlleraktionen oder ausgehende Metadaten. Ein Netzwerkaufruf aus einer Anzeige ersetzt noch keinen Decoder und liefert keinen Zugriff auf die intern laufenden vier Stem-Puffer.

Eine Companion-Variante wäre erheblich realistischer: geladenen Track erkennen → separat berechnen → fertige Stem-Version bereitstellen → kontrolliert neu laden. Für ein nahtloses Umschalten müsste zusätzlich ein geprüfter Lade-/Positions-/Tempo-/Loop-Zugriff vorhanden sein. Diese Brücke ist **noch nicht implementiert** und wäre auch dann nicht automatisch echte inkrementelle Live-Separation.

## Ergebnis dieses Prototyps

Implementiert ist der Cratebase-eigene Offline-Stem-Generator mit Hintergrundaufträgen und separatem NI-Stem-Container. Er arbeitet außerhalb von Traktor. Eine Mod, die live den laufenden Traktor-Audioweg ersetzt, wurde **nicht gebaut**; sie wäre ein eigenes, versionsabhängiges Engine-/Reverse-Engineering-Projekt mit Messungen von Puffern, Latenz, Seek, Loops, Tempo und Ausfallverhalten. Ohne diese Untersuchungen wäre ein RAM-/DLL-Patch kein nachgewiesener funktionsfähiger Prototyp.

Offene praktische Abnahmen: eigener Stem-Export in einer gesicherten Traktor-Testcollection laden, vier Stem-Regler prüfen, mehrere echte Clubtracks klanglich bewerten. Erst danach sollte eine Companion-Anbindung oder tiefere Engine-Untersuchung folgen.
