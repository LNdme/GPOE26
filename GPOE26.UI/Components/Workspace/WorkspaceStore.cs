using Microsoft.JSInterop;

namespace GPOE26.UI.Components.Workspace;

/// <summary>
/// Persistance de la disposition des panneaux. Implémentée côté Web par localStorage (BrowserWorkspaceStore) ;
/// côté application Windows, une implémentation ISessionStore (SQLite) s'y substituera en phase 4.
/// </summary>
public interface IWorkspaceStore
{
    Task<WorkspaceState?> LoadAsync();
    Task SaveAsync(WorkspaceState state);
    Task ClearAsync();

    // Chaque espace (lecture, éditeur de cours…) garde sa propre disposition : sans clé, c'est celle de la lecture.
    Task<WorkspaceState?> LoadAsync(string? layoutKey) => LoadAsync();
    Task SaveAsync(WorkspaceState state, string? layoutKey) => SaveAsync(state);
    Task ClearAsync(string? layoutKey) => ClearAsync();
}

/// <summary>Disposition dans le localStorage du navigateur. Données non sensibles : pas de chiffrement.</summary>
public sealed class BrowserWorkspaceStore(IJSRuntime js) : IWorkspaceStore
{
    private IJSObjectReference? _module;

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/GPOE26.UI/workspace.js");

    public Task<WorkspaceState?> LoadAsync() => LoadAsync(null);
    public Task SaveAsync(WorkspaceState state) => SaveAsync(state, null);
    public Task ClearAsync() => ClearAsync(null);

    public async Task<WorkspaceState?> LoadAsync(string? layoutKey)
    {
        try
        {
            var json = await (await ModuleAsync()).InvokeAsync<string?>("loadJson", layoutKey);
            return string.IsNullOrEmpty(json)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<WorkspaceState>(json);
        }
        catch (JSException) { return null; }
        catch (JSDisconnectedException) { return null; }
    }

    public async Task SaveAsync(WorkspaceState state, string? layoutKey)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            await (await ModuleAsync()).InvokeVoidAsync("saveJson", json, layoutKey);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }

    public async Task ClearAsync(string? layoutKey)
    {
        try
        {
            await (await ModuleAsync()).InvokeVoidAsync("clear", layoutKey);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }
}
