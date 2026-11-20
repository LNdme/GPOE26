namespace Chat.Model
{
    /// <summary>Brouillon de cours rédigé par l'IA (POST /chat/draft).</summary>
    public record CourseDraftRequest(
        string Subject,
        string? AdditionalInstructions = null
    );
}
