using System.ComponentModel;
using System.Text;
using GPOE26.Ai;
using GPOE26.Harness.Contract;
using GPOE26.Harness.Service;
using Microsoft.Extensions.AI;

namespace GPOE26.Harness.Tools;

/// <summary>
/// Les outils du répétiteur.
///
/// Ils ne réécrivent pas les agents de la phase 1 : ils les exposent. `RetrieverAgent`
/// savait déjà chercher dans un cours, `CorrecteurAgent` corriger une réponse — ce qui
/// change, c'est que le modèle décide désormais quand les appeler, au lieu de suivre un
/// pipeline fixé d'avance.
///
/// Chaque méthode publique annotée devient une <see cref="AIFunction"/> : la description
/// et les noms de paramètres partent tels quels au modèle, ce sont eux qui déterminent
/// s'il appelle le bon outil. Elles se lisent donc comme une consigne, pas comme un
/// commentaire de code.
/// </summary>
public sealed class StudyTools(
    CoursClient cours,
    OpenRouterClient openRouter,
    SpeechCache speech,
    ILogger<StudyTools> logger)
{
    /// <summary>Le cours de la session en cours. Posé par le TurnRunner avant chaque tour.</summary>
    public Guid CourseId { get; set; }

    /// <summary>Titre du cours, pour les invites qui en ont besoin.</summary>
    public string CourseTitle { get; set; } = string.Empty;

    // ── Comprendre ────────────────────────────────────────────────────────────

    [Description(
        "Cherche dans le cours de l'élève les passages qui traitent d'une question. " +
        "À utiliser avant toute explication : les réponses doivent venir du cours, pas de tes connaissances générales.")]
    public async Task<string> ChercherDansLeCours(
        [Description("Ce qu'on cherche, formulé comme dans le cours plutôt que comme dans la question de l'élève.")]
        string requete,
        [Description("Nombre de passages voulus. 5 pour une notion précise, 12 pour une vue d'ensemble.")]
        int nombre = 5)
    {
        var passages = await cours.SearchAsync(CourseId, requete, Math.Clamp(nombre, 1, 20));

        if (passages.Count == 0)
            return "Aucun passage du cours ne traite de cela. Dis-le à l'élève plutôt que d'inventer.";

        var sb = new StringBuilder();
        foreach (var p in passages)
        {
            sb.AppendLine($"--- [§ {p.HeadingPath}] ---");
            sb.AppendLine(p.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [Description(
        "Donne l'état du parcours de l'élève sur ce cours : ce qu'il a validé, ce qui reste, " +
        "et les notions sur lesquelles il a échoué.")]
    public async Task<string> OuEnEstLEleve()
    {
        var journey = await cours.GetJourneyAsync(CourseId);
        if (journey is null || journey.Steps.Count == 0)
            return "Le parcours de ce cours n'est pas encore construit.";

        var sb = new StringBuilder();
        sb.AppendLine($"{journey.PassedCount} étape(s) validée(s) sur {journey.Steps.Count}.");

        foreach (var step in journey.Steps)
        {
            var score = step.Score is { } s && step.Total is { } t ? $" ({s}/{t})" : "";
            sb.AppendLine($"- {step.Title} : {step.Status}{score}");
        }

        return sb.ToString();
    }

    [Description(
        "Prépare la lecture à voix haute d'une explication, pour un élève qui comprend mieux en écoutant. " +
        "Renvoie une référence que l'interface utilisera pour jouer l'audio.")]
    public async Task<string> LireAVoixHaute(
        [Description("Le texte à lire. Court : une explication, pas un chapitre.")]
        string texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return "Rien à lire.";

        try
        {
            var key = await speech.SynthesizeAsync(texte);
            return $"Audio prêt. Référence : {key}";
        }
        catch (OpenRouterException ex)
        {
            // Un échec de synthèse ne doit pas faire échouer le tour : l'élève a toujours
            // le texte sous les yeux.
            logger.LogWarning(ex, "Synthèse vocale impossible");
            return "La lecture à voix haute est indisponible pour le moment. Le texte reste affiché.";
        }
    }

    // ── S'exercer ─────────────────────────────────────────────────────────────

    [Description(
        "Produit un exercice de consolidation sur le cours, ou sur une de ses parties. " +
        "L'énoncé doit pouvoir se résoudre avec le seul contenu du cours.")]
    public async Task<string> GenererExercice(
        [Description("Partie du cours visée, telle qu'elle apparaît dans les passages. Vide pour l'ensemble du cours.")]
        string? partie = null)
    {
        var passages = await cours.SearchAsync(
            CourseId,
            string.IsNullOrWhiteSpace(partie) ? CourseTitle : partie,
            string.IsNullOrWhiteSpace(partie) ? 10 : 6);

        var prompt = new StringBuilder();
        prompt.AppendLine($"Cours : « {CourseTitle} »");
        if (!string.IsNullOrWhiteSpace(partie)) prompt.AppendLine($"Partie visée : {partie}");
        prompt.AppendLine();

        foreach (var p in passages)
        {
            prompt.AppendLine($"--- [§ {p.HeadingPath}] ---");
            prompt.AppendLine(p.Content);
        }

        prompt.AppendLine();
        prompt.Append("Propose UN exercice de consolidation, énoncé seul, sans corrigé.");

        return await openRouter.CompleteAsync(new AgentRequest
        {
            Kind = AgentKind.Exercice,
            SystemPrompt =
                "Tu proposes un exercice à un élève de lycée, sur son cours. L'énoncé doit se " +
                "résoudre avec le seul contenu du cours. Une consigne claire, tutoiement, sans corrigé.",
            Turns = [AgentTurn.User(prompt.ToString())],
            MaxTokens = 600,
        });
    }

    [Description(
        "Corrige une réponse rédigée par l'élève, en la confrontant au cours. " +
        "Dit ce qui est juste, ce qui manque, et donne un conseil.")]
    public async Task<string> CorrigerReponse(
        [Description("L'énoncé auquel l'élève répond.")] string enonce,
        [Description("Ce que l'élève a écrit, mot pour mot.")] string reponse)
    {
        if (string.IsNullOrWhiteSpace(reponse)) return "L'élève n'a rien écrit.";

        var passages = await cours.SearchAsync(CourseId, enonce, 6);

        var prompt = new StringBuilder();
        prompt.AppendLine($"Énoncé : {enonce}");
        prompt.AppendLine();
        prompt.AppendLine($"Réponse de l'élève :\n{reponse}");
        prompt.AppendLine();
        prompt.AppendLine("Extraits du cours :");

        foreach (var p in passages)
        {
            prompt.AppendLine($"--- [§ {p.HeadingPath}] ---");
            prompt.AppendLine(p.Content);
        }

        return await openRouter.CompleteAsync(new AgentRequest
        {
            Kind = AgentKind.Relecteur,
            SystemPrompt =
                "Tu corriges la réponse d'un élève de lycée en la confrontant à son cours. " +
                "Dis d'abord ce qui est juste, puis ce qui manque, puis un conseil concret. " +
                "N'invente rien qui ne soit dans les extraits. Tutoiement, ton bienveillant.",
            Turns = [AgentTurn.User(prompt.ToString())],
            MaxTokens = 800,
        });
    }

    // ── Coder ─────────────────────────────────────────────────────────────────
    //
    // L'espace de travail vit chez l'élève — dans l'application bureau, où les tests
    // s'exécutent en WASM, hors ligne. Le harness ne peut donc pas y toucher : ces outils
    // sont des délégations, dont la réponse revient du client.
    //
    // Ils ne sont exposés qu'aux sessions de nature Code, que le Web n'ouvre jamais. Tant
    // que l'application bureau n'existe pas, ils disent honnêtement qu'ils ne peuvent pas
    // agir plutôt que d'échouer d'une façon que le modèle interpréterait mal.

    private const string WorkspacePending =
        "L'espace de travail de l'élève n'est pas accessible depuis cette session. " +
        "Demande-lui ce que contient son fichier ou ce que disent ses tests.";

    [Description("Lit un fichier de l'espace de travail de l'élève.")]
    public string LireFichier([Description("Chemin du fichier, relatif à l'exercice.")] string chemin)
        => WorkspacePending;

    [Description("Écrit un fichier dans l'espace de travail de l'élève.")]
    public string EcrireFichier(
        [Description("Chemin du fichier.")] string chemin,
        [Description("Contenu complet du fichier.")] string contenu)
        => WorkspacePending;

    [Description("Exécute les tests de l'exercice et renvoie les échecs.")]
    public string ExecuterTests() => WorkspacePending;

    // ── Fabrication des fonctions ─────────────────────────────────────────────

    /// <summary>
    /// Les fonctions exposées à une session, dans l'ordre de la table.
    ///
    /// C'est ici que « enregistrement ≠ exposition » cesse d'être une intention : ce que
    /// le modèle reçoit dans ses `tools` est exactement ce que <see cref="ToolPolicy"/>
    /// autorise. Un outil non listé n'est pas seulement caché — il est inappelable.
    /// </summary>
    public IList<AITool> FunctionsFor(SessionKind kind)
    {
        var allowed = ToolPolicy.For(kind);
        var functions = new List<AITool>();

        foreach (var name in allowed)
        {
            AIFunction? function = name switch
            {
                ToolNames.ChercherDansLeCours => AIFunctionFactory.Create(ChercherDansLeCours, name),
                ToolNames.OuEnEstLEleve => AIFunctionFactory.Create(OuEnEstLEleve, name),
                ToolNames.LireAVoixHaute => AIFunctionFactory.Create(LireAVoixHaute, name),
                ToolNames.GenererExercice => AIFunctionFactory.Create(GenererExercice, name),
                ToolNames.CorrigerReponse => AIFunctionFactory.Create(CorrigerReponse, name),
                ToolNames.LireFichier => AIFunctionFactory.Create(LireFichier, name),
                ToolNames.EcrireFichier => AIFunctionFactory.Create(EcrireFichier, name),
                ToolNames.ExecuterTests => AIFunctionFactory.Create(ExecuterTests, name),
                _ => null,
            };

            if (function is not null) functions.Add(function);
            else logger.LogWarning("Outil « {Tool} » listé par la table mais sans implémentation", name);
        }

        return functions;
    }
}
