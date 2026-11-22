namespace GPOE26.UI.Components.Workspace;

/// <summary>État persisté d'un panneau (disposition globale à l'espace élève, clé localStorage gpoe26.workspace.v1).</summary>
public sealed record WsPanelState(bool Open = true, bool Collapsed = false, double Part = 1);

/// <summary>Disposition complète : quels panneaux sont ouverts, réduits, leurs proportions, lequel est agrandi.</summary>
public sealed class WorkspaceState
{
    public string? MaximizedId { get; set; }
    public Dictionary<string, WsPanelState> Panels { get; set; } = new();
}

/// <summary>
/// Règles de taille des panneaux, sans DOM : le composant et les tests les utilisent.
/// « Part » est un coefficient en fr, normalisé pour que chaque panneau déplié garde au moins MinPart.
/// </summary>
public static class WorkspaceSizing
{
    public const double SplitterPx = 6;
    public const double CollapsedPx = 52;
    public const double MinPart = 0.25;

    /// <summary>
    /// Disposition par défaut : tout ouvert a parts égales (panneaux « startCollapsed » démarrent en rail,
    /// panneaux « startClosed » démarrent fermés, comme l'agent de l'administration qui s'ouvre à la demande).
    /// </summary>
    public static WorkspaceState CreateDefault(IReadOnlyList<string> orderedIds, IReadOnlyDictionary<string, bool>? startCollapsed = null,
        IReadOnlySet<string>? startClosed = null) => new()
    {
        Panels = orderedIds.ToDictionary(id => id, id => NewPanel(id, startCollapsed, startClosed)),
    };

    private static WsPanelState NewPanel(string id, IReadOnlyDictionary<string, bool>? startCollapsed, IReadOnlySet<string>? startClosed) =>
        new(Open: startClosed?.Contains(id) != true, Collapsed: startCollapsed?.TryGetValue(id, out var c) == true && c, Part: 1);

    /// <summary>
    /// Aligne l'état enregistré sur les panneaux demandés : ajoute les absents, écarte les inconnus,
    /// normalise les parts, et oublie un MaximizedId qui n'existe plus ou n'est plus ouvert.
    /// Garantit au moins un panneau ouvert et déplié.
    /// Liste vide (appelé avant que les panneaux ne s'enregistrent) : l'état brut est rendu tel quel,
    /// sinon la disposition serait détruite à chaque chargement.
    /// « startCollapsed » : panneaux définis pour démarrer en rail (ex. sommaire) ; ne s'applique qu'aux panneaux inconnus.
    /// </summary>
    public static WorkspaceState Reconcile(WorkspaceState? saved, IReadOnlyList<string> orderedIds, IReadOnlyDictionary<string, bool>? startCollapsed = null,
        IReadOnlySet<string>? startClosed = null)
    {
        if (saved is null) return CreateDefault(orderedIds, startCollapsed, startClosed);
        if (orderedIds.Count == 0) return saved;

        var state = new WorkspaceState { MaximizedId = saved.MaximizedId };
        foreach (var id in orderedIds)
        {
            state.Panels[id] = saved.Panels.TryGetValue(id, out var p)
                ? p with { Part = double.IsFinite(p.Part) && p.Part > 0 ? p.Part : 1 }
                : NewPanel(id, startCollapsed, startClosed);
        }

        // Les panneaux déjà connus mais pas encore (ré)enregistrés cette passe sont conservés :
        // l'enregistrement est incrémental (sommaire, lecture, tuteur sur trois rendus) et perdre leurs
        // réglages à chaque étape reviendrait à les recréer en « ouvert » à chaque visite.
        foreach (var (id, p) in saved.Panels)
        {
            if (!state.Panels.ContainsKey(id)) state.Panels[id] = p;
        }

        if (state.MaximizedId is { } max && (!state.Panels.TryGetValue(max, out var m) || !m.Open))
            state.MaximizedId = null;

        return state;
    }

    public static IReadOnlyList<string> OpenIds(WorkspaceState state, IReadOnlyList<string> orderedIds) =>
        orderedIds.Where(id => state.Panels.TryGetValue(id, out var p) && p.Open).ToList();

    public static IReadOnlyList<string> ExpandedIds(WorkspaceState state, IReadOnlyList<string> orderedIds) =>
        orderedIds.Where(id => state.Panels.TryGetValue(id, out var p) && p.Open && !p.Collapsed).ToList();

    /// <summary>
    /// Panneau à afficher déplié quand TOUT est replié (disque local bricolé, vieille disposition) :
    /// le premier ouvert. Ne modifie pas l'état — juste l'affichage.
    /// </summary>
    public static string? FallbackExpandedId(WorkspaceState state, IReadOnlyList<string> orderedIds) =>
        ExpandedIds(state, orderedIds).FirstOrDefault() ?? OpenIds(state, orderedIds).FirstOrDefault();

    /// <summary>grid-template-columns : rails de 52 px pour les panneaux réduits. La poignée n'occupe une piste
    /// que si les deux panneaux voisins sont dépliés — le nombre de pistes doit égaler le nombre d'enfants de la grille.</summary>
    public static string BuildGridTemplate(WorkspaceState state, IReadOnlyList<string> orderedIds)
    {
        var open = OpenIds(state, orderedIds);
        if (state.MaximizedId is { } max && open.Contains(max)) return "1fr";

        // Tous repliés : le premier ouvert sert de panneau déplié de secours (vue, pas de réécriture)
        string? fallback = ExpandedIds(state, orderedIds).Count == 0 ? open.FirstOrDefault() : null;

        var tracks = new List<string>();
        for (var i = 0; i < open.Count; i++)
        {
            var collapsed = state.Panels[open[i]].Collapsed && open[i] != fallback;
            if (i > 0 && !collapsed && !(state.Panels[open[i - 1]].Collapsed && open[i - 1] != fallback))
                tracks.Add($"{SplitterPx:0}px");
            tracks.Add(collapsed ? $"{CollapsedPx:0}px"
                     // Culture invariante : en français « 1,42fr » est rejeté par le navigateur et toute la grille
                     // retombe à des parts égales (le panneau redimensionné reprend sa taille au relâchement)
                     : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{state.Panels[open[i]].Part:0.####}fr"));
        }
        return string.Join(' ', tracks);
    }

    /// <summary>Depuis les largeurs en pixels mesurées pendant le glissement, recalcule des parts fr (≥ MinPart).</summary>
    public static double[] PartsFromPixels(IReadOnlyList<double> pixels)
    {
        var n = pixels.Count;
        if (n == 0) return [];
        var total = pixels.Sum();
        if (total <= 0) return Enumerable.Repeat(1.0, n).ToArray();

        var parts = pixels.Select(p => p / total * n).ToArray();
        // Un panneau étranglé sous le minimum récupère du terrain : les autres cèdent leur part au prorata
        for (var pass = 0; pass < n; pass++)
        {
            var smallest = Array.IndexOf(parts, parts.Min());
            if (parts[smallest] >= MinPart) break;
            var deficit = MinPart - parts[smallest];
            parts[smallest] = MinPart;
            var others = Enumerable.Range(0, n).Where(i => i != smallest).ToArray();
            var othersSum = others.Sum(i => parts[i]);
            if (othersSum <= deficit)
            {
                foreach (var i in others) parts[i] = MinPart;
                break;
            }
            foreach (var i in others)
                parts[i] = Math.Max(MinPart, parts[i] - deficit * parts[i] / othersSum);
        }
        return parts;
    }

    /// <summary>Resize au clavier : déplace la frontière entre deux panneaux adjacents (total constant, minimum MinPart).</summary>
    public static (double Left, double Right) ResizePair(double left, double right, double delta)
    {
        var total = left + right;
        var newLeft = Math.Clamp(left + delta, MinPart, total - MinPart);
        return (newLeft, total - newLeft);
    }

    /// <summary>Partage 50/50 pour le double-clic sur la poignée.</summary>
    public static (double Left, double Right) EqualPair(double total) =>
        total <= 0 ? (1, 1) : (total / 2, total / 2);

    /// <summary>Part en pourcentage du panneau de gauche, pour aria-valuenow.</summary>
    public static int PairPercent(double left, double right)
    {
        var total = left + right;
        return total <= 0 ? 50 : (int)Math.Round(left / total * 100);
    }
}
