# Gespeicherte Traktor-Histories

Beim nächsten Start der **Cratebase-Traktor-Edition** erscheinen neue archivierte
NML-Sessions aus dem ursprünglichen und dem Edition-`History`-Ordner unter
**Session Histories**. Die laufende History bleibt Traktors eigener History-Punkt.
Die archivierten Sessions sind normale Playlisten: umbenennen, verschieben,
Tracks herausziehen und Playlisten löschen funktionieren mit Traktors Funktionen.
Gelöschte oder verschobene Playlisten werden beim nächsten Start nicht neu angelegt.
Die NML-Archive selbst werden weder geändert noch gelöscht. Die Collection wird vor
dem Ergänzen gesichert. Es werden keine Musikdateien kopiert.

In **Cratebase → Set-Planer → Session-Histories …** werden die History-Ordner der
installierten Traktor-Profile automatisch durchsucht. Weitere Ordner können
hinzugefügt werden; ihre Auswahl bleibt gespeichert. Identische Archivkopien
werden nur einmal angezeigt. »Session ansehen« erhält alle Einträge einschließlich
wiederholter Plays. Für mehrere Sessions die Checkboxen setzen und »Auswahl
zusammenführen« wählen. Optional werden doppelte Dateien entfernt und die
Session-Grenzen anhand von BPM, Tonart und Genre passend angeordnet. Die Reihenfolge
innerhalb einer Session wird dabei erhalten.

Kurz angespielte Tracks (<30 Sekunden), fehlende Metadaten sowie problematische
Übergänge erhalten Prüfhinweise. Das ist eine Metadatenbewertung, keine Erkennung
des tatsächlichen Übergangs im Recording. Einzelne Tracks lassen sich über
»Behalten« ausschließen. »Behaltene Tracks im Set-Planer öffnen« erzeugt einen
separaten Plan mit Herkunft und Hinweisen. Dort lassen sich die Tracks und
Übergänge vorhören, umordnen, entfernen und als normale Playlist speichern.
Vorhandene Track-Tags werden nicht überschrieben. Fehlende Dateien bleiben im
Plan sichtbar. Traktor-Cues und die ursprünglichen Sessions bleiben erhalten.

## Installer-Reparatur und Wiederherstellung am 31.10.2026

In `fix4` wurden gemeinsam genutzte Python-/uv-Verzeichnisse über optionale
Umgebungsvariablen rekursiv gelöscht. Bei fehlenden Variablen entspricht
`Path('')` dem aktuellen Arbeitsordner. Ein Installer-Test ohne diese Variablen
hat deshalb den Checkout getroffen. Diese Löschlogik ist entfernt. Defekte
virtuelle Umgebungen werden ausschließlich unter den geprüften Cratebase-Pfaden
in `.broken-*` umbenannt; die neue Umgebung wird anschließend angelegt.
Ein Regressionstest verbietet rekursive Löschungen bei leeren Cache-Variablen.

Der versionierte Stand `1cee11c` (`fix4`) wurde aus erhaltenen Git-Objekten
wiederhergestellt. Remote `https://github.com/DevBaka/CrateBase.git`, Branch `main`
und das Tracking auf `origin/main` wurden gegen den Remote-HEAD geprüft und
wiederhergestellt. Build-Ausgaben wurden erneut erzeugt. Die History-Erweiterungen
wurden danach neu erstellt. Nicht versionierte lokale Dateien konnten nicht
vollständig inventarisiert werden. Nutzerdaten unter AppData und die originale
Traktor-Collection lagen außerhalb des betroffenen Checkout-Pfads.

Der MSI Claw startet laut Rückmeldung Traktor, aber bislang ohne funktionierende
Stems. Die physische Intel-Arc-/Live-Anbindung ist daher **noch offen** und muss
direkt auf diesem Gerät anhand der GPU- und Launch-Protokolle geprüft werden.

Prüfstand: Desktop-Build ohne Fehler; 140 reale Histories mit 6.570 Einträgen
fehlerfrei gelesen. Der Import in eine Kopie der großen Collection wurde inklusive
erneutem Start ohne Duplikate und unveränderter Original-Collection geprüft.
Die Python-Reparatur wurde mit absichtlich fehlenden Bootstrap- und GUI-Interpretern
geprüft; auch der anschließende `pythonw`-Start funktioniert. Die native Anzeige
der History-Playlisten wird beim nächsten Start der Edition aktiv; während einer
laufenden Traktor-Session wird die Collection nicht verändert.
