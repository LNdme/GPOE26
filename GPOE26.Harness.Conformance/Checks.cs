using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GPOE26.Harness.Contract;

namespace GPOE26.Harness.Conformance;

/// <summary>
/// Les vérifications que l'étage conformité impose, extraites pour être exécutables deux
/// fois : par les tests eux-mêmes, et par les tests du juge qui exigent qu'elles échouent
/// sur un service cassé.
///
/// C'est cette réutilisation qui donne sa valeur à la preuve. Vérifier séparément qu'un
/// stub saboté renvoie bien n'importe quoi ne démontrerait que le sabotage ; c'est en
/// faisant tomber <b>le code qui garde la production</b> qu'on démontre le garde-fou.
/// </summary>
public static class Checks
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly Guid Eleve = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AutreEleve = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Parent = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Cours = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>Les outils exposés correspondent exactement à la table.</summary>
    public static async Task ToolsMatchPolicy(HarnessTarget target, SessionKind kind)
    {
        var descriptors = await target.AsStudent(Eleve)
            .GetFromJsonAsync<List<ToolDescriptor>>($"outils?kind={(int)kind}", Json);

        Assert.NotNull(descriptors);

        var exposed = descriptors!.Select(d => d.Name).OrderBy(n => n).ToArray();
        var expected = ToolPolicy.For(kind).OrderBy(n => n).ToArray();

        // Égalité stricte : un outil de trop est une capacité offerte au modèle que la
        // table n'a pas accordée, ce qui est aussi grave qu'un outil manquant.
        Assert.Equal(expected, exposed);
    }

    /// <summary>Aucun contenu d'échange ne remonte au parent.</summary>
    public static async Task NoMessageContentInFollowUp(HarnessTarget target)
    {
        var eleve = target.AsStudent(Eleve);

        var session = await TurnReader.OpenAsync(eleve, Cours, SessionKind.Lecture);
        await TurnReader.PlayAsync(eleve, session.Id, "je n'ai rien compris au chapitre 3");

        var suivi = await target.AsParent(Parent, Eleve).GetStringAsync($"suivi/{Eleve}/resume");

        // On cherche des marqueurs d'échange plutôt qu'un champ nommé : une fuite arrive
        // par un champ auquel personne n'avait pensé, pas par celui qu'on surveille.
        foreach (var marker in (string[])["je n'ai rien compris", "Élève :", "Eleve :", "\"role\"", "\"content\""])
            Assert.DoesNotContain(marker, suivi, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Un tour se termine, et le dernier évènement est terminal.</summary>
    public static async Task TurnEndsWithTerminal(HarnessTarget target)
    {
        var client = target.AsStudent(Eleve);
        var session = await TurnReader.OpenAsync(client, Cours, SessionKind.Lecture);

        var transcript = await TurnReader.PlayAsync(
            client, session.Id, "Explique-moi la première partie.", TimeSpan.FromSeconds(30));

        Assert.NotNull(transcript.Terminal);
        Assert.Equal(HarnessEventType.Done, transcript.Terminal!.Type);
        Assert.False(string.IsNullOrWhiteSpace(transcript.Terminal.Reply));
    }

    /// <summary>Un parent ne consulte que ses propres enfants.</summary>
    public static async Task ParentCannotViewOtherChild(HarnessTarget target)
    {
        var response = await target.AsParent(Parent, AutreEleve).GetAsync($"suivi/{Eleve}/resume");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Une session reprise rend ce qui s'y est dit.</summary>
    public static async Task ResumedSessionHasHistory(HarnessTarget target)
    {
        var client = target.AsStudent(Eleve);
        var session = await TurnReader.OpenAsync(client, Cours, SessionKind.Lecture);

        await TurnReader.PlayAsync(client, session.Id, "Première question.");

        var history = await client.GetFromJsonAsync<SessionHistoryDto>($"sessions/{session.Id}", Json);

        Assert.NotNull(history);
        Assert.NotEmpty(history!.Turns);
        Assert.Contains(history.Turns, t => t.Role == "user" && t.Content.Contains("Première question"));
        Assert.Contains(history.Turns, t => t.Role == "assistant");
    }

    /// <summary>
    /// Un outil qui échoue produit une erreur visible, pas un tour qui fait comme si.
    ///
    /// Le marqueur `__fail_tool__` est une convention de la suite : elle demande à
    /// l'implémentation de faire échouer son premier outil. Une implémentation qui ne le
    /// reconnaît pas répond normalement et passe le test — c'est voulu, on ne peut pas
    /// exiger d'un harness qu'il sache se saborder.
    /// </summary>
    public static async Task ToolFailureSurfacesAsError(HarnessTarget target)
    {
        var client = target.AsStudent(Eleve);
        var session = await TurnReader.OpenAsync(client, Cours, SessionKind.Lecture);

        var transcript = await TurnReader.PlayAsync(
            client, session.Id, "__fail_tool__ que se passe-t-il ?", TimeSpan.FromSeconds(30));

        Assert.NotNull(transcript.Terminal);

        // Si des outils ont été appelés et que l'un a échoué, le tour ne peut pas se
        // conclure sur une réponse comme si de rien n'était.
        var swallowed = transcript.Events.Any(
            e => e.Type == HarnessEventType.ToolResult && (e.ToolResult?.Contains("échec") ?? false));

        Assert.False(swallowed && transcript.Terminal!.Type == HarnessEventType.Done,
            "Un outil a échoué et le tour s'est conclu normalement : l'échec a été avalé.");
    }
}
