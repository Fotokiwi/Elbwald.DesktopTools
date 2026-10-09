# Elbwald Digital – Ergänzende Feature-Absprachen

Dieses Dokument ergänzt die bestehende App- und Feature-Roadmap um die zuletzt besprochenen Entscheidungen rund um **Bildbearbeitung, RAW und Audio**.

---

# 1. Bildbearbeitung

Die Bildbearbeitung soll kein reines Mini-Werkzeug zum Zuschneiden sein, sondern ein **vollwertiger, aber bewusst übersichtlicher Fotoeditor**.

Zielrichtung:

> **Foto entwickeln und optimieren – nicht Photoshop nachbauen.**

## Geplanter Funktionsumfang

### Geometrie

- Zuschneiden
- Drehen
- Spiegeln
- Horizont begradigen
- freie und feste Seitenverhältnisse
- Größenänderung

### Licht

- Belichtung
- Kontrast
- Lichter
- Tiefen
- Weißpunkt
- Schwarzpunkt
- Gamma / Tonwerte

### Farbe

- Weißabgleich
- Farbtemperatur
- Farbton
- Sättigung
- Dynamik / Vibrance
- einzelne RGB-Kanäle
- HSL-Bearbeitung einzelner Farbbereiche

### Schwarzweiß

Nicht nur einfache Entsättigung, sondern kontrollierte Schwarzweiß-Umwandlung mit Kanalsteuerung.

Beispiel:

```text
Rot      +25
Orange   +15
Gelb      +5
Grün     -10
Blau     -30
```

### Tonkurven

- Gesamt-Kurve
- Rot
- Grün
- Blau

### Details

- Nachschärfen
- Klarheit
- einfache Rauschreduzierung

### Effekte

- Vignette
- Körnung
- Stil-Presets

### Analyse / Anzeige

- Histogramm
- optional RGB-Histogramm
- Hinweise auf abgeschnittene Lichter / Schatten
- Vorher-/Nachher-Vergleich

---

# 2. Presets

Presets sind ausdrücklich Teil des Editors.

Beispiele:

- Natürlich
- Landschaft
- Porträt
- Warm
- Kühl
- Kräftig
- Matt
- Schwarzweiß
- Schwarzweiß kontrastreich
- Vintage

Eigene Presets sollen speicherbar und auch auf mehrere Bilder gleichzeitig anwendbar sein.

---

# 3. Non-Destructive Editing

Die Bildbearbeitung soll standardmäßig **nicht-destruktiv** arbeiten.

Das Original bleibt unangetastet. Intern werden nur Bearbeitungsschritte gespeichert:

```text
Original.jpg

Bearbeitungen:
├── Crop
├── Rotation +1,2°
├── Exposure +0,35 EV
├── Highlights -22
├── Shadows +18
├── Temperature +250 K
├── Saturation +4
└── Sharpening +15
```

Vorteile:

- Undo / Redo
- Bearbeitung zurücksetzen
- Vorher / Nachher
- Presets
- Einstellungen auf andere Bilder übertragen
- Export erst am Ende

---

# 4. Interne Bildverarbeitung

Die Verarbeitung soll möglichst hochwertig erfolgen:

- mindestens 16-Bit- bzw. Floating-Point-Verarbeitung
- sauberes Farbmanagement
- lineare Verarbeitung, wo sinnvoll
- Vorschau und finale Ausgabe getrennt behandeln

Grobe Pipeline:

```text
Original
↓
Decoder
↓
Color Management
↓
Floating-Point Image Buffer
↓
Geometrie
↓
Belichtung
↓
Weißabgleich
↓
Tonwerte
↓
Farbe / HSL
↓
Details
↓
Effekte
↓
Preview
```

Export:

```text
Bearbeitungspipeline
↓
Resize
↓
Output Color Space
↓
JPEG / PNG / WebP / AVIF / JXL
```

---

# 5. RAW-Unterstützung

RAW wird als **eigene Ausbaustufe** des Bildeditors betrachtet.

Perspektivisch relevante Formate:

```text
.CR2
.CR3
.NEF
.ARW
.ORF
.RAF
.DNG
```

RAW benötigt zusätzliche Verarbeitung:

- Demosaicing
- Weißabgleich aus Kameradaten
- Kameraprofile
- Objektivkorrekturen
- Highlight Recovery
- Farbraumtransformation
- RAW-spezifische Rauschbehandlung

Technisch soll möglichst auf bewährte Bibliotheken wie `LibRaw` zurückgegriffen werden.

## Entscheidung

**Bildbearbeitung und RAW sind als Features ausreichend stark.**

Eine zusätzliche Gesichtserkennung oder automatische Personenverwaltung ist aktuell nicht notwendig.

---

# 6. Collagen

Collagen bleiben Teil des Bildbereichs.

Mögliche Funktionen:

- mehrere Bilder auswählen
- Layout-Vorlagen
- Drag & Drop
- frei verschiebbare Bildflächen
- Zoom / Crop pro Bild
- Abstände
- Ränder
- Hintergrund
- Textfelder
- Titel und Datum
- Druckformate
- Social-Media-Formate
- Export als JPEG / PNG / PDF

---

# 7. Audio – Grundausrichtung

Audio soll deutlich mehr können als reines Tagging, aber **keine komplexe DAW** werden.

Ziel:

> **Audacity-Funktionen für Menschen, die Audacity nicht lernen wollen.**

Die Oberfläche soll typische Aufgaben in verständlicher Sprache anbieten und technische Details zunächst verbergen.

---

# 8. Audio-Bibliothek

## Tags bearbeiten

Der Tag-Bereich bündelt:

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
- Batch-Bearbeitung
- Tags → Dateiname
- Dateiname → Tags

## Album prüfen

Funktionen:

- inkonsistente Album-Namen erkennen
- fehlende Tracknummern erkennen
- fehlende Cover erkennen
- verschiedene Genres innerhalb eines Albums anzeigen
- Metadaten vereinheitlichen

Beispiel:

```text
12 Titel

✓ Tracknummern vollständig
⚠ 2 unterschiedliche Album-Namen
⚠ 1 Titel ohne Cover
⚠ 3 verschiedene Genres

[ Vereinheitlichen ]
```

---

# 9. CD-Import

**CD-Import wird als wichtiges Kernfeature des Audio-Bereichs eingeplant.**

Besonders wichtig ist die Verbindung mit sauberem Tagging.

Geplanter Workflow:

```text
Audio-CD einlegen
↓
Albuminformationen ermitteln
↓
Tracks auswählen
↓
Zielformat auswählen
↓
Rippen
↓
Tags und Cover setzen
↓
Dateien sauber benennen
↓
Ordnerstruktur erzeugen
```

Beispiel:

```text
Interpret/
└── 1973 - Album/
    ├── 01 - Titel.flac
    ├── 02 - Titel.flac
    └── ...
```

Mögliche Zielformate:

- FLAC
- MP3
- AAC
- Opus

Perspektivisch sinnvoll:

- optionale Online-Metadaten, z. B. MusicBrainz
- Cover-Suche
- AccurateRip-artige Verifikation
- Prüfen, ob ein Rip fehlerfrei gelesen wurde

Online-Dienste bleiben optional.

---

# 10. Audio Analyzer

Der allgemeine Media Analyzer soll Audio ausführlich untersuchen können.

Mögliche Informationen:

- Codec
- Bitrate
- VBR / CBR
- Sample Rate
- Bit Depth
- Channels
- Dauer
- Tags
- Cover
- ReplayGain
- Peak
- Loudness / LUFS
- Dateigröße
- technische Auffälligkeiten

Beispiel:

```text
Codec          FLAC
Sample Rate    44,1 kHz
Bit Depth      16 Bit
Channels       Stereo
Dauer          04:32
Peak           -0,3 dBFS
Loudness       -13,8 LUFS
ReplayGain     nicht vorhanden
```

---

# 11. Lautstärke und ReplayGain

ReplayGain soll ausdrücklich von echter Audiobearbeitung getrennt werden.

## ReplayGain

- Audiodaten bleiben unverändert
- nur Metadaten werden geschrieben
- Lautstärke kann in unterstützten Playern angeglichen werden

## Normalisierung

- Audiodaten werden tatsächlich verändert
- Ziel-Lautheit kann festgelegt werden
- geeignet für Exporte und Aufnahmen

Die UI soll diesen Unterschied klar erklären.

---

# 12. Einfacher Audioeditor

Der Audioeditor soll typische Alltagsaufgaben abdecken.

## Schnitt

- Anfang / Ende abschneiden
- Bereich ausschneiden
- Auswahl exportieren
- Stille am Anfang / Ende entfernen

## Übergänge

- Fade-In
- Fade-Out

## Lautstärke

- lauter / leiser
- normalisieren
- Lautstärke angleichen
- ReplayGain berechnen

## Kanäle

- Stereo → Mono
- Kanäle tauschen

---

# 13. Klangbearbeitung

## Equalizer

Ein einfacher grafischer EQ soll möglich sein.

```text
20 Hz   60   120   250   500   1k   2k   4k   8k   16k
 |       |     |     |     |    |    |    |    |     |
 0 dB ─────────────────────────────────────────────────
```

Damit lassen sich Frequenzbereiche anheben oder absenken.

Typische Anwendungen:

- Bass reduzieren oder anheben
- Höhen absenken
- dumpfen Klang aufhellen
- harsche Mitten reduzieren

## Parametrischer EQ

Als erweiterte Funktion:

- Frequenz
- Gain
- Bandbreite / Q

---

# 14. Audio-Restauration

Ein eigener Bereich `Restauration` ist sinnvoll.

Mögliche Werkzeuge:

- Brummen entfernen
- tieffrequentes Rumpeln entfernen
- hochfrequentes Rauschen reduzieren
- Bandrauschen reduzieren
- Klicks / Knackser reduzieren
- Stille entfernen
- Clipping erkennen

## Typische Filter

### Notch Filter

Zum Beispiel gegen:

- 50-Hz-Netzbrummen
- 60-Hz-Netzbrummen
- schmale störende Frequenzen

### High-Pass

Zum Entfernen von:

- tieffrequentem Rumpeln
- Trittschall
- unnötigen Subbass-Anteilen

### Low-Pass / High-Cut

Zum Reduzieren von:

- starkem Hochfrequenzrauschen
- Zischen

---

# 15. Klassische Rauschreduzierung

Für gleichmäßiges Rauschen reicht ein EQ nicht immer aus.

Mögliche klassische Verfahren:

- Noise Gate
- spektrale Subtraktion
- FFT-basierte Rauschminderung
- Noise Profile

Workflow:

```text
Rauschstelle markieren
↓
Rauschprofil erfassen
↓
gesamte Aufnahme analysieren
↓
ähnliche Frequenzanteile reduzieren
```

Keine KI notwendig.

Wichtig: Rauschminderung kann immer auch Nutzsignal beeinflussen. Daher immer:

- Vorschau
- A/B-Vergleich
- Intensität
- Undo / Redo
- non-destructive Bearbeitung

---

# 16. Spektralanalyse

Neben der Wellenform soll optional ein Spektrogramm verfügbar sein.

```text
20 kHz │░░░░░░░░░░░░░░░
       │░░▒▒▒▒▒▒▒▒▒░░░
       │▒▓██████▓▒░
       │██████████
 20 Hz └────────────────
            Zeit
```

Anwendungsfälle:

- Pfeiftöne erkennen
- Brummen lokalisieren
- hochfrequentes Rauschen erkennen
- auffällige Frequenzbereiche sehen
- mögliche Lossy-Transcodes erkennen

Starke spätere UX-Idee:

```text
Störende Frequenz im Spektrum anklicken
↓
"Absenken"
↓
Filter automatisch vorbereiten
```

---

# 17. Qualitätsanalyse

Der Analyzer kann Auffälligkeiten melden, ohne absolute Behauptungen aufzustellen.

Beispiel:

```text
⚠ Auffälliges Frequenzspektrum
```

statt:

```text
✗ Diese FLAC-Datei ist Fake
```

Mögliche Hinweise:

- ungewöhnlicher Frequenz-Cutoff
- möglicher früherer Lossy-Encode
- Clipping
- starke Lautstärkeschwankungen
- beschädigte Frames
- ungewöhnliche Sample-Rate innerhalb eines Albums

---

# 18. Audio-Konvertierung

Batch-Konvertierung soll Teil des Audio-Bereichs sein.

Mögliche Formate:

```text
FLAC
MP3
AAC
Opus
WAV
M4A
```

Statt technischer Encoder-Parameter sollen verständliche Presets angeboten werden:

- Maximale Qualität
- Für Auto / USB-Stick
- Für Smartphone
- Kleine Dateien
- Verlustfrei

Technische Details bleiben unter `Erweitert` verfügbar.

---

# 19. FLAC-Optimierung

FLAC-Dateien können ohne Qualitätsverlust neu komprimiert werden.

Dabei bleibt das Audiomaterial bitgenau erhalten.

Möglicher Nutzen:

- ältere FLAC-Dateien mit geringer Kompressionsstufe optimieren
- etwas Speicherplatz sparen
- Batch-Verarbeitung großer Archive

Leitgedanke:

> **Platz sparen, ohne Qualität anzutasten.**

---

# 20. CUE und lange Aufnahmen

Perspektivisch sinnvoll:

- `.cue`-Dateien lesen
- lange FLAC/WAV-Aufnahmen in einzelne Tracks teilen
- Marker manuell setzen
- Bereiche als einzelne Dateien exportieren
- Tags aus CUE übernehmen

Beispiel:

```text
Konzert.flac
Konzert.cue
↓
01 - Intro.flac
02 - Song.flac
03 - Song.flac
...
```

---

# 21. Bedienphilosophie des Audioeditors

Die Oberfläche soll nicht wie eine klassische DAW wirken.

Drei Bedienebenen:

## Einfach

Große, verständliche Aktionen:

- Rauschen reduzieren
- Brummen entfernen
- Stimme klarer machen
- Lautstärke angleichen
- Anfang / Ende abschneiden
- Höhen reduzieren
- Bass verstärken

## Anpassen

Wenige verständliche Regler.

Beispiel:

```text
Rauschen reduzieren

[ Schwach ] [ Mittel ] [ Stark ]

Intensität
────────●──────

☑ Stimme möglichst natürlich erhalten

[ Vorschau hören ]
```

## Erweitert

Technische Parameter für erfahrene Nutzer:

- Frequenz
- Q
- Gain
- Threshold
- Attack
- Release
- FFT Size
- Noise Floor

---

# 22. Presets im Audio-Bereich

## Sprache

- Podcast
- Sprachmemo
- Interview
- Stimme klarer
- Hintergrundrauschen reduzieren

## Musik

- Warm
- Klar
- Mehr Bass
- Weniger Höhen
- Vinyl-Aufnahme säubern

## Restauration

- Kassette
- Vinyl
- alte Sprachaufnahme
- Netzbrummen 50 Hz

Presets sind technisch gespeicherte Filterketten.

---

# 23. A/B-Vergleich

A/B-Vergleich ist für Audiobearbeitung Pflicht.

Der Nutzer soll jederzeit zwischen:

```text
Original
↔
Bearbeitet
```

umschalten können.

Zusätzlich sinnvoll:

> 5 Sekunden Vorschau ab aktueller Position

---

# 24. Analyzer und Editor verbinden

Der Analyzer soll direkt passende Reparaturaktionen anbieten können.

Beispiele:

```text
⚠ Deutliches 50-Hz-Brummen erkannt

[ Brummen reduzieren ]
```

```text
⚠ Auffällige Lautstärkeunterschiede

[ Lautstärke angleichen ]
```

```text
⚠ Spitzen erreichen 0 dBFS

[ Clipping prüfen ]
```

Damit muss der Nutzer technische Probleme nicht selbst interpretieren.

---

# 25. Sichtbare Audio-Struktur

Der Audio-Bereich soll trotz vieler Funktionen übersichtlich bleiben.

```text
Bearbeiten → Audio

├── Bibliothek
│   ├── CD importieren
│   ├── Tags bearbeiten
│   ├── Album prüfen
│   └── Cover verwalten
│
├── Bearbeiten
│   ├── Schneiden
│   ├── Lautstärke
│   ├── Klang
│   ├── Equalizer
│   └── Restauration
│
└── Konvertieren
    ├── FLAC
    ├── MP3
    ├── AAC
    └── Opus
```

Der Audio Analyzer bleibt im allgemeinen Bereich `Analysieren`, weil dort Bilder, Videos und Audio gemeinsam untersucht werden.

---

# Zusammenfassung der neuen Entscheidungen

## Bilder

- umfangreicher Fotoeditor statt Mini-Editor
- non-destructive Workflow
- Belichtung, Farbe, HSL, Kanäle, Schwarzweiß, Kurven, Schärfen usw.
- Presets
- Histogramm
- hochwertige interne Verarbeitung
- RAW als wichtige spätere Ausbaustufe
- Collagen bleiben Teil des Bildbereichs
- Gesichtserkennung ist derzeit nicht notwendig

## Audio

- Tagging bleibt wichtig, ist aber nur ein Teilbereich
- CD-Import wird Kernfeature
- Audio Analyzer wird umfangreich
- einfacher, anfängerfreundlicher Audioeditor
- Equalizer und parametrischer EQ
- Spektralanalyse
- Frequenzen gezielt absenken
- klassische Rauschreduzierung ohne KI
- Restauration für Brummen, Rauschen, Rumpeln und Knackser
- ReplayGain und Lautheitsanalyse
- Batch-Konvertierung
- FLAC-Optimierung
- CUE / lange Aufnahmen teilen
- A/B-Vergleich und Vorschau
- technische Details nur unter `Erweitert`

## Leitgedanke

Elbwald Digital soll keine Profi-DAW und kein Photoshop-Ersatz werden.

Stattdessen:

> **Leistungsfähige Medienwerkzeuge, die auch ohne Fachwissen verständlich bleiben.**
