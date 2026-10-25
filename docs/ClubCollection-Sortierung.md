# Club Collection: Ordnerstruktur und Aufräumvorschlag

Stand: 24. Oktober 2026. Grundlage: lesende Prüfung von `M:\Musik`, dessen direkten Unterordnern und ausgewählten tieferen Zweigen sowie der Cratebase-Datenbank mit importierter Traktor-Hierarchie. Kein vollständiges Anhören und keine automatische musikalische Neubewertung.

## Was an deiner bestehenden Struktur funktioniert

Deine Genre-Familien sind schon gut erkennbar: Techno, House, Drum & Bass, Hardstyle, Hardcore, HardTekk, Psy-Trance usw. Besonders die Unterscheidung von Hard Techno, Deep Techno, Minimal Techno, Neurofunk, Liquid und weiteren Unterstilen hilft beim spontanen Auflegen. Auch die Playlisten trennen bereits Genres, vorbereitete Sets und tatsächliche Set-Tracklists.

Die Unübersichtlichkeit entsteht vor allem dadurch, dass mehrere Ordnungskriterien dieselbe Ebene benutzen:

| Beobachtung | Konkretes Beispiel | Konsequenz für die Club Collection |
| --- | --- | --- |
| Herkunft neben Genre | `Beatport/Hard Techno` neben `Techno/Hard Techno` | Beide werden `Techno/Hard Techno` |
| Jahr und Shop zwischen Genre und Datei | `Techno/Hard Techno/Beatport/2015` | Jahr/Shop entfallen im Exportpfad |
| Unterschiedliche Schreibweisen | `Psytrance` / `Psy-Trance`, `HandsUp` / `Hands Up`, `DeepHouse` / `Deep House` | Einheitliche Ordnernamen |
| Genre, Stimmung und Set-Zweck vermischt | `dark clubbing`, `banger`, `Intro Songs`, `Prepared Sets` | Genre physisch; Stimmung/Edit/Set-Zweck als zusätzliche Merkmale bzw. Playlisten |
| Mehrere Generationen von Genre-Playlisten | `GenresOld`, `GenresOldNew`, alte Techno-Gruppen | Eine aktuelle Genre-Hierarchie; alte Strukturen zunächst als Archiv behalten |
| Fehlende Genre-Tags | 15.489 von 40.073 Datenbank-Tracks, ca. 38,7 % | Bekannte Genre-Ordner helfen als Vorschlag; Unklares bleibt prüfpflichtig |
| Genre-Tag widerspricht dem Ordner | `Shandi (Original Mix)` unter Hard Techno, Tag `Techno (Peak Time / Driving)` | Vorschau zeigt Widerspruch; bekannter spezifischer Genre-Ordner hat standardmäßig Vorrang |

## Empfohlene physische Struktur

Maximal zwei musikalische Ebenen verwenden: **Genre-Familie / Untergenre / Datei**. Keine Shop-, Download- oder Jahresordner dazwischen. Der Export benutzt direkt diese Struktur, ohne einen zusätzlichen `Musik`-Ordner:

```text
C:\Users\DJBaka\Music\ClubCollection\
├── Cratebase-Club-Collection.nml
├── Cratebase-Exportbericht.json
├── .cratebase-club-export.json
├── Techno\
│   ├── Hard Techno\
│   ├── Schranz\
│   ├── Deep Techno\
│   ├── Minimal Techno\
│   ├── Peak Time - Driving\
│   ├── Industrial Techno\
│   ├── Acid Techno\
│   └── Melodic Techno\
├── House\
│   ├── Deep House\
│   ├── Tech House\
│   ├── Bass House\
│   └── Progressive House\
├── Drum & Bass\
│   ├── Liquid\
│   ├── Neurofunk\
│   └── Jump Up\
├── Psy-Trance\
│   ├── Full-On\
│   ├── Dark - Forest\
│   └── Hi-Tech - Psycore\
├── Hardstyle\
├── Hardcore\
├── HardTekk\
├── Hands Up\
├── Industrial & EBM\
├── Gothic & Wave\
├── DJ Tools\
└── 00 Genre prüfen\
```

Dies ist das Zielbild, keine Sammlung leerer Ordner: Der Export erzeugt nur tatsächlich benötigte Genre-Ziele. Ein generisch zugeordnetes Genre landet unter `Allgemein`, bis ein konkreter Unterstil feststeht. Unbekannte bzw. mehrdeutige Tags erzeugen einen prüfbaren Vorschlag, keine automatische Zuordnung nach vermuteter Stimmung oder BPM.

Ein bekannter, spezifischer Quellordner wie `Beatport/Hard Techno` wird als `Techno/Hard Techno` erkannt. Labels wie Beatport, BPMSupreme, Buyed, FreeReleases und Jahreszahlen werden nicht als zusätzliche Exportebenen übernommen. In der Vorschau sind alle Zielordner bearbeitbar; eigene Zuordnungen werden gespeichert.

Bei `Visions (Original Mix)` widersprechen sich beispielsweise das Genre `Melodic House & Techno` und der Quellzweig `Beatport/Full/OnPsytrance`. Der Vorschlag orientiert sich am erkannten Psy-Trance-Ordner, weist aber auf die Unsicherheit hin. Das solltest du durch Anhören entscheiden. Ein Ordnername ist ebenso wenig ein musikalischer Beweis wie ein historischer Genre-Tag.

## Was ich zusätzlich verbessern würde

1. **Ein Hauptgenre pro Track festlegen.** Das bestimmt den physischen Ablageort. Andere sinnvolle Zuordnungen bleiben Mehrfachmitgliedschaften in Playlisten. Damit braucht ein Track keine Dateikopie pro Set, Vibe oder Genre-Überschneidung.
2. **Drei getrennte Entscheidungen treffen:** Genre/Untergenre, Energie und Charakter. Beispielsweise `Techno/Hard Techno`, Energie 4/5 und Charakter `dark, rolling`. Energie und Vibe gehören in zusätzliche Metadaten bzw. Playlisten; Genre-Ordner sollen beim Browsen verlässlich bleiben. Diese Werte müssten von dir vergeben werden, sie werden hier nicht automatisch behauptet.
3. **Extended und Intro unabhängig führen.** Ein Extended Mix kann melodisch starten, ein kurzer Track kann einen brauchbaren Drum-Intro haben. Keine separaten Dateikopien in `Extended`, `Intro` oder `Banger`-Ordnern. Deine vorhandenen Checkboxen bleiben dafür unabhängig.
4. **Shop und Bezugsjahr als Herkunft aufheben.** Sie sind nützlich für Recherche, sollen aber nicht die Auswahl im Club steuern. Der Exportbericht enthält weiterhin den Quellpfad mit dieser Herkunft. Optional könnten später Kommentar-/Quellenfelder dafür genutzt werden.
5. **Genre-Playlisten und Set-Playlisten trennen.** Sinnvolle obere Gruppen wären `Genres`, `Energie & Vibes`, `Prepared Sets`, `Played Sets` und `Tools`. `Nicht einsortiert` bleibt dein Eingang für Club-markierte Tracks ohne Club-Playlist. Alte Genre-Bäume zunächst archivieren, statt ihre Inhalte ungeprüft zusammenzuwerfen.
6. **Genre-Tags der Exportkopien später gezielt angleichen.** Der aktuelle Export kopiert die Audiodateien unverändert. Saubere Ordner bedeuten deshalb noch keine bereinigte Genre-Spalte in jeder anderen DJ-App. Eine spätere, ausdrückliche Funktion könnte nur die Exportkopien nach der geprüften Zuordnung taggen. Deine Originale sollten dabei weiter unangetastet bleiben.
7. **`00 Genre prüfen` vor einem Club-Einsatz abarbeiten.** Auf dem Hauptrechner darf das der Klärungsbereich sein; eine vorbereitete Club-Auswahl sollte dort möglichst keine ungeklärten Tracks mehr haben. Dateigröße und Club-Häkchen ersetzen keine Hörprüfung von Mastering, Arrangement oder Einsatzbereich.

Der Nutzen der flachen Genre-Ordner ist unabhängig von Traktor: Mixxx kann beispielsweise über „Computer“ direkt in Laufwerken und Ordnern browsen und Tracks laden. Metadaten und Playlisten sind dort zusätzliche Wege, kein Ersatz für eine verständliche physische Ablage. Quelle: [Mixxx Library-Handbuch](https://manual.mixxx.org/2.5/en/chapters/library).

## Was der Export jetzt macht

- Standardziel ist `C:\Users\DJBaka\Music\ClubCollection`; der Dialog erlaubt einen anderen Pfad und merkt ihn sich.
- Die Vorschau zeigt Club-Tracks, bekannte Dateigröße, fehlende Quelldateien, Genre-Ziele und Hinweise auf widersprüchliche Zuordnungen.
- Audiodateien werden in Originalqualität kopiert, mit lesbaren Dateinamen aus Artist und Titel. Gleiche Namen bekommen einen eindeutigen Zusatz; vorhandene Dateien werden nicht still überschrieben. Traktor-Stems behalten ihre `.stem.mp4`-Endung.
- Die NML liegt direkt im gewählten Hauptordner. Collection- und Playlist-Verweise zeigen auf die Kopien. Club-Hierarchie und Cues bleiben erhalten; übersprungene Dateien werden nicht als vorhandene Exportdateien ausgegeben.
- Nicht erreichbare Quellen stoppen den Export zunächst. Nur nach ausdrücklicher Auswahl dürfen sie übersprungen werden; der Bericht kennzeichnet das Ergebnis dann als unvollständig.
- Fortschritt und Abbruch sind verfügbar. Angefangene Audiodateien bleiben nicht als fertige Kopien liegen. Die NML wird nach den Dateikopien atomar veröffentlicht.
- Ein Manifest speichert Größe, Herkunft und SHA-256 der Kopien. Bei erneutem Export werden passende Kopien geprüft und wiederverwendet. Andere und ältere Dateien werden nicht automatisch gelöscht.
- Der Bericht enthält Quelle, Ziel, Genre-Zuordnung und lokale Extended-/Intro-Markierungen. Diese lokalen Checkboxen sind dadurch dokumentiert, aber keine universellen Audio-Tags anderer DJ-Anwendungen.

Die NML enthält Pfade des gewählten Exportziels. Nach dem Kopieren des gesamten Ordners auf einen Rechner mit anderem Laufwerksbuchstaben oder Benutzerpfad kann in Traktor **Relocate** nötig sein. Native Instruments beschreibt dafür das Auswählen des neuen obersten Musikordners. Quelle: [Traktor: Managing Your Track Collection](https://docs.native-instruments.com/ni-tech-manuals/traktor-pro-manual/en/managing-your-track-collection).

Ordner und Audiodateien lassen sich in anderen DJ-Anwendungen verwenden, soweit diese die jeweiligen Audioformate unterstützen. Die NML ist kein universelles Austauschformat für sämtliche DJ-Programme; eine automatische Konvertierung in jedes Fremdformat ist nicht Teil dieses Exports.
