using GPOE26.Desktop.Services;
using GPOE26.Offline;
using GPOE26.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GPOE26.Desktop;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // Adresses des services (appsettings.json embarqué, à adapter au déploiement)
        using (var settings = typeof(MauiProgram).Assembly.GetManifestResourceStream("appsettings.json"))
            if (settings is not null) builder.Configuration.AddJsonStream(settings);
        var config = builder.Configuration;

        void Client(string name, string key, string fallback, TimeSpan timeout) =>
            builder.Services.AddHttpClient(name, c =>
            {
                c.BaseAddress = new Uri(config[$"Services:{key}"] ?? fallback);
                c.Timeout = timeout;
            });
        Client(ApiClients.User, "User", "http://localhost:5197", TimeSpan.FromSeconds(30));
        Client(ApiClients.Cours, "Cours", "http://localhost:5188", TimeSpan.FromSeconds(60));
        Client(ApiClients.Quiz, "Quiz", "http://localhost:5163", TimeSpan.FromMinutes(5));    // photos de copies, correction IA
        Client(ApiClients.Chat, "Chat", "http://localhost:5212", TimeSpan.FromMinutes(4));    // tuteur IA (modèles gratuits lents)

        // Espace d'apprentissage partagé avec le site, puis fonctionnement hors connexion
        builder.Services.AddGpoeLearningUi();
        builder.Services.AddSingleton<ISessionStore, ProtectedSessionStore>();
        builder.Services.AddSingleton(new SiteLinks { SiteUrl = (config["App:SiteUrl"] ?? "").TrimEnd('/') });
        builder.Services.AddGpoeOffline(new OfflineOptions
        {
            DataDirectory = Path.Combine(FileSystem.AppDataDirectory, "hors-ligne")
        });
        AppTime.Zone = AppTime.Find(config["App:TimeZone"] ?? "Africa/Douala");

        var app = builder.Build();

        // Réseau signalé par Windows (Wi-Fi retrouvé…) : on vérifie tout de suite si les services répondent
        var monitor = app.Services.GetRequiredService<NetworkMonitor>();
        Connectivity.Current.ConnectivityChanged += (_, _) => _ = monitor.ProbeNowAsync();
        return app;
    }
}
