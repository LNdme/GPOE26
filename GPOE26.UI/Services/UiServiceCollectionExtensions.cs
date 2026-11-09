using Microsoft.Extensions.DependencyInjection;

namespace GPOE26.UI.Services;

public static class UiServiceCollectionExtensions
{
    /// <summary>
    /// Services de l'espace d'apprentissage. L'hôte doit fournir ISessionStore et les HttpClient
    /// nommés ApiClients.User / Cours / Quiz / Chat.
    /// </summary>
    public static IServiceCollection AddGpoeLearningUi(this IServiceCollection services)
    {
        services.AddScoped<SessionState>();
        services.AddScoped<IAccountApi, AccountApi>();
        services.AddScoped<IClassroomApi, ClassroomApi>();
        services.AddScoped<IFamilyApi, FamilyApi>();
        services.AddScoped<ICourseApi, CourseApi>();
        services.AddScoped<IAssessmentApi, AssessmentApi>();
        services.AddScoped<IProgressApi, ProgressApi>();
        services.AddScoped<ITutorApi, TutorApi>();
        services.AddScoped<IMediaApi, MediaApi>();
        services.AddScoped<IPlanApi, PlanApi>();
        services.AddScoped<IPracticeQuizApi, PracticeQuizApi>();
        return services;
    }
}
