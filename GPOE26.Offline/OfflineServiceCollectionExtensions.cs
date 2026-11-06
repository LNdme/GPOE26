using GPOE26.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GPOE26.Offline;

public static class OfflineServiceCollectionExtensions
{
    /// <summary>
    /// Active le fonctionnement hors connexion. À appeler APRÈS AddGpoeLearningUi : les API des pages
    /// sont remplacées par leurs versions hors ligne, qui s'appuient sur les clients HTTP d'origine.
    /// </summary>
    public static IServiceCollection AddGpoeOffline(this IServiceCollection services, OfflineOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<NetworkMonitor>();
        // La session reste ouverte hors connexion après l'expiration du jeton
        services.TryAddSingleton(new SessionOptions { KeepExpiredSession = true });

        services.AddScoped<OfflineDatabase>();
        services.AddScoped<SyncService>();

        // Clients HTTP d'origine, utilisés par les versions hors ligne
        services.AddScoped<CourseApi>();
        services.AddScoped<AssessmentApi>();
        services.AddScoped<ProgressApi>();
        services.AddScoped<ClassroomApi>();
        services.AddScoped<TutorApi>();

        services.Replace(ServiceDescriptor.Scoped<ICourseApi, OfflineCourseApi>());
        services.Replace(ServiceDescriptor.Scoped<IAssessmentApi, OfflineAssessmentApi>());
        services.Replace(ServiceDescriptor.Scoped<IProgressApi, OfflineProgressApi>());
        services.Replace(ServiceDescriptor.Scoped<IClassroomApi, OfflineClassroomApi>());
        services.Replace(ServiceDescriptor.Scoped<ITutorApi, OfflineTutorApi>());
        services.Replace(ServiceDescriptor.Scoped<IOfflineStatus, OfflineStatusService>());
        return services;
    }
}
