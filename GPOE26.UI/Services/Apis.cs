using GPOE26.UI.Models;

namespace GPOE26.UI.Services;

// ══════════════════════════════════════════════════════════════════════════════
// Interfaces consommées par les pages. Les implémentations HTTP ci-dessous servent
// le Web ; l'application Windows pourra les décorer d'un cache hors ligne.
// ══════════════════════════════════════════════════════════════════════════════

public interface IAccountApi
{
    Task<ApiResult<AuthResponse>> LoginAsync(string email, string password);
    Task<ApiResult<AuthResponse>> RegisterAsync(RegisterRequest request);
    Task<ApiResult<UserProfile>> MeAsync();
    Task<ApiResult<MySubscriptions>> MySubscriptionsAsync();
    Task<ApiResult<Entitlement>> RedeemKeyAsync(string code);
    Task<ApiResult<SubscriptionInfo>> CheckoutAsync(SubscriptionPlan plan, BillingPeriod period);
    Task<ApiResult<SubscriptionInfo>> StartTrialAsync();
    Task<ApiResult<SimulatedPayment>> SimulatePaymentAsync(SubscriptionPlan plan, BillingPeriod period, string provider);
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
    Task<ApiResult<Classroom>> JoinAsync(string code);
    Task<ApiResult<List<ClassroomMember>>> MembersAsync(Guid id);
    Task<ApiResult<bool>> RemoveMemberAsync(Guid classroomId, Guid studentId);
    Task<ApiResult<bool>> RegenerateCodeAsync(Guid id);
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
}

/// <summary>Images insérées dans un cours (hébergées par le service du site).</summary>
public interface IMediaApi
{
    Task<ApiResult<UploadedFile>> UploadImageAsync(ImageUpload image);
}

/// <summary>Quiz d'entraînement créé par l'IA à partir du texte d'un cours.</summary>
public interface IPracticeQuizApi
{
    Task<ApiResult<PracticeQuiz>> GenerateAsync(string title, string courseText, int questions);
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
    Task<ApiResult<SubmissionReview>> SubmitAsync(Guid id, Guid submissionId, List<AnswerDraft> answers);
    Task<ApiResult<SubmissionReview>> GetSubmissionAsync(Guid id, Guid submissionId);
    Task<ApiResult<List<SubmissionRow>>> SubmissionsAsync(Guid id);
    Task<ApiResult<SubmissionReview>> GradeAsync(Guid id, Guid submissionId, Guid questionId, decimal points, string? feedback);
    Task<ApiResult<AiSuggestion>> AiSuggestionAsync(Guid id, Guid submissionId, Guid questionId);
    Task<ApiResult<AnswerPhoto>> UploadPhotoAsync(Guid id, Guid submissionId, Guid questionId, ImageUpload photo);
    Task<ApiResult<bool>> DeletePhotoAsync(Guid id, Guid submissionId, Guid photoId);
    Task<string?> PhotoAsync(Guid id, Guid submissionId, Guid photoId);
    Task<ApiResult<AiFeedback>> AiFeedbackAsync(Guid id, Guid submissionId, Guid questionId);
}

/// <summary>Tuteur IA : une discussion conservée par cours, explications épinglées, photos d'exercices.</summary>
public interface ITutorApi
{
    Task<ApiResult<List<TutorConversationSummary>>> ConversationsAsync();
    Task<ApiResult<TutorConversation>> ConversationAsync(Guid courseId);
    Task<ApiResult<TutorExchange>> AskAsync(Guid courseId, string? message, Guid? imageId);
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
    public Task<ApiResult<AuthResponse>> RegisterAsync(RegisterRequest request) => PostAsync<AuthResponse>("/auth/register", request);
    public Task<ApiResult<UserProfile>> MeAsync() => GetAsync<UserProfile>("/auth/me");
    public Task<ApiResult<MySubscriptions>> MySubscriptionsAsync() => GetAsync<MySubscriptions>("/subscriptions/me");
    public Task<ApiResult<Entitlement>> RedeemKeyAsync(string code) => PostAsync<Entitlement>("/subscriptions/redeem", new { code });
    public Task<ApiResult<SubscriptionInfo>> CheckoutAsync(SubscriptionPlan plan, BillingPeriod period) =>
        PostAsync<SubscriptionInfo>("/subscriptions/checkout", new CheckoutRequest(plan, period));
    public Task<ApiResult<SubscriptionInfo>> StartTrialAsync() => PostAsync<SubscriptionInfo>("/subscriptions/trial");
    public Task<ApiResult<SimulatedPayment>> SimulatePaymentAsync(SubscriptionPlan plan, BillingPeriod period, string provider) =>
        PostAsync<SimulatedPayment>("/subscriptions/simulate-payment", new SimulatePaymentRequest(plan, period, provider));
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
    public Task<ApiResult<Classroom>> JoinAsync(string code) => PostAsync<Classroom>("/classrooms/join", new { code });
    public Task<ApiResult<List<ClassroomMember>>> MembersAsync(Guid id) => GetAsync<List<ClassroomMember>>($"/classrooms/{id}/members");
    public Task<ApiResult<bool>> RemoveMemberAsync(Guid classroomId, Guid studentId) =>
        DeleteAsync<bool>($"/classrooms/{classroomId}/members/{studentId}");
    public Task<ApiResult<bool>> RegenerateCodeAsync(Guid id) => PostAsync<bool>($"/classrooms/{id}/invite-code");
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
}

public class MediaApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.Site), IMediaApi
{
    public Task<ApiResult<UploadedFile>> UploadImageAsync(ImageUpload image) => UploadAsync<UploadedFile>("/api/upload", image);
}

public class PracticeQuizApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.Quiz), IPracticeQuizApi
{
    public Task<ApiResult<PracticeQuiz>> GenerateAsync(string title, string courseText, int questions) =>
        PostAsync<PracticeQuiz>("/api/quiz/generate", new GeneratePracticeQuizRequest(title, courseText, questions));
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
    public Task<ApiResult<UploadedImage>> UploadImageAsync(ImageUpload image) => UploadAsync<UploadedImage>("/tutor/images", image);
    public Task<string?> ImageAsync(Guid imageId) => DataUrlAsync($"/tutor/images/{imageId}");
    public Task<ApiResult<TutorMessage>> PinAsync(Guid messageId, bool pinned) =>
        PutAsync<TutorMessage>($"/tutor/messages/{messageId}/pin", new { pinned });
    public Task<ApiResult<List<PinnedExplanation>>> PinnedAsync() => GetAsync<List<PinnedExplanation>>("/tutor/pinned");
    public Task<ApiResult<bool>> ClearAsync(Guid courseId) => DeleteAsync<bool>($"/tutor/courses/{courseId}");
    public Task<ApiResult<string>> DraftCourseAsync(string subject, string? instructions) =>
        PostAsync<string>("/chat/draft", new { subject, additionalInstructions = instructions });
}
