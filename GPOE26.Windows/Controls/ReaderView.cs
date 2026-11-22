using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace GPOE26.Desktop.Controls;

/// <summary>
/// Surface de lecture (WebView2) : le cours au centre, ou la discussion du tuteur à droite.
/// La page (Assets/reader) est servie depuis le dossier de l'application sous https://app.gpoe26.local :
/// KaTeX compris, rien ne vient d'Internet, la lecture marche hors connexion.
/// Les appels avant la fin du chargement attendent la page (ils ne sont pas perdus).
/// </summary>
public sealed class ReaderView : UserControl
{
    public const string Host = "app.gpoe26.local";

    private readonly WebView2 _web = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;

    public ReaderView()
    {
        Content = _web;
        _web.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
        Loaded += async (_, _) => await StartAsync();
        ActualThemeChanged += async (_, _) => await ApplyThemeAsync();
    }

    private async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        try
        {
            await _web.EnsureCoreWebView2Async();
            var core = _web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "Assets"), CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDevToolsEnabled = System.Diagnostics.Debugger.IsAttached;
            core.Settings.AreDefaultContextMenusEnabled = true;   // copier une formule, un paragraphe
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;           // la taille se règle dans Réglages › Taille du texte
            core.NewWindowRequested += (_, e) => { e.Handled = true; Open(e.Uri); };
            core.WebMessageReceived += OnMessage;
            core.Navigate($"https://{Host}/reader/reader.html");
        }
        catch (Exception ex)
        {
            // Runtime WebView2 absent : la lecture est impossible, on le dit au lieu d'une zone vide
            Content = new TextBlock
            {
                Text = "Le composant d'affichage WebView2 est introuvable sur ce poste. Installez « Microsoft Edge WebView2 Runtime ».\n" + ex.Message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24)
            };
        }
    }

    private async void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        switch (doc.RootElement.GetProperty("type").GetString())
        {
            case "ready":
                _ready.TrySetResult();
                await ApplyThemeAsync();
                break;
            case "open":
                Open(doc.RootElement.GetProperty("url").GetString());
                break;
            case "action":
                var root = doc.RootElement;
                var values = new Dictionary<string, string>();
                if (root.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object)
                    foreach (var p in v.EnumerateObject()) values[p.Name] = p.Value.ToString();
                var value = root.TryGetProperty("value", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
                ActionRequested?.Invoke(this, new ToolAction(root.GetProperty("action").GetString() ?? "", value, values));
                break;
        }
    }

    /// <summary>Lien du cours ou d'une réponse : seules les adresses web s'ouvrent, dans le navigateur de l'utilisateur.</summary>
    private static async void Open(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.Host != Host)
            await global::Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private Task ApplyThemeAsync() =>
        CallAsync("setTheme", ActualTheme == ElementTheme.Dark ? "dark" : "light");

    public Task ShowCourseAsync(string title, string? subject, string html, string? imageBaseUrl) =>
        CallAsync("showCourse", title, subject, html, imageBaseUrl);

    /// <summary>Bouton d'une page d'outil (data-action) : l'action, sa valeur et les champs de son formulaire.</summary>
    public event EventHandler<ToolAction>? ActionRequested;

    /// <summary>Page d'un outil (HTML de <see cref="Services.ToolPages"/>, déjà échappé ou assaini).</summary>
    public Task ShowToolAsync(string html) => CallAsync("showTool", html);

    public Task ShowEmptyAsync(string text) => CallAsync("showEmpty", text);

    /// <summary>Réglage « Taille du texte » : 0, 1 ou 2 points de plus (cours et discussion).</summary>
    public Task SetTextStepAsync(int step) => CallAsync("setTextStep", step);

    public Task ScrollToAsync(string headingId) => CallAsync("scrollToHeading", headingId);

    public Task SetMessagesAsync(IEnumerable<ChatLine> messages) => CallAsync("setMessages", messages.ToList());

    public Task UpdateLastAsync(ChatLine message) => CallAsync("updateLast", message);

    private async Task CallAsync(string function, params object?[] args)
    {
        await _ready.Task;
        var json = string.Join(",", args.Select(a => JsonSerializer.Serialize(a, JsonSerializerOptions.Web)));
        await _web.CoreWebView2.ExecuteScriptAsync($"window.gpoe.{function}({json})");
    }
}

/// <summary>Une ligne de la discussion : texte brut pour l'élève, HTML assaini (Markdig) pour le tuteur.</summary>
public sealed record ChatLine(string Role, string? Text, string? Html, string? Meta, bool Typing = false);

/// <summary>Action demandée par une page d'outil : « quiz-submit », « open-course »…</summary>
public sealed record ToolAction(string Name, string? Value, IReadOnlyDictionary<string, string> Values);
