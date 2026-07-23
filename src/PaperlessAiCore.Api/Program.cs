using PaperlessAiCore.Api.Services;
using PaperlessAiCore.Core.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Working Directory auf Solution-Root setzen, damit data/settings.env immer unter
// <SolutionRoot>/data/ landet - unabhängig davon ob dotnet run, dotnet publish
// oder Docker gestartet wird. Sucht von AppContext.BaseDirectory aufwärts nach *.sln.
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !dir.GetFiles("*.sln").Any())
        dir = dir.Parent;
    if (dir != null)
        Directory.SetCurrentDirectory(dir.FullName);
}

// Log-Viewer (Admin-Dashboard, PRO): spiegelt Warning+ (alle Kategorien) und
// Information (eigene Komponenten) zusätzlich in einen In-Memory-Ringpuffer.
var logStore = new PaperlessAiCore.Api.Services.InMemoryLogStore();
builder.Services.AddSingleton(logStore);
builder.Logging.AddProvider(new PaperlessAiCore.Api.Services.InMemoryLoggerProvider(logStore));

// ---------------------------------------------------------------------------
// Dependency Injection – Komposition der Anwendung
// ---------------------------------------------------------------------------

// Domänenlogik (PaperlessAiCore.Core): Paperless-/LLM-Clients, Extraktion, Tools
builder.Services.AddPaperlessAiCore();

// Konfiguration & Aktivitäts-Log: einfache Dateien statt Datenbank
// (data/settings.env, data/activity.jsonl) - kein Schema-Migrations-Thema,
// menschenlesbar, leicht zu sichern/inspizieren.
builder.Services.AddSingleton<ISettingsService, FileSettingsService>();
builder.Services.AddSingleton<IActivityLogService, FileActivityLogService>();
builder.Services.AddSingleton<IWriteAuditLog, FileWriteAuditLog>();
builder.Services.AddSingleton<WorkerStatus>();
builder.Services.AddSingleton<IIngestScanService, IngestScanService>();
builder.Services.AddHostedService<IngestWorker>();
builder.Services.AddSingleton<ProcessMetricsService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProcessMetricsService>());
builder.Services.AddSingleton<BuildCounterService>();
builder.Services.AddSingleton<PaperlessMetadataCache>();
builder.Services.AddSingleton<BackfillJobService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSingleton<PaperlessAiCore.Core.ReviewQueueService>();
builder.Services.AddSingleton<PaperlessAiCore.Api.Services.ProcessingJobService>();
var app = builder.Build();


// Bewusst kein UseHttpsRedirection(): Self-Hosted-Kunden laufen meist ohne
// eigenes TLS-Zertifikat direkt im lokalen Netz / hinter einem Reverse-Proxy,
// der TLS terminiert. Lokales `dotnet run` funktioniert damit ohne Dev-Cert-Setup.

// Hostet die gebaute Blazor-WebAssembly-App (PaperlessAiCore.Web) als statische
// Dateien - ein einziger Container/Prozess für Backend + Frontend.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
