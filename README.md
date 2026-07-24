<div align="center">

<img src="https://raw.githubusercontent.com/STL2020/paperleo/main/docs/banner.svg" alt="paperLeo" width="800"/>

# paperLeo

### AI-powered document processing for Paperless-ngx — self-hosted, no cloud, no subscription

[![License](https://img.shields.io/badge/license-Open%20Core-1D6A3F?style=flat-square)](LICENSE)
[![Version](https://img.shields.io/badge/version-7.0.0-1D6A3F?style=flat-square)](https://github.com/STL2020/paperleo/releases)
[![Docker](https://img.shields.io/badge/docker-ghcr.io%2Fstl2020%2Fpaperleo-0D1A27?style=flat-square&logo=docker)](https://ghcr.io/stl2020/paperleo)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com)
[![Paperless-ngx](https://img.shields.io/badge/Paperless--ngx-2.x%20%7C%203.x-17a2b8?style=flat-square)](https://docs.paperless-ngx.com)

**[🚀 Quick Start](#-quick-start) · [✨ Features](#-features) · [📖 Documentation](#-documentation) · [💰 Pricing](#-pricing) · [🐛 Issues](https://github.com/STL2020/paperleo/issues)**

---

*paperLeo connects your Paperless-ngx instance to an AI and automatically assigns titles, dates, correspondents, tags, document types and custom fields — fully automated, without cloud, without monthly subscription.*

</div>

---

## ✨ Features

| Feature | Community | Pro |
|---|:---:|:---:|
| Auto-tagging & correspondent detection | ✅ | ✅ |
| Document type classification | ✅ | ✅ |
| Title generation | ✅ | ✅ |
| Custom fields (13 standard fields) | ✅ | ✅ |
| Webhook support | ✅ | ✅ |
| DE / EN UI language toggle | ✅ | ✅ |
| Archive Analysis (AI scans your archive) | ✅ | ✅ |
| 9-Step Prompt Wizard | ✅ | ✅ |
| Backup & Restore | ✅ | ✅ |
| **Title Builder** (custom title format) | ❌ | ✅ |
| **AI Cost Control** (token & budget limits) | ❌ | ✅ |
| **Unlimited documents** | ❌ | ✅ |
| **Priority Support** | ❌ | ✅ |

> Community version processes **50 documents** for free. Pro is a **one-time payment** — no subscription.

---

## 🚀 Quick Start

### Docker (recommended)

```yaml
# docker-compose.yml
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

Open **http://localhost:5080** → Settings → connect to Paperless-ngx → done.

### Requirements

- Paperless-ngx 2.x or 3.x (running and accessible)
- AI API key: [Google Gemini](https://ai.google.dev) (free tier available), [OpenAI](https://platform.openai.com), or any OpenAI-compatible provider (e.g. [Ollama](https://ollama.ai) for local AI)
- Docker or .NET 8 Runtime

---

## 🔧 How it works

```
New document in Paperless-ngx
        ↓
paperLeo detects it (polling or webhook)
        ↓
Document text → AI (Gemini / OpenAI / Ollama)
        ↓
AI returns: title, date, correspondent, tags, document type, custom fields
        ↓
paperLeo writes metadata back to Paperless-ngx
```

paperLeo runs **entirely on your own infrastructure**. No data leaves your server except to the AI provider of your choice.

---

## 🧙 9-Step Prompt Wizard

The Prompt Wizard personalizes the AI for your specific situation in 9 steps:

1. **Setup Check** — verify and create required custom fields
2. **People** — household members, family
3. **Addresses** — home, work, rental properties
4. **Vehicles** — license plates, models, leasing
5. **Employer & Profession** — current/previous employers, self-employed
6. **Tax & Finance** — tax rules, bank accounts, currency
7. **Contracts & Authorities** — contract types, service providers
8. **Title Builder** — define your document title format
9. **Result** — review, save and activate your personalized prompt

---

## 🔍 Archive Analysis

paperLeo can scan your existing Paperless archive and suggest data for the Prompt Wizard:

- Automatically searches for payslips, tax assessments, vehicle documents, bank statements
- Extracts: names, addresses, employers, vehicles, tax numbers, IBANs, social security numbers
- You confirm or reject each suggestion individually
- One click to import confirmed data into the Prompt Wizard

---

## 🤖 Supported AI Providers

| Provider | Model example | Notes |
|---|---|---|
| Google Gemini | `gemini-2.5-flash` | Free tier available, recommended |
| OpenAI | `gpt-4o-mini` | Paid, very accurate |
| Anthropic Claude | via OpenAI-compat. | Set custom API base |
| Ollama | `llama3`, `mistral` | Fully local, no API key needed |
| Any OpenAI-compatible | — | Custom API base URL supported |

---

## 🌍 Language Support

paperLeo ships with full **DE / EN** bilingual UI. Toggle in the top-right header. Language preference is saved automatically.

---

## 📖 Documentation

Full documentation is available in the **Help** tab inside paperLeo (Settings → Help).

Covers:
- Installation (Docker & local)
- Connection & AI setup
- Prompt Wizard walkthrough
- Archive Analysis
- Title Builder
- Backup & Restore
- Troubleshooting

---

## 💰 Pricing

| | Community | Pro |
|---|---|---|
| **Price** | Free | One-time payment |
| **Documents** | 50 | Unlimited |
| **License** | Community | Lifetime |
| **Get it** | Use for free | [Buy on Payhip](https://payhip.com/b/4auSd) |

---

## 🏗️ Architecture

One solution, four projects, **one** host process:

```
PaperlessAiCore.sln
├── src/
│   ├── PaperlessAiCore.Api/      # ASP.NET Core REST API + background worker
│   ├── PaperlessAiCore.Web/      # Blazor WASM frontend
│   ├── PaperlessAiCore.Core/     # Business logic (AI, Paperless client, license)
│   └── PaperlessAiCore.Shared/   # Shared DTOs
└── tests/
    └── PaperlessAiCore.Core.Tests/
```

**No database required.** All settings stored in `data/settings.env` (human-readable, easy to backup).

---

## 🔒 Privacy & Security

- **Self-hosted** — runs entirely on your infrastructure
- **No telemetry** — no data sent to Löwemann IT
- **No cloud dependency** — works offline (except AI API calls)
- **Open Core** — community edition source included
- Document text is sent **only to your chosen AI provider** — you control which one

---

## 🚧 What's New in v7.0.0

- 🌍 **Complete DE/EN localization** — all UI strings bilingual, language toggle in header
- 🔍 **Archive Analysis** — AI scans your Paperless archive and suggests personal data
- 🎨 **Title Builder** (Pro) — visual drag & drop document title format editor
- 🧙 **9-Step Prompt Wizard** — now with employer/vehicle/address person-linking
- 💾 **Backup tab** — export and import all settings as .env file
- 💰 **AI Cost Control** (Pro) — page limits, token limits, monthly budget limits
- ☀️ **Light mode fix** — Log viewer now correctly themed in light mode
- 🔧 **Paperless-ngx 3.x** — Accept header compatibility fixed

See [CHANGELOG.md](CHANGELOG.md) for full history.

---

## 🤝 Contributing

This is an **Open Core** project. The community edition is open source. Contributions welcome!

- 🐛 [Report a bug](https://github.com/STL2020/paperleo/issues/new?template=bug_report.md)
- 💡 [Request a feature](https://github.com/STL2020/paperleo/issues/new?template=feature_request.md)
- 📖 [Improve documentation](https://github.com/STL2020/paperleo/issues)

---

## 📄 License

Community edition: [MIT License](LICENSE)
Pro features: Commercial license via [Payhip](https://payhip.com/b/4auSd)

---

<div align="center">

Built with ❤️ by [Löwemann IT](https://www.loewemann.com)

[Website](https://www.loewemann.com) · [Payhip Shop](https://payhip.com/b/4auSd) · [Issues](https://github.com/STL2020/paperleo/issues)

</div>
