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
- optionaler Consume-Eingang unter /data/consume mit stabiler Dateiprüfung und Fehlerablage
- JSON-/CSV-Metadatenexport aus den Einstellungen

Die Anwendung benötigt keine externe Suchengine, Queue oder AI-Komponente. OCR ist optional zur Laufzeit: Fehlt Tesseract, bleibt der Fehler sichtbar und der Job wird mit Retry-Status gespeichert.

## Datenablage

Neue Uploads landen zunächst unter inbox/. Nach der Prüfung erzeugt Paper beispielsweise:

    Wohnung/Strom/2026-10-05 Stadtwerke Mannheim Rechnung.pdf

Das Verschieben der Datei und die Datenbankänderung werden konsistent behandelt. Bei einem Datenbankfehler wird ein bereits verschobenes Dokument nach Möglichkeit in den Inbox-Pfad zurückgelegt.

Für NAS-Betrieb kann Storage:Provider auf smb gesetzt und Storage:SmbRootPath auf einen erreichbaren UNC-/SMB-Pfad gesetzt werden. WakePolicy: Never vermeidet unnötige Zugriffe auf ein schlafendes NAS; OnDemand ist für eine spätere gezielte Wake-Integration vorbereitet. Lokal bleibt der Provider ohne weitere Abhängigkeiten aktiv.

## Automatischer Import

Dateien können in den gemounteten Ordner /data/consume gelegt werden. Paper wartet, bis eine Datei stabil ist, verschiebt sie intern in einen Verarbeitungspuffer und importiert sie danach in die Inbox. Ungültige Dateien oder Duplikate landen mit einer .error.txt-Begründung unter /data/consume/failed; dadurch entstehen keine wiederholten Fehlversuche.

## Export

Unter Einstellungen können die Dokumentmetadaten jederzeit als JSON oder CSV exportiert werden. Das ZIP-Backup enthält zusätzlich ein Manifest und die Originaldateien. Die Originaldateien bleiben im menschenlesbaren Regal unter /data/documents und benötigen für die Betrachtung keine proprietäre Dateistruktur.
