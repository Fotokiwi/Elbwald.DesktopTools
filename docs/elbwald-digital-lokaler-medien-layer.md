# Elbwald Digital – Lokaler Medien-Layer über dem Dateisystem

## Grundidee

Elbwald Digital soll Medien nicht in ein eigenes geschlossenes Bibliothekssystem einsperren.

Stattdessen werden zwei Ebenen klar getrennt:

> **Dateisystem = physische Ordnung**  
> **Elbwald = logische Ordnung**

Die Dateien bleiben ganz normale Dateien und Ordner auf dem Rechner, NAS, externen Laufwerken oder später Online-Speichern.

Elbwald verwaltet zusätzlich lokale Referenzen, Projekte, Bewertungen, Tags, Bearbeitungen und Sicherungsinformationen.

---

# Leitgedanke

> **Elbwald besitzt niemals deine Medien. Es kennt sie nur.**

Das bedeutet:

- keine proprietäre Medienablage
- kein Importzwang in eine interne Bibliothek
- keine Cloud-Abhängigkeit
- keine Accountpflicht
- kein Vendor-Lock-in
- Medien bleiben auch ohne Elbwald vollständig nutzbar

Wird Elbwald deinstalliert, bleiben alle Fotos, Videos und Audiodateien dort, wo sie vorher waren.

---

# 1. Physische Ebene – das Dateisystem

Die eigentlichen Mediendateien liegen ganz normal im Dateisystem.

Beispiel:

```text
D:\Fotos\2030\08\14\IMG_1234.jpg
D:\Fotos\2030\08\14\IMG_1235.jpg
D:\Fotos\2030\08\14\VID_1236.mp4
```

Elbwald kann diese Struktur auf Wunsch aktiv pflegen:

- Ordner anlegen
- Dateien sortieren
- Dateien verschieben
- Dateien kopieren
- Dateien umbenennen
- Metadaten schreiben
- Sicherungen erstellen
- Integrität prüfen

Die physische Ordnung bleibt damit weiterhin verständlich und außerhalb von Elbwald nutzbar.

---

# 2. Logische Ebene – Elbwald

Über den realen Dateien liegt eine lokale Referenzebene.

Ein Medium wird einmal physisch gespeichert, kann aber logisch an vielen Stellen gleichzeitig auftauchen.

Beispiel:

```text
IMG_1234.jpg

Zugeordnet zu:
- Italien 2030
- Familie
- Beste Fotos 2030
- Fotobuch Italien
- Kalender 2031
- Zu drucken
```

Es entstehen dabei **keine zusätzlichen Kopien der Datei**.

Die Projekte und Sammlungen speichern lediglich Referenzen.

---

# 3. Ein Medium – mehrere Projekte

Ein Foto kann gleichzeitig zu beliebig vielen Projekten gehören.

Beispiel:

```text
Physische Datei:

D:\Fotos\2030\08\14\IMG_1234.jpg
```

Logische Zuordnung:

```text
Italien 2030
└── IMG_1234.jpg

Familienfavoriten
└── IMG_1234.jpg

Fotobuch Italien
└── IMG_1234.jpg

Kalender 2031
└── IMG_1234.jpg
```

Die Datei selbst existiert weiterhin nur einmal.

Das ist ein zentraler Vorteil gegenüber einer rein ordnerbasierten Organisation.

---

# 4. Projekte und Ereignisse

Elbwald kann virtuelle Projekte bzw. Ereignisse verwalten.

Beispiele:

- Urlaub
- Geburtstag
- Hochzeit
- Familienfeier
- Ausflug
- Veranstaltung
- Fotobuch
- Kalender
- freie Sammlung

Beispiel:

```text
Projekt:
Italien 2030

Zeitraum:
03.08.2030 – 17.08.2030

Ort:
Italien

Kategorie:
Urlaub
```

Elbwald kann anhand dieser Informationen automatisch passende Medien vorschlagen.

---

# 5. Automatische Medienzuordnung

Eine Projektzuordnung kann über klassische, lokal auswertbare Informationen erfolgen.

Mögliche Kriterien:

- EXIF-Aufnahmedatum
- Dateidatum als Fallback
- GPS-Koordinaten
- vorhandene Tags
- Dateipfad
- Ordnername
- Kamera / Gerät
- manuelle Ein- und Ausschlüsse

Beispiel:

```text
Projekt:
Italien 2030

Zeitraum:
03.08.2030 – 17.08.2030
```

Elbwald analysiert die Bibliothek:

```text
1.284 Fotos
83 Videos
7 Audiodateien

passen wahrscheinlich zu diesem Ereignis.
```

Der Nutzer bestätigt oder korrigiert die Auswahl.

Keine KI ist dafür notwendig.

---

# 6. Projektinformationen und Dateimetadaten bleiben getrennt

Eine Zuordnung zu einem Elbwald-Projekt verändert die Originaldatei zunächst nicht.

Beispiel:

```text
Projekt:
Italien 2030

Elbwald-Informationen:
- Urlaub
- Italien
- August 2030
```

Optional kann der Nutzer später wählen:

> Diese Informationen auch in die Dateien schreiben

Dann können geeignete Metadaten aktualisiert werden.

Mögliche Formate:

- EXIF
- IPTC
- XMP
- passende Audio-Tags
- passende Video-Metadaten

Damit bleibt die Entscheidung beim Nutzer.

---

# 7. Lokale Medien-Datenbank

Für die Referenzebene wird perspektivisch eine lokale Datenbank benötigt.

Naheliegend wäre beispielsweise:

```text
Elbwald.db
```

auf Basis von SQLite.

Mögliche Tabellen / Bereiche:

```text
MediaItems
Projects
ProjectItems
Tags
Ratings
Favorites
AnalysisCache
BackupStatus
EditRecipes
StorageLocations
Metadata
```

Die Datenbank enthält Referenzen und Zusatzinformationen.

Die Mediendateien selbst bleiben außerhalb.

---

# 8. Beispiel eines MediaItem

Ein Eintrag könnte intern ungefähr folgende Informationen besitzen:

```text
MediaItem

ID
Path
Filename
FileSize
Hash
MediaType
Created
Modified

Metadata
AnalysisStatus
Rating
Favorite
EditRecipe
BackupStatus
```

Zusätzlich:

```text
Projects:
- Italien 2030
- Familienfavoriten
- Fotobuch Italien
```

---

# 9. Virtuelle Sammlungen

Neben klassischen Projekten sind einfache virtuelle Sammlungen denkbar.

Beispiele:

- Favoriten
- 5-Sterne-Fotos
- Zu drucken
- Noch bearbeiten
- Beste Fotos des Jahres
- Familienbilder
- Landschaft
- Fotobuch-Auswahl
- Nicht gesichert
- Ohne Aufnahmedatum

Diese Sammlungen müssen keine Ordner im Dateisystem erzeugen.

---

# 10. Bewertungen und Favoriten

Elbwald kann Medien bewerten, ohne die Datei physisch zu verändern.

Beispiel:

```text
IMG_1234.jpg

★★★★★
Favorit: Ja
Status: Bearbeitet
```

Diese Informationen können zunächst nur lokal gespeichert werden.

Optional können geeignete Werte später in XMP/IPTC geschrieben werden.

---

# 11. Sichtungsmodus

Gerade nach Urlauben oder Veranstaltungen kann ein schneller Auswahlmodus hilfreich sein.

Beispiel:

```text
← vorheriges       nächstes →

        FOTO

✕ Ablehnen
✓ Behalten
★ Favorit
```

Tastatursteuerung:

```text
1 = Ablehnen
2 = Behalten
3 = Favorit
```

Danach könnte eine Sammlung beispielsweise so aussehen:

```text
1.367 Medien

928 normal
284 behalten
105 Favoriten
50 aussortiert
```

Diese Auswahl kann anschließend für:

- Fotobücher
- Collagen
- Exporte
- Kalender
- Sicherungen

verwendet werden.

---

# 12. Bearbeitungsrezepte

Nicht-destruktive Bearbeitungen können ebenfalls referenzbasiert gespeichert werden.

Beispiel:

```text
Original:
IMG_1234.jpg

Bearbeitungsrezept:
- Crop
- Rotation +1,2°
- Exposure +0,35
- Highlights -22
- Saturation +4
```

Das Original bleibt unverändert.

Elbwald rendert daraus bei Bedarf:

- Vorschau
- Export
- Fotobuch-Version
- Web-Version
- Druck-Version

---

# 13. Projekte als Content Studio

Projekte können zum zentralen Einstieg für kreative Workflows werden.

Beispiel:

```text
ITALIEN 2030

03.–17. August 2030
1.367 Medien

[ Übersicht ]
[ Fotos ]
[ Videos ]
[ Favoriten ]
[ Gestalten ]
[ Exportieren ]
```

Unter `Gestalten`:

- Collage
- Fotobuch
- Kalender
- Kontaktbogen
- Diashow
- weitere kreative Ausgaben

Unter `Exportieren`:

- Originaldateien
- verkleinerte Kopien
- Web-Ausgabe
- Druck-Ausgabe
- ZIP
- PDF
- Ordnerstruktur

---

# 14. Automatische Tages- und Ortsgruppen

Projekte können Medien automatisch weiter strukturieren.

Beispiel nach Tagen:

```text
Italien 2030
│
├── 03.08. – Anreise
├── 04.08.
├── 05.08.
├── 06.08.
└── 17.08. – Rückreise
```

Der Nutzer kann Gruppen anschließend frei umbenennen.

Beispiel:

```text
05.08.
↓
Tagesausflug nach Florenz
```

Bei vorhandenen GPS-Daten sind auch Ortsgruppen möglich:

```text
Rom
Florenz
Pisa
Toskana
```

---

# 15. Medienübergreifende Projekte

Ein Projekt besteht nicht nur aus Fotos.

Ein Urlaub kann enthalten:

```text
1.184 Fotos
176 Videos
7 Audioaufnahmen
```

Elbwald behandelt diese Medien gemeinsam.

Damit entstehen keine getrennten Foto-, Video- und Audio-Welten.

---

# 16. Verbindung mit Backup und Redundanz

Projekte können auch ihren Sicherungsstatus anzeigen.

Beispiel:

```text
Italien 2030

✓ Originalbibliothek
✓ externe HDD
✓ NAS
⚠ Online: 83 % gesichert
```

Oder:

> Dieses Projekt zusätzlich auf Reise-SSD sichern

Die Projektansicht wird damit auch ein sinnvoller Einstieg in Export und Backup.

---

# 17. Umgang mit verschobenen Dateien

Da Nutzer ihre Dateien weiterhin frei außerhalb von Elbwald bearbeiten dürfen, muss Elbwald robust mit Pfadänderungen umgehen.

Ein Medium sollte daher nicht ausschließlich über den Dateipfad identifiziert werden.

Mögliche Identifikatoren:

- interne Media-ID
- Dateihash
- Dateigröße
- Metadaten
- Dateiname
- bekannte frühere Pfade

Beispiel:

```text
D:\Fotos\Unsorniert\IMG_1234.jpg
```

wird außerhalb von Elbwald verschoben nach:

```text
E:\Archiv\2030\Italien\IMG_1234.jpg
```

Elbwald sollte die Datei möglichst wiederfinden und die Referenz reparieren können.

---

# 18. Referenz-Reparatur

Wenn eine Datei fehlt:

```text
⚠ IMG_1234.jpg wurde nicht gefunden
```

mögliche Aktionen:

- automatisch suchen
- Speicherorte durchsuchen
- anhand Hash wiederfinden
- neuen Pfad manuell auswählen
- Referenz entfernen

Damit bleibt die logische Ebene robust, obwohl der Nutzer volle Kontrolle über das Dateisystem behält.

---

# 19. Portable Projekte und Export

Virtuelle Projekte sollten nicht dauerhaft an eine einzige lokale Elbwald-Datenbank gebunden sein.

Perspektivisch sinnvoll:

```text
Projekt exportieren
```

Beispielsweise als:

```text
Italien-2030.elbwald
```

oder als offenes JSON-/ZIP-basiertes Format.

Enthalten:

- Projektinformationen
- Medienreferenzen
- Bewertungen
- Tags
- Bearbeitungsrezepte
- Sammlungen
- Layoutinformationen

Optional können die Originalmedien mit exportiert werden.

---

# 20. Datenschutz und Offline First

Die gesamte Referenz- und Projektlogik funktioniert lokal.

Keine Pflicht zu:

- Benutzerkonto
- Cloud
- KI-Diensten
- externer Datenbank
- proprietärer Online-Bibliothek

Online-Dienste bleiben optional und werden nur für konkrete Funktionen genutzt.

Beispiele:

- MusicBrainz
- Online-Backup
- optionale Metadatenabfragen

---

# 21. Konsequenz für die Architektur

Die App besteht konzeptionell aus drei Ebenen:

```text
┌─────────────────────────────┐
│      Elbwald Projekte       │
│  Tags · Ratings · Layouts   │
│  Bearbeitungen · Backups    │
└──────────────┬──────────────┘
               │ Referenzen
┌──────────────▼──────────────┐
│       Media Index / DB      │
│  Pfade · Hashes · Metadata  │
└──────────────┬──────────────┘
               │
┌──────────────▼──────────────┐
│        Dateisystem          │
│ Bilder · Videos · Audio     │
└─────────────────────────────┘
```

---

# 22. Konsequenz für den Nutzer

Der Nutzer kann seine Medien weiterhin völlig unabhängig von Elbwald verwenden.

Er kann:

- Dateien im Explorer / Dateimanager öffnen
- Ordner selbst verschieben
- andere Programme verwenden
- Dateien extern bearbeiten
- Medien manuell sichern

Elbwald ergänzt diese Freiheit um eine komfortable logische Ebene.

---

# 23. Produktidentität

Mit diesem Konzept wird Elbwald nicht nur zu einer Sammlung einzelner Medienwerkzeuge.

Es wird zu einem:

> **lokalen Medien-Workspace über dem normalen Dateisystem**

Die App kann Ordnung schaffen, ohne Kontrolle zu übernehmen.

Sie verbindet:

- Dateisystemorganisation
- Analyse
- Projekte
- virtuelle Sammlungen
- Tagging
- Bearbeitung
- kreative Ausgaben
- Backup
- Redundanz

ohne die Mediendateien in ein geschlossenes System zu zwingen.

---

# Kurzfassung

```text
Dateien bleiben normale Dateien.

Elbwald:
- kennt sie
- analysiert sie
- referenziert sie
- gruppiert sie
- bewertet sie
- bearbeitet sie non-destructive
- organisiert sie
- sichert sie

Ein Medium kann beliebig vielen Projekten angehören,
ohne mehrfach gespeichert zu werden.
```

## Kernprinzip

> **Physische Ordnung im Dateisystem.  
> Logische Freiheit in Elbwald.**
