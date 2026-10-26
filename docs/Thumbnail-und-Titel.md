# Thumbnail- und Titelgenerator

Im **Set Recorder → Thumbnail & Titel erstellen …**, oder direkt unten in **History + Aufnahme → Thumbnail & Titel …**. Der History-Bereich übernimmt die ausgewählte Aufnahme und die aktuelle Tracklist. Die Aufnahme muss nicht mit Cratebase aufgenommen worden sein.

1. Video auswählen (MP4, MKV, MOV und weitere durch FFmpeg unterstützte Videoformate).
2. **Gute Frames suchen** extrahiert neun Frames über die Aufnahme verteilt. Dunkle/überbelichtete und detailarme Bilder werden schlechter bewertet. Das bestbewertete Frame wird gewählt; andere Kandidaten lassen sich anklicken. Über **Zeit in Sekunden → Frame holen** kann eine beliebige eigene Stelle gewählt werden.
3. Thema, Genres, Stimmung und gewünschte Sprache beschreiben. **Je nach Set unterschiedlich** ist der Standardstil; feste Stile und ein optionaler Kanal-/DJ-Name sind ebenfalls möglich.
4. **Lokale KI: Titel & Gestaltung** erstellt bis zu fünf Titelvarianten, eine kurze Thumbnail-Zeile, Unterzeile, Akzentfarbe und Textseite. Titel, Texte, Farben, Frame und Textseite bleiben bearbeitbar. Zu dunkle Akzentfarben werden für lesbare Beschriftungen ersetzt.
5. Den Textblock direkt mit der Maus in der Vorschau verschieben oder **Links ↔ Rechts / Oben ↔ Unten** verwenden. Auch bei verkleinerter Vorschau bleiben die Positionen auf die PNG-Größe bezogen. Der Text bleibt innerhalb eines Sicherheitsabstands zum Bildrand; **Position zurücksetzen** stellt die gewählte Textseite und vertikale Mitte wieder her. Größenänderungen berücksichtigen die verfügbare Bildhöhe.
6. **Thumbnail als PNG speichern …** exportiert genau die Vorschau in 1280 × 720. Zusätzlich werden der ausgewählte Titel samt Alternativen als Text sowie Gestaltungsdaten einschließlich Originalquelle, Frame-Zeit, Textposition, Schrift-/Effekteinstellungen und verwendetem Bild in separaten, datierten Begleitdateien gespeichert. **Titel kopieren** kopiert den bearbeiteten Titel.

## Manuelle Gestaltung

Sechs Vorlagen stehen als Ausgangspunkt bereit: **Neon Club**, **Industrial / Techno**, **Emotional / Atmospheric**, **Hands Up / Festival**, **Minimal / Foto** und **Retro / VHS**. Danach bleibt alles veränderbar:

- **Schrift & Skalierung:** installierte Schriftarten, Schriftgewicht, Kursiv, Schriftfarbe, getrennte Größen für Headline, Untertitel und Kanalname, gemeinsame Skalierung und Abstand.
- **Effekte, Rahmen & Hintergrund:** Schatten, Neon-Leuchten, Kontur oder 3D-Text; Effektstärke; Linie, Neon-Rahmen, Ecken oder Letterbox; Verlauf, Textbox, Band oder transparenter Texthintergrund mit einstellbarer Abdunklung.
- **Bild bearbeiten & zuschneiden:** eigenes Bild laden, Zoom und horizontale/vertikale Position des Bildausschnitts direkt in der Vorschau; Helligkeit, Kontrast, Sättigung und Gamma über **Bildanpassungen anwenden**. Bildkorrekturen erzeugen eine neue private Datei und beginnen bei erneutem Anwenden vom gewählten Ausgangsbild, damit sich dieselbe Korrektur nicht unbemerkt aufsummiert.

Die vorhandene lokale Qwen-KI erzeugt weiterhin Titel und Gestaltungsvorgaben. Die automatische Frame-Wahl ist eine technische Vorauswahl, keine Garantie für den besten Gesichtsausdruck oder die beste Bildkomposition. Die Vorschau erlaubt deshalb immer eine eigene Auswahl. KI-Titel vor Verwendung auf sachliche Richtigkeit prüfen.

## Echte lokale Bild-KI

Unter **Lokale Bild-KI · Frame umgestalten** ist zusätzlich eine getrennte Bild-zu-Bild-KI integriert. **Bild-KI installieren (ca. 2,2 GB)** lädt eine feste, SHA-256-geprüfte Version von [stable-diffusion.cpp](https://github.com/leejet/stable-diffusion.cpp/releases/tag/master-1015-e41fbcf) und das [SD-1.5-FP16-Modell](https://huggingface.co/Comfy-Org/stable-diffusion-v1-5-archive/tree/9cfd069101959ca3828bf9c04a4419870832b74f) in `%LOCALAPPDATA%\Cratebase\artwork-ai`. Keine separate Python-, Ollama- oder ComfyUI-Installation. Unter Windows wird Vulkan für die GPU genutzt, alternativ CPU. Lizenztexte (MIT für die Laufzeit, CreativeML Open RAIL-M für das Modell) werden mit abgelegt.

1. Frame oder eigenes Bild auswählen und bei Bedarf manuell korrigieren.
2. Gewünschte Bildgestaltung beschreiben; Englisch ist für dieses Modell empfohlen, etwa `cinematic nightclub photography, violet neon lights, atmospheric lighting`.
3. Veränderungsstärke einstellen (Standard 25 %). Niedrige Werte erhalten mehr vom Frame; hohe Werte können Gesichter, Hände, Geräte und andere Details verändern. Es handelt sich nicht um eine gesichtsidentitätserhaltende Retusche.
4. Seed festlegen (`-1` für zufällig) und **KI-Bildvariante generieren**. Die KI arbeitet auf dem Bild ohne die darübergelegten Thumbnail-Texte. Die Texte werden anschließend weiter als bearbeitbare Ebenen darübergelegt.
5. Die fertige Variante erscheint als zusätzliches, mit **KI** beschriftetes Bild in der Auswahl. Original und Varianten können verglichen werden; **Originalframe wiederherstellen** kehrt zum Original zurück.

Die lokale Bild-KI läuft mit 768 × 432 und 24 Schritten; der Editor komponiert daraus weiterhin ein PNG in 1280 × 720. Das kompakte SD-1.5-Basismodell bietet einfache stilistische Varianten, keine Garantie für professionelle Retusche oder fehlerfreie Details. Der GPU-Test auf der RTX 3060 Ti dauerte ungefähr 33 Sekunden einschließlich Laden; andere Treiber, CPU-Modus und parallel belegter Grafikspeicher können abweichen. Vor dem Bildlauf wird das eigene Textmodell des Thumbnail-Editors gestoppt, um Grafikspeicher freizugeben. Ein Abbruch oder Schließen beendet den zugehörigen Bild-KI-Prozess.

Es wird die vorhandene, von Cratebase verwaltete lokale KI unter `%LOCALAPPDATA%\Cratebase\ai` wiederverwendet. Bei Bedarf richtet **KI installieren / prüfen** dieselbe Laufzeit und dasselbe Modell wie im Set-Planer ein (erstmalig etwa 2,5 GB Modelldownload plus Laufzeit). Keine separate Ollama-Installation und kein Cloud-Upload. Bei belegter GPU lässt sich der CPU-Modus einschalten. **Abbrechen** beendet den laufenden Auftrag; Schließen beendet die zugehörige KI und FFmpeg-Prozesse.

Videoframes und manuelle Bildkorrekturen liegen ausschließlich im privaten Cache `%LOCALAPPDATA%\Cratebase\artwork`; Bild-KI-Varianten und Laufzeitprotokolle unter `artwork-ai\jobs`. Originalvideos und Traktor-Dateien werden nicht verändert. Lange Videos werden an neun Stellen gezielt angesprungen, statt sie für die Frame-Suche vollständig zu dekodieren. Cache und Exportdateien sind voneinander getrennt. Bilder und Tracklists werden für die KI-Verarbeitung nicht hochgeladen.

Prüfung: `dotnet run --project tests/Cratebase.IntegrationTests -c Release -- --artwork-checks` testet tatsächliche FFmpeg-Extraktion, Frame-Bewertung, Quellenintegrität, PNG-Rendering, Positionierung/Drag bei skalierter Vorschau, Effekte, Bildkorrekturen und den UI-Ablauf. Mit `--real-ai` wird zusätzlich die bereits installierte Titel-KI einschließlich der echten UI-Aktion geprüft; `--real-image-ai` prüft Bildgenerierung und Wiederherstellen über die Oberfläche. `--video "C:\Pfad\Aufnahme.mp4"` verwendet eine vorhandene Aufnahme lesend.

Technische Grundlage für gezielte Frame-Extraktion und Skalierung: [FFmpeg-Filterdokumentation](https://ffmpeg.org/ffmpeg-filters.html).
