using System.Globalization;
using System.Text;
using GPOE26.Harness.Contract;

namespace GPOE26.Harness.Conformance;

/// <summary>
/// Un scénario du match : ce qu'on demande, et ce qu'on espérait voir appelé.
/// </summary>
/// <param name="ExpectedTools">
/// Outils qu'un bon harness devrait mobiliser. Ce n'est pas une exigence — un modèle peut
/// répondre correctement autrement — mais l'écart se mesure et se compare.
/// </param>
public record Scenario(
    string Name,
    SessionKind Kind,
    string Message,
    IReadOnlyList<string> ExpectedTools
);

/// <summary>
/// L'étage comportement : on mesure, on ne juge pas.
///
/// Ces tests ne font jamais échouer un build. La raison est simple : ce qu'ils observent
/// dépend de ce qu'un modèle a décidé, et un build rouge parce qu'un modèle a formulé sa
/// réponse autrement serait un build qu'on finirait par ignorer.
///
/// Ce qu'ils produisent, c'est le tableau qui décidera lequel des deux harness on déploie.
/// Contre le stub, les chiffres n'ont aucun sens — c'est la plomberie qu'on valide ici,
/// pour qu'elle soit prête le jour où il y a deux vrais candidats.
/// </summary>
[Collection(HarnessCollection.Name)]
public class BehaviourTests(HarnessTarget target, ITestOutputHelper output)
{
    private static readonly Scenario[] Scenarios =
    [
        new("Expliquer une notion", SessionKind.Lecture,
            "Je n'ai pas compris ce que veut dire « fonction dérivable ». Explique-moi.",
            [ToolNames.ChercherDansLeCours]),

        new("Demander une définition", SessionKind.Lecture,
            "C'est quoi exactement la définition d'une asymptote ?",
            [ToolNames.ChercherDansLeCours]),

        new("Résumer le cours", SessionKind.Lecture,
            "Peux-tu me résumer l'ensemble de ce cours ?",
            [ToolNames.ChercherDansLeCours]),

        new("Savoir où on en est", SessionKind.Lecture,
            "Il me reste quoi à faire dans ce cours ?",
            [ToolNames.OuEnEstLEleve]),

        new("Demander un exercice", SessionKind.Exercice,
            "Donne-moi un exercice sur la deuxième partie.",
            [ToolNames.GenererExercice]),

        new("Faire corriger une réponse", SessionKind.Exercice,
            "J'ai répondu que la dérivée est nulle partout. C'est juste ?",
            [ToolNames.CorrigerReponse]),

        new("Comprendre un test qui échoue", SessionKind.Code,
            "Mon test échoue avec « IndexError ». Pourquoi ?",
            [ToolNames.LireFichier, ToolNames.ExecuterTests]),

        new("Corriger son code", SessionKind.Code,
            "Corrige ma fonction pour qu'elle passe les tests.",
            [ToolNames.LireFichier, ToolNames.EcrireFichier, ToolNames.ExecuterTests]),

        new("Question hors sujet", SessionKind.Lecture,
            "Quel temps fait-il à Yaoundé aujourd'hui ?",
            []),

        new("Question vague", SessionKind.Lecture,
            "Je comprends rien.",
            [ToolNames.ChercherDansLeCours]),
    ];

    [Fact]
    public async Task Tableau_comparatif()
    {
        var rows = new List<Measure>();

        foreach (var scenario in Scenarios)
        {
            var client = target.AsStudent(Checks.Eleve);

            try
            {
                var session = await TurnReader.OpenAsync(client, Checks.Cours, scenario.Kind);
                var transcript = await TurnReader.PlayAsync(client, session.Id, scenario.Message);

                var called = transcript.ToolsCalled.ToList();
                var wanted = scenario.ExpectedTools;

                rows.Add(new Measure(
                    scenario.Name,
                    scenario.Kind,
                    transcript.FirstTokenAfter,
                    transcript.Total,
                    transcript.Iterations,
                    called,
                    // Part des outils espérés qui ont effectivement été appelés. Sur un
                    // scénario sans outil attendu, la valeur n'a pas de sens : on la laisse
                    // vide plutôt que d'inventer un 100 %.
                    wanted.Count == 0 ? null : (double)wanted.Count(called.Contains) / wanted.Count,
                    transcript.Terminal?.Type ?? "(aucun)"));
            }
            catch (Exception ex)
            {
                // Un scénario qui casse est une information, pas une raison d'interrompre
                // la mesure des neuf autres.
                rows.Add(Measure.Failed(scenario, ex));
            }
        }

        var report = Render(rows, target.Description);
        output.WriteLine(report);

        // Écrit à côté des binaires de test : c'est ce fichier qu'on comparera entre les
        // deux harness, pas la sortie console d'une exécution passée.
        var path = Path.Combine(AppContext.BaseDirectory, "comportement.md");
        await File.WriteAllTextAsync(path, report);
        output.WriteLine($"\nTableau écrit dans {path}");

        // La seule exigence de cet étage : avoir pu mesurer. Le reste se lit, ne se juge pas.
        Assert.NotEmpty(rows);
    }

    private record Measure(
        string Scenario,
        SessionKind Kind,
        TimeSpan? FirstToken,
        TimeSpan Total,
        int Iterations,
        IReadOnlyList<string> ToolsCalled,
        double? ToolMatch,
        string Terminal
    )
    {
        public static Measure Failed(Scenario s, Exception ex) =>
            new(s.Name, s.Kind, null, TimeSpan.Zero, 0, [], null, $"échec : {ex.GetType().Name}");
    }

    private static string Render(IReadOnlyList<Measure> rows, string targetDescription)
    {
        var sb = new StringBuilder();
        var c = CultureInfo.InvariantCulture;

        sb.AppendLine("# Comportement du harness");
        sb.AppendLine();
        sb.AppendLine($"Cible : **{targetDescription}**  ");
        sb.AppendLine($"Mesuré le {DateTime.Now.ToString("d MMMM yyyy 'à' HH'h'mm", new CultureInfo("fr-FR"))}");
        sb.AppendLine();
        sb.AppendLine("| Scénario | Nature | 1er token | Total | Tours | Outils appelés | Outils attendus | Fin |");
        sb.AppendLine("|---|---|--:|--:|--:|---|--:|---|");

        foreach (var r in rows)
        {
            var first = r.FirstToken is { } f ? $"{f.TotalMilliseconds.ToString("F0", c)} ms" : "—";
            var total = r.Total == TimeSpan.Zero ? "—" : $"{r.Total.TotalMilliseconds.ToString("F0", c)} ms";
            var tools = r.ToolsCalled.Count == 0 ? "—" : string.Join(", ", r.ToolsCalled);
            var match = r.ToolMatch is { } m ? $"{(m * 100).ToString("F0", c)} %" : "—";

            sb.AppendLine($"| {r.Scenario} | {r.Kind} | {first} | {total} | {r.Iterations} | {tools} | {match} | {r.Terminal} |");
        }

        var measured = rows.Where(r => r.FirstToken is not null).ToList();
        if (measured.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Synthèse");
            sb.AppendLine();

            var median = Median(measured.Select(r => r.FirstToken!.Value.TotalMilliseconds).ToList());
            sb.AppendLine($"- Premier token, médiane : **{median.ToString("F0", c)} ms**");

            var matches = rows.Where(r => r.ToolMatch is not null).Select(r => r.ToolMatch!.Value).ToList();
            if (matches.Count > 0)
                sb.AppendLine($"- Outils attendus effectivement appelés : **{(matches.Average() * 100).ToString("F0", c)} %**");

            sb.AppendLine($"- Scénarios terminés normalement : **{rows.Count(r => r.Terminal == HarnessEventType.Done)}/{rows.Count}**");
        }

        sb.AppendLine();
        sb.AppendLine("> La médiane plutôt que la moyenne : un seul appel lent fausse une moyenne,");
        sb.AppendLine("> et c'est l'attente ordinaire qu'on cherche à comparer, pas le pire cas.");

        return sb.ToString();
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;

        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }
}
