using System.Text.Json;
using GPOE26.Desktop.Services;
using GPOE26.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace GPOE26.Desktop;

/// <summary>
/// Démarrage de l'application : services partagés avec le site (GPOE26.UI), adresses des API, mémoire locale.
/// Une seule fenêtre, donc une seule portée de services (l'équivalent d'un circuit Blazor sur le site).
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var store = new LocalStore();
        // Données de WebView2 avec le reste, plutôt qu'à côté de l'exécutable (dossier parfois en lecture seule)
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(store.Folder, "WebView2"));
        // Taille du texte choisie dans les réglages : appliquée avant que les contrôles ne lisent leurs ressources
        Typography.Apply(Resources, Typography.Step(await store.GetSettingAsync(Typography.SettingKey)));

        Services = ConfigureServices(store).CreateScope().ServiceProvider;
        _window = new MainWindow();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices(LocalStore store)
    {
        var addresses = ServiceAddresses.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        var services = new ServiceCollection();

        services.AddSingleton(store);
        services.AddSingleton(addresses);
        services.AddSingleton<Connectivity>();
        services.AddScoped<DesktopSessionStore>();
        services.AddScoped<ISessionStore>(sp => sp.GetRequiredService<DesktopSessionStore>());
        services.AddScoped<AppSession>();
        services.AddScoped<OfflineData>();
        services.AddScoped<DesktopTutorApi>();
        services.AddGpoeLearningUi();

        void Client(string name, string url, TimeSpan timeout) =>
            services.AddHttpClient(name, c => { c.BaseAddress = new Uri(url); c.Timeout = timeout; });
        Client(ApiClients.User, addresses.User, TimeSpan.FromSeconds(30));
        Client(ApiClients.Cours, addresses.Cours, TimeSpan.FromMinutes(3));   // comme le site : mise en forme et génération par l'IA
        Client(ApiClients.Quiz, addresses.Quiz, TimeSpan.FromMinutes(3));     // quiz et exercices générés par des modèles gratuits, parfois lents
        Client(ApiClients.Site, addresses.ApiService, TimeSpan.FromSeconds(30));
        Client(ApiClients.Chat, addresses.Chat, TimeSpan.FromMinutes(5));   // réponse du tuteur en flux : jusqu'à quatre minutes

        return services.BuildServiceProvider();
    }
}

/// <summary>Adresses des services, lues dans appsettings.json (section « Services »), comme Services:* sur le site.</summary>
/// <remarks>Web : le site, pour ce qui ne se fait que là (composer un devoir, l'emploi du temps).</remarks>
public sealed record ServiceAddresses(string User, string Cours, string Chat, string Quiz, string ApiService, string Web)
{
    public static ServiceAddresses Load(string path)
    {
        var defaults = new ServiceAddresses("http://localhost:5197", "http://localhost:5188", "http://localhost:5212",
            "http://localhost:5163", "http://localhost:5455", "http://localhost:5093");
        if (!File.Exists(path)) return defaults;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("Services", out var s)) return defaults;
        string Read(string name, string fallback) =>
            s.TryGetProperty(name, out var v) && v.GetString() is { Length: > 0 } url ? url : fallback;
        return new ServiceAddresses(Read("User", defaults.User), Read("Cours", defaults.Cours), Read("Chat", defaults.Chat),
            Read("Quiz", defaults.Quiz), Read("ApiService", defaults.ApiService), Read("Web", defaults.Web));
    }
}
