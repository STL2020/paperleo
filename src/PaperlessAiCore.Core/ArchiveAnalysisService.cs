using System.Text;
using System.Text.Json;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Core;

/// <summary>
/// Analysiert das Paperless-Archiv gezielt nach Kategorien und extrahiert
/// strukturierte Daten (Name, Adresse, Arbeitgeber, Kennzeichen, Steuernummer etc.)
/// </summary>
public class ArchiveAnalysisService
{
    // Kategorien mit Suchbegriffen für Paperless
    private static readonly (string Category, string Label, string[] SearchTerms, int MaxDocs)[] SearchCategories = [
        ("person",       "Personen & Name",        ["anschreiben", "brief", "rechnung", "bescheid"],           5),
        ("adresse",      "Adressen",               ["anschreiben", "brief", "mahnung"],                         5),
        ("arbeitgeber",  "Arbeitgeber",             ["lohnabrechnung", "gehaltsabrechnung", "lohnsteuerbescheinigung", "arbeitsvertrag"], 5),
        ("fahrzeug",     "Fahrzeuge",               ["kfz-steuer", "tuev", "tüv", "fahrzeugschein", "versicherung", "leasing", "kfz"], 5),
        ("steuer",       "Steuerdaten",             ["steuerbescheid", "steuererklarung", "steuererklärung", "finanzamt"], 5),
        ("bank",         "Bankverbindungen",        ["kontoauszug", "überweisung", "sepa", "lastschrift"],      5),
        ("versicherung", "Versicherungen",          ["versicherungsschein", "police", "versicherung"],          5),
        ("rente",        "Rente & Soziales",        ["rentenversicherung", "rentenbescheid", "sozialversicherung"], 5),
    ];

    private static readonly string ExtractionSystemPrompt = """
        Du analysierst einen deutschen Dokumententext und extrahierst strukturierte Personendaten.
        Antworte NUR mit einem JSON-Objekt. Kein Text davor oder danach.
        
        Extrahiere folgende Felder wenn vorhanden (sonst null):
        {
          "personen": [{"name": "Vollständiger Name", "rolle": "Hauptperson|Partner|Kind|Sonstiges"}],
          "adressen": [{"kurzname": "Zuhause|Arbeit|Mietobjekt", "adresse": "Straße Nr, PLZ Ort"}],
          "arbeitgeber": [{"firma": "Firmenname", "aktuell": true/false}],
          "fahrzeuge": [{"modell": "Marke Modell", "kennzeichen": "XX-XX-123"}],
          "steuernummer": "12/345/67890 oder null",
          "steuer_id": "12 345 678 901 oder null",
          "rentenversicherungsnr": "A0 123456 B 789 oder null",
          "ibans": ["DE12 3456 7890 1234 5678 90"],
          "krankenversicherung": "Name der KV oder null",
          "finanzamt": "Finanzamt Musterstadt oder null"
        }
        
        Wichtig:
        - Nur echte, eindeutige Werte extrahieren — lieber null als raten
        - Keine fiktiven oder Musterdaten
        - Namen nur wenn eindeutig einer Person zuzuordnen
        - IBAN vollständig oder gar nicht
        """;

    public static async Task<ArchiveAnalysisResult> AnalyzeAsync(
        PaperlessClient paperless,
        LlmClient llm,
        IProgress<(int pct, string phase)>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ArchiveAnalysisResult();
        var allFields = new List<(string category, string label, JsonElement json, string docName)>();
        
        var totalCategories = SearchCategories.Length;
        var done = 0;

        foreach (var (category, label, searchTerms, maxDocs) in SearchCategories)
        {
            if (ct.IsCancellationRequested) break;

            progress?.Report(((done * 100) / totalCategories, $"Suche: {label} …"));

            // Alle Suchbegriffe der Kategorie durchsuchen, Dokumente sammeln
            var docIds = new HashSet<int>();
            var docs = new List<PaperlessDocument>();

            foreach (var term in searchTerms)
            {
                if (docIds.Count >= maxDocs) break;
                try
                {
                    var r = await paperless.ListDocumentsAsync(
                        new Dictionary<string, string> { ["query"] = term, ["page_size"] = "3", ["ordering"] = "-created" },
                        ct);
                    foreach (var d in r.Results)
                    {
                        if (docIds.Add(d.Id) && !string.IsNullOrWhiteSpace(d.Content))
                            docs.Add(d);
                        if (docIds.Count >= maxDocs) break;
                    }
                }
                catch { /* Suchfehler überspringen */ }
            }

            result.DocumentsAnalyzed += docs.Count;

            // Jedes Dokument durch KI schicken
            foreach (var doc in docs.Take(maxDocs))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    // Text auf max 3000 Zeichen kürzen
                    var text = doc.Content!.Length > 3000
                        ? doc.Content[..3000]
                        : doc.Content;

                    var resp = await llm.ChatAsync([
                        new LlmChatMessage { Role = "system", Content = ExtractionSystemPrompt },
                        new LlmChatMessage { Role = "user", Content = $"Dokumenttext:\n{text}" }
                    ], ct: ct);

                    if (!string.IsNullOrWhiteSpace(resp.Content))
                    {
                        var clean = resp.Content.Trim();
                        if (clean.StartsWith("```")) clean = string.Join("\n",
                            clean.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")));

                        var json = JsonSerializer.Deserialize<JsonElement>(clean);
                        allFields.Add((category, label, json, doc.Title ?? $"Dok. #{doc.Id}"));
                    }
                }
                catch { /* KI-Fehler überspringen */ }
            }

            done++;
        }

        progress?.Report((95, "Ergebnisse zusammenführen …"));

        // Felder aggregieren und deduplizieren
        result.Fields = AggregateFields(allFields);
        result.Success = true;
        result.AnalyzedAt = DateTime.UtcNow;

        progress?.Report((100, "Fertig"));
        return result;
    }

    private static List<ArchiveField> AggregateFields(
        List<(string category, string label, JsonElement json, string docName)> raw)
    {
        var fields = new Dictionary<string, ArchiveField>();

        void Add(string cat, string key, string label, string? value, string docName)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "null") return;
            var fKey = $"{cat}:{key}:{value.ToLower().Trim()}";
            if (fields.TryGetValue(fKey, out var existing))
            {
                existing.FoundInDocuments++;
                existing.Confidence = Math.Min(100, existing.Confidence + 15);
            }
            else
            {
                fields[fKey] = new ArchiveField
                {
                    Category = cat,
                    Key = key,
                    Label = label,
                    Value = value.Trim(),
                    Confidence = 70,
                    FoundInDocuments = 1,
                    ExampleDocument = docName
                };
            }
        }

        foreach (var (cat, label, json, docName) in raw)
        {
            try
            {
                // Personen
                if (json.TryGetProperty("personen", out var personen) && personen.ValueKind == JsonValueKind.Array)
                    foreach (var p in personen.EnumerateArray())
                        Add("Person", "name", "Name",
                            p.TryGetProperty("name", out var n) ? n.GetString() : null, docName);

                // Adressen
                if (json.TryGetProperty("adressen", out var adressen) && adressen.ValueKind == JsonValueKind.Array)
                    foreach (var a in adressen.EnumerateArray())
                        Add("Adresse", "adresse", "Adresse",
                            a.TryGetProperty("adresse", out var ad) ? ad.GetString() : null, docName);

                // Arbeitgeber
                if (json.TryGetProperty("arbeitgeber", out var ags) && ags.ValueKind == JsonValueKind.Array)
                    foreach (var ag in ags.EnumerateArray())
                        Add("Arbeitgeber", "firma", "Arbeitgeber",
                            ag.TryGetProperty("firma", out var f) ? f.GetString() : null, docName);

                // Fahrzeuge
                if (json.TryGetProperty("fahrzeuge", out var fz) && fz.ValueKind == JsonValueKind.Array)
                    foreach (var v in fz.EnumerateArray())
                    {
                        Add("Fahrzeug", "modell", "Fahrzeug",
                            v.TryGetProperty("modell", out var m) ? m.GetString() : null, docName);
                        Add("Fahrzeug", "kennzeichen", "Kennzeichen",
                            v.TryGetProperty("kennzeichen", out var k) ? k.GetString() : null, docName);
                    }

                // Einfache Felder
                TryAdd(json, "steuernummer",        "Steuer",  "steuernummer",         "Steuernummer");
                TryAdd(json, "steuer_id",           "Steuer",  "steuer_id",            "Steuer-ID");
                TryAdd(json, "rentenversicherungsnr","Soziales","rentenversicherungsnr","Rentenvers.-Nr.");
                TryAdd(json, "krankenversicherung",  "Soziales","krankenversicherung",  "Krankenversicherung");
                TryAdd(json, "finanzamt",            "Steuer",  "finanzamt",            "Finanzamt");

                // IBANs
                if (json.TryGetProperty("ibans", out var ibans) && ibans.ValueKind == JsonValueKind.Array)
                    foreach (var iban in ibans.EnumerateArray())
                        Add("Bank", "iban", "IBAN", iban.GetString(), docName);
            }
            catch { /* Parse-Fehler überspringen */ }

            void TryAdd(JsonElement j, string prop, string c, string k, string lbl)
            {
                if (j.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
                    Add(c, k, lbl, val.GetString(), docName);
            }
        }

        return fields.Values
            .OrderByDescending(f => f.Confidence)
            .ThenByDescending(f => f.FoundInDocuments)
            .ToList();
    }
}
