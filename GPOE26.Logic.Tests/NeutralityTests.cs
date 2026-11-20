using System.Text.Json;
using System.Text.Json.Serialization;
using Chat.Agents;
using Chat.Endpoints;
using Chat.Service;
using User.DTOs;
using User.Model;

namespace GPOE26.Logic.Tests;

/// <summary>
/// Lot B2 : les consignes ne supposent plus un pays. Langue, échelle de notes, pays et système viennent du contexte
/// d'apprentissage (lot B1) ; un ancien profil camerounais garde ce qu'il avait (pays, sous-système, examen).
/// </summary>
public class NeutralityTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    /// <summary>Profil tel que Chat le reçoit : sérialisé par le service User, relu avec les options de PlatformClients.</summary>
    private static LearnerProfileRef ProfileOf(AppUser user)
    {
        var userOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var json = JsonSerializer.Serialize(new UserProfileDto(user), userOptions);
        return JsonSerializer.Deserialize<LearnerProfileRef>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static AppUser Student(Action<AppUser> setup)
    {
        var user = new AppUser { Username = "x", PasswordHash = "x", Role = UserRole.Student };
        setup(user);
        return user;
    }

    [Fact]
    public void Ancien_profil_camerounais_garde_ses_informations()
    {
        var p = ProfileOf(Student(u => { u.SubSystem = SchoolSubSystem.Francophone; u.Level = "3e"; u.ExamTarget = "BEPC"; }));
        var lines = AgentPrompts.LearnerLines(p, Today);

        // Le pays n'est plus dans la consigne générale : il arrive par le profil
        Assert.Equal("Pays : Cameroun", lines[0]);
        Assert.Contains("Classe ou niveau : 3e, Sous-système francophone", lines);
        Assert.Contains("Tranche : premier cycle du secondaire", lines);
        Assert.Contains("Examen visé : BEPC", lines);
        Assert.Contains("Notes exprimées sur 20", lines);
        Assert.Contains("réponds en français", AgentPrompts.LanguageRule(p));
    }

    [Fact]
    public void Profil_mexicain_en_espagnol_sans_cameroun()
    {
        var p = ProfileOf(Student(u => { u.Country = "MX"; u.Level = "Preparatoria"; u.LearningGoal = LearningGoal.Skill; }));
        var lines = AgentPrompts.LearnerLines(p, Today);
        var all = string.Join("\n", lines) + AgentPrompts.LanguageRule(p);

        Assert.Contains("Pays : Mexique", lines);
        Assert.Contains("Tranche : second cycle du secondaire", lines);
        Assert.Contains("Objectif : acquérir une compétence, sans examen à préparer", lines);
        Assert.Contains("Notes exprimées sur 10", lines);
        Assert.Contains("réponds en espagnol", AgentPrompts.LanguageRule(p));
        Assert.DoesNotContain("Cameroun", all);
        Assert.DoesNotContain("sous-système", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Profil_vide_sans_pays_ni_echelle_inventes()
    {
        var p = ProfileOf(Student(_ => { }));
        Assert.Empty(AgentPrompts.LearnerLines(p, Today));
        Assert.Contains("réponds en français", AgentPrompts.LanguageRule(p));
    }

    [Fact]
    public void Adulte_par_curiosite()
    {
        var p = ProfileOf(Student(u => { u.Country = LearningContextRules.OtherCountry; u.LevelBand = LevelBand.Adult; u.LearningGoal = LearningGoal.Curiosity; }));
        var lines = AgentPrompts.LearnerLines(p, Today);
        Assert.Contains(lines, l => l.StartsWith("Tranche : adulte qui apprend hors d'un cursus scolaire"));
        Assert.Contains("Objectif : apprendre par curiosité, sans examen à préparer", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Pays") || l.StartsWith("Notes"));
    }

    [Theory]
    [InlineData("fr", null, null, "réponds en français")]
    [InlineData("en", null, null, "réponds en anglais.")]
    [InlineData("es", null, null, "réponds en espagnol")]
    [InlineData("fr", "Anglophone", null, "réponds en anglais (l'apprenant suit le sous-système anglophone)")]
    [InlineData("fr", null, "CU", "réponds en espagnol")]
    [InlineData("en", null, "MX", "réponds en anglais")]
    public void La_langue_du_message_d_abord_puis_celle_du_profil(string language, string? subSystem, string? country, string expected)
    {
        var context = country is null ? null : new LearningContextRef(country, null, null, null, null, null, null, null, null, null, null);
        var p = new LearnerProfileRef("Student", null, null, null, language, subSystem, null, null, null, null, null, null, null, null, context);
        var rule = AgentPrompts.LanguageRule(p);
        Assert.StartsWith("Réponds dans la langue du dernier message de l'apprenant", rule);
        Assert.Contains(expected, rule);
    }

    [Fact]
    public void L_enseignant_est_decrit_par_son_pays_et_son_echelle()
    {
        var teacher = new AppUser { Username = "prof", PasswordHash = "x", Role = UserRole.Teacher, Country = "FR", EducationSystem = "Éducation nationale" };
        var lines = AgentPrompts.TeacherLines(ProfileOf(teacher));
        Assert.Contains("Pays : France", lines);
        Assert.Contains("Système d'enseignement : Éducation nationale", lines);
        Assert.Contains("Notes exprimées sur 20", lines);
    }

    [Theory]
    [InlineData(12.0, null, "12/20")]
    [InlineData(9.5, "Over20", "9,5/20")]
    [InlineData(12.0, "Over10", "6/10")]
    [InlineData(13.0, "Over100", "65/100")]
    [InlineData(18.0, "Letters", "A (90 %)")]
    [InlineData(11.0, "Letters", "F (55 %)")]
    public void Une_note_sur_20_est_ecrite_dans_l_echelle_de_la_personne(double on20, string? scale, string expected) =>
        Assert.Equal(expected, AgentPrompts.Grade(on20, scale));

    [Fact]
    public void Le_suivi_de_classe_suit_l_echelle_de_l_enseignant()
    {
        var insights = new ClassInsights(2, 9.5, [new AtRiskStudent(Guid.NewGuid(), "Awa", 8, "Moyenne sous la moitié des points")], []);
        var text = TeacherAssistant.Anonymized(insights, "Over10");
        Assert.Contains("moyenne : 4,8/10", text);
        Assert.Contains("Élève 1 : Moyenne sous la moitié des points (4/10)", text);
        Assert.DoesNotContain("/20", text);
    }

    [Fact]
    public void Aucune_consigne_ne_suppose_un_pays()
    {
        foreach (var name in PromptLibrary.Names)
        {
            var text = PromptLibrary.Get(name);
            Assert.DoesNotContain("Cameroun", text);
            Assert.DoesNotContain("Nanga", text);
        }
        Assert.Contains("N'impose jamais un pays", TeacherAssistant.System([]));
    }

    [Fact]
    public void L_agent_du_site_prend_le_nom_et_le_fuseau_de_l_etablissement()
    {
        var contact = JsonDocument.Parse("""{"name":"Colegio Benito Juárez","address":"Av. Reforma 10","city":"Puebla"}""").RootElement;
        var ctx = SiteAgentPrompt.BuildContext(null, null, null, null, contact, new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc));
        var system = SiteAgentPrompt.System("prof", false, ctx, "America/Mexico_City");

        Assert.StartsWith("Tu es l'agent du site web de Colegio Benito Juárez,", system);
        Assert.Contains("fuseau America/Mexico_City", system);
        Assert.DoesNotContain("Cameroun", system);

        // Coordonnées indisponibles : ni nom inventé ni fuseau vide
        var empty = SiteAgentPrompt.System("prof", false, SiteAgentPrompt.BuildContext(null, null, null, null, null, DateTime.UtcNow));
        Assert.StartsWith("Tu es l'agent du site web de l'établissement,", empty);
        Assert.Contains($"fuseau {SiteAgentPrompt.DefaultTimeZone}", empty);
    }

    [Fact]
    public void Les_mots_courants_espagnols_et_anglais_ne_comptent_pas()
    {
        var words = AgentPrompts.Significant("¿Cómo se calcula para la probabilidad de esta urna? Explain which ratio");
        Assert.Contains("calcula", words);
        Assert.Contains("probabilidad", words);
        Assert.Contains("ratio", words);
        Assert.DoesNotContain("como", words);
        Assert.DoesNotContain("para", words);
        Assert.DoesNotContain("esta", words);
        Assert.DoesNotContain("which", words);
    }
}
