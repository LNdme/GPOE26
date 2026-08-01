using System.Security.Claims;
using Cours.Model;
using Cours.Service;
using GPOE26.ServiceDefaults;

namespace GPOE26.Tests;

/// <summary>
/// Les règles déterministes du suivi d'étude : qui a le droit de voir quoi, où s'arrête
/// une séance de révision, et de quoi un parcours est fait.
///
/// Ces trois-là se testent sans base de données ni appel de modèle — et elles doivent
/// l'être : ce sont celles dont une erreur ne se voit pas à l'écran. Une séance mal
/// découpée donne un chiffre plausible mais faux ; une autorisation trop large ne
/// produit aucun message d'erreur, juste l'accès aux données d'un enfant qui n'est pas
/// le sien.
/// </summary>
public class StudyIdentityTests
{
    private static ClaimsPrincipal Student(Guid id) =>
        Principal(id, "Student", children: null);

    private static ClaimsPrincipal Parent(Guid id, params Guid[] children) =>
        Principal(id, "Parent", string.Join(',', children));

    private static ClaimsPrincipal Principal(Guid id, string role, string? children)
    {
        var claims = new List<Claim>
        {
            new("sub", id.ToString()),
            new(StudyIdentity.RoleClaim, role),
        };

        if (children is not null)
            claims.Add(new Claim(StudyIdentity.ChildrenClaim, children));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public void Un_eleve_voit_ses_propres_donnees()
    {
        var id = Guid.NewGuid();
        Assert.True(Student(id).CanViewStudent(id));
    }

    [Fact]
    public void Un_eleve_ne_voit_pas_un_autre_eleve()
    {
        Assert.False(Student(Guid.NewGuid()).CanViewStudent(Guid.NewGuid()));
    }

    [Fact]
    public void Un_parent_voit_l_enfant_qui_s_est_rattache_a_lui()
    {
        var child = Guid.NewGuid();
        Assert.True(Parent(Guid.NewGuid(), child).CanViewStudent(child));
    }

    [Fact]
    public void Un_parent_ne_voit_pas_l_enfant_d_un_autre()
    {
        var mine = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        Assert.False(Parent(Guid.NewGuid(), mine).CanViewStudent(someoneElse));
    }

    /// <summary>
    /// Le claim « children » ne suffit pas : sans le rôle Parent, il ne donne rien. Un
    /// jeton bricolé ne doit pas ouvrir de porte que le rôle n'ouvrirait pas.
    /// </summary>
    [Fact]
    public void Le_claim_children_sans_le_role_parent_ne_donne_rien()
    {
        var child = Guid.NewGuid();
        var principal = Principal(Guid.NewGuid(), "Student", child.ToString());

        Assert.False(principal.CanViewStudent(child));
    }

    [Fact]
    public void Un_parent_avec_plusieurs_enfants_les_voit_tous()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var parent = Parent(Guid.NewGuid(), first, second);

        Assert.True(parent.CanViewStudent(first));
        Assert.True(parent.CanViewStudent(second));
        Assert.Equal(2, parent.GetLinkedChildren().Count);
    }

    [Fact]
    public void ResolveStudentId_renvoie_soi_meme_quand_rien_n_est_demande()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, Student(id).ResolveStudentId(null));
    }

    [Fact]
    public void ResolveStudentId_refuse_un_eleve_hors_du_lien()
    {
        var parent = Parent(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(parent.ResolveStudentId(Guid.NewGuid()));
    }

    [Fact]
    public void Un_jeton_sans_identifiant_ne_resout_rien()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Null(anonymous.GetUserId());
        Assert.Null(anonymous.ResolveStudentId(Guid.NewGuid()));
    }
}

/// <summary>
/// Le découpage des séances de révision. Ce que mesure ActiveSeconds, c'est du temps
/// de travail — pas la durée pendant laquelle un onglet est resté ouvert.
/// </summary>
public class StudySessionTests
{
    [Fact]
    public void Une_seance_reste_ouverte_avant_le_delai_d_inactivite()
    {
        var now = DateTime.UtcNow;
        var session = new StudySession { LastActivityAt = now.AddMinutes(-29) };

        Assert.True(session.IsOpenAt(now));
    }

    [Fact]
    public void Une_seance_se_ferme_apres_trente_minutes_de_silence()
    {
        var now = DateTime.UtcNow;
        var session = new StudySession { LastActivityAt = now.AddMinutes(-31) };

        Assert.False(session.IsOpenAt(now));
    }

    [Fact]
    public void Le_delai_d_inactivite_est_de_trente_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), StudySession.IdleTimeout);
    }

    /// <summary>
    /// La règle qui empêche un onglet oublié de compter une nuit de révision : le crédit
    /// accordé entre deux signaux est plafonné, quel que soit l'écart réel.
    /// </summary>
    [Theory]
    [InlineData(60, 60)]      // rythme normal : un signal par minute
    [InlineData(90, 90)]      // à la limite
    [InlineData(1_200, 90)]   // machine en veille vingt minutes : plafonné
    [InlineData(-5, 0)]       // horloge qui recule : jamais négatif
    public void Le_credit_entre_deux_signaux_est_plafonne(int elapsedSeconds, int expected)
    {
        var last = DateTime.UtcNow;
        var session = new StudySession { LastActivityAt = last };

        Assert.Equal(expected, session.CreditFor(last.AddSeconds(elapsedSeconds)));
    }

    // ── Rejeu d'une période hors ligne ────────────────────────────────────────

    /// <summary>
    /// Le défaut que le hors ligne met au jour : sans horodatage propre, un signal
    /// d'hier soir rejoué ce matin compterait toute la nuit.
    /// </summary>
    [Fact]
    public void Un_signal_rejoue_garde_l_heure_ou_il_a_eu_lieu()
    {
        var now = DateTime.UtcNow;
        var hierSoir = now.AddHours(-14);

        Assert.Equal(hierSoir, StudySession.ResolveSignalTime(hierSoir, now));
    }

    [Fact]
    public void Un_signal_sans_horodatage_est_date_de_maintenant()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(now, StudySession.ResolveSignalTime(null, now));
    }

    /// <summary>
    /// Un horodatage dans le futur ne peut venir que d'une horloge déréglée ou d'une
    /// tentative de gonfler un temps de travail : on le remplace par l'heure réelle.
    /// </summary>
    [Fact]
    public void Un_horodatage_dans_le_futur_est_refuse()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(now, StudySession.ResolveSignalTime(now.AddHours(3), now));
    }

    [Fact]
    public void Un_leger_decalage_d_horloge_reste_accepte()
    {
        // Trente secondes d'avance sont ordinaires entre deux machines : les refuser
        // ferait retomber tous les signaux d'un appareil mal réglé sur l'heure serveur.
        var now = DateTime.UtcNow;
        var slightlyAhead = now.AddSeconds(30);

        Assert.Equal(slightlyAhead, StudySession.ResolveSignalTime(slightlyAhead, now));
    }

    /// <summary>
    /// Une soirée de travail hors ligne, rejouée le lendemain : les signaux se suivent à
    /// la minute, donc la séance reste une seule séance et la durée reflète le travail
    /// réel — pas les quatorze heures écoulées jusqu'à la synchronisation.
    /// </summary>
    [Fact]
    public void Une_soiree_hors_ligne_rejouee_donne_une_seance_de_la_bonne_duree()
    {
        var soiree = DateTime.UtcNow.AddHours(-14);
        var session = new StudySession { StartedAt = soiree, LastActivityAt = soiree };

        // Vingt signaux d'une minute, comme les émet la page pendant la lecture.
        for (var minute = 1; minute <= 20; minute++)
        {
            var at = soiree.AddMinutes(minute);

            Assert.True(session.IsOpenAt(at), "La séance ne doit pas se refermer entre deux signaux d'une minute.");

            session.ActiveSeconds += session.CreditFor(at);
            session.LastActivityAt = at;
        }

        Assert.Equal(20 * 60, session.ActiveSeconds);
    }

    /// <summary>
    /// Deux séances séparées par un sommeil restent deux séances, même rejouées d'un
    /// seul lot : c'est l'heure déclarée qui décide, pas l'ordre d'arrivée.
    /// </summary>
    [Fact]
    public void Deux_periodes_hors_ligne_eloignees_restent_deux_seances()
    {
        var soir = DateTime.UtcNow.AddHours(-14);
        var matin = DateTime.UtcNow.AddHours(-2);

        var session = new StudySession { StartedAt = soir, LastActivityAt = soir.AddMinutes(20) };

        Assert.False(session.IsOpenAt(matin));
    }

    [Fact]
    public void Une_heure_d_onglet_inactif_ne_credite_rien()
    {
        // Aucun signal n'est émis quand l'onglet n'est pas visible : sans signal, pas de
        // crédit. La séance se sera même refermée d'elle-même entre-temps.
        var now = DateTime.UtcNow;
        var session = new StudySession { LastActivityAt = now.AddHours(-1), ActiveSeconds = 600 };

        Assert.False(session.IsOpenAt(now));
        Assert.Equal(600, session.ActiveSeconds);
    }
}

/// <summary>
/// La construction du parcours : deux règles de décision, aucune dépendance.
/// </summary>
public class CourseJourneyBuilderTests
{
    private static string LongCourse(int parts)
    {
        var markdown = new System.Text.StringBuilder("# Cours\n\n");

        for (var i = 1; i <= parts; i++)
        {
            markdown.AppendLine($"## Partie {i}");
            markdown.AppendLine(new string('x', 2_000));
            markdown.AppendLine();
        }

        return markdown.ToString();
    }

    [Fact]
    public void Un_cours_court_se_lit_d_une_traite()
    {
        var (mode, _) = CourseJourneyBuilder.Build(Guid.NewGuid(), "# Cours\n\n## A\ntexte\n\n## B\ntexte");
        Assert.Equal(JourneyMode.Whole, mode);
    }

    [Fact]
    public void Un_cours_long_et_decoupe_se_lit_partie_par_partie()
    {
        var (mode, steps) = CourseJourneyBuilder.Build(Guid.NewGuid(), LongCourse(5));

        Assert.Equal(JourneyMode.PerSection, mode);
        Assert.Equal(5, steps.Count(s => s.Kind == StepKind.Lecture));
        Assert.Equal(5, steps.Count(s => s.Kind == StepKind.MiniTest));
    }

    /// <summary>
    /// Les deux conditions valent ensemble : quatre titres sur une page se lisent d'une
    /// traite, et découper n'ajouterait que des clics.
    /// </summary>
    [Fact]
    public void Quatre_titres_mais_une_page_ne_declenchent_pas_le_decoupage()
    {
        var markdown = "# Cours\n\n## A\ncourt\n\n## B\ncourt\n\n## C\ncourt\n\n## D\ncourt";
        var (mode, _) = CourseJourneyBuilder.Build(Guid.NewGuid(), markdown);

        Assert.Equal(JourneyMode.Whole, mode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void La_question_de_synthese_termine_toujours_le_parcours(bool longCourse)
    {
        var markdown = longCourse ? LongCourse(5) : "# Cours\n\n## A\ntexte";
        var (_, steps) = CourseJourneyBuilder.Build(Guid.NewGuid(), markdown);

        Assert.Equal(StepKind.Synthese, steps[^1].Kind);
        Assert.Single(steps, s => s.Kind == StepKind.Synthese);
    }

    [Fact]
    public void Seule_la_premiere_etape_est_ouverte()
    {
        var (_, steps) = CourseJourneyBuilder.Build(Guid.NewGuid(), LongCourse(5));

        Assert.Equal(StepStatus.Available, steps[0].Status);
        Assert.All(steps.Skip(1), s => Assert.Equal(StepStatus.Locked, s.Status));
    }

    [Fact]
    public void Une_etape_de_lecture_ne_se_valide_pas_par_un_score()
    {
        var (_, steps) = CourseJourneyBuilder.Build(Guid.NewGuid(), LongCourse(5));

        Assert.False(steps.First(s => s.Kind == StepKind.Lecture).IsEvaluated);
        Assert.True(steps.First(s => s.Kind == StepKind.Synthese).IsEvaluated);
    }

    [Fact]
    public void La_synthese_n_est_pas_un_QCM()
    {
        Assert.Equal(0, CourseJourneyBuilder.QuestionCountFor(StepKind.Synthese));
        Assert.Equal(0, CourseJourneyBuilder.QuestionCountFor(StepKind.ExerciceOuvert));
        Assert.True(CourseJourneyBuilder.QuestionCountFor(StepKind.TestFinal) > 0);
    }
}
