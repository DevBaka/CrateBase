# Windows: Start auf einem weiteren PC

Den kompletten Cratebase-Ordner auf den PC kopieren oder das Repository klonen.
Die vier Starter funktionieren auch aus einem Ordner mit Leerzeichen:

- `start.bat`: Cratebase
- `start-beatport.bat`: Beatport-Werkzeug
- `start-traktor-live-stems.bat`: Traktor mit Live Stems
- `start-traktor-sync.bat`: LAN-Synchronisierung, ohne GPU-Laufzeit

Beim ersten Start werden fehlende Laufzeiten heruntergeladen und unter
`%LOCALAPPDATA%\Cratebase` eingerichtet. Es ist keine globale Python-, .NET-,
CUDA-Toolkit-, Compiler- oder Ollama-Installation erforderlich. Die Apps werden
mit ihrer .NET-Laufzeit veröffentlicht. Windows kann beim Microsoft-VC++-Installer
Administratorrechte bzw. einen Neustart verlangen. Internet wird bei der ersten
Einrichtung benötigt; Python-/GPU-Pakete können mehrere GB belegen.

## Traktor und MSI Claw A1M

Die native Erweiterung unterstützt ausschließlich die geprüfte Windows-x64-Version
**Traktor Pro 4.5.1.21**. Diese muss auf dem jeweiligen PC regulär installiert und
aktiviert sein. Andere Programmversionen werden abgewiesen. Die private Kopie
verwendet `Dokumente\Native Instruments\Traktor 4.5 Cratebase Edition`; vorhandene
Collections und Einstellungen dort werden bei der Einrichtung nicht ersetzt.
Eine erstmalige Edition übernimmt die vorhandene Collection. Musikdateien müssen
auf diesem PC erreichbar sein; LAN-Sync der Collection ersetzt keine Musikkopie.

Die automatische Auswahl nutzt NVIDIA/CUDA oder Intel/AMD/DirectML. Der **MSI Claw
A1M nutzt seine Intel Arc über DirectML**, nicht CUDA. Dafür muss ein funktionierender
Intel-DX12-Grafiktreiber installiert sein. DirectML rechnet das neuronale Netz auf
der GPU; komplexe FFT/Inverse-FFT und einzelne nicht unterstützte Operationen laufen
auf der CPU. DirectML verwendet FP32 und Windows' Verwaltung des geteilten Speichers;
die CUDA-Regler für FP16 und VRAM-Budget haben dort keine Wirkung.

Beim ersten Start und nach Treiber-/Backendänderungen wird eine vollständige
Stem-Berechnung gegen eine CPU-Referenz geprüft. Bei fehlerhafter Ausgabe wird die
Einrichtung nicht als erfolgreich markiert. Eine bestandene Prüfung bestätigt die
Berechnung, keine garantierte Echtzeitleistung oder Ausfallsicherheit eines Live-Sets.
Bis Stem-Blöcke bereitstehen, bleibt der Originalmix als bestehender Fallback erhalten.

Zur gezielten Einrichtung kann man z. B. aus PowerShell ausführen:

```powershell
.\start-traktor-live-stems.bat -StemBackend directml
.\start-traktor-live-stems.bat -StemBackend cpu
```

Ohne Parameter wird die GPU erneut automatisch gewählt. Eine Änderung der
Laufzeit wird beim nächsten Start des GPU-Dienstes wirksam; bei laufendem Traktor
wird dieser nicht ausgetauscht. Der normale Start hier auf dem NVIDIA-PC bleibt CUDA.

Die bisherige Offline-Stem-Erzeugung innerhalb von Cratebase nutzt weiterhin ihre
eigene CPU-Laufzeit und funktioniert unabhängig von der GPU-Marke.

### Stem Settings

Im Live-Stem-Steuerfenster gibt es den Reiter **Stem Settings**. Er ist Teil des
Cratebase-Steuerfensters, nicht von Traktors nativem Preferences-Dialog.
Die Geräteauswahl listet CPU und die GPUs der installierten CUDA-/DirectML-Laufzeit
mit Namen auf. Gerätewechsel werden erst bei geschlossenem Traktor gespeichert
und beim nächsten Start des Dienstes übernommen. Die Auswahl bleibt gespeichert.

**Audiosek./Sek.** begrenzt die Renderleistung pro aktivem Auftrag: 1 entspricht
höchstens einer Audiosekunde pro Echtzeitsekunde, 4 höchstens vier; 0 bedeutet
unbegrenzt. Langsamere Hardware kann diesen Wert unterschreiten. Änderungen gelten
auch für laufende Aufträge ab dem nächsten Berechnungsfenster. Die Auftragsliste
zeigt zusätzlich die bisher erreichte durchschnittliche Geschwindigkeit.

Needle Search und Beatjump priorisieren die angeforderte Stelle einschließlich
der für die Überlappung benötigten Nachbarfenster. Fehlende Stem-Blöcke verwenden
den Originalmix; verfügbare Stems werden mit kurzem Crossfade wieder übernommen.
Manuell gesetzte Stem-Mutes, Filter und Fader werden dabei weiterhin respektiert:
Für den vollständigen Originalmix müssen alle vier Spuren hörbar sein.

## Diagnose und Prüfstand

Einrichtungsprotokolle: `%LOCALAPPDATA%\Cratebase\bootstrap\setup-*.log`.
GPU-/Traktor-Protokolle: `%LOCALAPPDATA%\Cratebase\traktor-lab\logs`.
Ein fehlgeschlagener Download kann durch erneuten Aufruf des Starters fortgesetzt
bzw. wiederholt werden. Erfolgreiche Installationen werden wiederverwendet.
Die alten `.cmd`-Starter reichen an die entsprechenden `.bat`-Starter weiter.

Geprüft am Entwicklungs-PC: eigenständiges Veröffentlichen von Cratebase und
Beatport; Installation der Sync-Laufzeit in leerem Benutzerverzeichnis;
CUDA- und DirectML-Ausgabe gegen CPU; DirectML-Worker mit Audio, allen vier Spuren
und vollständig veröffentlichtem Cache; automatisierte Installer- und native
Regressionstests. DirectML wurde auf NVIDIA getestet. Ein physischer Test auf dem
MSI Claw A1M einschließlich mehrerer Decks, Audiogerät, Leistungsprofil und
Controller steht noch aus. Windows-Grafiktreiber werden nicht automatisch ersetzt.
