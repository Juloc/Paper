# Paper

Paper ist ein leichtes, selbst gehostetes Dokumentenarchiv für Home-Server und NAS-Umgebungen. Es nimmt Uploads in einer Inbox an, verarbeitet OCR lokal mit Tesseract und legt abgelegte Dokumente in einer verständlichen Regalstruktur ab, die auch ohne Paper nachvollziehbar bleibt.

## Start

    cp .env.example .env
    docker compose up -d

Danach ist Paper unter http://localhost:8080 erreichbar. Ändere die Beispielpasswörter in .env vor dem Einsatz.
Die drei Pflichtwerte `PAPER_ADMIN_USERNAME`, `PAPER_ADMIN_PASSWORD` und `PAPER_POSTGRES_PASSWORD` müssen gesetzt sein; Compose bricht bei fehlenden Werten bewusst früh ab.

## Funktionen

- PDF, JPG, JPEG, PNG und TIFF
- sicherer Upload mit Dateisignaturprüfung, Größenlimit und SHA-256-Duplikaterkennung
- lokale, menschenlesbare Ablage unter /data/documents
- Inbox → Metadaten prüfen → Regalordner auswählen → Ablegen
- Inbox-Aktionen für späteres Zurückstellen und Ignorieren ohne Datenverlust
- Regalansicht mit echten Unterordnern und kollisionssicheren Dateinamen
- Korrespondenten, Dokumenttypen, Tags und relationale Custom Fields
- persistente PostgreSQL-Verarbeitungsjobs mit Retry und Crash-Recovery
- dezenter Processing-Status mit Fehlerliste und manuellem Retry
- Tesseract OCR für Bilder sowie PDF-OCR mit eingebettetem Text und Poppler-Rasterisierung als Fallback
- kleine regelbasierte Titel-, Datums-, Korrespondenten-, Dokumenttyp- und Tag-Erkennung
- transparente Regalvorschläge aus wiederkehrenden Benutzerkorrekturen; Vorschläge verändern den physischen Pfad erst beim bewussten Ablegen
- PostgreSQL Full Text Search über Titel, OCR-Text, Dateiname, Regalpfad und Metadaten
- paginierte Inbox- und Bestandsansichten für große Archive
- responsive Razor-UI, sichere Cookie-Authentifizierung und zeitbegrenzter Login-Brute-Force-Schutz ohne externe Infrastruktur
- optionaler Consume-Eingang unter /data/consume mit stabiler Dateiprüfung und Fehlerablage
- JSON-/CSV-Metadatenexport aus den Einstellungen
- vollständiges ZIP-Backup mit Manifest, Dateien, Metadaten und Lernregeln sowie Restore
- Paperless-ngx-Import für Dokumente, OCR-Text, Kataloge, Tags und Custom Fields
- transparente, korrigierbare Lernregeln in den Einstellungen
- manuelle Speicherprüfung gegen die in PostgreSQL registrierten Dokumentpfade und Dateigrößen, auch für direkte SMB-Speicher
- On-Demand-Bild-Thumbnails als lokaler, wegwerfbarer Preview-Cache; PDFs bleiben direkt im Browser betrachtbar

Die Anwendung benötigt keine externe Suchengine, Queue oder AI-Komponente. OCR ist optional zur Laufzeit: Fehlt Tesseract, bleibt der Fehler sichtbar und der Job wird mit Retry-Status gespeichert.
Das Container-Image bringt Poppler, Tesseract und die deutschen/englischen Sprachdaten mit. Ein nicht erreichbares OCR-Programm blockiert den Webserver nicht; der persistente Job bleibt sichtbar und wiederholbar.
Ein einzelner externer OCR-Prozess läuft standardmäßig höchstens fünf Minuten. Das Limit kann über `PAPER_OCR_PROCESS_TIMEOUT_SECONDS` (30 bis 1800 Sekunden) an langsame NAS-/Home-Server angepasst werden.
Bei gescannten PDFs werden standardmäßig höchstens 100 Seiten rasterisiert; `PAPER_OCR_MAX_PDF_PAGES` kann zwischen 1 und 1000 angepasst werden. Thumbnail-Decoding begrenzt zusätzlich die maximalen Quellpixel, damit ungewöhnliche Bilddimensionen den kleinen Home-Server nicht überlasten.

Für Monitoring steht `GET /health` ohne Anmeldung zur Verfügung. Der Endpunkt bestätigt nur eine erreichbare Datenbank und enthält keine Archiv- oder Konfigurationsdaten.

Die OCR-Sprache kann über `PAPER_OCR_LANGUAGE` als Tesseract-Sprachliste gesetzt werden, zum Beispiel `deu+eng` oder `eng+fra`.

## Datenablage

Neue Uploads landen zunächst unter inbox/. Nach der Prüfung erzeugt Paper beispielsweise:

    Wohnung/Strom/2026-10-05 Stadtwerke Mannheim Rechnung.pdf

Das Verschieben der Datei und die Datenbankänderung werden konsistent behandelt. Bei einem Datenbankfehler wird ein bereits verschobenes Dokument nach Möglichkeit in den Inbox-Pfad zurückgelegt.

Für NAS-Betrieb kann `Storage:Provider` auf `smb` gesetzt und `Storage:SmbRootPath` auf `smb://server/share/pfad` oder `\\server\share\pfad` gesetzt werden. Der Provider verbindet sich direkt per SMB2, authentifiziert sich pro Operation und benötigt keinen Host-Mount. Zugangsdaten gehören ausschließlich in `.env` oder eine Secret-Verwaltung. `WakePolicy: Never` vermeidet WoL-Pakete; mit `OnDemand` und `WakeMacAddress` wird ein gedrosseltes Wake-on-LAN-Paket vor dem Zugriff gesendet. Der lokale Provider bleibt ohne SMB-Abhängigkeit aktiv.
Beim ersten Compose-Start richtet ein kurzlebiger Init-Container die Eigentümer der gemounteten Datenvolumes auf den non-root App-Benutzer ein. Der App-Container selbst bleibt danach read-only, non-root, ohne Linux-Capabilities und mit `no-new-privileges`.

## Automatischer Import

Dateien können in den gemounteten Ordner /data/consume gelegt werden. Paper wartet, bis eine Datei stabil ist, verschiebt sie intern in einen Verarbeitungspuffer und importiert sie danach in die Inbox. Ungültige Dateien oder Duplikate landen mit einer .error.txt-Begründung unter /data/consume/failed; dadurch entstehen keine wiederholten Fehlversuche.

Auch stabile EML-Dateien werden verarbeitet: Paper liest verschachtelte MIME-Strukturen, dekodiert Base64- und Quoted-Printable-Anhänge und importiert unterstützte PDF-/Bildanhänge einzeln. Die E-Mail selbst wird nach erfolgreichem Import entfernt oder bei Fehlern mit einer Begründung nach failed verschoben.

Optional können ein oder mehrere IMAP-Konten über den Abschnitt `Mail` aktiviert werden. Die bestehende Einzelkonto-Konfiguration mit Umgebungsvariablen wie `PAPER_MAIL_ENABLED`, `PAPER_MAIL_HOST`, `PAPER_MAIL_USERNAME` und `PAPER_MAIL_PASSWORD` bleibt kompatibel; mehrere Konten werden als `Mail:Accounts`-Liste oder im Compose-Betrieb über `PAPER_MAIL_ACCOUNTS_JSON` konfiguriert. Der Import verwendet pro Konto IMAP-UIDs, speichert den letzten Stand in PostgreSQL, verarbeitet standardmäßig nur ungelesene Nachrichten, unterstützt Absender-/Betreff-/Dateiendungsfilter und markiert Nachrichten nur bei gesetztem `PAPER_MAIL_MARK_SEEN` als gelesen. In den Einstellungen kann der Abruf aller Konten manuell gestartet werden. Der Standard bleibt deaktiviert; Zugangsdaten gehören ausschließlich in `.env` oder eine Secret-Verwaltung.
Fehler werden zusätzlich als Mail-Fehlerhistorie mit Konto und UID gespeichert und in den Einstellungen angezeigt. Ein fehlgeschlagener UID-Lauf bleibt offen, damit er nach der Korrektur der Ursache erneut verarbeitet werden kann.

## Export

Unter Einstellungen können die Dokumentmetadaten jederzeit als JSON oder CSV exportiert werden. Das ZIP-Backup enthält zusätzlich ein Manifest und die Originaldateien. Die Originaldateien bleiben im menschenlesbaren Regal unter /data/documents und benötigen für die Betrachtung keine proprietäre Dateistruktur.

Ein solches ZIP kann in den Einstellungen wieder importiert werden. Paper streamt große Manifeste speicherschonend, prüft Dateisignaturen und SHA-256-Hashes, überspringt bereits vorhandene Dokumente und legt Regalordner, Metadaten und gelernte Analyse-Regeln bei Bedarf wieder an.

Paperless-ngx-Exporte können ebenfalls als ZIP unter Einstellungen importiert werden. Unterstützte Dokumente werden mit OCR-Text, Titel, Datum, Korrespondent, Dokumenttyp, Tags und Custom Fields in die Paper-Inbox übernommen; die physische Regalablage erfolgt anschließend bewusst nach einer kurzen Prüfung. Einzelne Manifestdateien aus Paperless im Split-Manifest-Modus werden für Custom-Field-Instanzen ebenfalls berücksichtigt.
