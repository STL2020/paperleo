<div align="center">

# 🦁 paperLeo

### KI-gestützte Dokumentenverarbeitung für Paperless-ngx

**Automatische Metadaten · Vollbackup · Live-Dashboard · 100% selbst-gehostet**

[![Version](https://img.shields.io/badge/version-8.0-teal?style=flat-square)](https://github.com/STL2020/paperleo/releases)
[![Build](https://github.com/STL2020/paperleo/actions/workflows/build.yml/badge.svg)](https://github.com/STL2020/paperleo/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-Open%20Core-blue?style=flat-square)](#lizenz)
[![Platform](https://img.shields.io/badge/platform-.NET%208%20%7C%20Docker-purple?style=flat-square)](#schnellstart)
[![Paperless-ngx](https://img.shields.io/badge/Paperless--ngx-compatible-green?style=flat-square)](https://docs.paperless-ngx.com/)

[Features](#features) · [Backup](#-paperless-ngx-vollbackup) · [Schnellstart](#schnellstart) · [Dashboard](#dashboard) · [KI-Provider](#ki-provider) · [Lizenz](#lizenz)

</div>

---

paperLeo verbindet deine [Paperless-ngx](https://docs.paperless-ngx.com/) Instanz mit einer KI deiner Wahl und erledigt automatisch, was sonst Stunden dauert: Dokumente benennen, Korrespondenten zuweisen, Tags vergeben, Typen klassifizieren. Und macht dabei auf Wunsch ein vollständiges Backup deines gesamten Archivs — direkt aus dem Browser, ohne Terminal.

> **Kein SaaS. Keine Cloud. Keine Accounts. Alles bleibt bei dir.**

---

## Features

| | Community | Pro |
|---|:---:|:---:|
| **KI-Metadaten** — Titel, Korrespondent, Tags, Dokumenttyp | ✅ | ✅ |
| **Automatisches Polling** — neue Dokumente sofort verarbeiten | ✅ | ✅ |
| **Webhook-Trigger** — Paperless-ngx startet Verarbeitung direkt | ✅ | ✅ |
| **Live-Dashboard** — KPIs, Charts, Token-Nutzung in Echtzeit | ✅ | ✅ |
| **KI-Chat & Dokumentensuche** mit Function Calling | ✅ | ✅ |
| **Eigener System-Prompt** — volle Kontrolle über KI-Verhalten | ✅ | ✅ |
| **Korrespondenten-Übersicht** mit Logos & Dokumentliste | ✅ | ✅ |
| **Job-Queue** — Echtzeit-Verarbeitungsstatus | ✅ | ✅ |
| **Light/Dark Theme**, DE/EN Mehrsprachigkeit | ✅ | ✅ |
| 📦 **Paperless-ngx Vollbackup & Restore** (PDFs + Metadaten) | — | ✅ |
| 🗓️ **Automatischer Backup-Zeitplan** | — | ✅ |
| 💰 **KI-Kosten-Aggregation** im Chat-Agenten | — | ✅ |

---

## 📦 Paperless-ngx Vollbackup

> **Das stärkste Feature von paperLeo — und einzigartig in der Paperless-ngx Toollandschaft.**

Paperless-ngx speichert dein gesamtes Dokumentenarchiv. Ein Datenverlust durch Serverfehler, versehentliches Löschen oder Hardware-Ausfall kann verheerend sein. paperLeo löst dieses Problem vollständig — ohne Kommandozeile, ohne SSH, direkt aus dem Browser.

### Was wird gesichert?

| Inhalt | Format |
|---|---|
| Alle Dokumente | PDF, originalgetreu |
| Tags, Korrespondenten, Dokumenttypen | JSON |
| Custom Fields & Werte | JSON |
| Vollständige Dokument-Metadaten | JSON |
| **Alles zusammen** | **ZIP-Archiv, eine Datei** |

### Restore

- Metadaten vollständig zurückspielen nach Neuinstallation oder Server-Umzug
- Fehlertolerantes Design: vorhandene Einträge werden übersprungen, nichts wird überschrieben
- Ideal für Migration von einer Paperless-ngx-Version zur nächsten

### Zeitplan

Automatisches Backup täglich, wöchentlich oder zu beliebigen Zeiten. Archiv-Dateien liegen im persistenten `data/`-Verzeichnis — überstehen Container-Neustarts und System-Updates.

### Warum das anders ist

Das offizielle `document_exporter`-Tool von Paperless-ngx funktioniert nur per SSH/CLI direkt auf dem Server. **paperLeo macht exakt dasselbe über die REST-API — ohne Terminal, ohne Server-Zugriff.**

Perfekt für:
- 🏠 **NAS-Nutzer** (Synology, QNAP, TrueNAS) ohne Shell-Zugriff
- 🐳 **Docker/Portainer/Unraid**-Betreiber
- 👥 Alle, die kein SSH-Zugang zu ihrem Server haben
- 🔒 Sicherheitsbewusste Nutzer die Off-Site-Backups ohne Cloud-Dienste wollen

---

## Schnellstart

### Mit Docker (empfohlen)

```bash
git clone https://github.com/STL2020/paperleo.git
cd paperleo
docker compose up --build
```

→ **http://localhost:8080** — der Setup-Wizard führt dich durch alles.

`data/settings.env` und alle Logs liegen im Volume `paperlessai_data` — nichts geht bei Updates verloren.

### Auf Synology NAS

```bash
docker compose -f docker-compose.synology.yml up -d
```

### Ohne Docker (.NET SDK)

```bash
dotnet restore
dotnet run --project src/PaperlessAiCore.Api
```

→ **http://localhost:5080**

### Setup in 3 Schritten

1. **Paperless-ngx URL & API-Token** eingeben (Token unter *Einstellungen → API-Token*)
2. **KI-Provider** wählen und API-Key eintragen (OpenAI, Gemini, Ollama, …)
3. **Fertig** — paperLeo verarbeitet ab sofort neue Dokumente automatisch

---

## KI-Provider

paperLeo ist kompatibel mit **jedem OpenAI-kompatiblen Endpunkt**:

| Provider | Modell-Beispiele | Lokal? |
|---|---|:---:|
| **OpenAI** | `gpt-4o`, `gpt-4o-mini` | ❌ |
| **Google Gemini** | `gemini-2.0-flash`, `gemini-1.5-pro` | ❌ |
| **Ollama** | `llama3`, `mistral`, `qwen2.5` | ✅ |
| **LM Studio** | beliebige GGUF-Modelle | ✅ |
| **OpenRouter** | 100+ Modelle | ❌ |
| **Custom Endpoint** | eigene OpenAI-kompatible API | ✅ |

Für **maximale Privatsphäre**: Ollama + lokales Modell — alle Dokumente verlassen niemals dein Netzwerk.

---

## Dashboard

<div align="center">
<em>Live-Dashboard mit KPI-Karten, Scan-Fortschritt, Charts und Log-Panel</em>
</div>

Das Dashboard zeigt in Echtzeit:

- **KPI-Karten** — Dokumente gesamt, KI-verarbeitet, Korrespondenten, Paperless-Version
- **Scan-Fortschritt** — aktuell verarbeitetes Dokument mit animiertem Fortschrittsbalken
- **Verarbeitungsfortschritt** — Donut-Chart: verarbeitet vs. ausstehend
- **Dokumenttypen** — Verteilung als Donut-Chart mit Legende
- **Token-Nutzung & KI-Kosten** — Gesamt-Tokens, Ø Prompt/Completion, geschätzte Monatskosten
- **System-Status** — Paperless-Verbindung, Lizenzstatus, CPU, RAM, Uptime
- **Live-Log** — Echtzeit-Einblick in die Verarbeitung als Slide-In-Panel

---

## Paperless-ngx Integration

paperLeo wurde speziell für Paperless-ngx entwickelt und nutzt die offizielle REST-API vollständig:

### Metadaten-Extraktion
Für jedes neue Dokument extrahiert die KI:
- **Titel** nach konfigurierbarem Schema (z. B. `Rechnung - Lieferant - Nr. XXX - Betrag €`)
- **Korrespondent** — automatisch angelegt wenn noch nicht vorhanden
- **Tags** — aus vorhandenen oder neu erstellt
- **Dokumenttyp** — Rechnung, Mahnung, Vertrag, Versicherung, …
- **Datum** — aus Dokumentinhalt extrahiert

### Schreib-Strategie
Tags, Korrespondent und Dokumenttyp werden über `/api/documents/bulk_edit/` gesetzt (asynchron, wie von Paperless-ngx empfohlen). Titel und Datum per schlankem PATCH. Jeder Schreibvorgang wird in `data/paperless-writes.log` protokolliert.

### Webhook-Integration
```
# In Paperless-ngx: Workflows → Neuer Workflow
Trigger:  Dokument hinzugefügt
Action:   Webhook → http://<paperleo-host>:8080/api/webhook/document
Parameter: url = {doc_url}
```
Dokumente werden sofort nach dem Upload verarbeitet — kein Warten auf den nächsten Poll-Zyklus.

---

## Architektur

```
paperLeo/
├── src/
│   ├── PaperlessAiCore.Api/       ASP.NET Core 8 — REST-API, Ingest-Worker, hostet UI
│   │   ├── Controllers/           Dashboard, Settings, Backup, Jobs, Webhook
│   │   └── Services/              IngestScanService, BackupService, ProcessingJobService
│   ├── PaperlessAiCore.Web/       Blazor WebAssembly — komplette UI
│   │   └── Pages/                 Dashboard, Backup, Settings, Jobs, Korrespondenten
│   ├── PaperlessAiCore.Core/      Business-Logik ohne UI-Bezug
│   │   ├── PaperlessClient.cs     Paperless-ngx API-Client (paginiert, retry-fähig)
│   │   ├── LlmClient.cs           OpenAI-kompatibler LLM-Client
│   │   ├── PaperlessBackupService Backup & Restore Engine
│   │   └── LicenseCheck.cs        Offline-Lizenzprüfung
│   └── PaperlessAiCore.Shared/    DTOs zwischen Web und Api
├── docker-compose.yml
└── docker-compose.synology.yml
```

**Keine Datenbank nötig.** Alles in `data/`:

| Datei | Inhalt |
|---|---|
| `settings.env` | Konfiguration — menschenlesbar, direkt editierbar |
| `activity.jsonl` | Verarbeitungs-Log (Append-only, strukturiert) |
| `jobs.jsonl` | Job-Queue-Persistenz über Neustarts hinweg |
| `paperless-writes.log` | Audit-Trail aller Schreibvorgänge an Paperless-ngx |

---

## Lizenz

paperLeo ist **Open Core**:

- **Community** (kostenlos, kein Schlüssel): KI-Extraktion, Dashboard, Chat, Webhook, alle Basis-Features
- **Pro** (Lizenzschlüssel `PAIC-XXXXXXXX-CCCC`): Vollbackup, Backup-Zeitplan, KI-Kosten-Aggregation

**Offline-Validierung** — kein Anruf nach Hause, kein Lizenzserver, funktioniert ohne Internet.

Lizenzschlüssel unter *Einstellungen → Lizenz* eintragen. Sofortige Aktivierung, kein Neustart.

---

## Changelog

### v8.0 (Juli 2025)
- 📦 Paperless-ngx Vollbackup & Restore (Pro)
- 🎨 Komplettes Dashboard-Redesign mit Live-Scan-Anzeige
- 🌓 Light/Dark Theme, vollständig themefähig
- ⚡ Live-Scan-Status (1s-Poll, aktuelles Dokument sichtbar)
- 📄 Korrespondenten-Seite mit LogoKit-Logos
- 🔄 Job-Queue mit Echtzeit-Auto-Refresh
- 🌐 Vollständige DE/EN Lokalisierung
- ⏱ Per-Dokument 3-Minuten-Timeout gegen blockierte Queue
- 🔁 App-Neustart direkt aus dem Dashboard

---

<div align="center">

**paperLeo** · Selbst-gehostete KI für Paperless-ngx · [MIT License (Community)](LICENSE)

*Wenn paperLeo dir hilft, gib dem Repo einen ⭐ — das hilft anderen, es zu finden.*

</div>
