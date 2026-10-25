# Cratebase auf Android – erster nativer Prototyp

Die Desktop-App bleibt der Server und muss laufen. Android verwendet dieselbe SQLite-Bibliothek über eine authentifizierte HTTP-API. Es gibt keine zweite Collection, keinen Traktor-Abgleich auf dem Handy und keinen Cloud-Dienst.

## Wiedergabegerät und Desktop-Fernsteuerung (Android 0.1.6)

Im Player über **Ausgabe: Dieses Handy / Desktop** das Ziel auswählen:

- **Desktop · laufenden PC-Player fernsteuern** verbindet sich mit der aktuellen Desktop-Wiedergabe. Der Handy-Player pausiert, die vorhandene PC-Queue bleibt bestehen. Auf dem Handy einen Song auswählen startet ihn anschließend auf dem PC.
- **Desktop · aktuellen Handy-Song übernehmen** überträgt Song, Queue-Ausschnitt, Position und Play/Pause-Zustand zum PC. Erst nach erfolgreichem Laden pausiert die Handy-Wiedergabe.
- **Dieses Handy · hier weiterhören** übernimmt den aktuellen Desktop-Song und seine Position als Stream. Der PC pausiert erst, wenn der Handy-Stream bereit ist. Schlägt der Stream fehl, wird der PC nicht automatisch pausiert.
- **Desktop-Lautstärke** stellt die Lautstärke des Cratebase-PC-Players ein.

Im Desktop-Modus steuern Play/Pause, Vor/Zurück und Positionsregler den bestehenden Desktop-Player. Titel, Tags, Wiedergabestatus und Position werden etwa alle 1,5 Sekunden abgefragt; die vollständigen Queue-Metadaten werden nur beim Gerätewechsel angefordert, um Datenvolumen zu sparen. Club/Extended/Intro/Trash und Favoriten beziehen sich dabei auf den angezeigten Desktop-Track. Änderungen direkt am Desktop erscheinen entsprechend auf dem Handy. Widget-Aktionen und Android-Medientasten werden ebenfalls zum ausgewählten Ziel geleitet. Das Handy streamt im Desktop-Modus keine Audiodatei und startet bei Verbindungsverlust keinen lokalen Ersatzstream. Bei ausdrücklich gewähltem Wechsel zum Handy bleibt der lokale Modus auch dann auswählbar, wenn der PC nicht erreichbar ist; eine Übernahme des PC-Tracks ist in diesem Fall nicht möglich.

Der aktuelle Desktop-Build mit aktiviertem Server ist erforderlich; dieselbe Server-Adresse und derselbe Schlüssel werden verwendet, auch über Internet. Es gibt hier zwei Ziele: dieses Handy und den zugehörigen Cratebase-Desktop, keine Spotify-/Cast-Geräteerkennung und kein synchrones Multiroom-Audio. Geräteübergaben sind nicht samplegenau; ein noch nicht gecachter Handy-Stream kann kurz puffern. Übertragen werden bis zu 250 Queue-Einträge um den aktuellen Song, während der Desktop beim bloßen Fernsteuern seine eigene vollständige Queue behält. Die Android-App merkt sich das gewählte Ziel. Das Schließen der Fernbedienung beendet die Desktop-Wiedergabe nicht.

Automatisierte Prüfung: `--remote-playback-checks` testet die echte Desktop-Bridge mit Headless-Oberfläche und Test-Audioengine; `--server-checks` prüft die authentifizierten Player-Endpunkte. Android testet Queue-/Positionsübergabe und die MediaSession-Abbildung des Desktop-Zustands. Der Gerätewechsel mit echtem PC-Audio und gesperrtem Handy muss zusätzlich praktisch geprüft werden.

Ab Android **0.1.5** und dem dazugehörigen Desktop-Build gibt es die zusätzliche Markierung **Trash**. Sie erscheint als Checkbox in den Desktop-Tracklisten, als Player-Taste und in der Mehrfachauswahl auf beiden Geräten. „Trash = Ja“ sammelt schlechte Tracks, „Trash = Nein“ blendet sie per Filter aus. Die Markierung bleibt unabhängig von Club/Extended/Intro und löscht, verschiebt oder überspringt keine Dateien automatisch. Sie wird in Cratebases Datenbank gespeichert, bei Scans erhalten und bei Cratebase-Dateikopien übernommen. Die Desktop-App muss für die additive Datenbankmigration und den erweiterten Server neu gestartet werden. Widget und Sperrbildschirm behalten ihre bisherigen sechs Aktionen.

## Start auf diesem PC

1. Desktop-App mit dem neuen Build starten. Oben **Handy-Server** öffnen.
2. Für einen ersten Test im eigenen WLAN **Im LAN lauschen** aktivieren. Standardport ist **5874**. **Speichern & starten** anklicken. Windows-Firewall muss eingehende Verbindungen auf diesem Port im privaten Netz zulassen.
3. Die APK unter `android/app/build/outputs/apk/debug/app-debug.apk` aufs Android-Handy übertragen und installieren. Es ist ein lokal signierter Entwicklungsbuild, kein Play-Store-Release. Mindestversion: Android 8.
4. In der App die Adresse des PCs eintragen, beispielsweise `http://192.168.178.20:5874`. Für diesen lokalen Test **HTTP im eigenen LAN erlauben** auswählen. Den Zugriffsschlüssel aus dem Desktop-Dialog kopieren und eintragen.
5. Benachrichtigungen erlauben, damit Player und Tag-Aktionen auf dem Sperrbildschirm erscheinen können.

Ab Android-Version 0.1.1 besitzt der Verbindungsdialog ein eigenes **Server-Port**-Feld. HTTPS ist nicht auf 443 beschränkt: beispielsweise `https://musik.deine-domain.de` plus Port `8443` ergibt `https://musik.deine-domain.de:8443`. Der Port muss zum HTTPS-Listener oder Reverse Proxy passen. Ein Cratebase-Server ohne PFX-Zertifikat spricht auf seinem eingestellten Port HTTP; im eigenen WLAN dann beispielsweise `http://192.168.178.45` plus Port `5874` und LAN-Freigabe verwenden. Für HTTPS über die Domain zuerst TLS am Server oder Proxy einrichten. „Verbinden“ prüft jetzt Anmeldung und Collection einschließlich Song-Anzahl, bevor die Einstellungen übernommen werden; bloßes Speichern gilt nicht mehr als erfolgreiche Verbindung.

Der Server ist standardmäßig aus und auf localhost beschränkt. Der Dialog bietet Port, LAN/IPv4/IPv6, Autostart mit Cratebase, Zugriffsschlüssel, FFmpeg-Pfad, Cache-Budget sowie optional ein PFX-Zertifikat. Ein neuer Schlüssel wird erst beim erneuten Start wirksam. Ein PFX-Passwort bleibt im Arbeitsspeicher; für Autostart mit passwortgeschütztem Zertifikat kann `CRATEBASE_TLS_PASSWORD` gesetzt werden.

Auf diesem PC liegt ein anhand der veröffentlichten SHA-256-Prüfsumme geprüfter FFmpeg-Build mit MP3-Encoder unter `%LOCALAPPDATA%\Cratebase\tools\ffmpeg.exe`. Neue Server-Einstellungen erkennen diesen automatisch. Auf einem anderen PC FFmpeg mit **libmp3lame** installieren und den Pfad im Dialog einstellen; alternativ `CRATEBASE_FFMPEG_PATH`. Originaldateien werden nie zum Streamen verändert.

## Unterwegs mit deiner Domain

HTTPS ist die Voreinstellung. Ab Android-Version **0.1.2** kann unter Verbindung & Streaming der separate Schalter **HTTP über Internet erlauben (unverschlüsselt)** aktiviert werden. Damit sind auch Domains und öffentliche IP-Adressen mit `http://` und frei gewähltem Port erlaubt. Für deinen aktuellen Server beispielsweise `http://devbaka-home.duckdns.org` plus Port `5874`. Beim Aktivieren wechselt eine eingetragene HTTPS-Adresse sichtbar zu HTTP; gespeichert wird die Freigabe erst nach erfolgreicher Verbindungsprüfung. Der vorhandene Zugriffsschlüssel bleibt erhalten. Bei HTTP werden Schlüssel, Musik und Änderungen unverschlüsselt übertragen. Lokales HTTP besitzt weiterhin eine separate LAN-Freigabe. Es gibt keinen automatischen HTTP-Rückfall nach TLS-Fehlern.

Zugriffsschlüssel werden auf dem Gerät im Android Keystore verschlüsselt gespeichert, als Bearer-Header gesendet und nicht in Stream-URLs geschrieben. Automatische HTTP-Weiterleitungen bleiben deaktiviert.

Am einfachsten endet HTTPS an einem Reverse Proxy wie Caddy auf demselben PC. Cratebase lauscht dann weiter auf **localhost:5874**, ohne LAN-Häkchen. Beispiel für eine eigene, durch deine echte Domain zu ersetzende Caddy-Konfiguration:

```caddyfile
musik.example.org {
    reverse_proxy 127.0.0.1:5874
}
```

DNS-A/AAAA-Einträge müssen auf deinen Anschluss zeigen. IPv4-Portfreigaben und gegebenenfalls IPv6-Firewallfreigaben für den Proxy einrichten. Bei automatischen öffentlichen Zertifikaten des Proxys dessen Anforderungen an Erreichbarkeit beachten. In der Android-App anschließend `https://musik.deine-domain.de` eintragen. Cratebases unverschlüsselten Backend-Port dabei nicht öffentlich freigeben. Alternativ kann Cratebase selbst ein gültiges PFX-Zertifikat laden; dann den konfigurierten HTTPS-Port erreichen lassen.

Domain, DNS, Router und Firewall wurden durch diese Implementierung nicht verändert. Der Zugriffsschlüssel berechtigt zum Bearbeiten der Bibliothek; nur auf eigenen Geräten verwenden. Schlüsselwechsel entzieht bisherigen Geräten den Zugriff. Noch keine getrennten Benutzer, Geräte-Pairing, Offline-Downloads oder Internet-Abnahme.

## Streaming

| Profil | Ungefährer Verbrauch pro Stunde ohne Netzwerk-Overhead |
|---|---:|
| MP3 96 kbit/s | 43 MB |
| MP3 160 kbit/s | 72 MB |
| MP3 256 kbit/s | 115 MB |
| MP3 320 kbit/s | 144 MB |

Standard: **160 kbit/s für Mobilfunk/getaktete Verbindungen**, **256 kbit/s für WLAN**. Beide sind unabhängig einstellbar. Bereits geladene Songs behalten ihr Profil; eine neue Wiedergabe übernimmt die aktuellen Einstellungen. Bei automatisch nachgeladenen Songs wird das dann aktive Netz berücksichtigt. Kein stiller Rückfall auf FLAC/Originalqualität.

Der Server erzeugt zunächst eine vollständige komprimierte Ableitung, anschließend unterstützt er Range-Anfragen zum Springen. Der erste Start eines noch nicht gecachten Songs kann deshalb kurz dauern. Ein einzelner Encoder vermeidet CPU-Überlastung. Cache: standardmäßig 2048 MB, automatisch werden alte Kopien entfernt; Dateien liegen unter Cratebases eigenem `stream-cache`. Temporäre Kopien werden bei Abbruch aufgeräumt. Auch die Größe einer einzelnen laufenden Konvertierung ist begrenzt. Der Server benötigt Zugriff auf die Originaldatei. Fehlende Dateien werden wie am Desktop markiert und im Handy-Player übersprungen.

## Bedienung

- **Bibliothek**: Collection, Club Collection mit Nicht einsortiert, Playlist-Gruppen und freigegebene Bibliotheksordner. Gruppen/Ordner werden schrittweise geöffnet. Playlisten und Untergruppen lassen sich anlegen, umbenennen, verschieben und nach Bestätigung löschen.
- **Suchen/Filter**: Ab Android 0.1.4 mit Schnellauswahl für BPM, Genre, Club, Extended, Intro, Format, Bitrate und Key. Alle Desktop-Felder stehen zusätzlich im Feldmenü bereit, auch vor vollständigem Laden des Serverkatalogs. Zahlen bieten passende Vergleiche und inklusive Bereiche (z. B. BPM 130–150), Tags eine Ja-/Nein-Auswahl und Formate Vorschläge wie FLAC/MP3/WAV. Genre-/Key-Vorschläge stammen aus den gerade geladenen Songs; eigene Werte sind möglich. Mit **+ Filter hinzufügen** mehrere UND-verknüpfte Bedingungen anlegen und anschließend **Anwenden**. Aktive Bedingungen sind einzeln entfernbar; Abbrechen verwirft den Entwurf. Filter, Suchfeld und Sortierung bleiben nach App-Neustart erhalten. Bitrate meint die Originaldatei, nicht die Streaming-Qualität. Datumsformat beispielsweise `2026-10-24`.
- Song antippen startet die Wiedergabe. Gedrückt halten aktiviert Mehrfachauswahl; **⋮** bietet gemeinsame Markierungen, Playlist-Zuordnung und Entfernen aus einer Playlist. Metadaten lassen sich einzeln bearbeiten. Es wird in Cratebases Datenbank geschrieben, nicht in die Audiodateien oder Traktor-NML.
- **Favoriten** aufklappen und Profil auswählen. Slot antippen sortiert den **aktuellen Handy-Player-Track** ins zugeordnete Ziel ein. Playlist-Slots erzeugen Referenzen, Ordner-Slots sichere Originalkopien mit eindeutigen Namen. Profile und Slot-Ziele werden in dieser Version am Desktop eingerichtet.
- Player unten: Play/Pause, Vor/Zurück, Positionsregler sowie **Club, Extended, Intro**. Navigation unterbricht die Wiedergabe nicht. Weitere Tracks einer langen Quelle werden vor dem Listenende in 100er-Seiten nachgeladen. Eine begrenzte letzte Wiedergabeliste samt Position wird zum Fortsetzen nach Prozessneustart gespeichert.
- Änderungen vom Handy aktualisieren offene Desktop-Listen und die Desktop-Player-Markierungen. Das Handy prüft während der geöffneten App auf Datenbankänderungen; aktuelle Player-Markierungen werden auch im Hintergrund regelmäßig aktualisiert. Nach einer Änderung an Favoritenprofilen am Desktop gegebenenfalls **↻** verwenden.

## Widget und Sperrbildschirm

Auf dem Android-Homescreen **Widgets → Cratebase** hinzufügen und über die Launcher-Griffe vergrößern. Das Widget lässt sich horizontal und vertikal bis zur vom Launcher erlaubten Größe ziehen und enthält alle sechs Aktionen plus Titel, Artist und Speicherrückmeldung.

Androids normale Mediensteuerung hat höchstens fünf Schaltflächenplätze. Deshalb gibt es zusätzlich zur MediaSession mit Play/Pause/Vor/Zurück eine eigene öffentliche Benachrichtigung mit **Club / Extended / Intro**. Diese Aktionen verlangen in der App kein Entsperren. Ob sie direkt oder erst nach Aufklappen sichtbar sind, hängt von Android, Hersteller, Benachrichtigungsrechten und Sperrbildschirm-Privatsphäre ab. Ausgeblendete Sperrbildschirm-Inhalte können von einer App nicht erzwungen werden. Siehe [Android-Mediensteuerung](https://developer.android.com/media/implement/surfaces/mobile), [Hintergrundwiedergabe](https://developer.android.com/media/media3/session/background-playback) und [anpassbare Widgets](https://developer.android.com/develop/ui/views/appwidgets/layouts).

## Build und Prüfung

Desktop: `dotnet build tests/Cratebase.IntegrationTests -c Release`.

Android: JDK 17 oder neuer, Android SDK 35 und Build Tools 36.0.0; `JAVA_HOME` und `ANDROID_HOME` setzen, dann im Ordner `android`:

```powershell
.\gradlew.bat assembleDebug testDebugUnitTest lintDebug
```

Server-Integrationstests: `dotnet run --project tests/Cratebase.IntegrationTests -c Release -- --server-checks`. Für einen expliziten Encoder `CRATEBASE_TEST_FFMPEG` auf den vollständigen Pfad setzen. Tests verwenden ausschließlich temporäre Datenbanken und Testaudio.

Geprüft werden Authentifizierung, paginierte Abfragen, Pfadgrenzen, Tags, Metadaten-Patches ohne Überschreiben gleichzeitiger Flag-Änderungen, Datenbankänderungserkennung, Slots mit Playlist und Datei-Kopie, fehlende Dateien, alle Bitraten, Range-Seeking, unveränderte Originale, Neustart, Dual Stack und PFX-HTTPS. Android prüft die getrennte LAN-/Internet-HTTP-Freigabe, HTTPS mit beliebigen Ports, unzulässige Protokolle und die Verbindungsprüfung vor dem Speichern. Hinzu kommen simulierte Start-/Player-/Widget-Tests mit Robolectric; Lint läuft ohne Fehler.

Zusätzlich wurde die APK auf dem angeschlossenen Android-15-Handy (Modell 23049PCD8G) installiert. Ein echter Instrumentation-Test mit isoliertem USB-Testserver prüfte Keystore-Verschlüsselung und Schlüssel-Roundtrip, Anmeldung, Bibliotheksnavigation über die App-Oberfläche, tatsächliches MP3-Playback, alle drei Markierungen mit Rücklesen vom Server, Favoritenzuordnung, Metadatenänderung, Widget-Layout und die Benachrichtigung mit drei Tag-Aktionen. Der Test war erfolgreich. Anschließend wurden Test-App, USB-Portumleitung und Testverbindung entfernt; die eigentliche App bleibt installiert. Für Audio-Fokus/Headset, Netzwechsel, Akku-Verhalten, Widget-Platzierung im Launcher und die tatsächliche Sichtbarkeit/Bedienung am gesperrten Bildschirm steht die praktische Abnahme noch aus.

Der Hardwaretest lässt sich gegen `--mobile-device-fixture` mit `adb reverse tcp:5875 tcp:5875` und dem Instrumentation-Test `DeviceWorkflowTest` wiederholen. Er ist ausdrücklich für eine temporäre Testbibliothek vorgesehen, setzt dafür vorübergehend die App-Verbindung und räumt sie anschließend auf. Nicht gegen die eigene Collection ausführen.

Orientierung für die Profilwahl: [Spotify-Audioqualität](https://support.spotify.com/au/article/audio-quality/) und [Tidal-Qualitätsstufen](https://support.tidal.com/hc/en-us/articles/17412130162961-HiRes-FLAC-audio). Cratebase verwendet hier MP3-CBR statt deren konkreter Codec-/Streaming-Infrastruktur. FFmpeg-Build und Prüfsummen: [Gyan FFmpeg Builds](https://www.gyan.dev/ffmpeg/builds/).
