namespace Chat.Model
{
    public record ChatMessageRequest(
      string Message,
      List<ConversationMessage> History,
      string? CourseContent = null,
      string? CourseId = null
  );

    public record ChatMessageResponse(
        string Reply,
        List<ConversationMessage> UpdatedHistory
    );

    public record ConversationMessage(string Role, string Content);

    // --- Summary ---

    public record CourseSummaryRequest(
        string? CourseContent = null,
        string? CourseId = null
    );

    public record CourseSummaryResponse(
        string Title,
        List<CoursePart> Parts
    );

    public record CoursePart(string Title, string Summary);

    // --- Draft Generation ---
    public record CourseDraftRequest(
        string Subject,
        string? AdditionalInstructions = null
    );




}
