using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PaperlessAiCore.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<PaperlessAiCore.Web.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Der Client wird von der Api ausgeliefert -> relative BaseAddress reicht,
// gleicher Origin für Api-Aufrufe (kein CORS nötig).
// WICHTIG: Der .NET-Standard-Timeout von HttpClient ist 100s. Unsere eigene Api
// kann bei Netzwerk-Hakeleien mit Paperless-ngx (Retry mit Backoff) legitim
// länger brauchen - ohne dieses erhöhte Timeout bricht der Browser die Anfrage
// vorzeitig ab, was serverseitig als TaskCanceledException auftaucht, obwohl
// die eigentliche Verarbeitung noch lief.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromMinutes(5),
});
builder.Services.AddScoped<ApiClient>();
builder.Services.AddSingleton<AppLocService>();

await builder.Build().RunAsync();
