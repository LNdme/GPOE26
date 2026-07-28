using GPOE26.Ai.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GPOE26.Ai;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre la passerelle OpenRouter et les agents de traitement de contenu.
    ///
    /// À appeler depuis les services qui produisent du contenu de cours (Cours) comme
    /// depuis ceux qui tutorent (Chat) : c'est la bibliothèque partagée qui évite une
    /// référence circulaire entre les deux services Aspire.
    /// </summary>
    public static IServiceCollection AddGpoeAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpenRouterOptions>(configuration.GetSection(OpenRouterOptions.SectionName));

        // Les appels LLM sont lents par nature (transcription d'une page, rédaction d'une
        // réponse) : un timeout court les ferait échouer alors qu'ils aboutiraient.
        services.AddHttpClient(OpenRouterClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        services.AddSingleton<OpenRouterClient>();
        services.AddSingleton<TranscriptionAgent>();
        services.AddSingleton<StructurateurAgent>();

        return services;
    }
}
