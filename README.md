# 🦁 paperLeo

**KI-gestützte Dokumentenverarbeitung für Paperless-ngx**

paperLeo ist eine selbst-gehostete Middleware die neue Dokumente in deiner [Paperless-ngx](https://docs.paperless-ngx.com/)-Instanz automatisch per KI klassifiziert — vollautomatisch, ohne Cloud-Zwang, ohne monatliches Abo.

[![GitHub release](https://img.shields.io/github/v/release/STL2020/paperleo?style=flat-square)](https://github.com/STL2020/paperleo/releases)
[![Docker Image](https://img.shields.io/badge/Docker-ghcr.io%2FSTL2020%2Fpaperleo-blue?style=flat-square&logo=docker)](https://ghcr.io/STL2020/paperleo)
[![License](https://img.shields.io/badge/License-Proprietary-red?style=flat-square)](#lizenz)
[![Built with .NET 8](https://img.shields.io/badge/.NET-8.0-purple?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)

---

## ✨ Was paperLeo macht

Jedes neue Dokument in Paperless-ngx wird automatisch analysiert und erhält:

| Feld | Beispiel |
|------|---------|
| **Titel** | `Telekom - Rechnung - Festnetz Februar 2025 - 49,90 €` |
| **Datum** | `2025-02-03` |
| **Korrespondent** | `Deutsche Telekom` |
| **Tags** | `Rechnung`, `Haushalt` |
| **Dokumenttyp** | `Rechnung` |
| **Custom Fields** | Rechnungsbetrag: `49.90`, Rechnungsnummer: `RE-2025-001234` |

Alles vollautomatisch. Kein manuelles Nacharbeiten mehr.

---

## 🚀 Schnellstart

### Docker Compose (empfohlen)

```yaml
services:
  paperleo:
    image: ghcr.io/stl2020/paperleo:latest
    ports:
      - "5080:8080"
    volumes:
      - ./data:/app/data
    restart: unless-stopped
```

```bash
docker compose up -d
```

Öffne **http://localhost:5080** und folge dem Setup-Wizard.

### Synology NAS / Portainer

1. Portainer → Stacks → Add Stack
2. Obigen YAML-Code einfügen
3. Deploy → `http://<NAS-IP>:5080`

---

## 📋 Voraussetzungen

- **Paperless-ngx** v1.17+ (läuft bereits)
- **API-Token** aus deinem Paperless-Profil
- **KI-API-Key** — einer der folgenden:
  - [Google Gemini](https://aistudio.google.com) (kostenloser Einstieg)
  - [OpenAI](https://platform.openai.com) (GPT-4o-mini empfohlen)
  - [Ollama](https://ollama.com) (lokal, komplett privat)

---

## ⚙️ Einrichtung in 5 Schritten

**1. Verbindung** — Paperless-URL + API-Token eingeben, Verbindung testen

**2. KI** — Provider + API-Key eingeben, KI testen

**3. Vokabular** — Tags und Dokumenttypen definieren die paperLeo kennen soll

**4. Prompt-Assistent** — 7-Schritte-Wizard: Name, Adresse, Fahrzeuge, Arbeitgeber, Steuer, Versorger, Titel-Schema. paperLeo generiert daraus einen personalisierten System-Prompt.

**5. Automatik starten** — Polling aktivieren oder Webhook in Paperless einrichten (empfohlen)

### Webhook einrichten (sofortige Verarbeitung)

In Paperless-ngx unter **Workflows → Neuer Workflow**:
- Trigger: `Dokument hinzugefügt`
- Action: `Webhook`
- URL: `http://<paperleo-host>:5080/api/webhook/document`
- Parameter: `url` = `{doc_url}`

---

## 🎯 KI-Provider im Vergleich

| | Google Gemini | OpenAI GPT | Ollama (lokal) |
|--|--|--|--|
| **Kosten** | Kostenloses Tier verfügbar | Ab ~$0.15/1M Token | Kostenlos |
| **Qualität** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Datenschutz** | Google-Cloud | OpenAI-Cloud | 100% lokal |
| **Einstieg** | Einfach | Einfach | Hardware nötig |
| **Empfohlenes Modell** | `gemini-2.0-flash` | `gpt-4o-mini` | `llama3` |

---

## 🔧 Custom Fields

paperLeo kann folgende strukturierte Daten aus Dokumenten extrahieren:

`Rechnungsbetrag` · `Rechnungsnummer` · `Steuernummer` · `Fahrzeug` · `Vertragsnummer` · `Vertragsende` · `Fälligkeitsdatum` · `Mietobjekt` · `Gewerk` · `Familienmitglied` · `Belegnummer`

Felder werden mit einem Klick in Paperless angelegt (**Verbindung & KI → Fehlende Felder anlegen**).

---

## 🆓 Community vs. 🦁 Pro

| Feature | Community | Pro |
|---------|-----------|-----|
| Vollständige KI-Verarbeitung | ✅ | ✅ |
| Titel, Datum, Korrespondent | ✅ | ✅ |
| Webhook-Support | ✅ | ✅ |
| Docker-Deployment | ✅ | ✅ |
| Dashboard & Aktivitätslog | ✅ | ✅ |
| Standard-Vokabular (5 Tags / 5 Typen) | ✅ | ✅ |
| **Eigenes Vokabular (unbegrenzt)** | ❌ | ✅ |
| **Import aus Paperless** | ❌ | ✅ |
| **KI-Vorschläge für Tags/Typen** | ❌ | ✅ |
| **Alle Custom Fields** | ❌ | ✅ |
| **Prompt-Assistent (7 Schritte)** | ❌ | ✅ |
| **Batch-Verarbeitung** | ❌ | ✅ |
| **Priority Support** | ❌ | ✅ |

👉 **[paperLeo Pro kaufen](https://payhip.com/b/4auSd)** — Einmalzahlung, kein Abo, Lizenzschlüssel sofort per E-Mail.

---

## 🏗️ Technologie

- **Backend:** ASP.NET Core 8, C#
- **Frontend:** Blazor WebAssembly
- **Storage:** Dateibasiert (`data/settings.env`) — kein Datenbankserver nötig
- **Container:** Docker, Multi-Stage-Build
- **KI:** OpenAI-kompatible API (Gemini, OpenAI, Ollama, u.v.m.)

---

## 🐛 Support & Issues

- **Bug melden:** [GitHub Issues](https://github.com/STL2020/paperleo/issues)
- **Feature-Wunsch:** [GitHub Issues](https://github.com/STL2020/paperleo/issues) mit Label `enhancement`
- **Pro-Support:** Per E-Mail (Adresse im Lizenzschlüssel-Mail)

---

## 📄 Lizenz

paperLeo ist **proprietäre Software**.

- Der **Quellcode** ist auf GitHub einsehbar (Source Available)
- Die **Community Edition** ist kostenlos nutzbar
- **Modifikation und Weiterverteilung** sind nicht gestattet
- Details: siehe [LICENSE](LICENSE)

---

*Made with ❤️ by [Löwemann IT](https://github.com/STL2020) · Bad Neuenahr-Ahrweiler*
