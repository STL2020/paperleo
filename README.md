# paperLeo — KI-Assistent für Paperless-ngx

**paperLeo** ist eine selbst-gehostete KI-Middleware für [Paperless-ngx](https://docs.paperless-ngx.com/). Sie analysiert Dokumente automatisch, vergibt Titel, Korrespondenten, Tags und Dokumenttypen — und sichert dein gesamtes Archiv lokal.

> **Kein SaaS. Keine Cloud. Keine Accounts. Läuft bei dir.**

---

## ✨ Features im Überblick

| Feature | Community | Pro |
|---|:---:|:---:|
| KI-Metadaten-Extraktion (Titel, Korrespondent, Tags, Typ) | ✅ | ✅ |
| Automatisches Polling & Webhook-Trigger | ✅ | ✅ |
| Live-Dashboard mit Token-Nutzung & Statistiken | ✅ | ✅ |
| Dokumenten-Suche & KI-Chat | ✅ | ✅ |
| Eigener System-Prompt & Vokabular | ✅ | ✅ |
| Mehrsprachig (DE/EN) | ✅ | ✅ |
| Light/Dark Theme | ✅ | ✅ |
| **📦 Paperless-ngx Vollbackup & Restore** | — | ✅ |
| Automatischer Backup-Zeitplan | — | ✅ |
| KI-Kosten-Aggregation im Chat-Agenten | — | ✅ |

---

## 📦 Paperless-ngx Backup — Das Killer-Feature

> **Das vollständige, API-basierte Archiv-Backup ist das stärkste Alleinstellungsmerkmal von paperLeo Pro — und einzigartig in der Paperless-ngx Toollandschaft.**

### Was wird gesichert?

paperLeo lädt direkt über die Paperless-ngx REST-API alles Relevante:

- 📄 **Alle Dokumente als PDF** — jedes einzelne mit originalem Dateinamen
- 🏷️ **Alle Metadaten als JSON** — Tags, Korrespondenten, Dokumenttypen, Custom Fields
- 📋 **Vollständige Dokument-Metadaten** — Titel, Datum, zugewiesene Tags/Korrespondenten
- 🗜️ **Alles in einem ZIP-Archiv** — eine Datei für NAS, USB oder Cloud-Backup

### Restore-Funktion

Metadaten (Tags, Korrespondenten, Dokumenttypen) werden vollständig zurückgespielt — ideal nach Server-Umzug oder Neuinstallation. Bereits vorhandene Einträge werden sicher übersprungen.

### Zeitplan (Schedule)

Automatisches Backup zu konfigurierbaren Zeiten, lokale Speicherung im persistenten `data/`-Verzeichnis.

### Warum das einzigartig ist

Paperless-ngx hat zwar eigene Backup-Kommandos (`document_exporter`), aber diese erfordern direkten Server-Zugriff via SSH/CLI.

**paperLeo macht das über die Web-API — ohne Terminal, ohne Server-Zugriff, direkt aus dem Browser.**

Perfekt für:
- 🏠 NAS-Betreiber (Synology, QNAP, TrueNAS) ohne Shell-Zugriff
- 🐳 Docker/Portainer/Unraid-Instanzen
- 👥 Technisch weniger erfahrene Nutzer

---

## 🚀 Schnellstart

### Ohne Docker

```powershell
dotnet restore
dotnet run --project src/PaperlessAiCore.Api
```

→ **http://localhost:5080** — Setup-Wizard führt durch Paperless-URL, Token und LLM-Provider.

### Mit Docker

```bash
docker compose up --build
```

→ **http://localhost:8080**

`data/settings.env` und alle Logs liegen im Volume `paperlessai_data` und überstehen Container-Neustarts.

### Auf Synology NAS

```bash
docker compose -f docker-compose.synology.yml up -d
```

---

## 🧠 Unterstützte KI-Provider

Kompatibel mit allen **OpenAI-kompatiblen APIs**:

| Provider | Beispiel-Modelle |
|---|---|
| OpenAI | `gpt-4o`, `gpt-4o-mini` |
| Google Gemini | `gemini-2.0-flash`, `gemini-1.5-pro` |
| Ollama (lokal) | `llama3`, `mistral`, `qwen2.5` |
| LM Studio (lokal) | beliebige GGUF-Modelle |
| OpenRouter | alle verfügbaren Modelle |
| Custom Endpoint | eigene OpenAI-kompatible Instanz |

---

## 📊 Dashboard

Das Live-Dashboard zeigt in Echtzeit:

- **KPI-Karten** — Dokumente gesamt, KI-verarbeitet, Korrespondenten, Paperless-Version
- **Scan-Fortschritt** — aktuell verarbeitetes Dokument mit animiertem Balken
- **Verarbeitungsfortschritt** — Donut-Chart verarbeitet vs. offen
- **Dokumenttypen** — Verteilung als Donut-Chart
- **Token-Nutzung & KI-Kosten** — Gesamt-Tokens, Ø Prompt/Completion, Monatskosten
- **System-Status** — Paperless-Verbindung, CPU, RAM, Uptime
- **Live-Log** — Echtzeit-Einblick per Slide-In-Panel

---

## ⚙️ Architektur

Eine Solution, vier Projekte, **ein** Host-Prozess:

```
src/
├── PaperlessAiCore.Api/      ASP.NET Core: REST-API + Ingest-Worker + hostet Blazor WASM
├── PaperlessAiCore.Web/      Blazor WebAssembly: gesamte UI
├── PaperlessAiCore.Core/     Business-Logik (PaperlessClient, LlmClient, BackupService, …)
└── PaperlessAiCore.Shared/   DTOs zwischen Web und Api
```

**Keine Datenbank** — alles in `data/`:

| Datei | Inhalt |
|---|---|
| `settings.env` | Konfiguration (menschenlesbar, editierbar) |
| `activity.jsonl` | Verarbeitungs-Log (Append-only) |
| `jobs.jsonl` | Job-Queue-Persistenz |
| `paperless-writes.log` | Audit-Trail aller Schreibvorgänge |

---

## 🔄 Dokumente verarbeiten

### Automatisches Polling (Standard)
Hintergrunddienst prüft alle `PollIntervalSeconds` auf neue, unverarbeitete Dokumente.

### Webhook (empfohlen)
In Paperless-ngx: *Workflows → Neuer Workflow → Trigger „Dokument hinzugefügt" → Webhook*:

```
http://<paperleo-host>:8080/api/webhook/document
```

Parameter `url` = `{doc_url}` — sofortige Verarbeitung ohne Poll-Wartezeit.

---

## 🔐 Lizenzmodell

**Offline validiert** — kein Anruf nach Hause, kein Lizenzserver:

| Modus | Lizenzschlüssel | Features |
|---|---|---|
| **Community** | nicht erforderlich | KI-Extraktion, Dashboard, Chat, Webhook |
| **Pro** | `PAIC-XXXXXXXX-CCCC` | + Backup/Restore, Backup-Zeitplan, KI-Kosten-Aggregation |

Schlüssel in den Settings unter **Lizenz** eintragen — sofortige Aktivierung, kein Neustart.

---

## 📋 Changelog

### v8.0 (Juli 2025)
- 📦 **Paperless-ngx Backup & Restore** (Pro) — vollständige API-basierte Datensicherung
- 🎨 **Komplett überarbeitetes Dashboard** — KPI-Cards, Live-Scan, Log-Panel
- 🌓 **Light/Dark Theme** — vollständig themefähige UI inkl. Sidebar
- ⚡ **Live-Scan-Status** — aktuell verarbeitetes Dokument (1s-Poll)
- 📄 **Korrespondenten-Seite** — A–Z Filter, LogoKit-Logos, Dokumentliste
- 🔄 **Job-Queue** — Echtzeit-Übersicht mit Auto-Refresh
- 🌐 **DE/EN Lokalisierung** — vollständige Mehrsprachigkeit
- ⏱ **Per-Dokument Timeout** — verhindert blockierte Queue
- 🔁 **Neustart-Button** — App-Neustart direkt aus dem Dashboard

### v7.x
- Smart-Home-Planer mit KNX/Loxone-Integration
- Payhip-Lizenzintegration
- Docker-Deploy-Optimierung für Synology

---

## 🧪 Tests

```bash
dotnet test tests/PaperlessAiCore.Core.Tests/
```

---

## 📝 Lizenz

paperLeo ist **Open Core**:
- **Community-Features**: MIT License
- **Pro-Features** (Backup, erweiterte KI-Kosten): proprietär, Lizenzschlüssel erforderlich

---

*Entwickelt für alle, die ihr Dokumenten-Archiv ernst nehmen.*
