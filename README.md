# Paperless-AI Core

Standalone, selbst-gehostete KI-Middleware für [Paperless-ngx](https://docs.paperless-ngx.com/).
Automatische Metadaten-Extraktion beim Import (Titel-Schema, Korrespondent, Tags) plus eine
Such-/Chat-Oberfläche mit LLM Function Calling. Kein SaaS, keine Accounts, kein externer
Lizenzserver – alles läuft lokal beim Kunden.

## Architektur (analog KnxToLox)

Eine Solution, vier Projekte, **ein** Host-Prozess:

- **`PaperlessAiCore.Api`** – ASP.NET Core: REST-API, Ingest-Hintergrunddienst, hostet zugleich
  die gebaute Blazor-WASM-App als statische Dateien. Konfiguration und Aktivitäts-Log liegen als
  einfache, menschenlesbare Dateien vor (`data/settings.env`, `data/activity.jsonl`) - keine
  Datenbank, kein Schema-Migrations-Thema.
- **`PaperlessAiCore.Web`** – Blazor WebAssembly: Setup-Wizard, Suchmaske/Chat, Settings-Screen,
  Aktivitäts-Log.
- **`PaperlessAiCore.Core`** – reine Business-Logik ohne Web-Bezug: Paperless-Client, LLM-Client
  (OpenAI-kompatibel), Extraktions-Logik, Tool-Definitionen für Function Calling, Lizenzprüfung.
- **`PaperlessAiCore.Shared`** – DTOs, die zwischen Web und Api geteilt werden.

Keine externe Datenbank nötig, keine ENV-Pflichtvariablen für den Betrieb – alles wird beim
ersten Start über den Setup-Wizard im Browser eingerichtet und landet in `data/settings.env`
(einfach zu öffnen, zu sichern oder auch manuell zu editieren; Neustart der App nötig, damit
manuelle Änderungen an der Datei greifen). Verarbeitete Dokumente werden als Append-only
JSON-Lines-Datei unter `data/activity.jsonl` protokolliert.

## Lokal starten (ohne Docker)

```powershell
cd C:\Projekte\paperless-ai-core-dotnet
dotnet restore
dotnet run --project src/PaperlessAiCore.Api
```

Danach im Browser: **http://localhost:5080** (Port siehe `Properties/launchSettings.json`,
anpassbar). Der Setup-Wizard führt einmalig durch Paperless-URL/Token und LLM-Provider.

> Hinweis: Der Blazor-WASM-Client wird beim `dotnet run`/`dotnet build` automatisch mitgebaut
> (Projektreferenz in `PaperlessAiCore.Api.csproj`). Für den allerersten Build können dabei
> zusätzliche WASM-Build-Tools nachinstalliert werden – das kann etwas dauern, ist aber einmalig.

## Mit Docker starten

```bash
cd paperless-ai-core-dotnet
docker compose up --build
```

→ **http://localhost:8080**. `data/settings.env` und `data/activity.jsonl` liegen im Volume
`paperlessai_data` und überstehen Container-Neubauten.

## Tests

```bash
dotnet test tests/PaperlessAiCore.Core.Tests/PaperlessAiCore.Core.Tests.csproj
```

## Schreibvorgang: Bulk-Edit statt großem PATCH

Tags, Korrespondent und Dokumenttyp werden über `/api/documents/bulk_edit/` gesetzt (laut
Paperless-ngx-API-Doku asynchron verarbeitet - der Request kehrt zurück, sobald der Task
eingereiht ist, nicht erst nach vollständiger Verarbeitung). Nur Titel und Datum laufen noch
über ein schlankes PATCH mit ausschließlich diesen beiden Feldern. Das reduziert deutlich das
Risiko von Timeouts bei großen/trägen Paperless-Instanzen gegenüber einem einzelnen PATCH mit
allen Feldern gleichzeitig.

Jeder Schreibversuch (PATCH + bulk_edit) wird mit Zeitstempel, Dauer und Erfolg/Fehler in
**`data/paperless-writes.log`** protokolliert - zur Nachvollziehbarkeit, welche Werte tatsächlich
gesendet wurden.

## Dashboard

Unter **"📊 Dashboard"** in der Navigation: Verarbeitungsstatus (Donut-Chart KI-verarbeitet vs.
unverarbeitet), System-Statistik (Tags/Korrespondenten-Anzahl aus Paperless), KI-Token-Nutzung
(Ø Prompt-/Completion-/Gesamt-Tokens pro Dokument, Gesamtverbrauch), Token-Verteilung als
Balkendiagramm, Dokumenttyp-Verteilung als Donut-Chart, sowie Hintergrunddienst-Status
(idle/Fehler, heute verarbeitet, zuletzt verarbeitet) inkl. **"Jetzt scannen"**-Button für einen
sofortigen manuellen Durchlauf unabhängig vom Poll-Intervall.

Token-Nutzung wird automatisch bei jeder Verarbeitung aus der `usage`-Antwort des LLM-Providers
miterfasst (OpenAI-kompatibles Format) und im Activity-Log mitgeschrieben - kein zusätzliches
Setup nötig, funktioniert automatisch mit, sobald der Provider Token-Zahlen zurückliefert.

## KI-Funktionen granular steuern

Im Settings-Screen unter "KI-Funktionen" lässt sich einzeln ein-/ausschalten, was die KI
tatsächlich zurückschreibt (Titel-Generierung, Korrespondent-Erkennung, Tag-Verschlagwortung,
Dokumenttyp-Klassifizierung), sowie "Nur vorhandene Tags/Korrespondenten/Dokumenttypen
verwenden", damit keine neuen Entitäten durch KI-Fantasie entstehen.

Zusätzlich kann ein **eigener System-Prompt** hinterlegt werden (Platzhalter `[tags]` /
`[document_types]` werden automatisch durch die live aus Paperless geladenen Listen ersetzt).
Das JSON-Parsing ist tolerant gegenüber leicht abweichenden Feldnamen (z.B. `document_date`
statt `date`), damit auch mitgebrachte Prompts aus anderen Tools weitgehend kompatibel sind.

## Zwei Wege, Dokumente verarbeiten zu lassen

1. **Automatisches Polling** (Standard): der Ingest-Hintergrunddienst prüft alle
   `PollIntervalSeconds` neue, unverarbeitete Dokumente.
2. **Webhook (empfohlen für schnelle Reaktion)**: in Paperless-ngx unter
   *Workflows → neuer Workflow → Trigger "Dokument hinzugefügt" → Action "Webhook"*
   folgende URL eintragen:

   ```
   http://<paperless-ai-core-host>:8080/api/webhook/document
   ```

   Als Parameter `url` = `{doc_url}` mitgeben. Damit wird das Dokument sofort nach dem
   Hinzufügen verarbeitet, statt auf den nächsten Poll-Zyklus zu warten (Muster
   übernommen von `clusterzx/paperless-ai`).

Extrahierte Metadaten werden vollständig als eigene Paperless-Entitäten angelegt/verknüpft
(Tags, Korrespondent **und** Dokumenttyp über `/api/document_types/`), nicht nur als Text im Titel.

## Open Core Lizenzmodell

| Modus | Voraussetzung | Funktionen |
|---|---|---|
| **Community** | kein Lizenzschlüssel im Settings-Screen | Auto-Tagging, Titel-Normalisierung, Standardsuche |
| **Pro** | gültiger Lizenzschlüssel (`PAIC-XXXXXXXX-CCCC`) | zusätzlich: Kosten-Aggregation im Such-Agenten |

Rein offline per Prüfsumme validiert (`PaperlessAiCore.Core/LicenseCheck.cs`),
kein Anruf nach Hause nötig – passt zum Standalone-Anspruch.

## Projektstruktur

```
paperless-ai-core-dotnet/
├── PaperlessAiCore.sln
├── docker-compose.yml
├── src/
│   ├── PaperlessAiCore.Api/
│   │   ├── Controllers/       (Settings, Status, Query)
│   │   ├── Services/          (SettingsService, IngestWorker, WorkerStatus)
│   │   ├── Data/               (EF Core DbContext)
│   │   ├── Domain/             (AppSettings, ProcessedDocumentLog)
│   │   ├── Program.cs
│   │   └── Dockerfile
│   ├── PaperlessAiCore.Web/
│   │   ├── Pages/              (Home, Settings, ActivityLogPage)
│   │   ├── Layout/
│   │   └── Services/ApiClient.cs
│   ├── PaperlessAiCore.Core/
│   │   ├── PaperlessClient.cs, LlmClient.cs, Tools.cs
│   │   ├── Prompts.cs, ExtractionService.cs, LicenseCheck.cs
│   └── PaperlessAiCore.Shared/
│       └── Contracts.cs
└── tests/PaperlessAiCore.Core.Tests/
```

## Status / Nächste Schritte

- [x] Solution-Struktur analog KnxToLox (Api hostet Web, EF Core/SQLite, kein SaaS-Ballast)
- [x] Setup-Wizard + admin-editierbare Settings (statt reiner `.env`)
- [x] Ingest-Hintergrunddienst mit Titel-Validierung
- [x] Such-Agent mit Function Calling (`search_documents`, PRO: `aggregate_costs`)
- [x] Offline-Lizenzprüfung
- [x] Unit-Tests für Lizenz & Betrags-Parsing
- [ ] `dotnet build`/`dotnet run` bei dir lokal verifizieren (in der Sandbox, in der dieses
      Projekt erstellt wurde, ist kein .NET SDK verfügbar – bitte gegenprüfen und mir eventuelle
      Fehlermeldungen schicken)
- [ ] Testlauf gegen eine echte Paperless-ngx-Instanz
- [ ] Chat/RAG-Erweiterung, Webhook-Endpunkt für direkten Trigger aus Paperless (siehe
      `clusterzx/paperless-ai` als Inspiration) – bei Bedarf als nächster Schritt
