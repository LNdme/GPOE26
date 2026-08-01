using System.Globalization;
using System.Text;
using Chat.Model;
using GPOE26.Ai;

namespace Chat.Agents;

/// <summary>
/// Rédige, à destination d'un parent, ce qu'il faut savoir du travail de son enfant sur
/// un cours.
///
/// Un tableau de chiffres ne dit pas grand-chose à quelqu'un qui n'a pas suivi le cours :
/// « 6/10 au test final » ne se traduit pas tout seul en « il confond encore les deux
/// formules ». C'est ce passage-là que fait cet agent.
///
/// Deux garde-fous, qui ne sont pas des détails de rédaction :
///
/// · La fiche de suivi du MemoireAgent est dérivée des échanges avec le répétiteur. Elle
///   entre ici parce qu'elle est la source la plus juste sur les difficultés réelles —
///   mais le bilan ne peut en tirer que des NOTIONS, jamais une phrase de l'enfant. Si
///   l'élève découvre que ses mots au tuteur remontent à ses parents, il cessera d'écrire
///   « je n'ai rien compris » et l'outil perdra ce qui le rend utile.
///
/// · Le ton. Ce texte sera lu par quelqu'un qui peut punir. Décrire un écart n'oblige pas
///   à le dramatiser.
/// </summary>
public sealed class BilanAgent(OpenRouterClient client)
{
    private const string SystemPrompt = """
        Tu écris à un PARENT au sujet du travail de son enfant sur un cours.

        Le parent n'a pas suivi ce cours et n'est pas enseignant. Écris comme on parle à
        quelqu'un qui veut simplement savoir où en est son enfant.

        Structure, en 4 à 8 phrases au total, sans titre ni puce :
        1. Ce que l'enfant a fait (temps de travail, étapes franchies) ;
        2. Ce qui est acquis ;
        3. Ce qui coince encore, en nommant la notion ;
        4. Une chose concrète que le parent peut faire ou demander.

        Règles absolues :
        - NE CITE JAMAIS une phrase de l'enfant, ni ce qu'il a écrit au répétiteur, ni une
          question qu'il a posée. Tu parles de NOTIONS, pas de propos.
        - Pas de jargon pédagogique (« compétences transversales », « remédiation »,
          « métacognition »). Des mots ordinaires.
        - Pas de note globale inventée, pas de comparaison à d'autres élèves.
        - Ton factuel et bienveillant. Un écart se décrit, il ne se dramatise pas.
        - Si les données sont trop minces pour conclure, dis-le simplement plutôt que de
          combler par des généralités.

        Réponds uniquement par le texte du bilan.
        """;

    public async Task<string> WriteAsync(
        ChildCourseDetail detail,
        string? tutorNotes,
        CancellationToken ct = default)
    {
        var prompt = new StringBuilder();
        var progress = detail.Progress;

        prompt.AppendLine($"Cours : « {progress.Title} » ({progress.Subject})");
        prompt.AppendLine();

        prompt.AppendLine("Ce que disent les données :");
        prompt.AppendLine($"- Étapes du parcours validées : {progress.StepsPassed} sur {progress.StepsTotal}.");

        if (progress.StepsFailed > 0)
            prompt.AppendLine($"- Étapes encore en échec : {progress.StepsFailed}.");

        if (progress.CurrentStepTitle is { Length: > 0 } current)
            prompt.AppendLine($"- Étape en cours : {current}.");

        prompt.AppendLine($"- Temps de travail cumulé sur ce cours : {FormatDuration(progress.ActiveSeconds)}.");
        prompt.AppendLine($"- Exercices rédigés : {progress.ExercisesDone}.");

        prompt.AppendLine(progress.LastStudiedAt is { } last
            ? $"- Dernière séance : {last.ToLocalTime().ToString("dddd d MMMM à HH'h'mm", French)}."
            : "- Aucune séance de révision enregistrée.");

        if (progress.WeakHeadings.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("Parties du cours ratées lors des tests :");
            foreach (var heading in progress.WeakHeadings)
                prompt.AppendLine($"- {heading}");
        }

        // Ce que l'enfant a rédigé pour son cours : du travail scolaire, pas une
        // conversation. C'est ce qui montre sa compréhension avec ses propres mots.
        var synthesis = detail.WrittenAnswers.Where(a => a.IsSynthesis).ToList();
        if (synthesis.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("Réponses à la question de synthèse (l'enfant explique le cours avec ses mots) :");

            foreach (var answer in synthesis)
            {
                prompt.AppendLine();
                if (answer.ScorePercent is { } score)
                    prompt.AppendLine($"[Appréciation : {score}/100]");

                prompt.AppendLine(Shorten(answer.Answer, 1500));

                if (answer.CorrectionSummary is { Length: > 0 } summary)
                    prompt.AppendLine($"Ce que la correction en a dit : {summary}");
            }
        }

        if (!string.IsNullOrWhiteSpace(tutorNotes))
        {
            prompt.AppendLine();
            prompt.AppendLine("""
                Observations du répétiteur (notes internes — tu peux en tirer les NOTIONS
                difficiles, en aucun cas les reprendre telles quelles ni évoquer le fait
                que l'enfant a posé des questions) :
                """);
            prompt.AppendLine(Shorten(tutorNotes, 2000));
        }

        prompt.AppendLine();
        prompt.Append("Rédige le bilan pour le parent.");

        var bilan = await client.CompleteAsync(new AgentRequest
        {
            Kind = AgentKind.Bilan,
            SystemPrompt = SystemPrompt,
            Turns = [AgentTurn.User(prompt.ToString())],
            MaxTokens = 700,
        }, ct);

        return bilan.Trim();
    }

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>« 24 min » ou « 1 h 05 » — un parent lit vite.</summary>
    private static string FormatDuration(int seconds)
    {
        var minutes = (int)Math.Round(seconds / 60.0);
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00}";
    }

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
