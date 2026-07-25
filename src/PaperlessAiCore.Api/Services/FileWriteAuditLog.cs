namespace PaperlessAiCore.Api.Services;

public interface IWriteAuditLog
{
    void Log(string message);
}

/// <summary>
/// Persistentes Logfile (data/paperless-writes.log) für JEDEN Schreibversuch gegen
/// Paperless-ngx (PATCH für Titel/Datum, bulk_edit für Tags/Korrespondent/Dokumenttyp).
/// Zeigt Start, Erfolg/Fehler und Dauer jeder Operation - damit der Schreibprozess
/// nachvollziehbar ist, ohne im laufenden Betrieb an der Konsole hängen zu müssen.
/// </summary>
public class FileWriteAuditLog : IWriteAuditLog
{
    private readonly string _path;
    private readonly object _lock = new();

    public FileWriteAuditLog(IConfiguration config)
    {
        var configuredPath = config["WriteAuditLogPath"]
            ?? Environment.GetEnvironmentVariable("WRITE_AUDIT_LOG_PATH")
            ?? "data/paperless-writes.log";
        _path = Path.GetFullPath(configuredPath);

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_path, line);
            }
            catch (IOException)
            {
                // Datei kurzzeitig gesperrt (z.B. paralleler Zugriff) - Logging darf
                // niemals den eigentlichen Schreibvorgang zu Paperless zum Absturz bringen.
            }
        }
    }
}
