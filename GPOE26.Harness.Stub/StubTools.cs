using GPOE26.Harness.Contract;

namespace GPOE26.Harness.Stub;

/// <summary>
/// Les outils tels que le stub les expose.
///
/// En temps normal il se contente de relayer <see cref="ToolPolicy"/> — c'est la table
/// qui fait loi, et une implémentation qui la recopierait finirait par en diverger. Le
/// seul écart possible est un sabotage explicite.
/// </summary>
public static class StubTools
{
    public static IReadOnlyList<string> For(SessionKind kind)
    {
        var tools = ToolPolicy.For(kind);

        // ExtraTool : on expose l'exécution de tests partout, y compris en lecture et en
        // suivi. Le test de conformité sur /outils doit s'en apercevoir.
        if (SabotageSettings.Is(Sabotage.ExtraTool) && !tools.Contains(ToolNames.ExecuterTests))
            return [.. tools, ToolNames.ExecuterTests];

        return tools;
    }

    /// <summary>Descripteurs complets, tels qu'un modèle les recevrait.</summary>
    public static IReadOnlyList<ToolDescriptor> DescriptorsFor(SessionKind kind) =>
        [.. For(kind).Select(Describe)];

    private static ToolDescriptor Describe(string name) => name switch
    {
        ToolNames.ChercherDansLeCours => new(name,
            "Cherche les passages du cours qui traitent d'une question.",
            """{"type":"object","properties":{"requete":{"type":"string"}},"required":["requete"]}"""),

        ToolNames.LireAVoixHaute => new(name,
            "Lit un texte à voix haute pour l'élève.",
            """{"type":"object","properties":{"texte":{"type":"string"}},"required":["texte"]}"""),

        ToolNames.OuEnEstLEleve => new(name,
            "Donne l'état du parcours de l'élève sur ce cours.",
            """{"type":"object","properties":{}}"""),

        ToolNames.GenererExercice => new(name,
            "Produit un exercice sur le cours ou sur une de ses parties.",
            """{"type":"object","properties":{"partie":{"type":"string"}}}"""),

        ToolNames.CorrigerReponse => new(name,
            "Corrige une réponse rédigée par l'élève.",
            """{"type":"object","properties":{"enonce":{"type":"string"},"reponse":{"type":"string"}},"required":["enonce","reponse"]}"""),

        ToolNames.LireFichier => new(name,
            "Lit un fichier de l'espace de travail de l'élève.",
            """{"type":"object","properties":{"chemin":{"type":"string"}},"required":["chemin"]}"""),

        ToolNames.EcrireFichier => new(name,
            "Écrit un fichier dans l'espace de travail de l'élève.",
            """{"type":"object","properties":{"chemin":{"type":"string"},"contenu":{"type":"string"}},"required":["chemin","contenu"]}""",
            Mutating: true),

        ToolNames.ExecuterTests => new(name,
            "Exécute les tests de l'exercice et renvoie les échecs.",
            """{"type":"object","properties":{}}""",
            Mutating: true),

        _ => new(name, name, """{"type":"object","properties":{}}"""),
    };
}
