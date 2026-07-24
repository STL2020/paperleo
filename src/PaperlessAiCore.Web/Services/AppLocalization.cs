namespace PaperlessAiCore.Web.Services;

/// <summary>
/// Zentrale Lokalisierung für paperLeo UI.
/// Verwendung in Razor: @Loc["key"] oder @AppLoc.T("key")
/// </summary>
public static class AppLoc
{
    public static string Current { get; private set; } = "de";
    public static event Action? OnChanged;

    public static void Set(string lang)
    {
        Current = lang == "en" ? "en" : "de";
        OnChanged?.Invoke();
    }

    public static string T(string key) =>
        Current == "en"
            ? (EN.TryGetValue(key, out var en) ? en : key)
            : (DE.TryGetValue(key, out var de) ? de : key);

    // ─────────────────────────────────────────────
    // NAVIGATION
    // ─────────────────────────────────────────────
    private static readonly Dictionary<string, string> DE = new()
    {
        // Nav groups
        ["nav.main"]          = "Hauptmenü",
        ["nav.settings"]      = "Einstellungen",
        ["nav.analysis"]      = "Analyse & Test",
        ["nav.prompt"]        = "Prompt",
        ["nav.info"]          = "Info",

        // Nav items
        ["nav.dashboard"]     = "Dashboard",
        ["nav.jobs"]          = "Aufgaben",
        ["nav.connection"]    = "Verbindung & KI",
        ["nav.behavior"]      = "Verhalten",
        ["nav.vocabulary"]    = "Vokabular",
        ["nav.batch"]         = "Batch & Aktionen",
        ["nav.archive"]       = "Archiv-Analyse",
        ["nav.test"]          = "Dokument testen",
        ["nav.assistant"]     = "Assistent",
        ["nav.license"]       = "Lizenz",
        ["nav.backup"]        = "Backup",
        ["nav.help"]          = "Hilfe",

        // Header
        ["header.pro"]        = "Vollversion",
        ["header.demo"]       = "Demo",
        ["header.saving"]     = "Speichert …",
        ["header.saved"]      = "Gespeichert",

        // Common buttons
        ["btn.save"]          = "Speichern",
        ["btn.saving"]        = "Speichert …",
        ["btn.test"]          = "Testen",
        ["btn.cancel"]        = "Abbrechen",
        ["btn.back"]          = "← Zurück",
        ["btn.next"]          = "Weiter →",
        ["btn.finish"]        = "Prompt speichern & aktivieren",
        ["btn.retry"]         = "Erneut versuchen",
        ["btn.add"]           = "Hinzufügen",
        ["btn.remove"]        = "Entfernen",
        ["btn.import"]        = "Importieren",
        ["btn.export"]        = "Exportieren",
        ["btn.reset"]         = "Zurücksetzen",
        ["btn.analyze"]       = "Analyse starten",
        ["btn.accept_all"]    = "Alle bestätigen",
        ["btn.to_wizard"]     = "In Prompt-Assistenten übernehmen",
        ["btn.re_analyze"]    = "Neu analysieren",

        // Status
        ["status.loading"]    = "Wird geladen …",
        ["status.connecting"] = "Verbindung wird hergestellt …",
        ["status.success"]    = "Erfolgreich",
        ["status.error"]      = "Fehler",
        ["status.pro"]        = "Pro",
        ["status.community"]  = "Community",
        ["status.connected"]  = "Verbunden",
        ["status.running"]    = "Läuft …",
        ["status.done"]       = "Fertig",

        // Settings — Verbindung & KI
        ["conn.title"]        = "Paperless-ngx",
        ["conn.url"]          = "Paperless-URL",
        ["conn.token"]        = "API-Token",
        ["conn.tag"]          = "Verarbeitungs-Tag",
        ["conn.test"]         = "Verbindung testen",
        ["conn.ki_title"]     = "KI-Provider",
        ["conn.model"]        = "Modell",
        ["conn.apikey"]       = "API-Key",
        ["conn.apibase"]      = "API-Base-URL (optional)",
        ["conn.maxtoken"]     = "Max. Tokens",
        ["conn.test_ki"]      = "KI-Verbindung testen",
        ["conn.webhook"]      = "Webhook (optional)",
        ["conn.cost_title"]   = "KI-Kostenkontrolle",
        ["conn.pages_limit"]  = "Seiten-Limit pro Dokument",
        ["conn.token_limit"]  = "Monatliches Token-Limit",
        ["conn.cost_limit"]   = "Monatliches Budget-Limit (€)",
        ["conn.token_used"]   = "Tokens verbraucht (dieser Monat)",
        ["conn.token_reset"]  = "Zurücksetzen",

        // Settings — Verhalten
        ["beh.title"]         = "Auto-Modus",
        ["beh.auto"]          = "Auto-Modus aktivieren",
        ["beh.interval"]      = "Scan-Intervall (Sekunden)",
        ["beh.tags"]          = "Tags vergeben",
        ["beh.correspondent"] = "Korrespondenten erkennen",
        ["beh.doctype"]       = "Dokumenttyp klassifizieren",
        ["beh.title_gen"]     = "Titel generieren",
        ["beh.custom_fields"] = "Custom Fields befüllen",
        ["beh.limits"]        = "Einschränkungen",
        ["beh.only_corr"]     = "Nur vorhandene Korrespondenten",
        ["beh.only_types"]    = "Nur vorhandene Dokumenttypen",
        ["beh.only_tags"]     = "Nur vorhandene Tags",
        ["beh.title_format"]  = "Titel-Format",

        // Settings — Vokabular
        ["voc.tags_title"]    = "Tag-Vokabular",
        ["voc.types_title"]   = "Dokumenttyp-Vokabular",
        ["voc.comma_sep"]     = "Vokabular (Komma-getrennt)",
        ["voc.ai_suggest"]    = "KI-Vorschlag",
        ["voc.import_pl"]     = "Aus Paperless importieren",
        ["voc.seed"]          = "In Paperless anlegen",
        ["voc.analyzing"]     = "Analysiere …",
        ["voc.importing"]     = "Importiere …",
        ["voc.seeding"]       = "Lege an …",
        ["voc.demo_tags"]     = "Community: Vokabular auf 5 Standard-Tags beschränkt.",
        ["voc.demo_types"]    = "Community: Vokabular auf 5 Standard-Typen beschränkt.",
        ["voc.pro_unlock"]    = "Pro = eigenes Vokabular, Import aus Paperless, KI-Vorschläge.",

        // Prompt-Assistent
        ["pa.step"]           = "Schritt",
        ["pa.of"]             = "von",
        ["pa.step1"]          = "Setup-Check",
        ["pa.step2"]          = "Personen",
        ["pa.step3"]          = "Adressen",
        ["pa.step4"]          = "Fahrzeuge",
        ["pa.step5"]          = "Arbeitgeber & Beruf",
        ["pa.step6"]          = "Steuer & Finanzen",
        ["pa.step7"]          = "Verträge & Behörden",
        ["pa.step8"]          = "Titel-Builder",
        ["pa.step9"]          = "Ergebnis",
        ["pa.add_person"]     = "Person hinzufügen",
        ["pa.add_address"]    = "Adresse hinzufügen",
        ["pa.add_vehicle"]    = "Fahrzeug hinzufügen",
        ["pa.add_employer"]   = "Weiteren Arbeitgeber hinzufügen",
        ["pa.prev_employer"]  = "+ Frühere Arbeitgeber",
        ["pa.freelance"]      = "Selbstständig / Nebengewerbe",
        ["pa.leasing"]        = "Leasing",
        ["pa.no_person"]      = "— Keine Person —",
        ["pa.saved"]          = "Prompt gespeichert und aktiv.",
        ["pa.restart"]        = "Von vorne",
        ["pa.quality"]        = "Prompt-Qualität",
        ["pa.preview_label"]  = "Vorschau",

        // Lizenz
        ["lic.title"]         = "Lizenz",
        ["lic.key"]           = "Lizenzschlüssel",
        ["lic.activate"]      = "Aktivieren",
        ["lic.activating"]    = "Prüfe …",
        ["lic.deactivate"]    = "Lizenz deaktivieren",
        ["lic.hint"]          = "Leer = Demo-Version (50 Dokumente). Vollversion: unbegrenzt + alle KI-Funktionen.",
        ["lic.buy"]           = "paperLeo Pro kaufen",
        ["lic.buy_sub"]       = "Einmalzahlung · Lizenzschlüssel sofort per E-Mail",

        // Backup
        ["bak.title"]         = "Backup & Wiederherstellung",
        ["bak.export_title"]  = "Einstellungen exportieren",
        ["bak.export_btn"]    = "Alle Einstellungen exportieren",
        ["bak.import_title"]  = "Einstellungen importieren",
        ["bak.import_btn"]    = "Backup-Datei auswählen & importieren",
        ["bak.path_title"]    = "Dateipfad",
        ["bak.exporting"]     = "Exportiere …",

        // Dashboard
        ["dash.scan_now"]     = "Jetzt scannen",
        ["dash.scanning"]     = "Scannt …",
        ["dash.docs_total"]   = "Dokumente gesamt",
        ["dash.docs_proc"]    = "Verarbeitet",
        ["dash.correspondents"]= "Korrespondenten",
        ["dash.status"]       = "System-Status",
        ["dash.paperless"]    = "Paperless",
        ["dash.license"]      = "Lizenz",
        ["dash.activity"]     = "Aktivität",
        ["dash.last_error"]   = "Letzter Fehler",
        ["dash.activity_log"] = "Aktivitätslog",
        ["dash.archive_widget"]= "Archiv-Analyse",
        ["dash.no_entries"]   = "Keine Einträge.",
        ["dash.demo_limit"]   = "Demo-Version",

        // Archiv-Analyse
        ["aa.title"]          = "Archiv-Analyse",
        ["aa.subtitle"]       = "paperLeo durchsucht dein Paperless-Archiv gezielt und schlägt Daten für den Prompt-Assistenten vor.",
        ["aa.what_title"]     = "Was passiert bei der Analyse?",
        ["aa.search"]         = "Gezielte Suche",
        ["aa.search_desc"]    = "paperLeo sucht nach Lohnabrechnungen, Steuerbescheiden, Kfz-Dokumenten, Kontoauszügen und mehr",
        ["aa.extract"]        = "KI-Extraktion",
        ["aa.extract_desc"]   = "Jedes gefundene Dokument wird durch die KI analysiert und strukturierte Daten werden extrahiert",
        ["aa.confirm"]        = "Bestätigung",
        ["aa.confirm_desc"]   = "Du prüfst jeden Vorschlag und entscheidest was übernommen wird",
        ["aa.import"]         = "Wizard-Import",
        ["aa.import_desc"]    = "Bestätigte Daten werden direkt in den Prompt-Assistenten übernommen",
        ["aa.prereq"]         = "Voraussetzung: KI-Verbindung muss unter Einstellungen → Verbindung & KI konfiguriert und getestet sein. Die Analyse dauert je nach Archivgröße 2-5 Minuten.",
        ["aa.running"]        = "Analyse läuft …",
        ["aa.docs_analyzed"]  = "Dokumente analysiert",
        ["aa.fields_found"]   = "Felder gefunden",
        ["aa.confirmed"]      = "Bestätigt",
        ["aa.failed"]         = "Analyse fehlgeschlagen",
        ["aa.empty"]          = "Keine Felder gefunden.",
        ["aa.empty_hint"]     = "Stelle sicher dass Paperless Tags und Dokumenttypen hat (Lohnabrechnung, Steuerbescheid, Kfz etc.)",
        ["aa.import_done"]    = "Archiv-Analyse importiert — Personen, Adressen, Fahrzeuge und Arbeitgeber wurden vorausgefüllt. Bitte prüfe und ergänze die Daten.",
        ["aa.extra_data"]     = "Weitere gefundene Daten (werden in den Prompt eingebaut):",
        ["aa.found_in"]       = "in",
        ["aa.found_docs"]     = "Dokumenten",
        ["aa.from"]           = "aus:",

        // Hilfe
        ["help.title"]        = "Benutzerhandbuch",
        ["help.subtitle"]     = "KI-gestützte Dokumentenverarbeitung für Paperless-ngx",
        ["help.what"]         = "Was ist paperLeo?",
        ["help.what_desc"]    = "paperLeo ist eine selbst-gehostete KI-Middleware für Paperless-ngx. Neue Dokumente werden automatisch erkannt und per KI mit Titel, Datum, Korrespondent, Tags, Dokumenttyp und Custom Fields versehen — vollautomatisch, ohne Cloud, ohne Abo.",
        ["help.toc"]          = "Inhaltsverzeichnis",
        ["help.support"]      = "Support & Links",
        ["help.github"]       = "GitHub — Issues & Changelog",
        ["help.buy_pro"]      = "paperLeo Pro kaufen",
        ["help.buy_sub"]      = "Einmalzahlung · Lizenzschlüssel sofort per E-Mail",
        ["help.footer"]       = "Standalone, selbst-gehostet · Kein Tracking, keine Cloud",
    };

    private static readonly Dictionary<string, string> EN = new()
    {
        // Nav groups
        ["nav.main"]          = "Main",
        ["nav.settings"]      = "Settings",
        ["nav.analysis"]      = "Analysis & Test",
        ["nav.prompt"]        = "Prompt",
        ["nav.info"]          = "Info",

        // Nav items
        ["nav.dashboard"]     = "Dashboard",
        ["nav.jobs"]          = "Jobs",
        ["nav.connection"]    = "Connection & AI",
        ["nav.behavior"]      = "Behavior",
        ["nav.vocabulary"]    = "Vocabulary",
        ["nav.batch"]         = "Batch & Actions",
        ["nav.archive"]       = "Archive Analysis",
        ["nav.test"]          = "Test Document",
        ["nav.assistant"]     = "Assistant",
        ["nav.license"]       = "License",
        ["nav.backup"]        = "Backup",
        ["nav.help"]          = "Help",

        // Header
        ["header.pro"]        = "Full Version",
        ["header.demo"]       = "Demo",
        ["header.saving"]     = "Saving …",
        ["header.saved"]      = "Saved",

        // Common buttons
        ["btn.save"]          = "Save",
        ["btn.saving"]        = "Saving …",
        ["btn.test"]          = "Test",
        ["btn.cancel"]        = "Cancel",
        ["btn.back"]          = "← Back",
        ["btn.next"]          = "Next →",
        ["btn.finish"]        = "Save & activate prompt",
        ["btn.retry"]         = "Try again",
        ["btn.add"]           = "Add",
        ["btn.remove"]        = "Remove",
        ["btn.import"]        = "Import",
        ["btn.export"]        = "Export",
        ["btn.reset"]         = "Reset",
        ["btn.analyze"]       = "Start analysis",
        ["btn.accept_all"]    = "Accept all",
        ["btn.to_wizard"]     = "Import to Prompt Assistant",
        ["btn.re_analyze"]    = "Re-analyze",

        // Status
        ["status.loading"]    = "Loading …",
        ["status.connecting"] = "Connecting …",
        ["status.success"]    = "Success",
        ["status.error"]      = "Error",
        ["status.pro"]        = "Pro",
        ["status.community"]  = "Community",
        ["status.connected"]  = "Connected",
        ["status.running"]    = "Running …",
        ["status.done"]       = "Done",

        // Settings — Connection & AI
        ["conn.title"]        = "Paperless-ngx",
        ["conn.url"]          = "Paperless URL",
        ["conn.token"]        = "API Token",
        ["conn.tag"]          = "Processing Tag",
        ["conn.test"]         = "Test connection",
        ["conn.ki_title"]     = "AI Provider",
        ["conn.model"]        = "Model",
        ["conn.apikey"]       = "API Key",
        ["conn.apibase"]      = "API Base URL (optional)",
        ["conn.maxtoken"]     = "Max. Tokens",
        ["conn.test_ki"]      = "Test AI connection",
        ["conn.webhook"]      = "Webhook (optional)",
        ["conn.cost_title"]   = "AI Cost Control",
        ["conn.pages_limit"]  = "Page limit per document",
        ["conn.token_limit"]  = "Monthly token limit",
        ["conn.cost_limit"]   = "Monthly budget limit (€)",
        ["conn.token_used"]   = "Tokens used (this month)",
        ["conn.token_reset"]  = "Reset",

        // Settings — Behavior
        ["beh.title"]         = "Auto Mode",
        ["beh.auto"]          = "Enable auto mode",
        ["beh.interval"]      = "Scan interval (seconds)",
        ["beh.tags"]          = "Assign tags",
        ["beh.correspondent"] = "Detect correspondents",
        ["beh.doctype"]       = "Classify document type",
        ["beh.title_gen"]     = "Generate title",
        ["beh.custom_fields"] = "Fill custom fields",
        ["beh.limits"]        = "Restrictions",
        ["beh.only_corr"]     = "Only existing correspondents",
        ["beh.only_types"]    = "Only existing document types",
        ["beh.only_tags"]     = "Only existing tags",
        ["beh.title_format"]  = "Title Format",

        // Settings — Vocabulary
        ["voc.tags_title"]    = "Tag Vocabulary",
        ["voc.types_title"]   = "Document Type Vocabulary",
        ["voc.comma_sep"]     = "Vocabulary (comma-separated)",
        ["voc.ai_suggest"]    = "AI Suggestion",
        ["voc.import_pl"]     = "Import from Paperless",
        ["voc.seed"]          = "Create in Paperless",
        ["voc.analyzing"]     = "Analyzing …",
        ["voc.importing"]     = "Importing …",
        ["voc.seeding"]       = "Creating …",
        ["voc.demo_tags"]     = "Community: Vocabulary limited to 5 default tags.",
        ["voc.demo_types"]    = "Community: Vocabulary limited to 5 default types.",
        ["voc.pro_unlock"]    = "Pro = custom vocabulary, Paperless import, AI suggestions.",

        // Prompt Assistant
        ["pa.step"]           = "Step",
        ["pa.of"]             = "of",
        ["pa.step1"]          = "Setup Check",
        ["pa.step2"]          = "People",
        ["pa.step3"]          = "Addresses",
        ["pa.step4"]          = "Vehicles",
        ["pa.step5"]          = "Employer & Profession",
        ["pa.step6"]          = "Tax & Finance",
        ["pa.step7"]          = "Contracts & Authorities",
        ["pa.step8"]          = "Title Builder",
        ["pa.step9"]          = "Result",
        ["pa.add_person"]     = "Add person",
        ["pa.add_address"]    = "Add address",
        ["pa.add_vehicle"]    = "Add vehicle",
        ["pa.add_employer"]   = "Add another employer",
        ["pa.prev_employer"]  = "+ Previous employers",
        ["pa.freelance"]      = "Self-employed / Side business",
        ["pa.leasing"]        = "Leasing",
        ["pa.no_person"]      = "— No person —",
        ["pa.saved"]          = "Prompt saved and active.",
        ["pa.restart"]        = "Start over",
        ["pa.quality"]        = "Prompt quality",
        ["pa.preview_label"]  = "Preview",

        // License
        ["lic.title"]         = "License",
        ["lic.key"]           = "License key",
        ["lic.activate"]      = "Activate",
        ["lic.activating"]    = "Checking …",
        ["lic.deactivate"]    = "Deactivate license",
        ["lic.hint"]          = "Empty = Demo (50 documents). Full version: unlimited + all AI features.",
        ["lic.buy"]           = "Buy paperLeo Pro",
        ["lic.buy_sub"]       = "One-time payment · License key delivered instantly by email",

        // Backup
        ["bak.title"]         = "Backup & Restore",
        ["bak.export_title"]  = "Export settings",
        ["bak.export_btn"]    = "Export all settings",
        ["bak.import_title"]  = "Import settings",
        ["bak.import_btn"]    = "Select backup file & import",
        ["bak.path_title"]    = "File path",
        ["bak.exporting"]     = "Exporting …",

        // Dashboard
        ["dash.scan_now"]     = "Scan now",
        ["dash.scanning"]     = "Scanning …",
        ["dash.docs_total"]   = "Total documents",
        ["dash.docs_proc"]    = "Processed",
        ["dash.correspondents"]= "Correspondents",
        ["dash.status"]       = "System Status",
        ["dash.paperless"]    = "Paperless",
        ["dash.license"]      = "License",
        ["dash.activity"]     = "Activity",
        ["dash.last_error"]   = "Last error",
        ["dash.activity_log"] = "Activity log",
        ["dash.archive_widget"]= "Archive Analysis",
        ["dash.no_entries"]   = "No entries.",
        ["dash.demo_limit"]   = "Demo version",

        // Archive Analysis
        ["aa.title"]          = "Archive Analysis",
        ["aa.subtitle"]       = "paperLeo searches your Paperless archive and suggests data for the Prompt Assistant.",
        ["aa.what_title"]     = "What happens during analysis?",
        ["aa.search"]         = "Targeted search",
        ["aa.search_desc"]    = "paperLeo searches for payslips, tax assessments, vehicle documents, bank statements and more",
        ["aa.extract"]        = "AI extraction",
        ["aa.extract_desc"]   = "Each found document is analyzed by AI and structured data is extracted",
        ["aa.confirm"]        = "Confirmation",
        ["aa.confirm_desc"]   = "You review each suggestion and decide what to keep",
        ["aa.import"]         = "Wizard import",
        ["aa.import_desc"]    = "Confirmed data is directly imported into the Prompt Assistant",
        ["aa.prereq"]         = "Prerequisite: AI connection must be configured and tested under Settings → Connection & AI. Analysis takes 2-5 minutes depending on archive size.",
        ["aa.running"]        = "Analysis running …",
        ["aa.docs_analyzed"]  = "Documents analyzed",
        ["aa.fields_found"]   = "Fields found",
        ["aa.confirmed"]      = "Confirmed",
        ["aa.failed"]         = "Analysis failed",
        ["aa.empty"]          = "No fields found.",
        ["aa.empty_hint"]     = "Make sure Paperless has tags and document types (payslip, tax assessment, vehicle etc.)",
        ["aa.import_done"]    = "Archive analysis imported — people, addresses, vehicles and employers have been pre-filled. Please review and complete the data.",
        ["aa.extra_data"]     = "Additional found data (will be included in prompt):",
        ["aa.found_in"]       = "in",
        ["aa.found_docs"]     = "documents",
        ["aa.from"]           = "from:",

        // Help
        ["help.title"]        = "User Manual",
        ["help.subtitle"]     = "AI-powered document processing for Paperless-ngx",
        ["help.what"]         = "What is paperLeo?",
        ["help.what_desc"]    = "paperLeo is a self-hosted AI middleware for Paperless-ngx. New documents are automatically detected and enriched with title, date, correspondent, tags, document type and custom fields — fully automatic, no cloud, no subscription.",
        ["help.toc"]          = "Table of Contents",
        ["help.support"]      = "Support & Links",
        ["help.github"]       = "GitHub — Issues & Changelog",
        ["help.buy_pro"]      = "Buy paperLeo Pro",
        ["help.buy_sub"]      = "One-time payment · License key delivered instantly by email",
        ["help.footer"]       = "Standalone, self-hosted · No tracking, no cloud",
    };
}

/// <summary>Injectable wrapper so Razor can use @inject AppLocService Loc</summary>
public class AppLocService
{
    public string this[string key] => AppLoc.T(key);
    public string Current => AppLoc.Current;
    public void Set(string lang) => AppLoc.Set(lang);
}
