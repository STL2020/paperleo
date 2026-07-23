using Microsoft.Extensions.DependencyInjection;

namespace PaperlessAiCore.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registriert benannte HttpClients für Paperless- und LLM-Aufrufe.
    /// PaperlessClient/LlmClient selbst werden bewusst NICHT als Singleton/Scoped
    /// registriert, da ihre Konfiguration (URL/Token/Model) zur Laufzeit aus den
    /// admin-editierbaren Settings kommt und sich ändern kann - Aufrufer bauen sie
    /// pro Verwendung mit `new PaperlessClient(httpClientFactory.CreateClient("paperless"), config)`.
    /// </summary>
    public static IServiceCollection AddPaperlessAiCore(this IServiceCollection services)
    {
        // 90s statt vorher 30s: Bei großen Paperless-Instanzen (mehrere tausend
        // Dokumente) kann ein PATCH auf ein Dokument serverseitig Reindexierung
        // auslösen und spürbar länger dauern als bei einer kleinen Instanz. Das
        // Referenzprojekt (Node/axios) setzt hier de facto gar kein Timeout.
        //
        // PooledConnectionLifetime/-IdleTimeout: .NETs HttpClient hält TCP-
        // Verbindungen standardmäßig sehr lange offen und wiederverwendet sie.
        // Kappt der Server (oder ein Gerät dazwischen, z.B. Router/Firewall) eine
        // inaktive Verbindung still im Hintergrund, merkt .NET das nicht sofort -
        // der nächste Request auf dieser "toten" Verbindung hängt dann bis zum
        // Timeout, statt sofort eine neue Verbindung aufzubauen. Direkte curl-Tests
        // gegen dieselbe Paperless-Instanz antworten dagegen immer sofort (jeder
        // curl-Aufruf nutzt eine frische Verbindung) - das deutet stark auf genau
        // dieses Problem hin. Erzwingt daher regelmäßiges Neuaufbauen der Verbindung.
        services.AddHttpClient("paperless", c => c.Timeout = TimeSpan.FromSeconds(90))
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromSeconds(60),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            });

        // LLM-Aufrufe (insbesondere bei längeren Dokumenten/größeren Modellen) dürfen
        // spürbar länger dauern als ein simpler API-Call im LAN.
        services.AddHttpClient("llm", c => c.Timeout = TimeSpan.FromSeconds(90))
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromSeconds(60),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            });

        return services;
    }
}
