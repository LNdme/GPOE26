using System.Text.Json;
using Quiz.Service;

namespace Quiz.Tests;

/// <summary>D6 : les réponses du modèle sont lues malgré le LaTeX, et les quiz mal formés sont écartés.</summary>
public class AiOutputTests
{
    [Fact]
    public void Un_commentaire_en_latex_ne_casse_plus_la_note()
    {
        // \s n'est pas un échappement JSON valide : le document entier était refusé (erreur 502)
        var suggestion = AiGradingService.Parse("""{"points": 1.5, "feedback": "Bien : $\sqrt{2gh}$, mais \frac{1}{2} oublié."}""", 2);
        Assert.Equal(1.5m, suggestion.Points);
        Assert.Contains("sqrt", suggestion.Feedback);
        Assert.Contains("frac", suggestion.Feedback);
    }

    [Fact]
    public void Une_note_ecrite_en_texte_est_lue() =>
        Assert.Equal(1.25m, AiGradingService.Parse("""{"points": "1,25", "feedback": "ok"}""", 2).Points);

    [Fact]
    public void Un_json_irreparable_donne_quand_meme_la_note()
    {
        var suggestion = AiGradingService.Parse("""{"points": 1, "feedback": "Calcul faux, voir \q{x}" """, 2);
        Assert.Equal(1m, suggestion.Points);
    }

    [Fact]
    public void Sans_note_lisible_l_erreur_reste_claire() =>
        Assert.Throws<FormatException>(() => AiGradingService.Parse("""{"note": "bien"}""", 2));

    private static JsonElement Questions(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Seules_les_questions_bien_formees_sont_gardees()
    {
        var questions = OpenAiQuizGeneratorService.ValidQuestions(Questions("""
            [
              {"text": "Que vaut g ?", "options": ["9,8", "8,9", "1", "0"], "correctOptionIndex": 0, "explanation": "g ≈ 9,8"},
              {"text": "Bonne réponse inexistante", "options": ["a", "b", "c", "d"], "correctOptionIndex": 4},
              {"text": "Une seule option", "options": ["a"], "correctOptionIndex": 0},
              {"text": "Option vide", "options": ["a", "", "c", "d"], "correctOptionIndex": 0},
              {"text": "Options en double", "options": ["a", "A", "c", "d"], "correctOptionIndex": 0},
              {"text": "", "options": ["a", "b"], "correctOptionIndex": 0},
              {"options": ["a", "b"], "correctOptionIndex": 0},
              "pas un objet"
            ]
            """));
        Assert.Equal("Que vaut g ?", Assert.Single(questions).Text);
    }

    [Fact]
    public void Une_reponse_qui_n_est_pas_une_liste_ne_donne_rien() =>
        Assert.Empty(OpenAiQuizGeneratorService.ValidQuestions(Questions("""{"questions": 3}""")));
}
