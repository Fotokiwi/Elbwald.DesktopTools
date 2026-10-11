# Elbwald Digital – Master-Roadmap & große ToDo

**Stand:** Patch **0091** · 11.10.2026  
**Zielplattformen:** Linux x64 · Windows x64  
**Stack:** C# · .NET 10 LTS · Avalonia · MVVM · CommunityToolkit.Mvvm · System.Text.Json · xUnit

> **Produktleitbild:**  
> Elbwald Digital ist kein geschlossenes Medienarchiv, sondern ein **lokaler Medien-Workspace über dem normalen Dateisystem**.
>
> **Dateisystem = physische Ordnung**  
> **Elbwald = logische Ordnung**
>
> Die Originalmedien bleiben normale Dateien und Ordner. Elbwald analysiert, organisiert, referenziert, bearbeitet und sichert sie nachvollziehbar und möglichst ohne Vendor-Lock-in.

---

## Status-Legende

- ✅ **weitgehend umgesetzt / nutzbar**
- 🟢 **funktionsfähig, weiterer Ausbau geplant**
- 🟡 **technische Grundlage vorhanden / noch nicht rund**
- ⬜ **noch offen**
- ⛔ **bewusst nicht jetzt**

---

# 1. Aktueller Gesamtstand

Elbwald Digital ist inzwischen deutlich über das ursprüngliche „kleine Tool-Suite“-MVP hinausgewachsen.

Bereits vorhanden sind:

- ✅ plattformübergreifender Avalonia-Host
- ✅ modulare Architektur
- ✅ zentrale Navigation und Shell
- ✅ sichere File-Operation-Engine
- ✅ Recovery / Journal / Cross-Process-Schutz
- ✅ Media Analyzer
- ✅ Media Index / Bibliothek
- ✅ Import-Workflow
- ✅ Sortieren
- ✅ Umbenennen
- ✅ Projekt- und Sammlungsgrundlage
- ✅ persistente Analyse- und Importzustände
- ✅ Thumbnail-/Preview-Cache
- ✅ zentrale Media-IDs und projektbasierte Referenzen
- ✅ erste Storage-/Library-Health-Bausteine
- ✅ portable Alpha-Publish-Basis für Linux und Windows

Die ursprüngliche Roadmap ist daher nicht mehr passend.  
Der Fokus verschiebt sich jetzt von **„Grundfunktionen überhaupt bauen“** zu:

1. bestehende Workflows konsolidieren,
2. Library Health und Qualitätsfunktionen ausbauen,
3. Projekte zum Content-Studio entwickeln,
4. Duplicate Finder ergänzen,
5. Undo/History nachziehen,
6. Backup erst auf dem stabilisierten Fundament aufbauen,
7. Audio und Bearbeitung anschließend erweitern.

---

# 2. Produktstruktur ab 0091

## Hauptnavigation

```text
Start
────────────────────────
Bibliothek
Projekte
────────────────────────
Analysieren
Organisieren
Bearbeiten
────────────────────────
Sichern
Werkzeuge
────────────────────────
Einstellungen
```

## Bedeutung der Bereiche

### Start
Geführter Einstieg und zentrale Übersicht:
- Galerie / Bibliothek im Zentrum
- letzte Analyse
- letzter Import
- Bibliotheksstatus
- direkte nächste Schritte

### Bibliothek
Der lokale Medienbestand:
- indexierte Medien
- Vorschauen
- Suche / Filter
- stabile Media-IDs
- Ausgangspunkt für Projekte und spätere Qualitätsfunktionen

### Projekte
Eigene logische Welt oberhalb des Dateisystems:
- virtuelle Sammlungen
- Projektmetadaten
- Medienreferenzen
- automatische Medienvorschläge
- perspektivisch Tagebuch / Urlaubstagebuch
- Fotobuch-/Content-Studio-Funktionen

### Analysieren
Read-only Prüfung unbekannter bzw. neuer Medien:
- Übersicht
- Dateien
- Aufnahmedaten
- Hinweise
- letzte Analyse persistent
- Ergebnis nach Neustart wiederherstellbar

### Organisieren
Physische Dateiorganisation:
- Importieren
- Sortieren
- Umbenennen

### Bearbeiten
Später:
- Metadaten
- Bilder
- Audio
- ggf. Video

### Sichern
Später:
- Backup Analyzer
- Backup Verify
- Backup / Sync

### Werkzeuge
Technische und diagnostische Zusatzfunktionen:
- Library Health
- Medienindex
- Diagnoseprotokoll
- Cache / Recovery / Wartung

---

# 3. Technisches Fundament

## 3.1 Host / Architektur

- ✅ .NET 10 / Avalonia
- ✅ MVVM mit CommunityToolkit.Mvvm
- ✅ Dependency Injection
- ✅ Contracts / Core / UI / App / Module getrennt
- ✅ dynamisches Modul-System
- ✅ `module.json`
- ✅ AssemblyLoadContext
- ✅ inkompatible/fehlerhafte Module isolieren
- ✅ modulbasierte Navigation
- 🟡 zentrale Settings-Abstraktion weiter konsolidieren
- 🟡 App-Pfade überall konsequent über zentrale Pfadservices führen
- ⬜ deaktivierte Module als Endnutzerfunktion
- ⬜ Add-on-Verwaltungsoberfläche
- ⬜ Installation / Update / Signierung von Zusatzmodulen

## 3.2 File Operation Safety

- ✅ Copy-Planung
- ✅ Move-Planung
- ✅ Rename-Planung
- ✅ Konflikterkennung
- ✅ kein stilles Überschreiben
- ✅ staged Copy über temporäre Datei
- ✅ Flush-to-Disk
- ✅ SHA-256-Verifikation
- ✅ Speicherplatz-Preflight
- ✅ Companion-Gruppen / Sidecars
- ✅ Cancellation / Fortschritt
- ✅ persistentes Operationsjournal
- ✅ Recovery Stores
- ✅ Startup-Recovery
- ✅ Recovery-Sperre für neue Schreiboperationen
- ✅ Journal-Integritätsprüfung
- ✅ sichere Journal-Reparatur
- ✅ Cross-Process-Lock
- ✅ Source-I/O-Fehler isolieren
- ✅ Diagnose-/Fehlermodell
- 🟡 gemeinsame Execution-Summary für alle Workflows vereinheitlichen
- ⬜ reguläres Undo-System

## 3.3 Media Core

- ✅ Dateiscanner
- ✅ Medien-/Dateityperkennung
- ✅ gemeinsames Medienmodell
- ✅ Metadatenauswertung
- ✅ Aufnahmezeit-Resolver
- ✅ Hashing
- ✅ Thumbnail-/Preview-System
- ✅ Analyse-/Thumbnail-Cache
- ✅ Fehlerbehandlung beschädigter/unlesbarer Dateien
- ✅ stabile Media-IDs
- ✅ lokaler Medienindex
- ✅ bekannte Speicherorte / Bibliotheksendpunkte
- 🟡 Pfadänderungs-/Relocation-Logik weiter härten
- 🟡 Storage Health zentralisieren
- ⬜ perceptual hashing

---

# 4. Startseite

## Stand

- ✅ neue geführte Startseite
- ✅ Bibliothek/Galerie als Zentrum
- ✅ echte Bibliothekszahlen
- ✅ echte repräsentative Bibliotheksbilder
- ✅ letzter Analysepfad / Analysezustand
- ✅ letzter Import
- ✅ direkte Navigation
- ✅ thematische Bilder für Analyse / Sortieren / Metadaten / Backup
- ✅ Offline-first / Keine Cloud / Safety-first
- ✅ Recovery-Hinweis nur bei Bedarf
- ✅ Hover-/Pressed-Zustände konsolidiert

## Später

- 🟡 Backup-Kachel mit echten Daten, sobald Backup existiert
- 🟡 „Weiterarbeiten“ noch stärker aus echter Activity-History speisen
- ⬜ optional personalisierbare Startseite
- ⬜ projektbezogene letzte Aktivitäten

---

# 5. Analysieren

## Stand

- ✅ Datei oder Ordner analysieren
- ✅ Unterordner
- ✅ read-only
- ✅ Übersicht
- ✅ Dateien
- ✅ Aufnahmedaten
- ✅ Hinweise
- ✅ Suche / Filter
- ✅ Galerie / Vorschau
- ✅ Dateidetails
- ✅ Kamera / GPS / Datum / Auflösung / Formate
- ✅ verständliche Qualitätsaussagen statt technischer Parser-Sprache
- ✅ interne Scrollflächen bei großen Ergebnislisten
- ✅ letzter Analysepfad persistent
- ✅ letztes vollständiges Analyseergebnis persistent
- ✅ Wiederherstellung nach Neustart
- ✅ direkter Übergang zum Import

## Nächste Qualitätsschritte

- 🟡 große reale Bestände testen
- 🟡 beschädigte / nicht lesbare Dateien systematisch testen
- 🟡 RAW-Sonderfälle weiter testen
- 🟡 Video-/Audio-Erkennung später integrieren
- ⬜ Analyzer-Berichte exportieren
- ⬜ Vergleich zweier Analysezustände / „was hat sich geändert?“

---

# 6. Importieren

## Stand

- ✅ letzte Analyse automatisch als Quelle
- ✅ alternative Quelle auswählbar
- ✅ Import-Stammverzeichnis
- ✅ sinnvoller Unterordner-Default
- ✅ Vorschau vor Import
- ✅ neue / vorhandene / Konfliktdateien getrennt
- ✅ interne Scrollflächen
- ✅ sichere Copy-Pipeline
- ✅ Hash-Verifikation
- ✅ kein Überschreiben
- ✅ Import-History
- ✅ optionales Aufräumen der Quelle
- ✅ zweistufige Löschfreigabe
- ✅ technisches Sicherheitsdetail aus normaler UI herausgenommen

## Noch offen

- 🟡 Import-History als eigener Verlauf
- 🟡 Importprofil/-preset speichern
- ⬜ Geräteerkennung / Kameraname für bessere Zielordnernamen
- ⬜ automatische Ereignis-/Projektzuordnung beim Import
- ⬜ Dublettenprüfung mit Duplicate Finder verknüpfen
- ⬜ optionaler direkter Workflow:
  `Analysieren → Importieren → Sortieren → Projekt zuordnen`

---

# 7. Sortieren

## Stand

- ✅ Quelle / Ziel
- ✅ letzter Import als bevorzugte Quelle
- ✅ Import-Wartehalle als Fallback
- ✅ beliebige externe Ordner weiterhin erlaubt
- ✅ Kopieren / Verschieben
- ✅ Vorschau
- ✅ keine Überschreibungen
- ✅ Presets für Ordnerstrukturen
- ✅ benutzerdefinierte Platzhalter
- ✅ sichere Datumslogik
- ✅ unsichere Datumsfälle bearbeitbar
- ✅ alternatives Datum wählen
- ✅ manuelles Datum
- ✅ „Datum unklar“
- ✅ Datei überspringen
- ✅ Massenentscheidung für Datumsfälle
- ✅ verständliche statt technische Warnungen
- ✅ echte Ausführung über Safety Engine

## Presets

- ✅ Jahr / Monat
- ✅ Jahr / Monat / Tag
- ✅ Jahr / Titel
- ✅ Jahr / Kamera / Monat
- ✅ Jahr / Quellordner
- ✅ Jahr / Medientyp / Monat
- 🟡 weitere Presets nur bei echtem Bedarf ergänzen

## Noch offen

- ⬜ gespeicherte eigene Sortierprofile
- ⬜ Favoriten unter Presets
- ⬜ Vorschau mit Thumbnail + Alt/Ziel
- ⬜ Undo
- ⬜ sehr große Bestände / Performance

---

# 8. Umbenennen

## Stand

- ✅ letzter Import / Import-Wartehalle als Quelle
- ✅ beliebige externe Quelle
- ✅ verständliche Presets
- ✅ Live-Beispiel aus realer Quelle
- ✅ eigene Regel als Expertenbereich
- ✅ Vorschau Alt → Neu
- ✅ Kollisionen
- ✅ Sequenzen nur wo nötig
- ✅ dynamischer Primärbutton
- ✅ Sidecars / Gruppen
- ✅ sichere Rename-Ausführung
- ✅ kein Überschreiben

## Presets

- ✅ Datum + Uhrzeit
- ✅ Datum + Originalname
- ✅ Datum + Kamera
- ✅ Datum + Titel
- ✅ Archiv · Datum + Nummer

## Noch offen

- ⬜ eigene Regeln speichern / benennen
- ⬜ Regelbibliothek
- ⬜ Undo
- ⬜ Umbenennungsverlauf
- ⬜ optional Metadaten und Dateiname gemeinsam bearbeiten

---

# 9. Bibliothek / Media Index

## Stand

- ✅ lokaler Medienindex
- ✅ stabile Media-ID
- ✅ Bibliotheksendpunkte
- ✅ Thumbnail-Nutzung
- ✅ Medienzählung / Ordnerzählung
- ✅ Suche / Bibliotheksnavigation
- ✅ Referenzgrundlage für Projekte
- ✅ kein proprietäres Mediengefängnis

## Nächste Schritte

- 🟡 Pfadänderungen / verschobene Medien noch robuster erkennen
- 🟡 Offline-/fehlende Volumes besser darstellen
- 🟡 Library Health enger integrieren
- ⬜ Ratings
- ⬜ Favoriten
- ⬜ Tags
- ⬜ virtuelle Sammlungen außerhalb von Projekten
- ⬜ gespeicherte Filter
- ⬜ Smart Collections
- ⬜ Medienhistorie / Aktivitätsverlauf

---

# 10. Projekte / Content Studio

## Stand 0091

- ✅ eigener Hauptmenüpunkt
- ✅ eigene Projektübersicht
- ✅ Projektkarten
- ✅ Projekt öffnen / zurück zur Übersicht
- ✅ Projektmetadaten
- ✅ Projektart
- ✅ Zeitraum
- ✅ Ort
- ✅ Notizen
- ✅ Medien über stabile Media-IDs zuordnen
- ✅ passende Medien anhand Zeitraum vorschlagen
- ✅ Projektmedien
- ✅ Projektzuordnung verändert Originaldatei nicht
- ✅ Bereiche:
  - Übersicht
  - Medien
  - Tagebuch
  - Informationen
- ✅ Tagebuch als „Geplant“ vorbereitet

## Nächster großer Ausbau

### Projektübersicht
- ⬜ Coverbild / Hero
- ⬜ Thumbnail-Collage
- ⬜ letzte Aktivität
- ⬜ Projektstatus / Fortschritt
- ⬜ Projektkarten visuell nach Typ unterscheiden

### Projekt-Medien
- ⬜ echte Galerieansicht
- ⬜ Multi-Select
- ⬜ Favoriten innerhalb eines Projekts
- ⬜ Reihenfolge
- ⬜ Projekt-Tags
- ⬜ automatische Vorschläge über:
  - Datum
  - GPS
  - Kamera
  - Pfad
  - Tags

### Projekttagebuch
- ⬜ Tages-/Kapiteleinträge
- ⬜ Text + Medien
- ⬜ automatische Tagesgruppierung
- ⬜ Medien eines Datums vorschlagen
- ⬜ Orte / Reiseroute
- ⬜ freie Kapitel
- ⬜ Markdown-/Rich-Text-Export

### Fotobuch / Content Studio
- ⬜ Medienauswahl
- ⬜ Reihenfolge
- ⬜ Favoriten
- ⬜ Layout-/Seitenkonzept
- ⬜ PDF-Export
- ⬜ Druckdienst-Workflow später evaluieren

---

# 11. Library Health

## Stand

- 🟡 einzelne Grundlagen vorhanden:
  - Storage Health
  - Analyzer-Hinweise
  - Diagnoseprotokoll
  - Recovery
  - Metadatenqualität
  - Medienindex
- 🟡 eigener Library-Health-Einstieg vorhanden

## Zielbild

Eine zentrale verständliche Gesundheitsansicht für die gesamte Bibliothek:

- ⬜ nicht lesbare Dateien
- ⬜ beschädigte Dateien
- ⬜ fehlende Datumsangaben
- ⬜ widersprüchliche Datumsangaben
- ⬜ ungewöhnliche Dateiformate
- ⬜ verlorene / nicht erreichbare Pfade
- ⬜ offline befindliche Speicherorte
- ⬜ mögliche Dubletten
- ⬜ nicht gesicherte Medien
- ⬜ wiederkehrende I/O-Probleme
- ⬜ kaputte Sidecars
- ⬜ verwaiste Indexeinträge
- ⬜ konkrete Reparatur-/Handlungsvorschläge

**Hohe Priorität.**

---

# 12. Duplicate Finder

## Stufe 1 – Bitidentisch

- ⬜ SHA-256-Gruppen bilden
- ⬜ Duplikatgruppen anzeigen
- ⬜ Original / Kopien verständlich darstellen
- ⬜ Speicherplatzpotential
- ⬜ Auswahlstrategie
- ⬜ sichere Lösch-/Verschiebevorschau
- ⬜ Recovery / Journal

## Stufe 2 – Bildidentität

- ⬜ perceptual hashing
- ⬜ gleiches Bild trotz:
  - anderer Auflösung
  - Rekodierung
  - anderem Dateinamen
  - kleiner Bearbeitung

## Später

- ⬜ ähnliche Fotos
- ⬜ Serienbilder
- ⬜ „beste Aufnahme“ unterstützen

**Sehr sinnvoller nächster neuer Funktionsblock.**

---

# 13. Undo / History

Recovery schützt vor technischen Fehlern.  
Undo soll bewusst abgeschlossene Benutzeraktionen zurücknehmen.

## Offen

- ⬜ abstrahiertes Undo-Journal
- ⬜ abgeschlossene Operationen als Historie
- ⬜ Rename rückgängig
- ⬜ Move rückgängig
- ⬜ Sortierung rückgängig
- ⬜ Quellbereinigung nur dort rückgängig, wo technisch möglich
- ⬜ klare Grenzen / nicht reversible Aktionen
- ⬜ UI „Letzte Aktionen“
- ⬜ Neustartübergreifende Undo-History

**Hohe technische Priorität**, bevor immer mehr schreibende Werkzeuge hinzukommen.

---

# 14. Backup / Sichern

Noch **nicht implementiert**.

## Reihenfolge

### Backup Analyzer
- ⬜ Quelle / Ziel vergleichen
- ⬜ fehlende Dateien
- ⬜ veraltete Dateien
- ⬜ beschädigte Dateien
- ⬜ Speicherplatz
- ⬜ Redundanzstatus

### Backup Verify
- ⬜ Hashbasierte Verifikation
- ⬜ Stichprobe / vollständig
- ⬜ Backup-Alter
- ⬜ Bibliotheksabdeckung

### Backup / Sync
Erst danach:

- ⬜ inkrementelles Backup
- ⬜ mehrere Backupziele
- ⬜ externe Datenträger
- ⬜ NAS
- ⬜ Versionierung
- ⬜ Retention
- ⬜ Recovery-Test
- ⬜ Zeitpläne
- ⬜ Sync nur mit klarer Konfliktstrategie

Grundsatz:

> Ein Backup ist erst dann ein Backup, wenn es überprüfbar ist.

---

# 15. Metadaten-Werkzeuge

## Metadata Editor

- 🟡 Navigation / Konzept vorhanden
- ⬜ Bildmetadaten komfortabel bearbeiten
- ⬜ Mehrfachbearbeitung
- ⬜ Tags
- ⬜ Titel / Beschreibung
- ⬜ Aufnahmedatum korrigieren
- ⬜ GPS
- ⬜ Kamera-/Copyrightfelder
- ⬜ Vorschau vor Schreiben
- ⬜ Undo

## Metadata Cleaner

- ⬜ GPS entfernen
- ⬜ Kameraangaben entfernen
- ⬜ personenbezogene Felder entfernen
- ⬜ selektive Exportkopie fürs Teilen

## Date & Metadata Fixer

- ⬜ Datumskonflikte bearbeiten
- ⬜ Dateisystemdatum korrigieren
- ⬜ EXIF-Datum setzen
- ⬜ Regeln auf Gruppen anwenden

---

# 16. Audio

Noch weitgehend offen.

## Audio Analyzer
- ⬜ MP3 / FLAC / OGG / M4A analysieren
- ⬜ Bitrate
- ⬜ Sample Rate
- ⬜ Channels
- ⬜ Dauer
- ⬜ Tags
- ⬜ Cover
- ⬜ Qualitäts-/Konsistenzhinweise

## Audio Tag ⭐
- ⬜ Titel
- ⬜ Interpret
- ⬜ Album
- ⬜ Album-Interpret
- ⬜ Jahr
- ⬜ Track
- ⬜ Disc
- ⬜ Genre
- ⬜ Kommentar
- ⬜ Cover lesen / schreiben / entfernen
- ⬜ Mehrfachauswahl
- ⬜ Multi-Edit
- ⬜ Änderungen markieren
- ⬜ Speichern mit Safety/Undo

## Später
- ⬜ Album Analyzer
- ⬜ Cover Manager
- ⬜ Audio Renamer
- ⬜ CD Import
- ⬜ ReplayGain
- ⬜ Konvertierung
- ⬜ einfacher Audio-Editor
- ⬜ Restauration / Rauschreduzierung

---

# 17. Video

Noch offen.

- ⬜ Video Analyzer
- ⬜ Video Inspector
- ⬜ Video Organizer
- ⬜ Thumbnail-/Keyframe-Unterstützung
- ⬜ technische Metadaten
- ⬜ Aufnahmedatum
- ⬜ Codec / Bitrate / Auflösung / FPS
- ⬜ Media Converter später

---

# 18. Bildbearbeitung

Nicht vorziehen, solange Organisations-/Undo-Schicht nicht weiter stabilisiert ist.

## Zielrichtung

> Foto entwickeln und optimieren – kein Photoshop-Nachbau.

### Geplant
- ⬜ Crop
- ⬜ Drehen / Spiegeln / Horizont
- ⬜ Belichtung
- ⬜ Lichter / Tiefen
- ⬜ Weißabgleich
- ⬜ HSL
- ⬜ Schwarzweiß
- ⬜ Tonkurven
- ⬜ Schärfen
- ⬜ Rauschreduzierung
- ⬜ Vignette / Körnung
- ⬜ Presets
- ⬜ Histogramm
- ⬜ Vorher / Nachher
- ⬜ non-destructive edit stack
- ⬜ Export
- ⬜ RAW später via bewährter Bibliothek

---

# 19. UX / Designsystem

## Stand

- ✅ zentrale Farbpalette
- ✅ Card-/Button-Grundstile
- ✅ moderne Shell
- ✅ geführte Startseite
- ✅ Nutzertexte in Analyzer / Import / Sort / Rename stark vereinfacht
- ✅ gefährliche Aktionen werden klar getrennt
- ✅ technische Details zunehmend einklappbar
- ✅ scrollbar begrenzte große Ergebnislisten

## Offen

- 🟡 Typografie als echtes Token-System
- 🟡 Spacing-Tokens
- ⬜ feste eingebettete Branding-Schrift(en)
- ⬜ Light / Dark / System Themes
- ⬜ zentrales Dialogsystem
- ⬜ Toast-/Notification-System
- ⬜ standardisierte Empty States
- ⬜ standardisierte Progress-Komponente
- ⬜ Accessibility:
  - Tastatur
  - Fokus
  - Screenreader
  - Kontraste
  - skalierte Schrift
- ⬜ DPI-/HiDPI-Tests
- ⬜ kleinere Bildschirmgrößen testen

---

# 20. Einstellungen

- 🟡 Settings-Seite vorhanden
- 🟡 einzelne Einstellungen bereits persistent
- ⬜ Settings-Service vollständig vereinheitlichen
- ⬜ Speicherorte:
  - Bibliothek
  - Import-Wartehalle
  - Cache
  - Backupziele später
- ⬜ UI-/Theme-Einstellungen
- ⬜ Verhalten der Dateifunktionen
- ⬜ Default-Regeln für Import/Sort/Rename
- ⬜ Cache-Limits
- ⬜ Diagnose-/Logging-Level
- ⬜ portable vs. systemweite Datenhaltung evaluieren

---

# 21. Diagnose / Logging

- ✅ zentrales Diagnosekonzept
- ✅ Recovery-/Safety-Informationen
- 🟡 Diagnoseoberfläche vorhanden
- ⬜ Logging-Abstraktion vollständig konsolidieren
- ⬜ optional Serilog
- ⬜ Log-Rotation
- ⬜ Export eines Support-Pakets
- ⬜ sensible Pfade / Daten beim Export anonymisieren
- ⬜ „Fehlerbericht kopieren“

---

# 22. Tests / Qualität

## Sofort relevant

- ⬜ kompletter Linux-E2E-Test aller Hauptworkflows
- ⬜ kompletter Windows-E2E-Test
- ⬜ Import mit tausenden Dateien
- ⬜ Sortieren mit tausenden Dateien
- ⬜ Rename mit tausenden Dateien
- ⬜ Abbruch während Copy
- ⬜ Abbruch während Move
- ⬜ Zielvolume voll
- ⬜ Quellvolume verschwindet
- ⬜ Quelle ändert sich zwischen Vorschau und Ausführung
- ⬜ Ziel entsteht zwischen Vorschau und Ausführung
- ⬜ Recovery nach Prozessabbruch
- ⬜ beschädigtes Journal
- ⬜ nicht beschreibbare Recovery-Ablage
- ⬜ Netzlaufwerk / langsames Laufwerk
- ⬜ externe USB-Medien
- ⬜ lange Pfade / Sonderzeichen
- ⬜ sehr große Einzeldateien
- ⬜ RAW + JPEG + Sidecars

## Automatisierung

- 🟡 Unit-/Core-Tests vorhanden
- ⬜ UI-/ViewModel-Tests ausbauen
- ⬜ Integrationstests mit temporären Dateisystemen
- ⬜ Windows-CI
- ⬜ Linux-CI
- ⬜ Publish-Smoke-Test automatisieren

---

# 23. Distribution / Release

## Stand

- ✅ self-contained Alpha-Publish-Skripte
- ✅ linux-x64
- ✅ win-x64
- ✅ portable Build-Struktur
- ✅ Tests vor Publish vorgesehen

## Offen

- ⬜ Buildprozess Stand 0091 erneut validieren
- ⬜ Windows-Installer
- ⬜ Linux AppImage
- ⬜ Linux DEB
- ⬜ GitHub Releases
- ⬜ automatische Release-Artefakte
- ⬜ Release Notes
- ⬜ Code Signing
- ⬜ Update-Konzept
- ⬜ macOS später evaluieren

---

# 24. Monetarisierung / Produktmodell

Noch keine technische Priorität, aber strategisch relevant.

Mögliche Richtung:

- kostenlose starke Basisversion
- dezente „Kaffee spendieren“-Möglichkeit
- Spendenwand / Unterstützerbereich
- optionale Pro-Module später

Wichtig:

> Die Basisfunktionen für Sicherheit, Bibliothek und lokale Medienhoheit sollten nicht künstlich beschnitten werden.

---

# 25. Empfohlene Priorität ab Patch 0091

## Phase 1 – Konsolidierung & Härtung

**Jetzt zuerst.**

- [ ] Hauptworkflows vollständig durchtesten:
  - Analyzer
  - Import
  - Sort
  - Rename
  - Library
  - Projekte
- [ ] Linux-E2E
- [ ] Windows-E2E
- [ ] große Bestände
- [ ] Fehler-/Abbruchpfade
- [ ] Settings / App Paths konsolidieren
- [ ] UI-Wiederholungen in gemeinsame Controls überführen
- [ ] aktuelle Projektdokumentation aktualisieren

**Ziel:**  
> 0.1-Alpha ist reproduzierbar stabil und nicht nur feature-reich.

---

## Phase 2 – Projekte sinnvoll vertiefen

- [ ] Projektübersicht visuell machen
- [ ] Projektmedien als Galerie
- [ ] Cover / Highlights
- [ ] automatische Vorschläge verbessern
- [ ] Tagebuch-MVP
- [ ] Tagesgruppierung
- [ ] Projektaktivität
- [ ] Reise-/Urlaubsprojekt als erstes spezialisiertes Projektprofil

**Ziel:**  
> Projekte werden ein echter Mehrwert oberhalb der Ordnerstruktur.

---

## Phase 3 – Library Health

- [ ] zentrale Qualitätsübersicht
- [ ] kaputte / fehlende Medien
- [ ] Datum / Metadaten
- [ ] Storage Health
- [ ] Indexkonsistenz
- [ ] Handlungsvorschläge

**Ziel:**  
> Der Nutzer sieht auf einen Blick, ob seine Mediensammlung gesund ist.

---

## Phase 4 – Duplicate Finder

- [ ] SHA-256-Duplikate
- [ ] sichere Bereinigung
- [ ] später perceptual hash

**Ziel:**  
> Erste hochwertige Bereinigungsfunktion.

---

## Phase 5 – Undo / History

- [ ] Operation History
- [ ] Rename Undo
- [ ] Move/Sort Undo
- [ ] UI für letzte Aktionen

**Ziel:**  
> Die schreibenden Funktionen werden auch nach erfolgreicher Ausführung reversibler.

---

## Phase 6 – Backup

- [ ] Backup Analyzer
- [ ] Backup Verify
- [ ] erst danach echtes Backup

**Ziel:**  
> Nachweisbar redundante, überprüfbare Sicherungen.

---

## Phase 7 – Metadaten / Audio

Danach zwei mögliche parallele Produktpfade:

### Metadaten
- Editor
- Cleaner
- Date Fixer

### Audio
- Analyzer
- Audio Tag
- Cover Manager

---

## Phase 8 – Bild / Video / Content Studio

Langfristiger Ausbau:

- Bildentwicklung
- Videoanalyse
- Fotobuch
- Content Studio
- Exporte
- Reisebuch

---

# 26. Was wir ausdrücklich nicht voreilig tun sollten

- ⛔ Cloud-Zwang
- ⛔ Accountpflicht
- ⛔ proprietäre Bibliotheksablage
- ⛔ automatisches Löschen ohne explizite Freigabe
- ⛔ Backup bauen, bevor Verify/Health stabil sind
- ⛔ große Bildbearbeitung vor Undo-/Safety-Konsolidierung
- ⛔ KI nur um „KI drin zu haben“
- ⛔ neue Module anfangen, während Kernworkflows bekannte kritische Fehler haben

---

# 27. Definition eines guten nächsten Alpha-Meilensteins

## Alpha 0.1 – „Local Media Workflow“

Ein sinnvoller Release-Meilenstein wäre erreicht, wenn ein Nutzer reproduzierbar:

1. einen unbekannten Ordner analysiert,
2. die Ergebnisse versteht,
3. ihn sicher importiert,
4. die Medien sicher sortiert,
5. Dateinamen vereinheitlicht,
6. die Medien in der Bibliothek wiederfindet,
7. sie einem Projekt zuordnet,
8. die App schließen und wieder öffnen kann,
9. bei Fehlern keine Dateien verliert,
10. denselben Ablauf unter Linux und Windows erfolgreich durchführen kann.

Dann ist Elbwald nicht mehr nur eine Preview-Sammlung, sondern ein zusammenhängender lokaler Medien-Workflow.

---

# 28. Unmittelbare nächste ToDo

Nach Stand 0091:

1. **0091 lokal bauen und Projekte-Navigation testen**
2. **aktuellen Projektbereich visuell bewerten**
3. **Linux-E2E der Kette Analyse → Import → Sort → Rename → Bibliothek → Projekt**
4. **Windows-Smoke-/E2E-Test**
5. Danach bewusst entscheiden zwischen:
   - **Projekte/Tagebuch weiterbauen**
   - **Library Health**
   - **Duplicate Finder**
   - **Undo/History**

Meine technische Priorisierung wäre:

```text
Konsolidierung
    ↓
Projekte-MVP abrunden
    ↓
Library Health
    ↓
Duplicate Finder
    ↓
Undo / History
    ↓
Backup
```

---

# 29. Patch-Regeln

Weiterhin verbindlich:

- jeder Patch als ZIP
- Originalordnerstruktur beibehalten
- nur neue/geänderte Dateien
- `PATCH.md` beilegen
- Löschanweisungen separat im Chat
- keine stillen Datei-Löschungen
- Safety-relevante Änderungen explizit dokumentieren

---

# 30. Kurzfazit

Der wichtigste Wandel seit der alten Roadmap:

> Elbwald Digital ist inzwischen kein „Photo-Sort-Projekt mit ein paar Add-ons“ mehr.

Es ist bereits der Kern eines:

> **lokalen, sicheren und projektorientierten Medien-Workspaces**

Die nächste Entwicklungsphase sollte deshalb nicht primär möglichst viele neue Features hinzufügen, sondern die vorhandenen starken Bausteine **stabilisieren, verbinden und vertiefen**.
