using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace GPOE26.UI.Components.Workspace;

/// <summary>
/// État partagé entre l'espace et ses panneaux : la disposition (ouverte, réduite, proportions, agrandissement),
/// la persistance et les actions. Les panneaux s'enregistrent à leur initialisation, dans l'ordre de rendu.
/// </summary>
public sealed class WorkspaceContext
{
    private readonly List<(string Id, string Title, string Icon, bool StartCollapsed, string? ShortcutKey, bool StartClosed)> _panels = new();
    private readonly IWorkspaceStore _store;
    private readonly string? _layoutKey;
    private DotNetObjectReference<WorkspaceContext>? _dotNet;

    /// <param name="layoutKey">Disposition propre à cet espace (null : celle de la lecture d'un cours).</param>
    public WorkspaceContext(IWorkspaceStore store, string? layoutKey = null)
    {
        _store = store;
        _layoutKey = layoutKey;
    }

    /// <summary>Lettres des raccourcis Alt+… des panneaux de cet espace.</summary>
    public IReadOnlyList<string> ShortcutKeys =>
        _panels.Where(p => p.ShortcutKey is { Length: 1 }).Select(p => p.ShortcutKey!.ToLowerInvariant()).ToList();

    public WorkspaceState State { get; private set; } = new();

    /// <summary>Panneaux ouverts, dans l'ordre de la page (stable pendant la visite).</summary>
    public IReadOnlyList<string> OpenIds { get; private set; } = [];

    public IReadOnlyList<string> ExpandedIds { get; private set; } = [];

    public event Action? Changed;

    /// <summary>Tous les panneaux de la page, dans l'ordre d'enregistrement (ouverts ou fermés).</summary>
    public IReadOnlyList<string> AllIds => _panels.Select(p => p.Id).ToList();

    public int AllIndexOf(string id) => OpenIds.ToList().IndexOf(id);

    /// <summary>Replié effectif pour l'affichage : faux pour le panneau de secours (tous repliés).</summary>
    public bool EffectiveCollapsed(string id)
    {
        if (!State.Panels.TryGetValue(id, out var p)) return false;
        if (!p.Collapsed) return false;
        return !(ExpandedIds.Count == 0 && OpenIds.FirstOrDefault() == id);
    }

    private IReadOnlySet<string> StartClosedSet() => _panels.Where(p => p.StartClosed).Select(p => p.Id).ToHashSet();

    /// <summary>Le panneau est-il ouvert (affiché dans la grille) ?</summary>
    public bool IsOpen(string id) => State.Panels.TryGetValue(id, out var p) && p.Open;

    private IReadOnlyDictionary<string, bool> StartCollapsedMap() =>
        _panels.Where(p => p.StartCollapsed).ToDictionary(p => p.Id, _ => true);

    public void Register(string id, string title, string icon, bool startCollapsed = false, string? shortcutKey = null, bool startClosed = false)
    {
        if (_panels.Any(p => p.Id == id)) return;
        _panels.Add((id, title, icon, startCollapsed, shortcutKey, startClosed));
        // Reconcile ajoute le nouveau venu à la disposition (ouvert, déplié sauf si rail ou fermeture demandés au départ)
        State = WorkspaceSizing.Reconcile(State, _panels.Select(p => p.Id).ToList(), StartCollapsedMap(), StartClosedSet());
        Recompute();
        Notify();
    }

    public IReadOnlyList<(string Id, string Title, string Icon)> ClosedPanels =>
        _panels.Where(p => State.Panels.TryGetValue(p.Id, out var s) && !s.Open)
               .Select(p => (p.Id, p.Title, p.Icon))
               .ToList();

    /// <summary>Charge la disposition enregistrée (au premier rendu) et l'aligne sur les panneaux de la page.</summary>
    public async Task LoadAsync()
    {
        var saved = await _store.LoadAsync(_layoutKey);
        State = WorkspaceSizing.Reconcile(saved, _panels.Select(p => p.Id).ToList(), StartCollapsedMap(), StartClosedSet());
        Recompute();
        Notify();
    }

    public DotNetObjectReference<WorkspaceContext> DotNetRef =>
        _dotNet ??= DotNetObjectReference.Create(this);

    [JSInvokable]
    public async Task CommitSplitAsync(double[] expandedPixels, double totalWidth)
    {
        var expanded = ExpandedIds;
        var parts = WorkspaceSizing.PartsFromPixels(expandedPixels);
        for (var i = 0; i < expanded.Count && i < parts.Length; i++)
        {
            var p = State.Panels[expanded[i]];
            State.Panels[expanded[i]] = p with { Part = parts[i] };
        }
        await SaveAsync();
        Recompute();
        Notify();
    }

    [JSInvokable]
    public async Task ResetSplitAsync(int gap)
    {
        NudgePair(gap, 0, equalize: true);
        await SaveAsync();
        Recompute();
        Notify();
    }

    public async Task OnSplitterKeyAsync(int gap, KeyboardEventArgs e)
    {
        var step = (e.ShiftKey ? 0.24 : 0.08) * (e.Key == "ArrowLeft" ? -1 : e.Key == "ArrowRight" ? 1 : 0);
        if (step == 0) return;
        NudgePair(gap, step);
        await SaveAsync();
        Recompute();
        Notify();
    }

    private void NudgePair(int gap, double delta, bool equalize = false)
    {
        var open = OpenIds.ToList();
        if (gap < 0 || gap + 1 >= open.Count) return;
        var a = State.Panels[open[gap]];
        var b = State.Panels[open[gap + 1]];
        if (a.Collapsed || b.Collapsed) return;
        var (left, right) = equalize
            ? WorkspaceSizing.EqualPair(a.Part + b.Part)
            : WorkspaceSizing.ResizePair(a.Part, b.Part, delta);
        State.Panels[open[gap]] = a with { Part = left };
        State.Panels[open[gap + 1]] = b with { Part = right };
    }

    /// <summary>Fermé → rouvre (déplié) ; déplié → se replie en rail ; rail → se déplie.</summary>
    public async Task TogglePanelAsync(string id)
    {
        if (!State.Panels.TryGetValue(id, out var p)) return;
        if (!p.Open) { await OpenPanelAsync(id); return; }
        await ToggleCollapseAsync(id);
    }

    [JSInvokable]
    public Task ShortcutPanelAsync(string id) => TogglePanelAsync(id);

    /// <summary>Alt+lettre : bascule le panneau qui porte ce raccourci.</summary>
    [JSInvokable]
    public Task ShortcutKeyAsync(string key) =>
        _panels.FirstOrDefault(p => string.Equals(p.ShortcutKey, key, StringComparison.OrdinalIgnoreCase)).Id is { } id
            ? TogglePanelAsync(id)
            : Task.CompletedTask;

    /// <summary>Échap : quitter le mode agrandissement.</summary>
    [JSInvokable]
    public async Task ShortcutEscAsync()
    {
        if (State.MaximizedId is null) return;
        State.MaximizedId = null;
        await SaveAsync();
        Recompute();
        Notify();
    }

    public async Task OpenPanelAsync(string id)
    {
        if (!State.Panels.TryGetValue(id, out var p)) return;
        State.Panels[id] = p with { Open = true, Collapsed = false };
        if (State.MaximizedId is { } max && max != id) State.MaximizedId = null;
        await SaveAsync();
        Recompute();
        Notify();
    }

    public async Task CloseAsync(string id)
    {
        if (OpenIds.Count <= 1) return; // la page garde au moins un panneau
        if (!State.Panels.TryGetValue(id, out var p)) return;
        State.Panels[id] = p with { Open = false };
        if (State.MaximizedId == id) State.MaximizedId = null;
        await SaveAsync();
        Recompute();
        Notify();
    }

    public async Task ToggleCollapseAsync(string id)
    {
        if (!State.Panels.TryGetValue(id, out var p) || !p.Open) return;

        if (!p.Collapsed && ExpandedIds.Count <= 1) return; // au moins un panneau déplié

        State.Panels[id] = p with { Collapsed = !p.Collapsed };
        if (p.Collapsed)
        {
            // Le rail rouvre sans toucher au maximisé ; rouvrir un rail agrandi n'a pas de sens
        }
        if (State.MaximizedId == id && !p.Collapsed) State.MaximizedId = null;
        await SaveAsync();
        Recompute();
        Notify();
    }

    public async Task ToggleMaximizeAsync(string id)
    {
        if (!State.Panels.TryGetValue(id, out var p) || !p.Open) return;
        State.MaximizedId = State.MaximizedId == id ? null : id;
        if (State.MaximizedId is not null)
            State.Panels[id] = p with { Collapsed = false };
        await SaveAsync();
        Recompute();
        Notify();
    }

    public async Task ResetAsync()
    {
        State = WorkspaceSizing.CreateDefault(_panels.Select(p => p.Id).ToList(), StartCollapsedMap(), StartClosedSet());
        await _store.ClearAsync(_layoutKey);
        await SaveAsync();
        Recompute();
        Notify();
    }

    private void Recompute()
    {
        var ids = _panels.Select(p => p.Id).ToList();
        OpenIds = WorkspaceSizing.OpenIds(State, ids);
        ExpandedIds = WorkspaceSizing.ExpandedIds(State, ids);
    }

    private void Notify() => Changed?.Invoke();

    private async Task SaveAsync() => await _store.SaveAsync(State, _layoutKey);

    public void DisposeDotNet() => _dotNet?.Dispose();
}
