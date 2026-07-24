using PaperlessAiCore.Shared;
using System.Text.RegularExpressions;

namespace PaperlessAiCore.Web.Services;

/// <summary>
/// Rein clientseitiger Duplikat-Algorithmus – läuft komplett im Browser (Blazor WASM).
/// Kein API-Call, kein Timeout, sofortiges Ergebnis.
/// 4 Erkennungsregeln: Wort-Präfix · Substring · Token-Überlappung · Levenshtein
/// </summary>
public static class CorrespondentDuplicateFinder
{
    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AM","AN","AUF","AUS","BEI","DER","DIE","DES","FUR","FUER",
        "IM","IN","MIT","NACH","UND","VOM","VON","ZU","ZUM","ZUR",
        "BONN","KOELN","BERLIN","HAMBURG","MUNCHEN","FRANKFURT","RHEIN","SIEG",
        "KREIS","LANDKREIS","STADTKREIS",
    };

    private static readonly Dictionary<string, string[]> KnownAbbrevs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FH"]  = ["FACHHOCHSCHULE"],
        ["HS"]  = ["HOCHSCHULE"],
        ["UNI"] = ["UNIVERSITAT","UNIVERSITAET"],
        ["TH"]  = ["TECHNISCHE HOCHSCHULE"],
        ["HWK"] = ["HANDWERKSKAMMER"],
        ["IHK"] = ["INDUSTRIE UND HANDELSKAMMER"],
        ["VHS"] = ["VOLKSHOCHSCHULE"],
        ["BG"]  = ["BERUFSGENOSSENSCHAFT"],
    };

    public static List<CorrespondentMergeSuggestion> FindGroups(List<string> names)
    {
        var entries = names.Select(n => (Original: n, Norm: Normalize(n))).ToList();
        var groups  = new List<List<string>>();
        var assigned = new HashSet<int>();

        for (int i = 0; i < entries.Count; i++)
        {
            if (assigned.Contains(i)) continue;
            var (origI, normI) = entries[i];
            if (normI.Length < 2) continue;
            var group = new List<string> { origI };

            for (int j = i + 1; j < entries.Count; j++)
            {
                if (assigned.Contains(j)) continue;
                var (origJ, normJ) = entries[j];
                if (normJ.Length < 2) continue;
                if (IsSimilar(normI, normJ)) { group.Add(origJ); assigned.Add(j); }
            }

            if (group.Count > 1) { assigned.Add(i); groups.Add(group); }
        }

        return groups.Select(g =>
        {
            var primary = g.OrderBy(n => Normalize(n).Length).ThenBy(n => n).First();
            return new CorrespondentMergeSuggestion
            {
                PrimaryName = primary,
                Aliases     = g.Where(n => !string.Equals(n, primary, StringComparison.OrdinalIgnoreCase)).ToList(),
                AllNames    = g,
                Level       = 2,
            };
        }).ToList();
    }

    private static bool IsSimilar(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        var ta = Tokens(a); var tb = Tokens(b);

        // Regel 1: Wort-Präfix
        if (ta.Count > 0 && tb.Count > 0)
        {
            var (sh, lo) = ta.Count <= tb.Count ? (ta, tb) : (tb, ta);
            if (sh.SequenceEqual(lo.Take(sh.Count), StringComparer.OrdinalIgnoreCase)
                && sh.Any(t => !GenericWords.Contains(t))) return true;
        }

        // Regel 2: Gleiche Struktur, erster Token ist Abkürzung/Variante
        if (ta.Count > 1 && tb.Count > 1)
        {
            int common = 0;
            for (int k = 1; k <= Math.Min(ta.Count, tb.Count); k++)
            {
                if (string.Equals(ta[ta.Count-k], tb[tb.Count-k], StringComparison.OrdinalIgnoreCase)) common++;
                else break;
            }
            if (common >= 1)
            {
                var ra = ta.Take(ta.Count - common).ToList();
                var rb = tb.Take(tb.Count - common).ToList();
                if (ra.Count == 1 && rb.Count == 1 && TokenSimilar(ra[0], rb[0])) return true;
            }
        }

        // Regel 3: Bedeutungsvolle Token-Überlappung ≥ 60%
        if (ta.Count > 0 && tb.Count > 0)
        {
            var sa = ta.Where(t => !GenericWords.Contains(t)).ToList();
            var sb = tb.Where(t => !GenericWords.Contains(t)).ToList();
            if (sa.Count >= 2 && sb.Count >= 2)
            {
                int overlap = sa.Count(t => sb.Any(x => TokenSimilar(t, x)));
                if (Math.Min(sa.Count, sb.Count) > 0 && overlap * 100 / Math.Min(sa.Count, sb.Count) >= 60) return true;
            }
        }

        // Regel 4: Levenshtein ≤ 15% bei kurzen Namen
        if (a.Length <= 25 && b.Length <= 25)
        {
            int d = Lev(a, b), max = Math.Max(a.Length, b.Length);
            if (max > 0 && d * 100 / max <= 15) return true;
        }
        return false;
    }

    private static bool TokenSimilar(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (KnownAbbrevs.TryGetValue(a, out var ea) && ea.Any(e => b.StartsWith(e, StringComparison.OrdinalIgnoreCase))) return true;
        if (KnownAbbrevs.TryGetValue(b, out var eb) && eb.Any(e => a.StartsWith(e, StringComparison.OrdinalIgnoreCase))) return true;
        if (a.Length >= 5 && b.EndsWith(a, StringComparison.OrdinalIgnoreCase)) return true;
        if (b.Length >= 5 && a.EndsWith(b, StringComparison.OrdinalIgnoreCase)) return true;
        if (a.Length <= 12 && b.Length <= 12 && Lev(a, b) * 100 / Math.Max(a.Length, b.Length) <= 20) return true;
        return false;
    }

    private static string Normalize(string name)
    {
        var n = name.Replace("ä","ae").Replace("ö","oe").Replace("ü","ue").Replace("ß","ss")
                    .Replace("Ä","Ae").Replace("Ö","Oe").Replace("Ü","Ue").Trim();
        foreach (var f in new[]{ " GmbH & Co. KG"," GmbH & Co KG"," GmbH"," AG"," KG",
                                   " e.V."," eV"," SE"," Ltd."," Ltd"," Inc."," Inc"," GbR"," mbH"})
            if (n.EndsWith(f, StringComparison.OrdinalIgnoreCase)) n = n[..^f.Length].TrimEnd();
        n = Regex.Replace(n, @"[.\-_/&]", " ");
        n = Regex.Replace(n, @"\s+", " ").Trim().ToUpperInvariant();
        return n;
    }

    private static List<string> Tokens(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 1).ToList();

    private static int Lev(string a, string b)
    {
        var dp = new int[a.Length+1, b.Length+1];
        for (int i=0;i<=a.Length;i++) dp[i,0]=i;
        for (int j=0;j<=b.Length;j++) dp[0,j]=j;
        for (int i=1;i<=a.Length;i++)
            for (int j=1;j<=b.Length;j++)
                dp[i,j] = a[i-1]==b[j-1] ? dp[i-1,j-1] : 1+Math.Min(dp[i-1,j-1],Math.Min(dp[i-1,j],dp[i,j-1]));
        return dp[a.Length,b.Length];
    }
}
