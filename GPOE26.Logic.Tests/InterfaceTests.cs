using Chat.Endpoints;
using GPOE26.ServiceDefaults.Llm;
using GPOE26.UI.Components;
using GPOE26.UI.Pages;

namespace GPOE26.Logic.Tests;

/// <summary>E4 : la réponse du tuteur lue au fil de l'eau, morceau par morceau.</summary>
public class LlmStreamTests
{
    [Fact]
    public void Un_morceau_de_reponse_est_lu()
    {
        var piece = LlmClient.ReadStreamLine("""data: {"choices":[{"delta":{"content":"Bonjour"}}]}""", out var done);
        Assert.Equal("Bonjour", piece);
        Assert.False(done);
    }

    [Theory]
    [InlineData(": OPENROUTER PROCESSING")]
    [InlineData("")]
    [InlineData("""data: {"choices":[{"delta":{"role":"assistant"}}]}""")]
    [InlineData("""data: {"choices":[]}""")]
    public void Les_lignes_sans_texte_sont_ignorees(string line)
    {
        Assert.Null(LlmClient.ReadStreamLine(line, out var done));
        Assert.False(done);
    }

    [Fact]
    public void La_fin_du_flux_est_reconnue()
    {
        Assert.Null(LlmClient.ReadStreamLine("data: [DONE]", out var done));
        Assert.True(done);
    }

    [Fact]
    public void Une_erreur_du_fournisseur_dans_le_flux_est_signalee() =>
        Assert.Throws<HttpRequestException>(() =>
            LlmClient.ReadStreamLine("""data: {"error":{"message":"Provider overloaded"}}""", out _));

    [Fact]
    public void La_reflexion_d_un_modele_n_est_pas_montree_meme_coupee_en_morceaux()
    {
        var filter = new LlmClient.ThinkingFilter();
        var shown = string.Concat(new[] { "<thi", "nk>je cherche", " encore</thi", "nk>  La vitesse", " vaut g t." }
            .Select(filter.Visible));
        Assert.Equal("La vitesse vaut g t.", shown);
    }

    [Fact]
    public void Une_reponse_sans_reflexion_passe_telle_quelle()
    {
        var filter = new LlmClient.ThinkingFilter();
        var shown = string.Concat(new[] { "<", "b>Gras</b> et ", "<think> au milieu" }.Select(filter.Visible));
        Assert.Equal("<b>Gras</b> et <think> au milieu", shown);
    }
}

/// <summary>E2 : la session ne tombe plus sans prévenir.</summary>
public class SessionNoticeTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(30, SessionNotice.NoticeState.Hidden)]
    [InlineData(6, SessionNotice.NoticeState.Hidden)]
    [InlineData(5, SessionNotice.NoticeState.Ending)]
    [InlineData(1, SessionNotice.NoticeState.Ending)]
    [InlineData(0, SessionNotice.NoticeState.Ended)]
    [InlineData(-10, SessionNotice.NoticeState.Ended)]
    public void Le_bandeau_suit_le_temps_restant(int minutesLeft, SessionNotice.NoticeState expected) =>
        Assert.Equal(expected, SessionNotice.StateFor(true, Now.AddMinutes(minutesLeft), Now));

    [Fact]
    public void Rien_n_est_affiche_a_un_visiteur_deconnecte() =>
        Assert.Equal(SessionNotice.NoticeState.Hidden, SessionNotice.StateFor(false, Now.AddMinutes(-5), Now));
}

/// <summary>E9 : à la fin du temps, l'envoi de la copie est retenté jusqu'au succès.</summary>
public class AutoSubmitTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 30)]
    [InlineData(50, 30)]
    public void Les_essais_s_espacent_sans_depasser_30_secondes(int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), TakeAssessment.AutoSubmitRetryDelay(failures));
}

/// <summary>Calibrage : l'aperçu des discussions du tuteur se lit sans Markdown ni LaTeX bruts.</summary>
public class TutorPreviewTests
{
    [Fact]
    public void Le_gras_et_les_formules_deviennent_du_texte_simple() =>
        Assert.Equal("Bonne question ! La vitesse vaut v = g t, soit 9,8 × 2 = 19,6 m/s.",
            TutorEndpoints.PlainText(@"Bonne question ! La **vitesse** vaut $v = g t$, soit $9{,}8 \times 2 = 19{,}6$ m/s."));

    [Fact]
    public void Titres_fractions_et_racines_sont_lisibles() =>
        Assert.Equal("Méthode h = 1/2 g t² et v = √(2gh)",
            TutorEndpoints.PlainText("## Méthode\n" + @"\[ h = \frac{1}{2} g t^2 \] et $v = \sqrt{2gh}$"));

    [Fact]
    public void L_apercu_est_coupe_a_140_caracteres()
    {
        var preview = TutorEndpoints.Preview(new string('a', 300))!;
        Assert.Equal(141, preview.Length);
        Assert.EndsWith("…", preview);
    }
}
