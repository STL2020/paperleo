# Changelog

All notable changes to paperLeo are documented here.

---

## [7.0.0] — 2026-07-24

### 🌍 Localization
- Complete DE/EN bilingual UI — all strings in navigation, settings, dashboard, jobs, help
- Language toggle button in header (top right)
- Language preference saved to sessionStorage (survives page reload)
- Help tab fully bilingual with 16 chapters

### ✨ New Features
- **Archive Analysis** — AI scans your existing Paperless archive across 8 categories and suggests personal data (names, addresses, employers, vehicles, tax numbers, IBANs, social security numbers)
- **Title Builder** (Pro) — visual drag & drop block editor for document title format with live preview
- **9-Step Prompt Wizard** — new steps: Employer & Profession (with person linking), Title Builder
- **Person linking** — vehicles and employers can be linked to specific household members
- **Backup tab** — dedicated menu item for settings export/import as .env file
- **AI Cost Control** (Pro) — page limits per document, monthly token limits, monthly budget limits in €
- **Dokument testen** — now a dedicated menu item under Analysis & Test

### 🔧 Fixes
- Log viewer colors in light mode (was using hardcoded dark background)
- Paperless-ngx 3.x Accept header (removed `version=` suffix causing 406 errors)
- Sidebar stays clickable while content scrolls (z-index fix)
- Step navigation now correctly shows 9/9 total steps
- Archive Analysis import now correctly pre-fills Prompt Wizard fields
- Auto-save debounce using `@bind:after` pattern (no more double-save issues)
- Stray `}` character in Hilfe tab footer removed
- Version display in footer now uses `AppInfo.Version` instead of Assembly reflection

---

## [6.0.0] — 2026-07-24

### ✨ New Features
- **Archiv-Analyse** — KI durchsucht Paperless-Archiv nach Personendaten
- **Titel-Builder** — visueller Baustein-Editor für Dokumenttitel-Format
- **9-Schritt-Wizard** — Arbeitgeber & Beruf, Person-Verknüpfung bei Fahrzeugen
- **Backup-Tab** — Einstellungen exportieren/importieren
- **KI-Kostenkontrolle** (Pro) — Seiten-Limit, Token-Limit, Budget-Limit

---

## [5.2.0] — 2026-07-23

### ✨ New Features
- Wizard Schritt 8: Titel-Builder mit Drag & Drop
- Wizard Schritt 5: Arbeitgeber & Beruf mit Person-Verknüpfung
- Backup-Funktion: Export/Import settings.env
- GitHub URLs korrigiert (STL2020/paperleo)

### 🔧 Fixes
- Auto-Save mit `@bind:after` Pattern
- Sticky Sidebar (z-index Fix)
- Verarbeitungs-Tag wird jetzt korrekt gespeichert

---

## [5.1.9] — 2026-07-23

### ✨ New Features  
- Auto-Save: kein Speichern-Button mehr nötig
- Dashboard: Skeleton-Ladeanimation mit Phasen-Anzeige
- Paperless-ngx 3.x: Accept-Header auf `application/json` (kein `version=`)

### 🔧 Fixes
- Version-Anzeige: `v@(AppInfo.Version)` statt Assembly-Reflection
- `}` im Hilfe-Tab Footer entfernt

---

## [5.1.4] — 2026-07-22

### 🚀 Initial Public Release
- Auto-Tagging, Korrespondent-Erkennung, Dokumenttyp-Klassifizierung
- Titel-Generierung mit Custom-Prompt
- 8-stufiger Prompt-Assistent
- Dashboard mit KPI-Cards und Aktivitätslog
- Lizenzierung via Payhip (Community / Pro)
- Docker-Deployment via GitHub Actions → ghcr.io
