using System.Net;
using System.Security.Claims;
using System.Text;
using GPOE26.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using User.Model;

namespace GPOE26.Tests;

/// <summary>
/// La règle enseignant, celle que <see cref="StudyIdentity.CanViewStudent"/> ne connaît
/// délibérément pas.
///
/// Ce qui se joue ici : un enseignant ne tient pas dans un jeton. Cent cinquante élèves
/// ne se portent pas en claim, donc la réponse vient d'un annuaire — et un contrôle
/// d'accès qui dépend du réseau doit dire ce qu'il fait quand le réseau se tait. C'est
/// le vrai sujet de ce fichier : pas « l'enseignant voit-il sa classe », qui est facile,
/// mais « que se passe-t-il quand on ne peut pas savoir ».
///
/// Ces tests ne touchent ni base ni service : l'annuaire est interrogé à travers un
/// <see cref="StubHandler"/> qui compte les appels, ce qui permet aussi de vérifier la
/// mise en cache — un enseignant de trente élèves doit produire UN appel, pas trente.
/// </summary>
public class TeacherAccessTests
{
    // ── Montage ───────────────────────────────────────────────────────────────

    private static ClaimsPrincipal Teacher(Guid? id = null) =>
        Principal(id ?? Guid.NewGuid(), "Teacher");

    private static ClaimsPrincipal Student(Guid id) => Principal(id, "Student");

    private static ClaimsPrincipal Parent(Guid id, params Guid[] children)
    {
        var principal = Principal(id, "Parent");
        ((ClaimsIdentity)principal.Identity!).AddClaim(
            new Claim(StudyIdentity.ChildrenClaim, string.Join(',', children)));
        return principal;
    }

    private static ClaimsPrincipal Principal(Guid id, string role) =>
        new(new ClaimsIdentity(
            [new Claim("sub", id.ToString()), new Claim(StudyIdentity.RoleClaim, role)],
            "Test"));

    /// <summary>
    /// Raccourci qui passe le jeton d'annulation de xunit. Sans lui, chaque appel de
    /// <c>CanViewAsync</c> traînerait un troisième argument qui n'apprend rien au lecteur.
    /// </summary>
    private static Task<bool> CanView(StudentDirectory directory, ClaimsPrincipal principal, Guid studentId) =>
        directory.CanViewAsync(principal, studentId, TestContext.Current.CancellationToken);

    /// <summary>
    /// Un annuaire en mémoire. <paramref name="roster"/> null simule un service User
    /// muet — panne, timeout, 500 : de l'extérieur c'est la même chose.
    /// </summary>
    private static (StudentDirectory Directory, StubHandler Handler) Build(
        IEnumerable<Guid>? roster, string authorization = "Bearer jeton-de-test")
    {
        var handler = new StubHandler(roster);

        var context = new DefaultHttpContext();
        if (authorization.Length > 0)
            context.Request.Headers.Authorization = authorization;

        var accessor = new HttpContextAccessor { HttpContext = context };

        var directory = new StudentDirectory(
            new StubClientFactory(handler),
            accessor,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<StudentDirectory>.Instance);

        return (directory, handler);
    }

    // ── Ce que l'enseignant voit, et ce qu'il ne voit pas ─────────────────────

    [Fact]
    public async Task Un_enseignant_voit_un_eleve_de_sa_classe()
    {
        var eleve = Guid.NewGuid();
        var (directory, _) = Build([eleve]);

        Assert.True(await CanView(directory, Teacher(), eleve));
    }

    [Fact]
    public async Task Un_enseignant_ne_voit_pas_l_eleve_d_une_autre_classe()
    {
        var (directory, _) = Build([Guid.NewGuid(), Guid.NewGuid()]);

        Assert.False(await CanView(directory, Teacher(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Un_enseignant_sans_effectif_ne_voit_personne()
    {
        var (directory, _) = Build([]);

        Assert.False(await CanView(directory, Teacher(), Guid.NewGuid()));
    }

    /// <summary>
    /// Le point qui fait tenir tout le reste : le rôle ne donne rien par lui-même.
    /// C'est l'effectif renvoyé par le service User — qui lit l'identité du porteur du
    /// jeton — qui décide. Un élève qui réclamerait « Teacher » interrogerait l'annuaire
    /// sous sa propre identité et n'obtiendrait aucun effectif.
    /// </summary>
    [Fact]
    public async Task Reclamer_le_role_enseignant_ne_donne_acces_a_rien()
    {
        var imposteur = Guid.NewGuid();
        var cible = Guid.NewGuid();

        // Le jeton dit « Teacher », l'annuaire ne lui connaît aucun élève.
        var (directory, handler) = Build([]);
        var principal = Principal(imposteur, "Teacher");

        Assert.False(await CanView(directory, principal, cible));
        Assert.Equal(1, handler.Calls); // Il a bien fallu demander : rien n'est présumé.
    }

    /// <summary>
    /// La fonction pure ne bouge pas : un enseignant n'y gagne aucun droit. C'est la
    /// preuve que l'étape n'a pas élargi l'autorisation existante, seulement ajouté une
    /// porte à côté.
    /// </summary>
    [Fact]
    public void CanViewStudent_ignore_toujours_les_enseignants()
    {
        Assert.False(Teacher().CanViewStudent(Guid.NewGuid()));
    }

    [Fact]
    public void Un_enseignant_est_reconnu_par_son_role_et_lui_seul()
    {
        Assert.True(Teacher().IsTeacher());
        Assert.False(Student(Guid.NewGuid()).IsTeacher());
        Assert.False(Parent(Guid.NewGuid()).IsTeacher());
    }

    // ── Ce qui ne passe pas par le réseau ─────────────────────────────────────

    /// <summary>
    /// Un élève et un parent se décident sur le jeton seul. Si l'annuaire était consulté
    /// pour eux, chaque page de l'espace famille coûterait un appel de plus — et une
    /// panne de User casserait un chemin qui n'en a pas besoin.
    /// </summary>
    [Fact]
    public async Task Un_eleve_et_son_parent_ne_declenchent_aucun_appel()
    {
        var enfant = Guid.NewGuid();
        var (directory, handler) = Build([enfant]);

        Assert.True(await CanView(directory, Student(enfant), enfant));
        Assert.True(await CanView(directory, Parent(Guid.NewGuid(), enfant), enfant));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Un_eleve_qui_en_demande_un_autre_est_refuse_sans_appel()
    {
        var (directory, handler) = Build([Guid.NewGuid()]);

        Assert.False(await CanView(directory, Student(Guid.NewGuid()), Guid.NewGuid()));
        Assert.Equal(0, handler.Calls);
    }

    // ── Échelle : un appel pour toute la classe ───────────────────────────────

    /// <summary>
    /// La vérification d'échelle demandée par le plan. Trente élèves consultés d'affilée
    /// ne doivent produire qu'un seul appel : l'annuaire ramène l'union des effectifs,
    /// pas une réponse par élève.
    /// </summary>
    [Fact]
    public async Task Une_classe_de_trente_ne_produit_qu_un_seul_appel()
    {
        var classe = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToList();
        var (directory, handler) = Build(classe);
        var enseignant = Teacher();

        foreach (var eleve in classe)
            Assert.True(await CanView(directory, enseignant, eleve));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Le_cache_est_propre_a_chaque_enseignant()
    {
        var mien = Guid.NewGuid();
        var (directory, handler) = Build([mien]);

        Assert.True(await CanView(directory, Teacher(), mien));

        // Un autre enseignant : le stub renvoie le même effectif, mais l'appel doit
        // repartir. Une clé de cache commune ferait voir à l'un les élèves de l'autre.
        Assert.True(await CanView(directory, Teacher(), mien));
        Assert.Equal(2, handler.Calls);
    }

    // ── Annuaire muet ─────────────────────────────────────────────────────────

    /// <summary>
    /// La question qui compte. Une autorisation qui s'ouvre quand elle ne peut pas
    /// vérifier n'est pas une autorisation — c'est un trou qui ne se manifeste que le
    /// jour d'une panne, quand plus personne ne regarde.
    /// </summary>
    [Fact]
    public async Task Un_annuaire_muet_refuse_l_acces()
    {
        var (directory, _) = Build(roster: null);

        Assert.False(await CanView(directory, Teacher(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Un_annuaire_muet_ne_casse_pas_l_acces_de_l_eleve()
    {
        // La panne ne doit atteindre que le chemin qui en dépend.
        var soi = Guid.NewGuid();
        var (directory, _) = Build(roster: null);

        Assert.True(await CanView(directory, Student(soi), soi));
    }

    /// <summary>
    /// Un échec n'est pas mémorisé : une coupure d'une seconde ne doit pas refuser
    /// l'accès pendant les cinq minutes du cache.
    /// </summary>
    [Fact]
    public async Task Un_echec_n_est_pas_mis_en_cache()
    {
        var eleve = Guid.NewGuid();
        var handler = new StubHandler(roster: null);
        var directory = DirectoryOver(handler);
        var enseignant = Teacher();

        Assert.False(await CanView(directory, enseignant, eleve));

        // Le service revient.
        handler.Roster = [eleve];
        Assert.True(await CanView(directory, enseignant, eleve));

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Un_succes_est_mis_en_cache()
    {
        var eleve = Guid.NewGuid();
        var handler = new StubHandler([eleve]);
        var directory = DirectoryOver(handler);
        var enseignant = Teacher();

        Assert.True(await CanView(directory, enseignant, eleve));

        // L'élève quitte la classe. La révocation est différée — c'est assumé, et c'est
        // le prix du cache. Le test le fige pour que personne ne le découvre en prod.
        handler.Roster = [];
        Assert.True(await CanView(directory, enseignant, eleve));

        Assert.Equal(1, handler.Calls);
    }

    // ── Le jeton relayé ───────────────────────────────────────────────────────

    /// <summary>
    /// L'annuaire relaie le jeton de l'appelant : c'est User qui décide quels élèves
    /// lui appartiennent. Un appel sous une identité de service permettrait de demander
    /// l'effectif de n'importe quel enseignant.
    /// </summary>
    [Fact]
    public async Task Le_jeton_de_l_appelant_est_relaye_tel_quel()
    {
        var eleve = Guid.NewGuid();
        var (directory, handler) = Build([eleve], authorization: "Bearer jeton-precis");

        await CanView(directory, Teacher(), eleve);

        Assert.Equal("Bearer", handler.LastScheme);
        Assert.Equal("jeton-precis", handler.LastParameter);
    }

    [Fact]
    public async Task Sans_jeton_a_relayer_l_acces_est_refuse()
    {
        var eleve = Guid.NewGuid();
        var (directory, handler) = Build([eleve], authorization: "");

        Assert.False(await CanView(directory, Teacher(), eleve));
        Assert.Equal(0, handler.Calls); // Inutile d'appeler : la réponse serait anonyme.
    }

    [Fact]
    public async Task Un_enseignant_sans_identifiant_ne_voit_personne()
    {
        var (directory, handler) = Build([Guid.NewGuid()]);
        var sansSub = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(StudyIdentity.RoleClaim, "Teacher")], "Test"));

        Assert.Empty(await directory.RosterAsync(sansSub, TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    // ── Outils ────────────────────────────────────────────────────────────────

    private static StudentDirectory DirectoryOver(StubHandler handler)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer jeton-de-test";

        return new StudentDirectory(
            new StubClientFactory(handler),
            new HttpContextAccessor { HttpContext = context },
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<StudentDirectory>.Instance);
    }

    private sealed class StubClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://user.test") };
    }

    /// <summary>
    /// Le service User, réduit à ce dont l'annuaire a besoin. <c>Roster</c> à null =
    /// service muet.
    /// </summary>
    private sealed class StubHandler(IEnumerable<Guid>? roster) : HttpMessageHandler
    {
        public IEnumerable<Guid>? Roster { get; set; } = roster;

        public int Calls { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastScheme { get; private set; }
        public string? LastParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = request.RequestUri?.AbsolutePath;
            LastScheme = request.Headers.Authorization?.Scheme;
            LastParameter = request.Headers.Authorization?.Parameter;

            if (Roster is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var json = "[" + string.Join(',', Roster.Select(id => $"\"{id}\"")) + "]";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}

/// <summary>
/// Le code de classe. Il n'a de valeur que par ce qui le distingue du code parent :
/// celui-ci sera dicté devant trente élèves et vivra une année, là où le code parent
/// vaut trente minutes et un usage.
/// </summary>
public class SchoolClassCodeTests
{
    [Fact]
    public void Un_code_de_classe_fait_six_caracteres()
    {
        Assert.Equal(SchoolClass.CodeLength, SchoolClass.NewCode().Length);
    }

    /// <summary>
    /// Aucun caractère ambigu : 0/O et 1/I/L se confondent à l'oral comme au tableau, et
    /// une seule ambiguïté coûte trente mains levées.
    /// </summary>
    [Fact]
    public void Un_code_de_classe_n_a_aucun_caractere_ambigu()
    {
        // Cent tirages : assez pour qu'un caractère interdit sorte s'il est dans l'alphabet.
        for (var i = 0; i < 100; i++)
        {
            var code = SchoolClass.NewCode();

            Assert.DoesNotContain(code, c => "OIL01".Contains(c));
            Assert.All(code, c => Assert.True(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)));
        }
    }

    [Fact]
    public void Deux_codes_tires_de_suite_different()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => SchoolClass.NewCode()).ToHashSet();
        Assert.Equal(50, codes.Count);
    }

    /// <summary>
    /// La différence de nature avec le code parent, figée par un test parce qu'elle est
    /// facile à effacer par mégarde en « harmonisant » les deux.
    /// </summary>
    [Fact]
    public void Un_code_de_classe_reste_utilisable_apres_un_premier_eleve()
    {
        var classe = new SchoolClass
        {
            Name = "Terminale C",
            Subject = "Mathématiques",
            SchoolYear = "2025-2026",
            Code = SchoolClass.NewCode(),
        };

        Assert.True(classe.AcceptsJoin);

        classe.Enrollments.Add(new ClassEnrollment { StudentId = Guid.NewGuid() });
        Assert.True(classe.AcceptsJoin);

        // Mais il se coupe en une action : un code affiché en classe finit par en sortir.
        classe.JoinEnabled = false;
        Assert.False(classe.AcceptsJoin);
    }
}
