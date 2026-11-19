using System.Security.Claims;
using GPOE26.Agent.Abstractions;
using Chat.Data;
using Chat.Model;
using Chat.Service;
using GPOE26.Workspaces.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Chat.Endpoints;

/// <summary>
/// Tuteur IA de l'espace d'apprentissage : une discussion par élève et par cours, conservée en base.
/// Lire ses discussions reste possible sans abonnement ; poser une nouvelle question demande Premium.
/// </summary>
public static class TutorEndpoints
{
    public const long MaxImageBytes = 6 * 1024 * 1024;
    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp"];

    public static void MapTutorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tutor")
            .WithTags("Tuteur")
            .RequireAuthorization()
            .RequireWorkspaceContext();

        group.MapGet("/mcp/extensions", (IAgentExtensionRegistry registry) =>
        {
            var extensions = registry.Manifests
                .Where(manifest => manifest.Kind == AgentExtensionKind.McpServer)
                .Select(manifest => new McpExtensionCatalogDto(
                    manifest.Id,
                    manifest.Name,
                    manifest.Description,
                    manifest.Publisher.Name,
                    manifest.Capabilities.Select(capability => new McpCapabilityCatalogDto(
                        capability.Id,
                        capability.Description,
                        capability.RequiredPermissionIds)).ToArray(),
                    manifest.RequestedPermissions.Select(permission => new McpPermissionCatalogDto(
                        permission.Id,
                        permission.Description,
                        permission.Required)).ToArray()))
                .ToArray();
            return Results.Ok(extensions);
        })
        .WithSummary("Lister les extensions MCP déclarées par l'opérateur, sans configuration ni secret");

        // ── GET /tutor/conversations ──────────────────────────────────────────────
        group.MapGet("/conversations", async (ClaimsPrincipal principal, TutorContext db) =>
        {
            var userId = UserId(principal);
            var workspace = principal.GetWorkspaceContext();
            var list = await db.Conversations
                .Where(c => c.UserId == userId &&
                    (c.WorkspaceId == workspace.WorkspaceId ||
                     (workspace.IsPersonal && c.WorkspaceId == null)) &&
                    c.Messages.Any())
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new ConversationSummaryDto(c.Id, c.CourseId, c.CourseTitle, c.Subject,
                    c.Messages.Count, c.Messages.Count(m => m.Pinned),
                    c.Messages.OrderByDescending(m => m.CreatedAt).Select(m => m.Content).FirstOrDefault(),
                    c.UpdatedAt))
                .ToListAsync();
            return Results.Ok(list.Select(c => c with { LastMessage = Preview(c.LastMessage) }));
        })
        .WithSummary("Mes discussions avec le tuteur (une par cours)");

        // ── GET /tutor/courses/{courseId} ─────────────────────────────────────────
        group.MapGet("/courses/{courseId:guid}", async (Guid courseId, ClaimsPrincipal principal, TutorContext db) =>
        {
            var userId = UserId(principal);
            var workspace = principal.GetWorkspaceContext();
            var conversation = await db.Conversations.AsNoTracking()
                .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
                .FirstOrDefaultAsync(c =>
                    c.UserId == userId &&
                    c.CourseId == courseId &&
                    (c.WorkspaceId == workspace.WorkspaceId ||
                     (workspace.IsPersonal && c.WorkspaceId == null)));
            return Results.Ok(conversation is null
                ? new ConversationDto(null, courseId, null, new())
                : new ConversationDto(conversation.Id, courseId, conversation.CourseTitle,
                    conversation.Messages.Select(m => new TutorMessageDto(m)).ToList()));
        })
        .WithSummary("La discussion sur un cours (vide si l'élève n'a encore rien demandé)");

        // ── POST /tutor/courses/{courseId}/messages ───────────────────────────────
        group.MapPost("/courses/{courseId:guid}/messages", async (Guid courseId, AskTutorRequest req, ClaimsPrincipal principal,
            TutorConversationService tutor, CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var workspace = principal.GetWorkspaceContext();
            var outcome = await tutor.AskAsync(
                userId,
                workspace,
                principal.FindFirst("level")?.Value,
                courseId,
                req,
                ct);
            return outcome.Response is not null
                ? Results.Ok(outcome.Response)
                : Error(outcome.ErrorStatus!.Value, outcome.ErrorMessage!);
        })
        .WithSummary("Poser une question au tuteur (Premium) — la question et la réponse sont enregistrées");

        // ── POST /tutor/images ────────────────────────────────────────────────────
        group.MapPost("/images", async (IFormFile file, ClaimsPrincipal principal, TutorContext db, PlatformClients platform, CancellationToken ct) =>
        {
            if (!await platform.IsPremiumAsync(ct))
                return Error(403, "Le tuteur IA est réservé aux abonnés Premium.");
            if (file.Length == 0 || file.Length > MaxImageBytes)
                return Error(400, "La photo doit faire moins de 6 Mo.");
            if (!ImageTypes.Contains(file.ContentType))
                return Error(400, "Formats acceptés : JPEG, PNG ou WebP.");

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            var image = new TutorImage { UserId = UserId(principal), ContentType = file.ContentType, Data = stream.ToArray() };
            db.Images.Add(image);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ImageUploadedDto(image.Id));
        })
        .DisableAntiforgery()
        .WithSummary("Envoyer la photo d'un exercice traité sur papier");

        // ── GET /tutor/images/{id} ────────────────────────────────────────────────
        group.MapGet("/images/{id:guid}", async (Guid id, ClaimsPrincipal principal, TutorContext db) =>
        {
            var userId = UserId(principal);
            var image = await db.Images.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
            return image is null ? Results.NotFound() : Results.File(image.Data, image.ContentType);
        })
        .WithSummary("Une photo envoyée par l'élève");

        // ── PUT /tutor/messages/{id}/pin ──────────────────────────────────────────
        group.MapPut("/messages/{id:guid}/pin", async (Guid id, PinRequest req, ClaimsPrincipal principal, TutorContext db) =>
        {
            var userId = UserId(principal);
            var workspace = principal.GetWorkspaceContext();
            var message = await db.Messages.Include(m => m.Conversation)
                .FirstOrDefaultAsync(m =>
                    m.Id == id &&
                    m.Conversation.UserId == userId &&
                    (m.Conversation.WorkspaceId == workspace.WorkspaceId ||
                     (workspace.IsPersonal && m.Conversation.WorkspaceId == null)));
            if (message is null) return Results.NotFound();
            message.Pinned = req.Pinned;
            await db.SaveChangesAsync();
            return Results.Ok(new TutorMessageDto(message));
        })
        .WithSummary("Épingler (ou non) une explication pour la revoir");

        // ── GET /tutor/pinned ─────────────────────────────────────────────────────
        group.MapGet("/pinned", async (ClaimsPrincipal principal, TutorContext db) =>
        {
            var userId = UserId(principal);
            var workspace = principal.GetWorkspaceContext();
            var pinned = await db.Messages.AsNoTracking()
                .Where(m => m.Pinned &&
                    m.Conversation.UserId == userId &&
                    (m.Conversation.WorkspaceId == workspace.WorkspaceId ||
                     (workspace.IsPersonal && m.Conversation.WorkspaceId == null)))
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id, m.ConversationId, m.Conversation.CourseId, m.Conversation.CourseTitle, m.Conversation.Subject,
                    m.Content, m.CreatedAt,
                    Question = db.Messages.Where(q => q.ConversationId == m.ConversationId && q.Role == "user" && q.CreatedAt < m.CreatedAt)
                        .OrderByDescending(q => q.CreatedAt).Select(q => q.Content).FirstOrDefault()
                })
                .ToListAsync();
            return Results.Ok(pinned.Select(p => new PinnedExplanationDto(p.Id, p.CourseId, p.CourseTitle, p.Subject,
                p.Question, p.Content, p.CreatedAt)));
        })
        .WithSummary("Explications épinglées, tous cours confondus");

        // ── DELETE /tutor/courses/{courseId} ──────────────────────────────────────
        group.MapDelete("/courses/{courseId:guid}", async (Guid courseId, ClaimsPrincipal principal, TutorContext db) =>
        {
            var userId = UserId(principal);
            var workspace = principal.GetWorkspaceContext();
            var conversation = await db.Conversations.FirstOrDefaultAsync(c =>
                c.UserId == userId &&
                c.CourseId == courseId &&
                (c.WorkspaceId == workspace.WorkspaceId ||
                 (workspace.IsPersonal && c.WorkspaceId == null)));
            if (conversation is null) return Results.NoContent();

            var imageIds = await db.Messages.Where(m => m.ConversationId == conversation.Id && m.ImageId != null)
                .Select(m => m.ImageId!.Value).ToListAsync();
            await db.Images.Where(i => imageIds.Contains(i.Id)).ExecuteDeleteAsync();
            db.Conversations.Remove(conversation);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithSummary("Effacer la discussion sur un cours (recommencer)");
    }

    private static Guid UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new UnauthorizedAccessException();

    private static string? Preview(string? text) =>
        text is null ? null : text.Length > 140 ? text[..140].TrimEnd() + "…" : text;

    private static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);
}

public sealed record McpExtensionCatalogDto(
    string Id,
    string Name,
    string Description,
    string PublisherName,
    IReadOnlyList<McpCapabilityCatalogDto> Capabilities,
    IReadOnlyList<McpPermissionCatalogDto> Permissions);

public sealed record McpCapabilityCatalogDto(
    string Id,
    string Description,
    IReadOnlyList<string> RequiredPermissionIds);

public sealed record McpPermissionCatalogDto(
    string Id,
    string Description,
    bool Required);
