using Chat.Model;
using GPOE26.ServiceDefaults.Llm;

namespace Chat.Service;

/// <summary>Brouillon de cours par le client LLM partagé (OpenRouter ou tout fournisseur compatible OpenAI, section « LLM »).</summary>
public class CompatibleLlmService(LlmClient llm) : ILlmService
{
    public Task<string> GenerateDraftAsync(CourseDraftRequest request, CancellationToken ct) =>
        llm.CompleteAsync(
        [
            new("system", "Tu es un professeur expert. Rédige un cours détaillé en Markdown (titres, listes, formules KaTeX $$...$$, blocs de code si pertinent), sans phrase d'introduction."),
            new("user", $"Sujet : {request.Subject}\nDirectives : {request.AdditionalInstructions ?? "Aucune"}")
        ], ct: ct);
}
