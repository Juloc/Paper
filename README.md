# Paper

Paper ist ein leichtes, selbst gehostetes Dokumentenarchiv für Home-Server und NAS-Umgebungen. Es nimmt Uploads in einer Inbox an, verarbeitet OCR lokal mit Tesseract und legt abgelegte Dokumente in einer verständlichen Regalstruktur ab, die auch ohne Paper nachvollziehbar bleibt.

## Start

    cp .env.example .env
    docker compose up -d

Danach ist Paper unter http://localhost:8080 erreichbar. Ändere die Beispielpasswörter in .env vor dem Einsatz.

## MVP

- PDF, JPG, JPEG, PNG und TIFF
- sicherer Upload mit Dateisignaturprüfung, Größenlimit und SHA-256-Duplikaterkennung
- lokale, menschenlesbare Ablage unter /data/documents
- Inbox → Metadaten prüfen → Regalordner auswählen → Ablegen
- Regalansicht mit echten Unterordnern und kollisionssicheren Dateinamen
- Korrespondenten, Dokumenttypen, Tags und relationale Custom Fields
- persistente PostgreSQL-Verarbeitungsjobs mit Retry und Crash-Recovery
- Tesseract OCR und kleine regelbasierte Titel-, Datums-, Korrespondenten-, Dokumenttyp- und Tag-Erkennung
- PostgreSQL Full Text Search über Titel, OCR-Text, Dateiname, Regalpfad und Metadaten
- responsive Razor-UI und sichere Cookie-Authentifizierung

Die Anwendung benötigt keine externe Suchengine, Queue oder AI-Komponente. OCR ist optional zur Laufzeit: Fehlt Tesseract, bleibt der Fehler sichtbar und der Job wird mit Retry-Status gespeichert.

## Datenablage

Neue Uploads landen zunächst unter inbox/. Nach der Prüfung erzeugt Paper beispielsweise:

    Wohnung/Strom/2026-10-05 Stadtwerke Mannheim Rechnung.pdf

Das Verschieben der Datei und die Datenbankänderung werden konsistent behandelt. Bei einem Datenbankfehler wird ein bereits verschobenes Dokument nach Möglichkeit in den Inbox-Pfad zurückgelegt.
