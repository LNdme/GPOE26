using System.Text.Json;
using System.Text.Json.Serialization;
using User.DTOs;
using User.Endpoints;
using User.Model;

namespace GPOE26.Logic.Tests;

/// <summary>Lot B1 : contexte d'apprentissage (pays, système, tranche, objectif, échelle, fuseau), déduit des anciens profils.</summary>
public class LearningContextTests
{
    private static AppUser Student(Action<AppUser>? setup = null)
    {
        var user = new AppUser { Username = "awa", PasswordHash = "x", Role = UserRole.Student };
        setup?.Invoke(user);
        return user;
    }

    [Fact]
    public void Ancien_profil_camerounais_deduit_entierement()
    {
        var ctx = LearningContextRules.Resolve(Student(u =>
        {
            u.SubSystem = SchoolSubSystem.Francophone;
            u.Level = "3e";
            u.ExamTarget = "BEPC";
        }));

        Assert.Equal("CM", ctx.Country);
        Assert.Equal("Sous-système francophone", ctx.EducationSystem);
        Assert.Equal(LevelBand.LowerSecondary, ctx.LevelBand);
        Assert.Equal(LearningGoal.Exam, ctx.Goal);
        Assert.Equal(GradeScale.Over20, ctx.GradeScale);
        Assert.Equal("Africa/Douala", ctx.TimeZone);
        Assert.Equal(["country", "educationSystem", "levelBand", "goal", "timeZone", "gradeScale"], ctx.Inferred);
    }

    [Fact]
    public void Ancien_profil_anglophone()
    {
        var ctx = LearningContextRules.Resolve(Student(u => { u.SubSystem = SchoolSubSystem.Anglophone; u.Level = "Upper Sixth"; }));
        Assert.Equal("CM", ctx.Country);
        Assert.Equal("Sous-système anglophone", ctx.EducationSystem);
        Assert.Equal(LevelBand.UpperSecondary, ctx.LevelBand);
        Assert.Null(ctx.Goal);
    }

    [Fact]
    public void Profil_vide_reste_neutre()
    {
        // Aucun pays imposé : rien ne dit que la personne est au Cameroun
        var ctx = LearningContextRules.Resolve(Student());
        Assert.Null(ctx.Country);
        Assert.Null(ctx.EducationSystem);
        Assert.Null(ctx.LevelBand);
        Assert.Null(ctx.Goal);
        Assert.Null(ctx.TimeZone);
        Assert.Equal(LearningContextRules.DefaultGradeScale, ctx.GradeScale);
        Assert.Equal(["gradeScale"], ctx.Inferred);
    }

    [Theory]
    [InlineData("Baccalauréat", null)]   // aussi français, sénégalais… : ne dit pas le pays
    [InlineData("GCE O Level", "CM")]
    [InlineData("Probatoire C", "CM")]
    [InlineData("EXANI-II", null)]
    public void Pays_deduit_seulement_d_un_examen_propre_au_cameroun(string exam, string? country) =>
        Assert.Equal(country, LearningContextRules.Resolve(Student(u => u.ExamTarget = exam)).Country);

    [Fact]
    public void Les_choix_explicites_l_emportent()
    {
        var ctx = LearningContextRules.Resolve(Student(u =>
        {
            u.SubSystem = SchoolSubSystem.Francophone;   // ancien champ, toujours rempli
            u.ExamTarget = "BEPC";
            u.Country = "MX";
            u.EducationSystem = "SEP";
            u.LevelBand = LevelBand.UpperSecondary;
            u.LearningGoal = LearningGoal.Curiosity;
            u.TimeZone = "America/Monterrey";
        }));

        Assert.Equal("MX", ctx.Country);
        Assert.Equal("SEP", ctx.EducationSystem);
        Assert.Equal(LevelBand.UpperSecondary, ctx.LevelBand);
        Assert.Equal(LearningGoal.Curiosity, ctx.Goal);
        Assert.Equal("America/Monterrey", ctx.TimeZone);
        Assert.Equal(GradeScale.Over10, ctx.GradeScale);   // pas choisie : celle du Mexique
        Assert.Equal(["gradeScale"], ctx.Inferred);
    }

    [Fact]
    public void Autre_pays_arrete_les_deductions_camerounaises()
    {
        var ctx = LearningContextRules.Resolve(Student(u => { u.SubSystem = SchoolSubSystem.Francophone; u.Country = LearningContextRules.OtherCountry; }));
        Assert.Equal("ZZ", ctx.Country);
        Assert.Null(ctx.EducationSystem);
        Assert.Null(ctx.TimeZone);
        Assert.Equal(LearningContextRules.DefaultGradeScale, ctx.GradeScale);
    }

    [Theory]
    [InlineData("6ème", null, LevelBand.LowerSecondary)]
    [InlineData("Form 3", null, LevelBand.LowerSecondary)]
    [InlineData("Tle C", null, LevelBand.UpperSecondary)]
    [InlineData("1ère D", null, LevelBand.UpperSecondary)]
    [InlineData("Lower Sixth", null, LevelBand.UpperSecondary)]
    [InlineData("Licence 2", null, LevelBand.Higher)]
    [InlineData("BTS", null, LevelBand.Higher)]
    [InlineData("CM2", null, LevelBand.Primary)]
    [InlineData("tercero de secundaria", null, LevelBand.LowerSecondary)]
    [InlineData("Preparatoria", null, LevelBand.UpperSecondary)]
    [InlineData("Niveau inconnu", EducationStage.Higher, LevelBand.Higher)]
    [InlineData("13e arrondissement", null, null)]
    [InlineData(null, EducationStage.Secondary, null)]
    public void Tranche_deduite_du_niveau_libre(string? level, EducationStage? stage, LevelBand? expected) =>
        Assert.Equal(expected, LearningContextRules.BandOf(level, stage));

    [Fact]
    public void Un_enseignant_n_a_pas_de_tranche_deduite()
    {
        var teacher = new AppUser { Username = "prof", PasswordHash = "x", Role = UserRole.Teacher, Level = "Terminale" };
        Assert.Null(LearningContextRules.Resolve(teacher).LevelBand);
    }

    [Fact]
    public void Chaque_prereglage_est_coherent()
    {
        Assert.Contains(LearningContextRules.Presets, p => p.Code == "CM");
        Assert.Equal(LearningContextRules.Presets.Count, LearningContextRules.Presets.Select(p => p.Code).Distinct().Count());
        foreach (var preset in LearningContextRules.Presets)
        {
            Assert.True(LearningContextRules.IsCountryCode(preset.Code), preset.Code);
            Assert.True(LearningContextRules.IsTimeZone(preset.TimeZone), preset.TimeZone);
            Assert.NotEqual(LearningContextRules.OtherCountry, preset.Code);
        }
    }

    // ─── Modification depuis « Mon profil » ─────────────────────────────────────

    [Fact]
    public void Le_profil_enregistre_le_contexte_choisi()
    {
        var user = Student(u => u.SubSystem = SchoolSubSystem.Francophone);
        var error = ProfileRules.Apply(user, new EditProfileRequest(null,
            Country: "mx", TimeZone: "America/Mexico_City", GradeScale: GradeScale.Over10, EducationSystem: " SEP ",
            LevelBand: LevelBand.UpperSecondary, Goal: LearningGoal.Skill));

        Assert.Null(error);
        Assert.Equal("MX", user.Country);
        Assert.Equal("America/Mexico_City", user.TimeZone);
        Assert.Equal(GradeScale.Over10, user.GradeScale);
        Assert.Equal("SEP", user.EducationSystem);
        Assert.Equal(LevelBand.UpperSecondary, user.LevelBand);
        Assert.Equal(LearningGoal.Skill, user.LearningGoal);
        Assert.Equal(SchoolSubSystem.Francophone, user.SubSystem);   // l'ancien champ n'est pas effacé
    }

    [Fact]
    public void Une_chaine_vide_rend_la_valeur_a_nouveau_deduite()
    {
        var user = Student(u => { u.SubSystem = SchoolSubSystem.Francophone; u.Country = "FR"; u.TimeZone = "Europe/Paris"; });
        Assert.Null(ProfileRules.Apply(user, new EditProfileRequest(null, Country: "", TimeZone: "")));
        Assert.Null(user.Country);
        Assert.Equal("CM", LearningContextRules.Resolve(user).Country);
        Assert.Equal("Africa/Douala", LearningContextRules.Resolve(user).TimeZone);
    }

    [Theory]
    [InlineData("Cameroun", null, "Pays inconnu")]
    [InlineData("C1", null, "Pays inconnu")]
    [InlineData(null, "Mars/Olympus", "Fuseau horaire inconnu")]
    [InlineData(null, "UTC", "Fuseau horaire inconnu")]
    public void Valeurs_refusees(string? country, string? timeZone, string message)
    {
        var user = Student();
        Assert.StartsWith(message, ProfileRules.Apply(user, new EditProfileRequest(null, Country: country, TimeZone: timeZone)));
        Assert.Null(user.Country);
        Assert.Null(user.TimeZone);
    }

    [Fact]
    public void Tranche_et_objectif_reserves_a_l_eleve()
    {
        var parent = new AppUser { Username = "papa", PasswordHash = "x", Role = UserRole.Parent };
        Assert.Null(ProfileRules.Apply(parent, new EditProfileRequest(null, Country: "CM", LevelBand: LevelBand.Primary, Goal: LearningGoal.Exam)));
        Assert.Equal("CM", parent.Country);
        Assert.Null(parent.LevelBand);
        Assert.Null(parent.LearningGoal);
    }

    [Fact]
    public void Le_profil_expose_le_contexte_comme_l_interface_le_lit()
    {
        // Même sérialisation que le service User (énumérations en texte, camelCase) et que l'interface
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var dto = new UserProfileDto(Student(u => { u.SubSystem = SchoolSubSystem.Anglophone; u.Level = "Form 5"; }));
        var json = JsonSerializer.Serialize(dto, options);

        Assert.Contains("\"learningContext\":{\"country\":\"CM\"", json);
        var profile = JsonSerializer.Deserialize<GPOE26.UI.Models.UserProfile>(json, options)!;
        Assert.Equal("CM", profile.LearningContext!.Country);
        Assert.Equal(GPOE26.UI.Models.LevelBand.LowerSecondary, profile.LearningContext.LevelBand);
        Assert.Contains("country", profile.LearningContext.Inferred!);
    }
}
