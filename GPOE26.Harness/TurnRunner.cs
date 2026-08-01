using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using GPOE26.Harness.Contract;
using GPOE26.Harness.Data;
using GPOE26.Harness.Model;
using GPOE26.Harness.Service;
using GPOE26.Harness.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace GPOE26.Harness;

/// <summary>
/// Joue un tour et traduit ce que fait le modèle en évènements du contrat.
///
/// Toute la boucle vient de <c>FunctionInvokingChatClient</c> : il exécute les appels
/// d'outils et relance le tour jusqu'à la réponse finale. Ce qui reste ici, c'est ce qu'il
/// ne fait pas — décider quels outils sont exposés, rendre la boucle visible à l'élève, et
/// persister ce qui s'est dit.
///
/// Différence de fond avec <c>RepetiteurOrchestrator</c> qu'il remplace : le graphe n'est
/// plus écrit d'avance. Le pipeline figé était le bon choix pour « explique-moi cette
/// notion » ; il ne l'est plus pour « mon test échoue, pourquoi ? », où l'enchaînement
/// dépend de ce qu'on découvre en chemin.
/// </summary>
public sealed class TurnRunner(
    IChatClient chatClient,
    StudyTools tools,
    CoursClient cours,
    HarnessContext db,
    ILogger<TurnRunner> logger)
{
    /// <summary>
    /// Au-delà, on arrête. Une boucle qui tourne dix fois sur un cours de lycée n'est pas
    /// en train de bien travailler : elle est en train de tourner en rond, et chaque tour
    /// se paie.
    /// </summary>
    public const int MaxIterations = 6;

    /// <summary>Tours conservés tels quels dans le contexte. Au-delà, la compaction résume.</summary>
    public const int TurnsBeforeCompaction = 12;

    public async IAsyncEnumerable<HarnessEvent> RunAsync(
        HarnessSession session,
        TurnRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return HarnessEvent.OfStep("Ouverture du cours…");

        var course = await cours.GetCourseAsync(session.CourseId, ct);
        if (course is null)
        {
            yield return HarnessEvent.OfError(
                "Je n'arrive pas à ouvrir ce cours. Rechargez la page et réessayez.");
            yield break;
        }

        tools.CourseId = session.CourseId;
        tools.CourseTitle = course.Title;

        var messages = BuildMessages(session, course, request);

        var options = new ChatOptions
        {
            Tools = tools.FunctionsFor(session.Kind),
            MaxOutputTokens = 1600,
        };

        yield return HarnessEvent.OfStep("Réflexion…");

        var answer = new StringBuilder();
        var toolsUsed = new List<string>();
        var iteration = 0;
        string? failure = null;

        // Énumération manuelle : on ne peut pas encadrer un `yield return` d'un try/catch,
        // et une coupure en plein flux doit rester rattrapable.
        var stream = chatClient.GetStreamingResponseAsync(messages, options, ct)
            .GetAsyncEnumerator(ct);

        try
        {
            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await stream.MoveNextAsync()) break;
                    update = stream.Current;
                }
                catch (OperationCanceledException)
                {
                    yield break; // l'élève a fermé la page
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Tour interrompu sur le cours {CourseId}", session.CourseId);
                    failure = "La réponse a été interrompue. Réessayez dans un instant.";
                    break;
                }

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent call:
                            iteration++;
                            toolsUsed.Add(call.Name);
                            yield return HarnessEvent.OfStep(StepLabel(call.Name));
                            yield return HarnessEvent.OfToolCall(
                                call.Name, Describe(call.Arguments), iteration);
                            break;

                        case FunctionResultContent result:
                            yield return HarnessEvent.OfToolResult(
                                toolsUsed.LastOrDefault() ?? "outil", Summarize(result.Result), iteration);
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(update.Text))
                {
                    answer.Append(update.Text);
                    yield return HarnessEvent.OfToken(update.Text);
                }

                // Garde-fou : le modèle peut boucler sans jamais conclure.
                if (iteration > MaxIterations)
                {
                    logger.LogWarning("Boucle arrêtée après {Iterations} appels d'outils", iteration);
                    failure = "Je n'arrive pas à conclure sur cette question. Reformulez-la ?";
                    break;
                }
            }
        }
        finally
        {
            await stream.DisposeAsync();
        }

        if (failure is not null)
        {
            yield return HarnessEvent.OfError(failure);
            yield break;
        }

        var reply = answer.ToString().Trim();
        if (reply.Length == 0)
        {
            yield return HarnessEvent.OfError(
                "Je n'ai pas réussi à formuler de réponse. Reformulez votre question ?");
            yield break;
        }

        var citations = ExtractCitations(reply);
        await PersistAsync(session, request.Message, reply, citations, toolsUsed, ct);

        yield return HarnessEvent.OfDone(reply, citations);
    }

    // ── Contexte envoyé au modèle ─────────────────────────────────────────────

    private static List<ChatMessage> BuildMessages(
        HarnessSession session, CourseSnapshot course, TurnRequest request)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt(session.Kind, course.Title)),
        };

        // Le résumé des tours compactés prend la place de ce qu'il remplace, à la même
        // position : sans lui, une session longue perdrait le fil.
        if (!string.IsNullOrWhiteSpace(session.Summary))
            messages.Add(new ChatMessage(ChatRole.System,
                $"Ce qui s'est dit plus tôt dans cette session :\n{session.Summary}"));

        foreach (var turn in session.Turns.Where(t => !t.Compacted).OrderBy(t => t.Index))
        {
            messages.Add(new ChatMessage(
                turn.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
                turn.Content));
        }

        var question = new StringBuilder(request.Message);

        // L'état courant, s'il est fourni : ce que l'élève a sous les yeux, que l'agent
        // devrait voir sans avoir à le demander.
        if (request.Context is { Count: > 0 })
        {
            question.AppendLine();
            question.AppendLine();
            question.AppendLine("[État courant]");
            foreach (var (key, value) in request.Context)
                question.AppendLine($"{key} : {value}");
        }

        messages.Add(new ChatMessage(ChatRole.User, question.ToString()));
        return messages;
    }

    private static string SystemPrompt(SessionKind kind, string courseTitle)
    {
        var common = $"""
            Tu es le répétiteur d'un élève de lycée, sur son cours « {courseTitle} ».

            Règles qui ne changent jamais :
            - Tes explications viennent du COURS DE L'ÉLÈVE, pas de tes connaissances générales.
              Appelle chercher_dans_le_cours avant d'expliquer quoi que ce soit.
            - Si le cours ne dit rien sur la question, dis-le. Ne comble pas.
            - Cite les sections utilisées sous la forme [§ Chapitre › Section].
            - Tutoiement, phrases courtes, pas de jargon inutile.
            - Si la question ne porte pas sur le cours, ramène l'élève au cours sans le sermonner.
            """;

        return kind switch
        {
            SessionKind.Exercice => common + """

                L'élève s'entraîne. Fais-le produire avant de corriger : une réponse donnée
                trop tôt lui retire l'exercice.
                """,

            SessionKind.Code => common + """

                L'élève écrit du code. Lis son fichier et fais tourner ses tests avant de
                conclure — l'erreur qu'il décrit n'est pas toujours celle qui se produit.
                Donne une piste d'abord ; ne livre une correction complète que s'il bute encore.
                """,

            _ => common,
        };
    }

    // ── Lisibilité de la boucle ───────────────────────────────────────────────

    /// <summary>
    /// Ce que l'élève voit pendant qu'un outil tourne. Un nom technique laisserait croire
    /// à une erreur ; une phrase dit ce qui se passe.
    /// </summary>
    private static string StepLabel(string tool) => tool switch
    {
        ToolNames.ChercherDansLeCours => "Recherche dans votre cours…",
        ToolNames.OuEnEstLEleve => "Vérification de votre parcours…",
        ToolNames.LireAVoixHaute => "Préparation de la lecture…",
        ToolNames.GenererExercice => "Préparation d'un exercice…",
        ToolNames.CorrigerReponse => "Correction de votre réponse…",
        ToolNames.LireFichier => "Lecture de votre fichier…",
        ToolNames.EcrireFichier => "Modification de votre fichier…",
        ToolNames.ExecuterTests => "Exécution de vos tests…",
        _ => "Travail en cours…",
    };

    private static string? Describe(IDictionary<string, object?>? arguments)
    {
        if (arguments is null or { Count: 0 }) return null;

        try
        {
            return JsonSerializer.Serialize(arguments);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Le flux transporte un RÉSUMÉ du résultat, pas le résultat.
    ///
    /// Une recherche ramène volontiers plusieurs milliers de caractères que personne
    /// n'affiche : les envoyer alourdirait le flux sans rien apporter à l'élève.
    /// </summary>
    private static string Summarize(object? result)
    {
        var text = result?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return "(vide)";

        var firstLine = text.Split('\n', 2)[0].Trim();
        return firstLine.Length <= 120 ? firstLine : firstLine[..120] + "…";
    }

    /// <summary>Les sections citées, lues dans la réponse elle-même.</summary>
    private static List<string> ExtractCitations(string reply)
    {
        var citations = new List<string>();

        foreach (var match in System.Text.RegularExpressions.Regex
            .Matches(reply, @"\[§\s*([^\]]+)\]").Cast<System.Text.RegularExpressions.Match>())
        {
            var heading = match.Groups[1].Value.Trim();
            if (heading.Length > 0 && !citations.Contains(heading)) citations.Add(heading);
        }

        return citations;
    }

    // ── Persistance ───────────────────────────────────────────────────────────

    private async Task PersistAsync(
        HarnessSession session, string question, string reply,
        List<string> citations, List<string> toolsUsed, CancellationToken ct)
    {
        try
        {
            var index = session.Turns.Count;

            db.Turns.Add(new HarnessTurn
            {
                SessionId = session.Id,
                Index = index,
                Role = "user",
                Content = question,
            });

            db.Turns.Add(new HarnessTurn
            {
                SessionId = session.Id,
                Index = index + 1,
                Role = "assistant",
                Content = reply,
                Citations = citations.Count > 0 ? string.Join(" | ", citations) : null,
                ToolsUsed = toolsUsed.Count > 0 ? string.Join(" | ", toolsUsed) : null,
            });

            session.LastActivityAt = DateTime.UtcNow;
            db.Sessions.Update(session);

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // L'historique est un confort : ne jamais perdre une réponse déjà rédigée
            // parce que la base est indisponible.
            logger.LogWarning(ex, "Tour non enregistré pour la session {SessionId}", session.Id);
        }
    }

    /// <summary>
    /// Compacte une session : les tours anciens sont résumés puis retirés du contexte.
    ///
    /// On résume au lieu de tronquer. Couper les premiers tours ferait oublier au
    /// répétiteur ce sur quoi l'élève butait au début — précisément ce qu'il faut retenir.
    /// </summary>
    public async Task CompactAsync(HarnessSession session, CancellationToken ct = default)
    {
        var live = session.Turns.Where(t => !t.Compacted).OrderBy(t => t.Index).ToList();
        if (live.Count <= TurnsBeforeCompaction)
        {
            session.CompactedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        var toCompact = live.Take(live.Count - 4).ToList();

        var transcript = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(session.Summary))
            transcript.AppendLine($"Résumé existant :\n{session.Summary}\n");

        foreach (var turn in toCompact)
            transcript.AppendLine($"{turn.Role} : {turn.Content}");

        try
        {
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System,
                        "Résume cet échange entre un élève et son répétiteur, en 6 puces au plus. " +
                        "Garde ce sur quoi l'élève a buté et ce qui a été éclairci ; jette le reste."),
                    new ChatMessage(ChatRole.User, transcript.ToString()),
                ],
                new ChatOptions { MaxOutputTokens = 500 },
                ct);

            session.Summary = response.Text.Trim();
        }
        catch (Exception ex)
        {
            // Sans résumé, on ne compacte pas : mieux vaut un contexte long qu'un contexte
            // amputé sans mémoire de ce qu'il contenait.
            logger.LogWarning(ex, "Compaction impossible pour la session {SessionId}", session.Id);
            return;
        }

        foreach (var turn in toCompact) turn.Compacted = true;

        session.CompactedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
