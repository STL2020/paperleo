namespace PaperlessAiCore.Api.Services;

/// <summary>
/// Zählt bei jedem Anwendungsstart automatisch um 1 hoch und persistiert den Wert in
/// data/build-counter.txt. Einfacher, zuverlässiger Ersatz für eine "echte" MSBuild-
/// Versionsnummer, die bei jedem `dotnet build` hochzählen würde (dafür bräuchte es
/// zusätzliche Build-Tooling-Infrastruktur, die hier nicht nötig ist) - dieser Zähler
/// erhöht sich stattdessen bei jedem Neustart der Anwendung, was für die Referenz
/// "welcher Stand läuft gerade" im Alltag genauso gut funktioniert.
/// </summary>
public class BuildCounterService
{
    public int BuildNumber { get; }

    public BuildCounterService(IConfiguration config)
    {
        var path = config["BuildCounterPath"]
            ?? Environment.GetEnvironmentVariable("BUILD_COUNTER_PATH")
            ?? "data/build-counter.txt";
        path = Path.GetFullPath(path);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var current = 0;
        if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var parsed))
        {
            current = parsed;
        }

        current++;
        File.WriteAllText(path, current.ToString());
        BuildNumber = current;
    }
}
