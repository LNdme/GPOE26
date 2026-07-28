using Chat.Data;
using Chat.Model;
using GPOE26.Ai;
using Microsoft.EntityFrameworkCore;

namespace Chat.Agents;

/// <summary>
/// Tient la fiche de suivi de l'élève sur un cours : ce qu'il a déjà travaillé, et
/// surtout ce sur quoi il bute.
///
/// Tourne APRÈS la réponse, hors du chemin critique : l'élève ne doit jamais attendre
/// la mise à jour d'une fiche.
/// </summary>
public sealed class MemoireAgent(
    OpenRouterClient client,
    ChatContext db,
    ILogger<MemoireAgent> logger)
{
    private const string SystemPrompt = """
        Tu tiens la fiche de suivi d'un élève sur un cours, à l'usage d'un répétiteur.

        On te donne la fiche actuelle et le dernier échange. Renvoie la fiche MISE À JOUR.

        La fiche contient, en Markdown, au plus 10 puces :
        - les notions que l'élève a travaillées ;
        - les points sur lesquels il a montré une difficulté (c'est le plus utile) ;
        - ce qui semble acquis.

        Règles :
        - Fusionne avec l'existant, ne recommence pas de zéro.
        - Si une difficulté revient, note-la une seule fois en précisant qu'elle persiste.
        - Supprime une difficulté quand l'échange montre qu'elle est levée.
        - Pas de phrase d'introduction : renvoie uniquement les puces.
        """;

    public async Task UpdateAsync(
        Guid studentId,
        Guid courseId,
        string question,
        string answer,
        StudentIntent intent,
        CancellationToken ct = default)
    {
        try
        {
            var memory = await db.Memories
                .FirstOrDefaultAsync(m => m.StudentId == studentId && m.CourseId == courseId, ct);

            var current = memory?.Notes ?? "(fiche vide)";

            var updated = await client.CompleteAsync(new AgentRequest
            {
                Kind = AgentKind.Memoire,
                SystemPrompt = SystemPrompt,
                Turns =
                [
                    AgentTurn.User(
                        $"Fiche actuelle :\n{current}\n\n" +
                        $"Dernier échange (intention détectée : {intent}) :\n" +
                        $"Élève : {question}\n" +
                        $"Répétiteur : {answer}")
                ],
                MaxTokens = 500,
            }, ct);

            if (string.IsNullOrWhiteSpace(updated)) return;

            if (memory is null)
            {
                db.Memories.Add(new StudentCourseMemory
                {
                    StudentId = studentId,
                    CourseId = courseId,
                    Notes = updated.Trim(),
                });
            }
            else
            {
                memory.Notes = updated.Trim();
                memory.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // La fiche est un confort, pas une fonctionnalité critique : son échec ne
            // doit jamais remonter à l'élève, qui a déjà reçu sa réponse.
            logger.LogWarning(ex, "Mise à jour de la fiche de suivi impossible (élève {StudentId}, cours {CourseId})",
                studentId, courseId);
        }
    }

    /// <summary>Fiche de suivi à injecter dans le contexte du tuteur, si elle existe.</summary>
    public async Task<string?> GetNotesAsync(Guid studentId, Guid courseId, CancellationToken ct = default)
    {
        try
        {
            return await db.Memories
                .Where(m => m.StudentId == studentId && m.CourseId == courseId)
                .Select(m => m.Notes)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lecture de la fiche de suivi impossible");
            return null;
        }
    }
}
