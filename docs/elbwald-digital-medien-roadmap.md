# Elbwald Digital – Medien-Tool Roadmap

## Phase 0 – Gemeinsames Fundament

Bevor das erste AddOn richtig wächst, brauchen wir zentrale Dienste im Core:

### Media Core

- Dateisystem-Scanner
- Medien-/Dateityperkennung
- gemeinsames `MediaFile`-Modell
- Metadaten-Modell
- Dateihashing
- Thumbnail-/Preview-Service
- Cache für Analyseergebnisse
- Fortschrittsanzeige + Abbruch
- Fehlerbehandlung für defekte/unlesbare Dateien
- Logging

Noch keine große Benutzerfunktion – aber alles Weitere hängt daran.

---

# Phase 1 – Bilder

## 1. Media Analyzer ⭐

Unser erstes richtiges Modul.

Zunächst Fokus auf Bilder, später wird derselbe Analyzer um Audio und Video erweitert.

Der Nutzer wählt:

- Datei
- Ordner
- inklusive Unterordner

### Analysebereiche

#### Dateien

- Anzahl
- Gesamtgröße
- Formate
- größte Dateien

#### Bilder

- JPEG / PNG / WebP / TIFF / HEIC / RAW
- Auflösungen
- Hoch-/Querformat
- Dateigrößen
- Aufnahmedaten
- Kameramodelle
- EXIF-Verfügbarkeit
- GPS-Daten
- fehlende oder verdächtige Metadaten

Dazu später Diagramme und Statistiken.

**Wichtig:** vollständig read-only.

Das gibt uns direkt Scanner, Metadatenleser, Cache, Preview-System und Media-Model.

---

## 2. Media Inspector

Der Analyzer beantwortet:

> Was befindet sich in diesem Ordner?

Der Inspector beantwortet:

> Was genau ist diese Datei?

### Informationen

- Vorschau
- Dateiinformationen
- EXIF
- Kamera
- Objektiv
- GPS
- Zeitstempel
- Farbraum
- Auflösung
- Hashes
- technische Metadaten

Der Inspector kann später als gemeinsame Detailansicht innerhalb anderer AddOns verwendet werden.

---

## 3. Media Renamer

Jetzt verändern wir erstmals Dateien.

Beispiel:

```text
IMG_3827.JPG
↓
2026-08-14_15-32-17.JPG
```

oder:

```text
2026-08-14_Urlaub_Ostsee_001.jpg
```

### Regelbausteine

```text
{Date}
{Time}
{Year}
{Month}
{Camera}
{OriginalName}
{Counter}
```

Hier entsteht eine weitere zentrale Komponente:

### File Operation Engine

- Vorschau
- Konflikterkennung
- Dry Run
- Undo-Protokoll

Diese Engine brauchen wir anschließend überall.

---

## 4. Photo Sort ⭐

Jetzt können Analyzer, Metadata und File Operations kombiniert werden.

Beispiel:

```text
Unsortiert/
    IMG1234.jpg
    IMG1235.jpg

↓

Fotos/
    2024/
        07 - Juli/
    2025/
        01 - Januar/
```

Regeln wie:

```text
{Year}/{Month}
```

oder:

```text
{Year}/{Camera}/{Month}
```

### Funktionen

- Kopieren oder verschieben
- vorhandene Dateien erkennen
- Namenskonflikte lösen
- unbekanntes Aufnahmedatum behandeln
- RAW/JPEG zusammenhalten
- Videos optional berücksichtigen
- Dry Run

---

## 5. Duplicate Finder ⭐

Zu diesem Zeitpunkt verfügen wir bereits über Scanner, Previews und Hashes.

### Stufe 1 – Bitidentische Dateien

```text
SHA-256(A) == SHA-256(B)
```

### Stufe 2 – Bildidentität

Das gleiche Foto trotz:

- anderer Auflösung
- erneuter Komprimierung
- anderem Dateinamen
- eventuell leichtem Zuschnitt

über Perceptual Hashing.

### Später – Similar Photos

- Serienbilder
- ähnliche Aufnahmen
- fast identische Varianten

---

## 6. Metadata Cleaner

Zum Beispiel vor dem Teilen:

> Entferne persönliche Metadaten.

Selektiv:

- GPS
- Kamerainformationen
- Software
- Kommentare
- EXIF komplett

Vorschau:

```text
Entfernt werden:

GPS Position
Kamera-Seriennummer
Adobe Lightroom Version
```

---

## 7. Date & Metadata Fixer

Beispiel:

```text
EXIF Aufnahme:
14.08.2019 17:32

Dateidatum:
03.04.2026 09:21
```

Das Tool erkennt mögliche Inkonsistenzen und kann beispielsweise:

```text
Dateidatum ← EXIF-Aufnahmedatum
```

oder umgekehrt Metadaten ergänzen.

---

## 8. Library Health ⭐

Jetzt ergibt dieses Modul wirklich Sinn.

Es verwendet die bisherigen Analysefunktionen.

Beispiel:

```text
18.527 Bilder analysiert

✓ 17.921 ohne Probleme

⚠ 231 mögliche Dubletten
⚠ 172 ohne Aufnahmedatum
⚠ 84 inkonsistente Dateidaten
⚠ 63 extrem kleine Bilder
⚠ 32 ungewöhnliche Formate
⚠ 24 möglicherweise beschädigt
⚠ 1.834 enthalten GPS-Daten
```

Hinter jedem Punkt können passende Aktionen angeboten werden:

- Anzeigen
- Mit Photo Sort beheben
- Mit Duplicate Finder prüfen
- Mit Metadata Cleaner entfernen

Langfristig eignet sich Library Health sehr gut als zentrale Startseite der Medienwerkzeuge.

---

# Phase 2 – Video

## 9. Video Analyzer

Der bestehende Media Analyzer bekommt Video-Unterstützung.

Über `ffprobe` können unter anderem ausgelesen werden:

- Container
- Video-Codec
- Audio-Codec
- Dauer
- Auflösung
- FPS
- Bitrate
- HDR
- Farbraum
- Audio-Kanäle
- Untertitel
- Erstellungsdatum

Das gemeinsame Medienmodell wächst damit zu:

```text
MediaFile
├── ImageInfo
├── VideoInfo
└── später AudioInfo
```

---

## 10. Video Inspector

Gleiche UX wie beim Bild-Inspector.

Dadurch bleibt die Suite konsistent.

---

## 11. Video Organizer

Photo Sort kann sich schrittweise zu **Media Sort** entwickeln.

Damit können Fotos und Videos gemeinsam verarbeitet werden.

Beispiel Smartphone-Bibliothek:

```text
IMG_4821.HEIC
IMG_4822.HEIC
VID_4823.MOV
IMG_4824.HEIC
```

Diese Dateien gehören chronologisch zusammen und sollten nicht getrennt behandelt werden.

---

## 12. Media Converter / Optimizer

Mit FFmpeg steht bereits eine starke technische Basis zur Verfügung.

### Video

- MKV → MP4
- MOV → MP4
- H.264 → H.265 / AV1
- Audio extrahieren
- Dateigröße reduzieren

### Bilder

- PNG → JPEG
- HEIC → JPEG
- WebP
- Größenänderung
- Komprimierung

Converter und Optimizer können eventuell als ein gemeinsames AddOn umgesetzt werden.

---

# Phase 3 – Audio

## 13. Audio Analyzer

Erweiterung des Media Analyzer um:

```text
MP3
FLAC
AAC
M4A
OGG
OPUS
WAV
```

### Analyse

- Codec
- Bitrate
- VBR/CBR
- Sample Rate
- Bit Depth
- Channels
- Länge
- Tags
- Cover
- ReplayGain

---

## 14. Audio Inspector

Gemeinsame Detailansicht:

```text
Technik
Tags
Cover
Datei
```

---

## 15. Audio Tag ⭐

Einzeldatei- und Mehrfachbearbeitung.

### Tags

- Artist
- Album
- Title
- Track
- Disc
- Year
- Genre
- Album Artist
- Composer
- Comment
- Cover

Zusätzlich Batch-Bearbeitung.

---

## 16. Album Analyzer / Album Cleaner

Beispiel:

```text
01 - Song.mp3
02 - Song.mp3
03 - Song.mp3
...
```

Analyse:

```text
Album: 12 Tracks

⚠ Track 4 hat kein Cover
⚠ Track 7: Artist = "Beatles"
  andere Tracks: "The Beatles"
⚠ Track 9 hat keine Tracknummer
⚠ Genres: Rock / Pop Rock / Pop
```

Danach:

> Album vereinheitlichen

Später mit MusicBrainz-Unterstützung.

---

## 17. Cover Manager

- Cover anzeigen
- einbetten
- entfernen
- extrahieren
- mehrere Tracks gleichzeitig bearbeiten
- falsche Cover erkennen

Später eventuell automatische Suche.

---

## 18. Audio Renamer

Tags → Dateiname:

```text
{Track:00} - {Artist} - {Title}
```

ergibt:

```text
03 - Pink Floyd - Time.flac
```

Oder andersherum:

```text
03 - Pink Floyd - Time.flac
```

wird als Quelle für Tags interpretiert.

---

# Phase 4 – Zusammenführung

## 19. Media Library Analyzer

Nicht nur einen Ordner untersuchen, sondern eine gesamte Medienbibliothek.

Beispiel:

```text
Medienbibliothek

Fotos       42.381     287 GB
Videos       3.827     841 GB
Musik       18.294     193 GB

Gesamt      64.502   1,32 TB
```

### Auswertungen

- Jahre
- Formate
- Speicherverbrauch
- Qualität
- Metadatenqualität
- Dubletten
- Probleme

---

## 20. Library Health 2.0

Medienübergreifend:

```text
Fotos
Videos
Musik
```

Ein zentraler Gesundheitscheck für die gesamte Bibliothek.

---

# Phase 5 – Import

## 21. Media Importer ⭐

### Quellen

```text
SD-Karte
Kamera
USB-Stick
Ordner
Smartphone
```

### Ziel

```text
Meine Medienbibliothek
```

### Ablauf

1. Dateien analysieren
2. Dubletten prüfen
3. Metadaten prüfen
4. Dateien umbenennen
5. Verzeichnisstruktur erzeugen
6. kopieren
7. Hash überprüfen

Damit werden fast alle bisher entwickelten Komponenten kombiniert.

Für den Anwender könnte daraus eine einfache Hauptaktion werden:

> Neue Fotos importieren

---

# Phase 6 – Backup

## 22. Backup Analyzer

Noch kein Backup durchführen.

Stattdessen beantworten:

> Ist meine Bibliothek vollständig gesichert?

Beispiel:

```text
Bibliothek

42.381 Fotos

Backup HDD:
✓ 41.972 vorhanden
⚠ 409 fehlen
```

Nicht nur Dateinamen vergleichen, sondern Hashes.

---

## 23. Backup Verify

Ein bestehendes Backup überprüfen:

```text
✓ vorhanden
✓ Hash korrekt
✓ lesbar

⚠ verändert
✗ fehlt
✗ beschädigt
```

---

## 24. Backup / Sync

Erst ganz zuletzt:

```text
Quelle
    ↓
Backup-Ziel
```

### Funktionen

- inkrementelles Backup
- Versionshistorie
- Überprüfung
- Ausschlussregeln
- medienspezifische Regeln

Backup ist sicherheitstechnisch anspruchsvoller und sollte erst umgesetzt werden, wenn File Operations, Hashing und Fehlerbehandlung wirklich ausgereift sind.

---

# Roadmap kompakt

```text
FOUNDATION
│
├── Media Core
├── Scanner
├── Metadata
├── Preview
└── Hashing
│
▼
IMAGES
│
├── 1  Media Analyzer ⭐
├── 2  Media Inspector
├── 3  Media Renamer
├── 4  Photo Sort ⭐
├── 5  Duplicate Finder ⭐
├── 6  Metadata Cleaner
├── 7  Date / Metadata Fixer
└── 8  Library Health ⭐
│
▼
VIDEO
│
├── 9  Video Analyzer
├── 10 Video Inspector
├── 11 Video Organizer
└── 12 Media Converter
│
▼
AUDIO
│
├── 13 Audio Analyzer
├── 14 Audio Inspector
├── 15 Audio Tag ⭐
├── 16 Album Cleaner
├── 17 Cover Manager
└── 18 Audio Renamer
│
▼
MEDIA LIBRARY
│
├── 19 Library Analyzer
└── 20 Library Health 2.0
│
▼
IMPORT
│
└── 21 Media Importer ⭐
│
▼
BACKUP
│
├── 22 Backup Analyzer
├── 23 Backup Verify
└── 24 Backup / Sync
```

## Priorität der Zugpferde

Die wichtigsten sichtbaren Module der Suite:

- ⭐ Media Analyzer
- ⭐ Photo Sort
- ⭐ Duplicate Finder
- ⭐ Library Health
- ⭐ Audio Tag
- ⭐ Media Importer

## Empfohlene Entwicklungsrichtung

Nach Shell- und AddOn-Infrastruktur nicht direkt Photo Sort fertigbauen, sondern zuerst den **Media Analyzer**.

Dadurch entstehen bereits:

- Scanner
- EXIF-/Metadaten-Auswertung
- Vorschaulogik
- Medienmodell
- Cache
- Fehlerbehandlung
- Fortschrittsanzeige

Photo Sort und die späteren Module können darauf deutlich sauberer aufbauen.
