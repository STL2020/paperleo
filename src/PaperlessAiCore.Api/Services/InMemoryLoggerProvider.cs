using Microsoft.Extensions.Logging;

namespace PaperlessAiCore.Api.Services;

/// <summary>
/// Spiegelt Log-Einträge zusätzlich zur normalen Konsolen-Ausgabe in den
/// InMemoryLogStore, damit sie im Admin-Dashboard sichtbar sind. Filtert
/// bewusst auf Warning+ (alle Kategorien) sowie Information von eigenen
/// PaperlessAiCore-Komponenten, um das ASP.NET-Core-Rauschen (Request-Logs
/// etc.) draußen zu halten.
/// </summary>
public class InMemoryLoggerProvider(InMemoryLogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, store);

    public void Dispose() { }

    private class InMemoryLogger(string categoryName, InMemoryLogStore store) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Warning || (logLevel >= LogLevel.Information && categoryName.StartsWith("PaperlessAiCore"));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message += $" | {exception.GetType().Name}: {exception.Message}";
            }

            var shortCategory = categoryName.Contains('.') ? categoryName[(categoryName.LastIndexOf('.') + 1)..] : categoryName;

            store.Add(new LogEntry(DateTime.UtcNow, logLevel.ToString(), shortCategory, message));
        }
    }
}
