# Paper

Paper ist ein kleines, selbst gehostetes Dokumentenarchiv für schwache Home-Server. Es nimmt Uploads in einer Inbox an, verarbeitet OCR lokal mit Tesseract und macht Dokumente über PostgreSQL Full Text Search schnell auffindbar.

## Start

    cp .env.example .env
    docker compose up -d

Danach ist Paper unter http://localhost:8080 erreichbar. Ändere die Beispielpasswörter in .env vor dem Einsatz.

## MVP

- PDF, JPG, JPEG, PNG und TIFF
- sicherer Upload mit Dateisignaturprüfung, Größenlimit und SHA-256-Duplikaterkennung
- lokale Ablage unter /data/documents
- Inbox → Prüfung → Archiv
- persistente PostgreSQL-Verarbeitungsjobs mit Retry und Crash-Recovery
- Tesseract OCR und kleine regelbasierte Titel-, Datums- und Tag-Erkennung
- PostgreSQL Full Text Search über Titel, OCR-Text und Tags
- responsive Razor-UI und sichere Cookie-Authentifizierung

Die Anwendung benötigt keine externe Suchengine, Queue oder AI-Komponente. OCR ist optional zur Laufzeit: Fehlt Tesseract, bleibt der Fehler sichtbar und der Job wird mit Retry-Status gespeichert.
