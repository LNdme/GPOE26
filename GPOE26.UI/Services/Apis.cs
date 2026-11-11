using GPOE26.UI.Models;

namespace GPOE26.UI.Services;

// ══════════════════════════════════════════════════════════════════════════════
// Interfaces consommées par les pages. Les implémentations HTTP ci-dessous servent
// le Web ; l'application Windows pourra les décorer d'un cache hors ligne.
// ══════════════════════════════════════════════════════════════════════════════

public interface IAccountApi
{
    Task<ApiResult<AuthResponse>> LoginAsync(string email, string password);
    /// <summary>Élève d'un établissement : code de l'établissement, matricule, mot de passe ou code de sa fiche.</summary>
    Task<ApiResult<AuthResponse>> LoginMatriculeAsync(string school, string matricule, string password);
    /// <summary>Premier mot de passe, après une connexion avec le code de sa fiche.</summary>
    Task<ApiResult<bool>> FirstPasswordAsync(string newPassword);
    Task<ApiResult<AuthResponse>> RegisterAsync(RegisterRequest request);
    Task<ApiResult<UserProfile>> MeAsync();
    Task<ApiResult<UserProfile>> UpdateProfileAsync(EditProfileRequest request);
    Task<ApiResult<bool>> ChangePasswordAsync(string currentPassword, string newPassword);
    Task<ApiResult<MySubscriptions>> MySubscriptionsAsync();
    Task<ApiResult<Entitlement>> RedeemKeyAsync(string code);
    Task<ApiResult<SubscriptionInfo>> CheckoutAsync(SubscriptionPlan plan, BillingPeriod period);
    Task<ApiResult<SubscriptionInfo>> StartTrialAsync();
    Task<ApiResult<SimulatedPayment>> SimulatePaymentAsync(SubscriptionPlan plan, BillingPeriod period, string provider);

    /// <summary>
    /// Prolonge la session si elle approche de son terme, sans autre effet. À appeler régulièrement
    /// par un écran où l'on reste longtemps sans rien envoyer (composition d'une épreuve).
    /// </summary>
    Task KeepSessionAliveAsync();
}

/// <summary>Agents : emploi du temps, plan du jour, mémoire et assistant enseignant.</summary>
public interface IAgentApi
{
    Task<ApiResult<List<StudySlot>>> ScheduleAsync();
    Task<ApiResult<bool>> SaveScheduleAsync(List<StudySlot> slots);
    Task<ApiResult<DayPlan>> TodayPlanAsync();
    Task<ApiResult<bool>> RecordStudyAsync(Guid courseId, int minutes);
    Task<ApiResult<List<AgentMemoryItem>>> MemoriesAsync();
    Task<ApiResult<bool>> UpdateMemoryAsync(Guid id, string content);
    Task<ApiResult<bool>> DeleteMemoryAsync(Guid id);
    Task<ApiResult<bool>> ClearMemoriesAsync();
    Task<ApiResult<MemorySettings>> MemorySettingsAsync();
    Task<ApiResult<MemorySettings>> SetMemoryEnabledAsync(bool enabled);
    Task<ApiResult<TeacherAgentProfile>> TeacherProfileAsync();
    Task<ApiResult<TeacherAgentProfile>> SaveTeacherProfileAsync(TeacherAgentProfile profile);
    Task<ApiResult<ComposeResult>> ComposeAsync(ComposeRequest request);
    Task<ApiResult<bool>> LearnStyleAsync(string text);
    Task<ApiResult<ClassInsightsResult>> ClassInsightsAsync(Guid classroomId);
    Task<ApiResult<AssistantAnswer>> AskAssistantAsync(Guid? classroomId, string message);
    Task<ApiResult<List<AssistantMessage>>> AssistantConversationAsync();
    Task<ApiResult<bool>> ClearAssistantConversationAsync();
}

/// <summary>Catalogue public des formules.</summary>
public interface IPlanApi
{
    Task<ApiResult<List<PlanInfo>>> ListAsync();
}

public interface IClassroomApi
{
    Task<ApiResult<List<Classroom>>> MineAsync();
    Task<ApiResult<Classroom>> GetAsync(Guid id);
    Task<ApiResult<Classroom>> CreateAsync(CreateClassroomRequest request);
    Task<ApiResult<List<ClassroomMember>>> MembersAsync(Guid id);
    Task<ApiResult<bool>> RemoveMemberAsync(Guid classroomId, Guid studentId);
}

public interface IFamilyApi
{
    Task<ApiResult<List<ChildLink>>> ChildrenAsync();
    Task<ApiResult<ChildLink>> LinkChildAsync(string code);
    Task<ApiResult<ChildLink>> CreateChildAsync(CreateChildRequest request);
    Task<ApiResult<List<ParentLink>>> MyParentsAsync();
    Task<ApiResult<bool>> RespondAsync(Guid linkId, bool approve);
}

public interface ICourseApi
{
    Task<ApiResult<List<CourseSummary>>> MineAsync();
    Task<ApiResult<List<CourseSummary>>> SharedWithMeAsync();
    Task<ApiResult<CourseDetail>> GetAsync(Guid id);
    Task<ApiResult<CourseSummary>> ShareAsync(Guid id, bool publish, List<Guid> classroomIds);
    Task<ApiResult<CourseDetail>> CreateAsync(CourseInput course);
    Task<ApiResult<bool>> UpdateAsync(Guid id, CourseInput course);
    Task<ApiResult<bool>> DeleteAsync(Guid id);
    Task<ApiResult<CourseDetail>> UploadPdfAsync(Guid id, ImageUpload pdf);
    Task<ApiResult<CourseDetail>> RestructureAsync(Guid id);
    Task<ApiResult<CourseDetail>> ImportLinkAsync(string url);
    Task<ApiResult<CourseDetail>> ImportPhotosAsync(IEnumerable<ImageUpload> photos);
    Task<ApiResult<List<LabSuggestion>>> LabSuggestionsAsync(Guid id);
    Task<ApiResult<CourseDetail>> AcceptLabAsync(Guid id, Guid suggestionId);
    Task<ApiResult<CourseDetail>> AcceptAllLabsAsync(Guid id);
    Task<ApiResult<bool>> RefuseLabAsync(Guid id, Guid suggestionId);
    Task<ApiResult<CourseFeedback>> FeedbackAsync(Guid id);
    Task<ApiResult<CourseFeedback>> SetFeedbackAsync(Guid id, bool useful);
    Task<ApiResult<bool>> ClearFeedbackAsync(Guid id);
}

/// <summary>Images insérées dans un cours (hébergées par le service du site).</summary>
public interface IMediaApi
{
    Task<ApiResult<UploadedFile>> UploadImageAsync(ImageUpload image);
}

/// <summary>Quiz d'entraînement créé par l'IA à partir du texte d'un cours.</summary>
public interface IPracticeQuizApi
{
    Task<ApiResult<PracticeQuiz>> GenerateAsync(string title, Guid courseId, int questions, string difficulty);
    Task<ApiResult<PracticeProblemSet>> GenerateProblemsAsync(string title, Guid courseId, int count, string difficulty);
    Task<ApiResult<PracticeResult>> SubmitAsync(Guid quizId, List<PracticeAnswer> answers);
}

public interface IAssessmentApi
{
    Task<ApiResult<List<Assessment>>> MineAsTeacherAsync();
    Task<ApiResult<List<StudentAssessment>>> MineAsStudentAsync();
    Task<ApiResult<Assessment>> GetAsync(Guid id);
    Task<ApiResult<Assessment>> CreateAsync(AssessmentDraft draft);
    Task<ApiResult<Assessment>> UpdateAsync(Guid id, AssessmentDraft draft);
    Task<ApiResult<Assessment>> PublishAsync(Guid id);
    Task<ApiResult<Assessment>> CloseAsync(Guid id);
    Task<ApiResult<bool>> DeleteAsync(Guid id);
    Task<ApiResult<Attempt>> StartAttemptAsync(Guid id);
    /// <summary>Enregistre les réponses en cours de saisie : retrouvées si la page est rechargée ou la connexion coupée.</summary>
    Task<ApiResult<bool>> SaveDraftAsync(Guid id, Guid submissionId, List<AnswerDraft> answers);
    Task<ApiResult<SubmissionReview>> SubmitAsync(Guid id, Guid submissionId, List<AnswerDraft> answers);
    Task<ApiResult<SubmissionReview>> GetSubmissionAsync(Guid id, Guid submissionId);
    Task<ApiResult<List<SubmissionRow>>> SubmissionsAsync(Guid id);
    Task<ApiResult<SubmissionReview>> GradeAsync(Guid id, Guid submissionId, Guid questionId, decimal points, string? feedback);
    Task<ApiResult<AiSuggestion>> AiSuggestionAsync(Guid id, Guid submissionId, Guid questionId);
    Task<ApiResult<AnswerPhoto>> UploadPhotoAsync(Guid id, Guid submissionId, Guid questionId, ImageUpload photo);
    Task<ApiResult<bool>> DeletePhotoAsync(Guid id, Guid submissionId, Guid photoId);
    /// <summary>Photos de la tentative et leur lecture par l'IA (qui se fait en arrière-plan).</summary>
    Task<ApiResult<List<AnswerPhoto>>> AttemptPhotosAsync(Guid id, Guid submissionId);
    Task<string?> PhotoAsync(Guid id, Guid submissionId, Guid photoId);
    Task<ApiResult<AiFeedback>> AiFeedbackAsync(Guid id, Guid submissionId, Guid questionId);
}

/// <summary>Tuteur IA : une discussion conservée par cours, explications épinglées, photos d'exercices.</summary>
public interface ITutorApi
{
    Task<ApiResult<List<TutorConversationSummary>>> ConversationsAsync();
    Task<ApiResult<TutorConversation>> ConversationAsync(Guid courseId);
    Task<ApiResult<TutorExchange>> AskAsync(Guid courseId, string? message, Guid? imageId);

    /// <summary>Même question, mais chaque morceau de la réponse est transmis à <paramref name="onDelta"/> dès qu'il est écrit.</summary>
    Task<ApiResult<TutorExchange>> AskStreamAsync(Guid courseId, string? message, Guid? imageId, Func<string, Task> onDelta, CancellationToken ct = default);
    Task<ApiResult<UploadedImage>> UploadImageAsync(ImageUpload image);
    Task<string?> ImageAsync(Guid imageId);
    Task<ApiResult<TutorMessage>> PinAsync(Guid messageId, bool pinned);
    Task<ApiResult<List<PinnedExplanation>>> PinnedAsync();
    Task<ApiResult<bool>> ClearAsync(Guid courseId);
    Task<ApiResult<string>> DraftCourseAsync(string subject, string? instructions);
}

public interface IProgressApi
{
    Task<ApiResult<StudentProgress>> MineAsync();
    Task<ApiResult<StudentProgress>> ChildAsync(Guid studentId);
    Task<ApiResult<ClassroomProgress>> ClassroomAsync(Guid classroomId);
}

// ─── Implémentations HTTP ─────────────────────────────────────────────────────

public class AccountApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.User), IAccountApi
{
    public Task<ApiResult<AuthResponse>> LoginAsync(string email, string password) =>
        PostAsync<AuthResponse>("/auth/login", new LoginRequest(email, password));
    public Task<ApiResult<AuthResponse>> LoginMatriculeAsync(string school, string matricule, string password) =>
        PostAsync<AuthResponse>("/auth/login-matricule", new { school, matricule, password });
    public Task<ApiResult<bool>> FirstPasswordAsync(string newPassword) => PostAsync<bool>("/auth/me/first-password", new { newPassword });
    public Task<ApiResult<AuthResponse>> RegisterAsync(RegisterRequest request) => PostAsync<AuthResponse>("/auth/register", request);
    public Task<ApiResult<UserProfile>> MeAsync() => GetAsync<UserProfile>("/auth/me");
    public Task<ApiResult<UserProfile>> UpdateProfileAsync(EditProfileRequest request) => PutAsync<UserProfile>("/auth/me/profile", request);
    public Task<ApiResult<bool>> ChangePasswordAsync(string currentPassword, string newPassword) =>
        PostAsync<bool>("/auth/me/password", new { currentPassword, newPassword });
    public Task<ApiResult<MySubscriptions>> MySubscriptionsAsync() => GetAsync<MySubscriptions>("/subscriptions/me");
    public Task<ApiResult<Entitlement>> RedeemKeyAsync(string code) => PostAsync<Entitlement>("/subscriptions/redeem", new { code });
    public Task<ApiResult<SubscriptionInfo>> CheckoutAsync(SubscriptionPlan plan, BillingPeriod period) =>
        PostAsync<SubscriptionInfo>("/subscriptions/checkout", new CheckoutRequest(plan, period));
    public Task<ApiResult<SubscriptionInfo>> StartTrialAsync() => PostAsync<SubscriptionInfo>("/subscriptions/trial");
    public Task<ApiResult<SimulatedPayment>> SimulatePaymentAsync(SubscriptionPlan plan, BillingPeriod period, string provider) =>
        PostAsync<SimulatedPayment>("/subscriptions/simulate-payment", new SimulatePaymentRequest(plan, period, provider));
    public Task KeepSessionAliveAsync() => RenewSessionIfDueAsync();
}

public class AgentApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.Chat), IAgentApi
{
    public Task<ApiResult<List<StudySlot>>> ScheduleAsync() => GetAsync<List<StudySlot>>("/agent/schedule");
    public Task<ApiResult<bool>> SaveScheduleAsync(List<StudySlot> slots) => PutAsync<bool>("/agent/schedule", slots);
    public Task<ApiResult<DayPlan>> TodayPlanAsync() => GetAsync<DayPlan>("/agent/plan/today");
    public Task<ApiResult<bool>> RecordStudyAsync(Guid courseId, int minutes) => PostAsync<bool>("/agent/sessions", new { courseId, minutes });
    public Task<ApiResult<List<AgentMemoryItem>>> MemoriesAsync() => GetAsync<List<AgentMemoryItem>>("/agent/memories");
    public Task<ApiResult<bool>> UpdateMemoryAsync(Guid id, string content) => PutAsync<bool>($"/agent/memories/{id}", new { content });
    public Task<ApiResult<bool>> DeleteMemoryAsync(Guid id) => DeleteAsync<bool>($"/agent/memories/{id}");
    public Task<ApiResult<bool>> ClearMemoriesAsync() => DeleteAsync<bool>("/agent/memories");
    public Task<ApiResult<MemorySettings>> MemorySettingsAsync() => GetAsync<MemorySettings>("/agent/memory-settings");
    public Task<ApiResult<MemorySettings>> SetMemoryEnabledAsync(bool enabled) => PutAsync<MemorySettings>("/agent/memory-settings", new MemorySettings(enabled, null));
    public Task<ApiResult<TeacherAgentProfile>> TeacherProfileAsync() => GetAsync<TeacherAgentProfile>("/teacher-agent/profile");
    public Task<ApiResult<TeacherAgentProfile>> SaveTeacherProfileAsync(TeacherAgentProfile profile) => PutAsync<TeacherAgentProfile>("/teacher-agent/profile", profile);
    public Task<ApiResult<ComposeResult>> ComposeAsync(ComposeRequest request) => PostAsync<ComposeResult>("/teacher-agent/compose", request);
    public Task<ApiResult<bool>> LearnStyleAsync(string text) => PostAsync<bool>("/teacher-agent/learn-style", new { text });
    public Task<ApiResult<ClassInsightsResult>> ClassInsightsAsync(Guid classroomId) => PostAsync<ClassInsightsResult>($"/teacher-agent/classes/{classroomId}/insights");
    public Task<ApiResult<AssistantAnswer>> AskAssistantAsync(Guid? classroomId, string message) => PostAsync<AssistantAnswer>("/teacher-agent/ask", new { classroomId, message });
    public Task<ApiResult<List<AssistantMessage>>> AssistantConversationAsync() => GetAsync<List<AssistantMessage>>("/teacher-agent/conversation");
    public Task<ApiResult<bool>> ClearAssistantConversationAsync() => DeleteAsync<bool>("/teacher-agent/conversation");
}

public class PlanApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.Site), IPlanApi
{
    public Task<ApiResult<List<PlanInfo>>> ListAsync() => GetAsync<List<PlanInfo>>("/api/plans");
}

public class ClassroomApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.User), IClassroomApi
{
    public Task<ApiResult<List<Classroom>>> MineAsync() => GetAsync<List<Classroom>>("/classrooms/mine");
    public Task<ApiResult<Classroom>> GetAsync(Guid id) => GetAsync<Classroom>($"/classrooms/{id}");
    public Task<ApiResult<Classroom>> CreateAsync(CreateClassroomRequest request) => PostAsync<Classroom>("/classrooms", request);
    public Task<ApiResult<List<ClassroomMember>>> MembersAsync(Guid id) => GetAsync<List<ClassroomMember>>($"/classrooms/{id}/members");
    public Task<ApiResult<bool>> RemoveMemberAsync(Guid classroomId, Guid studentId) =>
        DeleteAsync<bool>($"/classrooms/{classroomId}/members/{studentId}");
}

public class FamilyApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.User), IFamilyApi
{
    public Task<ApiResult<List<ChildLink>>> ChildrenAsync() => GetAsync<List<ChildLink>>("/parents/children");
    public Task<ApiResult<ChildLink>> LinkChildAsync(string code) => PostAsync<ChildLink>("/parents/children/link", new { code });
    public Task<ApiResult<ChildLink>> CreateChildAsync(CreateChildRequest request) => PostAsync<ChildLink>("/parents/children", request);
    public Task<ApiResult<List<ParentLink>>> MyParentsAsync() => GetAsync<List<ParentLink>>("/me/parents");
    public Task<ApiResult<bool>> RespondAsync(Guid linkId, bool approve) =>
        PostAsync<bool>($"/me/parents/{linkId}/{(approve ? "approve" : "reject")}");
}

public class CourseApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.Cours, stringEnums: false), ICourseApi
{
    public Task<ApiResult<List<CourseSummary>>> MineAsync() => GetAsync<List<CourseSummary>>("/cours");
    public Task<ApiResult<List<CourseSummary>>> SharedWithMeAsync() => GetAsync<List<CourseSummary>>("/cours/shared");
    public Task<ApiResult<CourseDetail>> GetAsync(Guid id) => GetAsync<CourseDetail>($"/cours/{id}");
    public Task<ApiResult<CourseSummary>> ShareAsync(Guid id, bool publish, List<Guid> classroomIds) =>
        PutAsync<CourseSummary>($"/cours/{id}/sharing", new ShareCourseRequest(publish, classroomIds));
    public Task<ApiResult<CourseDetail>> CreateAsync(CourseInput course) => PostAsync<CourseDetail>("/cours", course);
    public Task<ApiResult<bool>> UpdateAsync(Guid id, CourseInput course) => PutAsync<bool>($"/cours/{id}", course);
    public Task<ApiResult<bool>> DeleteAsync(Guid id) => DeleteAsync<bool>($"/cours/{id}");
    public Task<ApiResult<CourseDetail>> UploadPdfAsync(Guid id, ImageUpload pdf) => UploadAsync<CourseDetail>($"/cours/{id}/upload", pdf);
    public Task<ApiResult<CourseDetail>> RestructureAsync(Guid id) => PostAsync<CourseDetail>($"/cours/{id}/restructure");
    public Task<ApiResult<CourseDetail>> ImportLinkAsync(string url) => PostAsync<CourseDetail>("/cours/import-link", new { url });
    public Task<ApiResult<CourseDetail>> ImportPhotosAsync(IEnumerable<ImageUpload> photos) => UploadManyAsync<CourseDetail>("/cours/import-photos", photos);
    public Task<ApiResult<List<LabSuggestion>>> LabSuggestionsAsync(Guid id) => GetAsync<List<LabSuggestion>>($"/cours/{id}/lab-suggestions");
    public Task<ApiResult<CourseDetail>> AcceptLabAsync(Guid id, Guid suggestionId) => PostAsync<CourseDetail>($"/cours/{id}/lab-suggestions/{suggestionId}/accept");
    public Task<ApiResult<CourseDetail>> AcceptAllLabsAsync(Guid id) => PostAsync<CourseDetail>($"/cours/{id}/lab-suggestions/accept-all");
    public Task<ApiResult<bool>> RefuseLabAsync(Guid id, Guid suggestionId) => PostAsync<bool>($"/cours/{id}/lab-suggestions/{suggestionId}/refuse");
    public Task<ApiResult<CourseFeedback>> FeedbackAsync(Guid id) => GetAsync<CourseFeedback>($"/cours/{id}/feedback");
    public Task<ApiResult<CourseFeedback>> SetFeedbackAsync(Guid id, bool useful) => PutAsync<CourseFeedback>($"/cours/{id}/feedback", new { useful });
    public Task<ApiResult<bool>> ClearFeedbackAsync(Guid id) => DeleteAsync<bool>($"/cours/{id}/feedback");
}

public class MediaApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.Site), IMediaApi
{
    public Task<ApiResult<UploadedFile>> UploadImageAsync(ImageUpload image) => UploadAsync<UploadedFile>("/api/upload", image);
}

public class PracticeQuizApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.Quiz), IPracticeQuizApi
{
    public Task<ApiResult<PracticeQuiz>> GenerateAsync(string title, Guid courseId, int questions, string difficulty) =>
        PostAsync<PracticeQuiz>("/api/quiz/generate", new GeneratePracticeQuizRequest(title, courseId, questions, difficulty));
    public Task<ApiResult<PracticeProblemSet>> GenerateProblemsAsync(string title, Guid courseId, int count, string difficulty) =>
        PostAsync<PracticeProblemSet>("/api/quiz/problems", new GeneratePracticeProblemsRequest(title, courseId, count, difficulty));
    public Task<ApiResult<PracticeResult>> SubmitAsync(Guid quizId, List<PracticeAnswer> answers) =>
        PostAsync<PracticeResult>($"/api/quiz/{quizId}/submit", new { answers });
}

public class AssessmentApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.Quiz), IAssessmentApi
{
    public Task<ApiResult<List<Assessment>>> MineAsTeacherAsync() => GetAsync<List<Assessment>>("/assessments/mine");
    public Task<ApiResult<List<StudentAssessment>>> MineAsStudentAsync() => GetAsync<List<StudentAssessment>>("/assessments/mine");
    public Task<ApiResult<Assessment>> GetAsync(Guid id) => GetAsync<Assessment>($"/assessments/{id}");
    public Task<ApiResult<Assessment>> CreateAsync(AssessmentDraft draft) => PostAsync<Assessment>("/assessments", ToInput(draft));
    public Task<ApiResult<Assessment>> UpdateAsync(Guid id, AssessmentDraft draft) => PutAsync<Assessment>($"/assessments/{id}", ToInput(draft));
    public Task<ApiResult<Assessment>> PublishAsync(Guid id) => PostAsync<Assessment>($"/assessments/{id}/publish");
    public Task<ApiResult<Assessment>> CloseAsync(Guid id) => PostAsync<Assessment>($"/assessments/{id}/close");
    public Task<ApiResult<bool>> DeleteAsync(Guid id) => DeleteAsync<bool>($"/assessments/{id}");
    public Task<ApiResult<Attempt>> StartAttemptAsync(Guid id) => PostAsync<Attempt>($"/assessments/{id}/attempts");
    public Task<ApiResult<bool>> SaveDraftAsync(Guid id, Guid submissionId, List<AnswerDraft> answers) =>
        PutAsync<bool>($"/assessments/{id}/attempts/{submissionId}/draft", new SubmitAttemptRequest(answers));
    public Task<ApiResult<SubmissionReview>> SubmitAsync(Guid id, Guid submissionId, List<AnswerDraft> answers) =>
        PostAsync<SubmissionReview>($"/assessments/{id}/attempts/{submissionId}/submit", new SubmitAttemptRequest(answers));
    public Task<ApiResult<SubmissionReview>> GetSubmissionAsync(Guid id, Guid submissionId) =>
        GetAsync<SubmissionReview>($"/assessments/{id}/submissions/{submissionId}");
    public Task<ApiResult<List<SubmissionRow>>> SubmissionsAsync(Guid id) => GetAsync<List<SubmissionRow>>($"/assessments/{id}/submissions");
    public Task<ApiResult<SubmissionReview>> GradeAsync(Guid id, Guid submissionId, Guid questionId, decimal points, string? feedback) =>
        PutAsync<SubmissionReview>($"/assessments/{id}/submissions/{submissionId}/answers/{questionId}/grade", new GradeAnswerRequest(points, feedback));
    public Task<ApiResult<AiSuggestion>> AiSuggestionAsync(Guid id, Guid submissionId, Guid questionId) =>
        PostAsync<AiSuggestion>($"/assessments/{id}/submissions/{submissionId}/answers/{questionId}/ai-suggestion");

    public Task<ApiResult<AnswerPhoto>> UploadPhotoAsync(Guid id, Guid submissionId, Guid questionId, ImageUpload photo) =>
        UploadAsync<AnswerPhoto>($"/assessments/{id}/attempts/{submissionId}/questions/{questionId}/photos", photo);
    public Task<ApiResult<bool>> DeletePhotoAsync(Guid id, Guid submissionId, Guid photoId) =>
        DeleteAsync<bool>($"/assessments/{id}/attempts/{submissionId}/photos/{photoId}");
    public Task<ApiResult<List<AnswerPhoto>>> AttemptPhotosAsync(Guid id, Guid submissionId) =>
        GetAsync<List<AnswerPhoto>>($"/assessments/{id}/attempts/{submissionId}/photos");
    public Task<string?> PhotoAsync(Guid id, Guid submissionId, Guid photoId) =>
        DataUrlAsync($"/assessments/{id}/submissions/{submissionId}/photos/{photoId}");
    public Task<ApiResult<AiFeedback>> AiFeedbackAsync(Guid id, Guid submissionId, Guid questionId) =>
        PostAsync<AiFeedback>($"/assessments/{id}/submissions/{submissionId}/answers/{questionId}/ai-feedback");

    /// <summary>Dates saisies dans le fuseau de l'application, envoyées en UTC ; options vides retirées des questions sans choix.</summary>
    private static object ToInput(AssessmentDraft d) => new
    {
        d.Title, d.Kind, d.Description, d.Subject, d.CourseId, d.ClassroomIds,
        OpensAt = d.OpensAt is { } o ? AppTime.ToUtc(o) : (DateTime?)null, DueAt = d.DueAt is { } due ? AppTime.ToUtc(due) : (DateTime?)null,
        d.DurationMinutes, d.MaxAttempts, d.Shuffle, d.ShowCorrection, d.AllowOffline, d.AiAssistedGrading, d.ScaleTo,
        Questions = d.Questions.Select(q => new
        {
            q.Type, q.Text, q.Points,
            Options = q.Type is QuestionType.SingleChoice or QuestionType.MultipleChoice ? q.Options : [],
            CorrectOptions = q.Type is QuestionType.SingleChoice or QuestionType.MultipleChoice ? q.CorrectOptions : [],
            TrueFalseAnswer = q.Type == QuestionType.TrueFalse ? q.TrueFalseAnswer : null,
            AcceptedAnswers = q.Type == QuestionType.ShortAnswer ? q.AcceptedAnswers : [],
            NumericAnswer = q.Type == QuestionType.Numeric ? q.NumericAnswer : null,
            Tolerance = q.Type == QuestionType.Numeric ? q.Tolerance : null,
            Unit = q.Type == QuestionType.Numeric ? q.Unit : null,
            Criteria = q.Type == QuestionType.OpenEnded ? q.Criteria : null,
            q.Explanation, q.CourseId, q.SectionId
        })
    };
}

public class ProgressApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.Quiz), IProgressApi
{
    public Task<ApiResult<StudentProgress>> MineAsync() => GetAsync<StudentProgress>("/progress/me");
    public Task<ApiResult<StudentProgress>> ChildAsync(Guid studentId) => GetAsync<StudentProgress>($"/progress/students/{studentId}");
    public Task<ApiResult<ClassroomProgress>> ClassroomAsync(Guid classroomId) => GetAsync<ClassroomProgress>($"/progress/classrooms/{classroomId}");
}

public class TutorApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.Chat), ITutorApi
{
    public Task<ApiResult<List<TutorConversationSummary>>> ConversationsAsync() =>
        GetAsync<List<TutorConversationSummary>>("/tutor/conversations");
    public Task<ApiResult<TutorConversation>> ConversationAsync(Guid courseId) => GetAsync<TutorConversation>($"/tutor/courses/{courseId}");
    public Task<ApiResult<TutorExchange>> AskAsync(Guid courseId, string? message, Guid? imageId) =>
        PostAsync<TutorExchange>($"/tutor/courses/{courseId}/messages", new { message, imageId });
    public Task<ApiResult<TutorExchange>> AskStreamAsync(Guid courseId, string? message, Guid? imageId, Func<string, Task> onDelta, CancellationToken ct = default) =>
        PostStreamAsync<TutorExchange>($"/tutor/courses/{courseId}/messages/stream", new { message, imageId }, onDelta, ct);
    public Task<ApiResult<UploadedImage>> UploadImageAsync(ImageUpload image) => UploadAsync<UploadedImage>("/tutor/images", image);
    public Task<string?> ImageAsync(Guid imageId) => DataUrlAsync($"/tutor/images/{imageId}");
    public Task<ApiResult<TutorMessage>> PinAsync(Guid messageId, bool pinned) =>
        PutAsync<TutorMessage>($"/tutor/messages/{messageId}/pin", new { pinned });
    public Task<ApiResult<List<PinnedExplanation>>> PinnedAsync() => GetAsync<List<PinnedExplanation>>("/tutor/pinned");
    public Task<ApiResult<bool>> ClearAsync(Guid courseId) => DeleteAsync<bool>($"/tutor/courses/{courseId}");
    public Task<ApiResult<string>> DraftCourseAsync(string subject, string? instructions) =>
        PostAsync<string>("/chat/draft", new { subject, additionalInstructions = instructions });
}
