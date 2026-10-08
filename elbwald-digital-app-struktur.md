# Elbwald Digital – Künftige App-Struktur

## Grundprinzip

Die Hauptnavigation der App soll **nach Aufgaben** aufgebaut sein, nicht nach Medientypen.

Der Nutzer denkt eher:

- Was möchte ich tun?
- Möchte ich analysieren, organisieren, bearbeiten oder sichern?

und nicht:

- Ist das jetzt ein Foto-, Audio- oder Video-Tool?

Dadurch bleibt die Navigation übersichtlich und kann wachsen, ohne dass mit jedem neuen Modul ein weiterer Menüpunkt dazukommt.

---

# Hauptnavigation

```text
ELBWALD DIGITAL
│
├── Start
├── Analysieren
├── Organisieren
├── Bearbeiten
├── Sichern
├── Werkzeuge
└── Einstellungen
```

---

# 1. Start

Die Startseite dient als ruhiger Einstieg und Dashboard.

Mögliche Inhalte:

- zentrale Aktion: **Medien analysieren**
- Bibliotheksstatus
- Schnellaktionen
- zuletzt verwendete Ordner / Projekte
- Hinweise auf Probleme
- lokaler Datenschutz-Hinweis

Beispiel:

```text
Willkommen

Was möchtest du machen?

[ Medien analysieren ]
[ Medien organisieren ]
[ Bild bearbeiten ]
[ Medien sichern ]

Zuletzt verwendet
...
```

---

# 2. Analysieren

Der Analyzer ist der zentrale, read-only Bereich der App.

Er arbeitet medienübergreifend.

```text
Analysieren

[ Alle Medien ] [ Bilder ] [ Videos ] [ Audio ]

Übersicht
Dateien
Dubletten
Metadaten
Probleme
Speicherplatz
```

## Enthaltene Funktionen

### Media Analyzer

- Datei- und Ordneranalyse
- Medienarten erkennen
- Speicherverbrauch
- Formate
- Größen
- Auflösungen
- Codecs
- Tags
- EXIF
- Metadaten
- Statistiken

### Media Inspector

Detailansicht für einzelne Dateien:

- Dateiinformationen
- Bilddaten
- EXIF
- GPS
- Kamera
- Audio-Tags
- Video-Codecs
- Hashes
- technische Metadaten

### Library Health

- beschädigte Dateien
- fehlende Metadaten
- verdächtige Zeitstempel
- ungewöhnliche Formate
- problematische Dateien
- allgemeiner Zustand der Bibliothek

### Duplicate Finder

- bitidentische Dateien
- gleiche Inhalte
- ähnliche Bilder
- mögliche Dubletten

---

# 3. Organisieren

Hier landen alle Funktionen, die Dateien strukturieren.

```text
Organisieren

[ Importieren ]
[ Sortieren ]
[ Umbenennen ]
```

## Importieren

Quellen:

- Kamera
- SD-Karte
- Smartphone
- USB
- Ordner
- Netzlaufwerk

Möglicher Ablauf:

```text
Import
↓
Dubletten prüfen
↓
Umbenennen
↓
Sortieren
↓
Kopieren
↓
Integrität prüfen
```

## Sortieren

Aus Photo Sort und Video Organizer wird langfristig ein gemeinsamer Medien-Sortierer.

Beispiele:

```text
{Year}/{Month}
```

```text
{Year}/{Camera}/{Month}
```

Funktionen:

- Fotos sortieren
- Videos sortieren
- Fotos und Videos gemeinsam behandeln
- RAW/JPEG-Paare zusammenhalten
- kopieren oder verschieben
- Vorschau / Dry Run
- Konflikte erkennen

## Umbenennen

Ein gemeinsamer Renamer für verschiedene Medientypen.

Beispiel Foto:

```text
{Date}_{Time}_{Camera}
```

Beispiel Audio:

```text
{Track:00} - {Artist} - {Title}
```

Die verfügbaren Variablen passen sich an den Dateityp an.

---

# 4. Bearbeiten

Hier wird nach Medientyp unterschieden, weil sich die Bearbeitungs-Workflows tatsächlich unterscheiden.

```text
Bearbeiten

├── Bilder
├── Audio
└── Video
```

## Bilder

```text
Bilder

[ Bild bearbeiten ]
[ Collage erstellen ]
[ Stapelbearbeitung ]
```

### Bild bearbeiten

Ein einfacher, alltagstauglicher Bildeditor.

Funktionen:

- beschneiden
- drehen
- spiegeln
- Horizont ausrichten
- Größe ändern
- Helligkeit
- Kontrast
- Sättigung
- Farbtemperatur
- Schärfen
- einfache Auto-Optimierung
- Vorher/Nachher
- Undo / Redo

Standardmäßig nicht-destruktiv.

### Collage erstellen

Funktionen:

- mehrere Bilder auswählen
- Layout-Vorlagen
- Drag & Drop
- Bildflächen verschieben und skalieren
- Crop / Zoom pro Bild
- Abstände
- Ränder
- Hintergrund
- Textfelder
- Titel / Datum
- Druckformate
- Social-Media-Formate
- Export als JPEG / PNG / PDF

### Stapelbearbeitung

Mehrere Bilder gleichzeitig:

- verkleinern
- drehen
- Format ändern
- Qualität optimieren
- Metadaten entfernen
- für Web / Mail / Messenger exportieren

### Bildexport / Optimierung

Mögliche Formate:

- JPEG
- PNG
- WebP
- AVIF
- JPEG XL

Presets:

- Originalqualität
- Für Web
- Für E-Mail
- Für Messenger
- Für Druck
- Für Archiv

---

## Audio

```text
Audio

[ Tags bearbeiten ]
[ Album prüfen ]
```

### Tags bearbeiten

Bündelt:

- Audio Tag
- Cover Manager
- Audio Renamer

Funktionen:

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

Zusätzlich:

- Cover einbetten
- Cover entfernen
- Cover extrahieren
- Tags → Dateiname
- Dateiname → Tags
- Batch-Bearbeitung

### Album prüfen

Bündelt:

- Album Analyzer
- Album Cleaner

Beispiel:

```text
12 Titel

✓ Tracknummern vollständig
⚠ 2 unterschiedliche Album-Namen
⚠ 1 Titel ohne Cover
⚠ 3 verschiedene Genres

[ Vereinheitlichen ]
```

Später eventuell MusicBrainz-Unterstützung.

---

## Video

Video kann später schrittweise ergänzt werden.

Mögliche Funktionen:

- einfache Schnitte
- drehen
- konvertieren
- komprimieren
- Container wechseln
- Audio extrahieren
- Stapelverarbeitung

Kein vollständiger Videoschnitt-Editor geplant.

---

# 5. Sichern

Der Bereich wird nicht als klassische Backup-Suite gedacht, sondern als Werkzeug für **redundante, überprüfbare Sicherungen**.

```text
Sichern

[ Sicherungsstatus ]
[ Sicherung erstellen ]
[ Wiederherstellen ]
```

## Sicherungsstatus

Bündelt:

- Backup Analyzer
- Backup Verify

Beispiel:

```text
Fotos

Original       18.427 Dateien
Festplatte     18.427 Dateien   ✓
NAS            18.421 Dateien   ⚠
Online         18.427 Dateien   ✓
```

Mögliche Prüfungen:

- Datei vorhanden
- Hash korrekt
- Datei lesbar
- Datei verändert
- Datei fehlt
- Sicherung unvollständig

## Sicherung erstellen

Mögliche Ziele:

- externe Festplatte
- Ordner
- NAS
- Netzlaufwerk
- später Online-Speicher

Mögliche Modi:

```text
Originalgetreu
Dedupliziert
Cloud-optimiert
```

### Cloud-optimierte Sicherung

Mögliche Optimierungen:

- Deduplizierung
- Chunking
- Kompression
- Verschlüsselung
- Integritätsprüfung
- JPEG → JPEG XL Lossless Transcoding

Beispiel:

```text
JPEG verlustfrei als JPEG XL speichern

Geschätzte Ersparnis:
38,4 GB

✓ Ursprüngliche JPEG-Dateien vollständig rekonstruierbar
```

Die lokale Bibliothek bleibt dabei unverändert.

## Wiederherstellen

```text
Sicherung auswählen
↓
Dateien auswählen
↓
Ziel auswählen
↓
Wiederherstellen
↓
Hashes überprüfen
```

Bei verlustfrei optimierten JPEG-Backups kann aus JXL wieder das ursprüngliche JPEG rekonstruiert werden.

---

# 6. Werkzeuge

Hier landen kleinere Funktionen, die keinen eigenen Hauptbereich benötigen.

```text
Werkzeuge

├── Metadaten
└── Konvertieren
```

## Metadaten

Bündelt:

- Metadata Cleaner
- Date Fixer
- Metadata Fixer

Mögliche Bereiche:

```text
[ Prüfen ]
[ Korrigieren ]
[ Entfernen ]
```

Funktionen:

- GPS entfernen
- Kameradaten entfernen
- EXIF bereinigen
- Dateidatum korrigieren
- EXIF-Zeitstempel korrigieren
- Metadaten ergänzen
- sensible Informationen entfernen

## Konvertieren

Ein gemeinsamer Medien-Konverter.

### Bilder

- JPEG
- PNG
- WebP
- AVIF
- JPEG XL
- HEIC

### Video

- MP4
- MKV
- MOV
- verschiedene Codecs

### Audio

- MP3
- FLAC
- AAC
- Opus
- WAV
- weitere Formate

---

# 7. Einstellungen

Mögliche Bereiche:

- Darstellung
- Sprache
- Standardpfade
- Cache
- Performance
- Updates
- Erweiterungen
- Datenschutz
- Portable-Modus

Add-ons / Erweiterungen gehören nicht als eigener Hauptpunkt in die Navigation.

Der Nutzer muss nicht wissen, ob eine Funktion intern als AddOn implementiert ist.

---

# Sichtbare Struktur statt Modulflut

Intern darf Elbwald Digital viele einzelne Module besitzen.

Beispielsweise:

- Media Analyzer
- Image Analyzer
- Video Analyzer
- Audio Analyzer
- Media Inspector
- Library Health
- Duplicate Finder
- Photo Sort
- Video Organizer
- Media Renamer
- Media Importer
- Metadata Cleaner
- Date Fixer
- Image Editor
- Image Optimizer
- Collage Maker
- Media Converter
- Audio Tag
- Album Cleaner
- Cover Manager
- Backup Analyzer
- Backup Verify
- Backup Engine
- Restore
- JPEG-XL-Optimierung

Der Nutzer sieht davon aber nur wenige klare Bereiche.

---

# Finale Navigationsstruktur

```text
ELBWALD DIGITAL
│
├── 🏠 Start
│
├── 🔍 Analysieren
│   ├── Übersicht
│   ├── Dateien
│   ├── Dubletten
│   └── Probleme
│
├── 📁 Organisieren
│   ├── Importieren
│   ├── Sortieren
│   └── Umbenennen
│
├── ✏️ Bearbeiten
│   ├── Bilder
│   │   ├── Bildeditor
│   │   ├── Collagen
│   │   └── Stapelbearbeitung
│   │
│   ├── Audio
│   │   ├── Tags
│   │   └── Album prüfen
│   │
│   └── Video
│       └── spätere Bearbeitungsfunktionen
│
├── 💾 Sichern
│   ├── Sicherungsstatus
│   ├── Sicherung erstellen
│   └── Wiederherstellen
│
├── 🔧 Werkzeuge
│   ├── Metadaten
│   └── Konvertieren
│
└── ⚙️ Einstellungen
```

---

# Leitgedanke

Die Sidebar beantwortet:

> **Was möchte ich tun?**

Erst innerhalb eines Bereichs wird bei Bedarf unterschieden:

> **Mit welcher Art Medium?**

Dadurch bleibt die App übersichtlich, auch wenn später viele weitere Funktionen dazukommen.
