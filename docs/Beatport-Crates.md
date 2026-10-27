# Beatport Crates – eigenständiger Avalonia-Prototyp

Start unter Windows: `start-beatport.bat`. Alternativ:

```powershell
dotnet run --project src/Cratebase.Beatport.App -c Release
```

**Demo laden** funktioniert ohne Konto: Vier ausdrücklich fiktive Veröffentlichungen werden zu drei Tracks zusammengeführt. Ein anderer Mix bleibt erhalten. Über **Angehört** lässt sich die hellgrüne Markierung testen; ein Demo-Track ist dunkelgrün als Playlist-Mitglied markiert. Demo schreibt nichts zu Beatport und enthält keine Hörproben.

## Implementierter Workflow

1. Beatport-Benutzername oder E-Mail und Passwort eingeben, dann **Anmelden**. Ein API-Token muss nicht selbst beschafft werden. Die App liest nach der Anmeldung eigene Playlists und deren Tracks vollständig ein.
2. Subgenre oder Artist auswählen. Genre-/Subgenre-Auswahl wird über die API geladen; alternativ können bekannte IDs aus Beatports Filter-URL eingetragen werden. Artist-Suche liefert maximal 100 Treffer; bei Bedarf genauer suchen.
3. Alle Katalogseiten sammeln. Extended- und Streaming-Filter sind standardmäßig aktiv, können aber ausgeschaltet werden. Extended bedeutet ausdrücklich „Extended“ im Mix-Namen, keine aus der Laufzeit geschätzte Klassifizierung. Fehlende Streaming-Angaben werden bei aktivem Streaming-Filter ausgeschlossen.
4. Vorschau mit bestehender oder neuer Playlist abgleichen. Bereits vorhandene Aufnahmen werden übersprungen. Änderungen werden in Batches von 50 Tracks geschrieben, ohne automatischen Schreib-Retry. Bei Abbruch können vorherige Batches bereits gespeichert sein; erneuter Abgleich liest die Playlist neu ein.
5. Alternativ bestehendes Ziel auf Duplikate prüfen. Vorschau zeigt die zu entfernenden Einträge. Checkbox aktivieren, dann entfernen. Die erste Version bleibt; die Playlist selbst wird nicht gelöscht.

Hellgrün bedeutet angehört, dunkelgrün bedeutet Mitglied einer eigenen Playlist und hat Vorrang. Hörproben laufen über LibVLC; der Status wird erst beim Playing-Ereignis gesetzt. Manuelles Markieren ist zusätzlich möglich. Hörstatus liegt unter `%LOCALAPPDATA%\Cratebase.Beatport\heard.json`; Tokens werden dort nicht gespeichert. Playlist-Mitgliedschaften werden beim Verbinden aktualisiert. Aktivitäten auf Beatport oder in Traktor werden nicht automatisch überwacht.

## Wichtige Grenze: Live-Anbindung noch nicht bestätigt

Die App implementiert jetzt **Benutzername-/Passwort-Login mit Cookie-Sitzung, OAuth-Code-Austausch und Token-Refresh**. Die öffentliche Client-Konfiguration wird direkt aus Beatports API-Dokumentation geladen; deren Erreichbarkeit und das aktuelle Konfigurationsformat wurden geprüft. Der Ablauf orientiert sich an einer [öffentlichen Login-Implementierung](https://github.com/evgenygurin/beatport-mcp/blob/main/src/beatport_mcp/auth.py). Dies ist keine zugesicherte öffentliche Drittanbieter-Login-Schnittstelle und kann sich ändern.

Das Passwort geht im HTTPS-Anfragekörper ausschließlich an `api.beatport.com`. Das Eingabefeld wird nach jedem Anmeldeversuch geleert. Passwort, Session-Cookies, Access- und Refresh-Token werden nicht auf Datenträger geschrieben oder protokolliert. Tokens bleiben nur bis zum Abmelden/Beenden im Arbeitsspeicher. Ohne Passwortspeicherung kann die Sitzung automatisch erneuert werden; wird auch der Refresh-Token ungültig, ist eine neue Anmeldung nötig. Bei einer abgewiesenen Leseanfrage wird einmal nach Erneuerung wiederholt; Schreibanfragen werden nicht automatisch wiederholt. **Abmelden** verwirft die lokale Sitzung; es ist keine serverseitige globale Abmeldung aller Geräte.

Ein echter Account-Login ist noch nicht durchgeführt worden. Mit einem echten Konto sind Login, mögliche zusätzliche Bestätigungen, Katalogantworten, Subgenre-Endpunkt/-Felder, Playlist-Schreibformat, Playlist-Größenlimits und Streaming-Verfügbarkeit noch zu prüfen. Falls Beatport eine Browser-Bestätigung, CAPTCHA oder MFA verlangt, meldet die App die Einschränkung; ein eingebetteter Browser-Login ist noch nicht vorhanden. Die UI zeigt API-Fehler; bei nicht verifizierbarem Subgenre wird die Sammlung verworfen. Es wird keine erfolgreiche Live-Verbindung oder Traktor-Synchronisierung behauptet.

Der API-Adapter orientiert sich an den [Beatport-v4-Dokumenten](https://api.beatport.com/v4/docs/) und einer [öffentlichen Client-Implementierung](https://github.com/evgenygurin/beatport-mcp/blob/main/src/beatport_mcp/server.py). Der Subgenre-Endpunkt ist eine noch live zu verifizierende Annahme. Die [Beatport-Streaming-Seite](https://stream.beatport.com/) beschreibt den Playlist-Zugriff in Traktor. Die App erzeugt keine lokale NML als Ersatz für eine echte Beatport-Streaming-Playlist und bewertet nicht, ob ein Song musikalisch gut ist.

## Duplikatregeln

- Gleiche Beatport-Track-ID: Duplikat.
- Gleicher Mix und gleiche nichtleere ISRC: Duplikat, auch über Releases hinweg.
- Sonst: normalisierte Artists, Titel und Mix müssen übereinstimmen; bekannte Laufzeiten dürfen maximal zwei Sekunden abweichen.
- Andere Mix-Namen bleiben getrennt, selbst bei identischer ISRC. Unklare Fälle bleiben erhalten. Fehlende ISRC/Laufzeit kann bedeuten, dass echte Duplikate nicht erkannt werden.

## Vorbereitung für spätere Cratebase-Module

`Cratebase.Beatport` enthält Modelle, API-Adapter, Duplikatlogik und Hörhistorie ohne Avalonia- oder Cratebase-Desktop-Abhängigkeit. `IBeatportCatalog` ist die austauschbare Serviceschnittstelle. `Cratebase.Beatport.App` ist der eigenständige Avalonia-Host.

Damit ist der fachliche Kern wiederverwendbar. Ein installierbares Addon-System ist **noch nicht implementiert**. Sinnvoller nächster Architektur-Schritt nach Live-Validierung: kleine Host-Verträge für Modul-ID/Version, Registrierung von Ansichten und Services, modulbezogenen Datenspeicher und Lifecycle. Erst danach Manifest, Kompatibilitätsprüfung und Installation entwickeln. Fremde .NET-Addons laufen mit den Rechten des Hosts; dynamisches Laden wäre keine Sandbox. Die bestehende Cratebase-App lädt oder verändert dieser Prototyp derzeit nicht.

## Prüfungen

```powershell
dotnet run --project tests/Cratebase.Beatport.Tests -c Release
```

Prüft Duplikate über Releases, getrennte Remixe/Artists, fehlende Laufzeiten, vorhandene Playlist-Tracks, persistente Hörhistorie, mehrseitige API-Antworten, abgelaufene Tokens, wiederholte Seiten, Subgenre-Verifikation und den Avalonia-Demo-Workflow im Headless-Renderer. Login-Tests prüfen Client-Konfiguration, Übermittlung der Zugangsdaten im Anfragekörper, Code-Austausch, Token-Refresh ohne erneute Passwortanmeldung, einmaligen Lese-Retry bei 401, redigierte Fehler, Abbruch und Zurückweisung unerwarteter Redirects bzw. OAuth-State-Werte. API-Tests verwenden simulierte Antworten; es werden keine Account-Änderungen vorgenommen. Ein gerendertes Demo-Bild liegt nach dem Test im temporären Verzeichnis unter `cratebase-beatport-preview.png`.
