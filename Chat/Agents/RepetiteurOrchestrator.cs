using System.Runtime.CompilerServices;
using System.Text;
using Chat.Data;
using Chat.Model;
using Chat.Service;
using Microsoft.EntityFrameworkCore;

namespace Chat.Agents;

/// <summary>
/// Enchaîne les agents répétiteurs :
///
///   Routeur ──► Retriever ──► (Tuteur | Exercice) ──► Relecteur* ──► Mémoire
///                                                     * explication et définition seulement
///
/// C'est un pipeline C# déterministe, pas une boucle pilotée par le modèle : le graphe
/// de décision est connu d'avance, donc laisser un modèle le parcourir n'apporterait
/// rien et rendrait la latence et le coût imprévisibles. Chaque étape reste testable
/// isolément.
/// </summary>
public sealed class RepetiteurOrchestrator(
    CoursClient cours,
    RouteurAgent routeur,
    RetrieverAgent retriever,
    TuteurAgent tuteur,
    ExerciceAgent exercice,
    RelecteurAgent relecteur,
    MemoireAgent memoire,
    ChatContext db,
    ILogger<RepetiteurOrchestrator> logger)
{
    /// <summary>Intentions pour lesquelles une affirmation non soutenue par le cours est vraiment coûteuse.</summary>
    private static readonly StudentIntent[] ReviewedIntents =
        [StudentIntent.Explication, StudentIntent.Definition];

    public async IAsyncEnumerable<TutorStreamEvent> RunAsync(
        Guid studentId,
        TutorRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // ── 1. Le cours ───────────────────────────────────────────────────────
        yield return new TutorStreamEvent("step", Step: "Ouverture du cours…");

        var course = await cours.GetCourseAsync(request.CourseId, ct);
        if (course is null)
        {
            yield return new TutorStreamEvent("error",
                Error: "Je n'arrive pas à ouvrir ce cours. Rechargez la page et réessayez.");
            yield break;
        }

        // ── 2. Intention ──────────────────────────────────────────────────────
        yield return new TutorStreamEvent("step", Step: "Analyse de votre question…");

        var intent = await routeur.ClassifyAsync(request.Message, course.Title, request.History, ct);

        if (intent == StudentIntent.HorsSujet)
        {
            const string redirect =
                "Je suis là pour t'aider sur **ce cours**. Pose-moi une question dessus — " +
                "une notion à éclaircir, un exemple, ou un exercice pour t'entraîner.";

            await PersistAsync(studentId, request, redirect, intent, [], ct);
            yield return new TutorStreamEvent("done", Reply: redirect, Intent: intent.ToString());
            yield break;
        }

        // ── 3. Passages pertinents ────────────────────────────────────────────
        yield return new TutorStreamEvent("step", Step: "Recherche dans votre cours…");

        // Un résumé a besoin d'une vue large ; une définition d'un passage précis.
        var k = intent == StudentIntent.Resume ? 12 : 5;
        var passages = await retriever.RetrieveAsync(request.CourseId, request.Message, request.History, k, ct);

        // ── 4. Rédaction, en flux ─────────────────────────────────────────────
        yield return new TutorStreamEvent("step",
            Step: intent == StudentIntent.Exercice ? "Préparation d'un exercice…" : "Rédaction de la réponse…");

        var stream = intent == StudentIntent.Exercice
            ? exercice.StreamExerciseAsync(request.Message, passages, course.Title, ct)
            : tuteur.StreamAnswerAsync(request.Message, intent, passages, request.History, course.Title, ct);

        var builder = new StringBuilder();
        string? failure = null;

        // Énumération manuelle : on ne peut pas encadrer un `yield return` d'un try/catch,
        // or une coupure réseau en plein flux doit rester rattrapable.
        await using (var enumerator = stream.GetAsyncEnumerator(ct))
        {
            while (true)
            {
                string token;
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                    token = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    yield break; // l'élève a quitté la page
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Échec de la génération pour le cours {CourseId}", request.CourseId);
                    failure = "La rédaction de la réponse a été interrompue. Réessayez dans un instant.";
                    break;
                }

                builder.Append(token);
                yield return new TutorStreamEvent("token", Token: token);
            }
        }

        if (failure is not null)
        {
            yield return new TutorStreamEvent("error", Error: failure);
            yield break;
        }

        var answer = builder.ToString().Trim();
        if (answer.Length == 0)
        {
            yield return new TutorStreamEvent("error",
                Error: "Je n'ai pas réussi à formuler de réponse. Reformulez votre question ?");
            yield break;
        }

        // ── 5. Relecture ──────────────────────────────────────────────────────
        //
        // Les tokens ont déjà été diffusés : si la relecture impose une correction, la
        // réponse révisée part dans l'évènement "done", que le client affiche à la place
        // du brouillon streamé.
        if (ReviewedIntents.Contains(intent) && passages.Count > 0)
        {
            yield return new TutorStreamEvent("step", Step: "Vérification avec le cours…");

            var verdict = await relecteur.ReviewAsync(answer, passages, ct);

            if (!verdict.IsFaithful)
            {
                string? revised = null;
                try
                {
                    revised = await tuteur.ReviseAsync(
                        request.Message, intent, passages, request.History,
                        course.Title, answer, verdict.Critique ?? "affirmation non soutenue par le cours", ct);
                }
                catch (Exception ex)
                {
                    // Une seule tentative de révision : en cas d'échec, la réponse initiale
                    // vaut mieux que rien.
                    logger.LogWarning(ex, "Révision impossible, réponse initiale conservée");
                }

                if (!string.IsNullOrWhiteSpace(revised)) answer = revised.Trim();
            }
        }

        var citations = passages
            .Select(p => p.HeadingPath)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct()
            .ToList();

        await PersistAsync(studentId, request, answer, intent, citations, ct);

        yield return new TutorStreamEvent("done",
            Reply: answer,
            Citations: citations,
            Intent: intent.ToString());

        // ── 6. Fiche de suivi ─────────────────────────────────────────────────
        //
        // Après le "done" : l'élève a sa réponse, l'interface ne l'attend plus.
        await memoire.UpdateAsync(studentId, request.CourseId, request.Message, answer, intent, ct);
    }

    /// <summary>Enregistre l'échange dans le fil d'étude de l'élève sur ce cours.</summary>
    private async Task PersistAsync(
        Guid studentId,
        TutorRequest request,
        string answer,
        StudentIntent intent,
        List<string> citations,
        CancellationToken ct)
    {
        try
        {
            var conversation = await db.Conversations
                .FirstOrDefaultAsync(c => c.StudentId == studentId && c.CourseId == request.CourseId, ct);

            if (conversation is null)
            {
                conversation = new StudyConversation
                {
                    StudentId = studentId,
                    CourseId = request.CourseId,
                };
                db.Conversations.Add(conversation);
            }

            conversation.UpdatedAt = DateTime.UtcNow;

            db.Messages.Add(new StudyMessage
            {
                ConversationId = conversation.Id,
                Role = "user",
                Content = request.Message,
                Intent = intent,
            });

            db.Messages.Add(new StudyMessage
            {
                ConversationId = conversation.Id,
                Role = "assistant",
                Content = answer,
                Citations = citations.Count > 0 ? string.Join(" | ", citations) : null,
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // L'historique est un confort : ne jamais faire échouer une réponse déjà
            // rédigée parce que la base est indisponible.
            logger.LogWarning(ex, "Impossible d'enregistrer l'échange pour le cours {CourseId}", request.CourseId);
        }
    }
}
