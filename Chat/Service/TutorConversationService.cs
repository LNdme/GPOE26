using Chat.Data;
using Chat.Model;
using GPOE26.Agent.Abstractions;
using GPOE26.Workspaces.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Chat.Service;

public sealed class TutorConversationService(
    TutorContext db,
    PlatformClients platform,
    IAgentRuntime runtime,
    TutorCourseToolContext courseToolContext,
    TutorCourseToolExecutor toolExecutor,
    IAgentProfileRouter profileRouter)
{
    private const int HistoryForModel = 16;

    public async Task<TutorAskOutcome> AskAsync(
        Guid userId,
        WorkspaceTokenContext workspace,
        string? learnerLevel,
        Guid courseId,
        AskTutorRequest request,
        CancellationToken cancellationToken)
    {
        var text = request.Message?.Trim() ?? "";
        if (text.Length == 0 && request.ImageId is null)
            return TutorAskOutcome.Failure(400, "Écris ta question ou joins une photo.");
        if (text.Length > 4000)
            return TutorAskOutcome.Failure(400, "Ta question est trop longue (4 000 caractères au maximum).");
        // Formule et quota d'IA : vérifiés par la route (AiQuota, fonction tutor-ai) avant d'appeler ce service

        var course = await platform.CourseAsync(courseId, cancellationToken);
        if (course is null || course.Id != courseId)
            return TutorAskOutcome.Failure(404, "Cours introuvable.");
        courseToolContext.AuthorizeCourse(userId, course);

        TutorImage? image = null;
        if (request.ImageId is { } imageId)
        {
            image = await db.Images.FirstOrDefaultAsync(
                item => item.Id == imageId && item.UserId == userId,
                cancellationToken);
            if (image is null)
                return TutorAskOutcome.Failure(404, "Photo introuvable : envoie-la de nouveau.");
        }

        var conversation = await db.Conversations
            .Include(item => item.Messages.OrderBy(message => message.CreatedAt))
            .FirstOrDefaultAsync(
                item => item.UserId == userId &&
                        item.CourseId == courseId &&
                        (item.WorkspaceId == workspace.WorkspaceId ||
                         (workspace.IsPersonal && item.WorkspaceId == null)),
                cancellationToken);
        if (conversation is null)
        {
            conversation = new TutorConversation
            {
                UserId = userId,
                WorkspaceId = workspace.WorkspaceId,
                CourseId = courseId
            };
            db.Conversations.Add(conversation);
        }
        else
        {
            conversation.WorkspaceId ??= workspace.WorkspaceId;
        }

        conversation.CourseTitle = course.Title;
        conversation.Subject = course.Subject;

        var profile = profileRouter.Resolve(request.ProfileId);
        if (profile.AllowedToolNames.Contains(AgentToolNames.McpRead, StringComparer.Ordinal))
            await toolExecutor.PrepareMcpToolsAsync(cancellationToken);

        if (!toolExecutor.Tools.Any(tool => string.Equals(
                tool.Name,
                AgentToolNames.McpRead,
                StringComparison.Ordinal)))
        {
            profile = profile with
            {
                AllowedToolNames = profile.AllowedToolNames
                    .Where(name => !string.Equals(name, AgentToolNames.McpRead, StringComparison.Ordinal))
                    .ToArray()
            };
        }

        if (text.Length == 0)
            text = "Voici ma copie : peux-tu vérifier ma démarche ?";

        var history = conversation.Messages.TakeLast(HistoryForModel)
            .Select(message => new AgentMessage(
                message.Role == "assistant" ? AgentMessageRole.Assistant : AgentMessageRole.User,
                message.ImageId is null ? message.Content : $"{message.Content}\n[photo de copie jointe]"))
            .ToList();
        var runRequest = new AgentRunRequest(
            Guid.NewGuid().ToString("N"),
            conversation.Id,
            profile,
            new AgentPresentationContext(
                LearnerLevel: string.IsNullOrWhiteSpace(learnerLevel) ? "secondaire" : learnerLevel,
                Subject: course.Subject,
                CourseTitle: course.Title),
            history,
            text,
            image is null
                ? []
                : [new AgentAttachment(image.ContentType, image.Data)]);

        AgentRunResult result;
        try
        {
            result = await runtime.RunToCompletionAsync(
                runRequest,
                toolExecutor,
                cancellationToken);
        }
        catch (AgentRuntimeException)
        {
            return TutorAskOutcome.Failure(
                502,
                "Le tuteur ne répond pas pour le moment. Ta question n'a pas été perdue : réessaie dans un instant.");
        }

        var now = DateTime.UtcNow;
        var question = new TutorMessage
        {
            ConversationId = conversation.Id,
            Role = "user",
            Content = text,
            ImageId = image?.Id,
            CreatedAt = now
        };
        var answer = new TutorMessage
        {
            ConversationId = conversation.Id,
            Role = "assistant",
            Content = result.Answer,
            CreatedAt = now.AddMilliseconds(1)
        };
        db.Messages.AddRange(question, answer);
        conversation.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return TutorAskOutcome.Success(new AskTutorResponse(
            new TutorMessageDto(question),
            new TutorMessageDto(answer)));
    }
}

public sealed record TutorAskOutcome(AskTutorResponse? Response, int? ErrorStatus, string? ErrorMessage)
{
    public static TutorAskOutcome Success(AskTutorResponse response) => new(response, null, null);

    public static TutorAskOutcome Failure(int status, string message) => new(null, status, message);
}
