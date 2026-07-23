# Changelog

Alle wichtigen Änderungen an paperLeo werden hier dokumentiert.
Format basiert auf [Keep a Changelog](https://keepachangelog.com/de/1.0.0/).

---

## [5.1.0] — 2026-07-23

### Neu
- **Vollständiges Benutzerhandbuch** in der App (Hilfe-Tab) mit 12 Kapiteln
- **Webhook-Support** für sofortige Verarbeitung ohne Polling-Verzögerung
- **Custom Fields** — 13 strukturierte Felder (Rechnungsbetrag, Fahrzeug, Vertragsnummer, …)
- **Prompt-Assistent** — 7-Schritte-Wizard für personalisierten KI-System-Prompt
- **Lizenz-Tab** mit PayHip-API-Validierung (Online-Prüfung in Echtzeit)
- **Community vs. Pro** — klar getrennte Feature-Sets, Demo-Badge im Header

### Verbessert
- Navigation (Hilfe/Lizenz-Tab) — Bug behoben bei dem Tab-Wechsel nicht funktionierte
- Demo-Badge im Header klickbar — springt direkt zum Lizenz-Tab
- Einstellungen werden jetzt persistent unter `data/settings.env` im Solution-Root gespeichert
- Community-Vokabular auf 5 Standard-Tags und 5 Standard-Typen vorbelegt und gesperrt
- PayHip-API auf korrekte v2-Endpoint migriert (GET + `product-secret-key` Header)

### Behoben
- `_http` vs `http` Namenskonflikt im ApiClient (Primary Constructor)
- `@section` Razor-Keyword-Konflikt in Settings.razor
- Working-Directory-Erkennung über `.sln`-Suche statt hardcodiertem Pfad

---

## [5.0.0] — 2026-07-10

### Erstveröffentlichung
- Blazor WebAssembly Frontend + ASP.NET Core 8 Backend
- Dateibasierte Konfiguration (`settings.env`) — kein Datenbankserver
- Unterstützung für Google Gemini, OpenAI und OpenAI-kompatible Provider (Ollama)
- Automatisches Polling mit konfigurierbarem Intervall
- Tag-, Dokumenttyp- und Korrespondenten-Erkennung
- Titel-Generierung nach konfigurierbarem Schema
- Dashboard mit Aktivitätslog und Echtzeit-Status
- Docker Multi-Stage-Build (Linux, ARM64 + AMD64)
- Korrespondenten-Dubletten-Finder
- Bulk-Löschaktionen (Tags, Typen, Korrespondenten)
- Vokabular-Import aus bestehender Paperless-Instanz

---

## Geplant

### [5.2.0]
- [ ] GitHub Actions — automatischer Docker-Build bei Release
- [ ] Dark/Light-Theme-Umschalter
- [ ] Mehrsprachige UI (DE/EN)
- [ ] Batch-Reprocessing mit Fortschrittsanzeige
- [ ] E-Mail-Benachrichtigung bei Verarbeitungsfehlern

### [5.3.0]
- [ ] Regelbasierte Nachbearbeitung (z.B. »wenn Korrespondent = Telekom → Tag Haushalt«)
- [ ] Statistik-Dashboard (Dokumente/Tag, KI-Kosten, häufigste Korrespondenten)
- [ ] API-Endpunkt für externe Trigger
