using GPOE26.ApiService.Model;
using Microsoft.EntityFrameworkCore;

namespace GPOE26.ApiService.Data;

/// <summary>Nombre d'éléments ajoutés par <see cref="DemoContent.AddMissingAsync"/>.</summary>
public record DemoContentResult(int Team, int News, int Events, int Activities)
{
    public int Total => Team + News + Events + Activities;
}

/// <summary>
/// Contenu fictif pour présenter le site : équipe éducative, actualités, événements et activités.
/// Ajouté sur une base neuve, ou à la demande d'un administrateur depuis le tableau de bord.
/// Un élément déjà présent (même nom ou même titre) n'est jamais recréé ni modifié :
/// relancer l'ajout complète le site sans doublon et sans toucher aux contenus de l'établissement.
/// Les personnes citées sont imaginaires.
/// </summary>
public static class DemoContent
{
    public static async Task<DemoContentResult> AddMissingAsync(ApiServiceContext db, DateTime? now = null)
    {
        var today = (now ?? DateTime.UtcNow).Date;
        DateTime Day(int offset, int hour = 8) => DateTime.SpecifyKind(today.AddDays(offset).AddHours(hour), DateTimeKind.Utc);

        var teamNames = await db.Hierarchies.Select(h => (h.Name ?? "") + "|" + (h.PreName ?? "")).ToListAsync();
        var team = Team().Where(m => !teamNames.Contains((m.Name ?? "") + "|" + (m.PreName ?? ""))).ToList();

        var slugs = await db.NewsArticles.Select(n => n.Slug).ToListAsync();
        var titles = await db.NewsArticles.Select(n => n.Title).ToListAsync();
        var news = News(Day).Where(n => !slugs.Contains(n.Slug) && !titles.Contains(n.Title)).ToList();

        var eventTitles = await db.Events.Select(e => e.Title).ToListAsync();
        var events = Events(Day).Where(e => !eventTitles.Contains(e.Title)).ToList();

        var activityNames = await db.Activities.Select(a => a.Name).ToListAsync();
        var activities = Activities().Where(a => !activityNames.Contains(a.Name)).ToList();

        db.Hierarchies.AddRange(team);
        db.NewsArticles.AddRange(news);
        db.Events.AddRange(events);
        db.Activities.AddRange(activities);
        await db.SaveChangesAsync();
        return new DemoContentResult(team.Count, news.Count, events.Count, activities.Count);
    }

    private static Hierarchy Member(string role, string name, string preName, string department, string? specialization,
        string description, string? citation = null) => new()
    {
        Role = role, Name = name, PreName = preName, Department = department, Specialization = specialization,
        Description = description, Citation = citation
    };

    public static IEnumerable<Hierarchy> Team() =>
    [
        Member("Administration", "M. DONGMO", "Jean", "Direction", "Proviseur",
            "Dirige l'établissement depuis 2019 et coordonne les projets numériques du lycée.",
            "Un élève qui comprend ce qu'il fait n'oublie pas ce qu'il a appris."),
        Member("Administration", "Mme. ABOMO", "Marie", "Pédagogie", "Censeure",
            "Organise les emplois du temps, les examens blancs et le suivi pédagogique des classes."),
        Member("Administration", "M. ESSOMBA", "Paul", "Vie scolaire", "Surveillant général",
            "Veille à l'assiduité, à la discipline et au bien-être des élèves."),
        Member("Administration", "Mme. NGONO", "Clarisse", "Intendance", "Intendante",
            "Gère le budget, les équipements des ateliers et la restauration scolaire."),
        Member("Enseignants", "M. MBARGA", "Serge", "Génie électrique", "Électrotechnique",
            "Professeur d'électrotechnique, responsable de l'atelier électricité et du club robotique.",
            "On apprend l'électricité en la mesurant, pas en la récitant."),
        Member("Enseignants", "Mme. FOUDA", "Estelle", "Sciences", "Mathématiques",
            "Enseigne les mathématiques en seconde et en terminale, prépare les élèves au baccalauréat."),
        Member("Enseignants", "M. NJOYA", "Ibrahim", "Génie mécanique", "Fabrication mécanique",
            "Forme les élèves à l'usinage, au dessin technique et à la maintenance des machines."),
        Member("Enseignants", "Mme. TCHOUA", "Brigitte", "Lettres", "Français et anglais",
            "Anime le club de lecture et accompagne les élèves dans les épreuves de langues."),
    ];

    private static NewArticle Article(string title, string slug, string category, string excerpt, string content, DateTime publishedAt) => new()
    {
        Title = title, Slug = slug, Category = category, Excerpt = excerpt, Content = content,
        IsPublished = true, PublishedAt = publishedAt, CreatedAt = publishedAt, UpdatedAt = publishedAt
    };

    public static IEnumerable<NewArticle> News(Func<int, int, DateTime> day) =>
    [
        Article("Rentrée scolaire : un premier trimestre bien lancé", "rentree-premier-trimestre", "Administratif",
            "Effectifs, nouveaux enseignants et calendrier du trimestre : le point après la rentrée.",
            "La rentrée s'est déroulée dans de bonnes conditions. Quatre nouvelles classes de seconde ont été ouvertes et deux enseignants ont rejoint l'équipe pédagogique.\n\n" +
            "Le calendrier du premier trimestre est affiché à la vie scolaire et disponible dans l'espace élève de la plateforme. Les conseils de classe se tiendront à la mi-décembre.",
            day(-40, 9)),
        Article("Le club robotique qualifié pour la finale régionale", "club-robotique-finale-regionale", "Culturel",
            "Le robot suiveur de ligne conçu par nos élèves de première a terminé deuxième des qualifications.",
            "Après six semaines de travail en atelier, l'équipe du club robotique a présenté un robot suiveur de ligne entièrement conçu et câblé au lycée.\n\n" +
            "Elle représentera l'établissement à la finale régionale de Bertoua. Bravo aux élèves et à leur encadrant, M. MBARGA.",
            day(-21, 10)),
        Article("Inscriptions aux examens officiels : dernier délai", "inscriptions-examens-officiels", "Administratif",
            "Les candidats au CAP, au probatoire et au baccalauréat doivent compléter leur dossier avant la fin du mois.",
            "Les dossiers d'inscription aux examens officiels sont à déposer au secrétariat. Pièces à fournir : acte de naissance, deux photos d'identité et le reçu des frais d'examen.\n\n" +
            "Aucun dossier incomplet ne sera transmis. Les professeurs principaux accompagnent les élèves qui ont besoin d'aide.",
            day(-12, 8)),
        Article("Tournoi interclasses de football : les résultats", "tournoi-interclasses-football", "Sport",
            "La Terminale F3 remporte le tournoi après une finale disputée face à la Première MEM.",
            "Douze équipes ont participé au tournoi interclasses organisé pendant la semaine sportive. La finale s'est jouée devant tout l'établissement et s'est conclue aux tirs au but.\n\n" +
            "Merci aux enseignants d'EPS et aux élèves arbitres pour l'organisation.",
            day(-8, 16)),
        Article("Cours et devoirs accessibles en ligne pour tous les élèves", "cours-en-ligne-tous-les-eleves", "Général",
            "Chaque élève peut désormais retrouver ses cours, ses devoirs et un tuteur IA dans son espace personnel.",
            "La plateforme GPOE26 réunit les cours partagés par les enseignants, les évaluations à rendre et l'emploi du temps.\n\n" +
            "Les élèves y trouvent aussi un tuteur qui répond à leurs questions sur le cours. Les identifiants sont remis par le professeur principal.",
            day(-4, 11)),
        Article("Semaine de la sécurité électrique dans les ateliers", "semaine-securite-electrique", "Général",
            "Habilitation, consignes et exercices pratiques : une semaine pour adopter les bons réflexes.",
            "Tous les élèves des filières industrielles suivront un module de prévention des risques électriques animé par les professeurs d'atelier.\n\n" +
            "Un exercice d'évacuation est prévu en fin de semaine. Le port des équipements de protection est obligatoire dans les ateliers.",
            day(-1, 9)),
    ];

    private static SchoolEvent Event(string title, string type, string location, DateTime start, DateTime? end, string description) => new()
    {
        Title = title, Type = type, Location = location, StartDate = start, EndDate = end, Description = description, IsPublic = true
    };

    public static IEnumerable<SchoolEvent> Events(Func<int, int, DateTime> day) =>
    [
        Event("Forum des métiers de l'industrie", "Culturel", "Grande salle, LT Nanga-Eboko", day(9, 9), day(9, 16),
            "Des techniciens et ingénieurs de la région présentent leur métier et répondent aux questions des élèves de seconde et de première."),
        Event("Webinaire : réussir son orientation après le bac technique", "Général", "En ligne (lien dans l'espace élève)", day(13, 15), day(13, 17),
            "Une conseillère d'orientation présente les BTS, IUT et écoles d'ingénieurs accessibles après un baccalauréat technique. Questions en direct."),
        Event("Visite virtuelle des ateliers pour les familles", "JPOE", "En ligne (diffusion vidéo)", day(20, 10), day(20, 12),
            "Les professeurs d'atelier font découvrir en vidéo les machines, les projets des élèves et les filières proposées. Ouvert aux familles et futurs élèves."),
        Event("Examen blanc du baccalauréat", "Administratif", "Salles d'examen du lycée", day(27, 7), day(31, 13),
            "Épreuves écrites dans les conditions de l'examen pour toutes les classes de terminale. Le planning détaillé est affiché à la vie scolaire."),
        Event("Finale régionale de robotique", "Culturel", "Lycée technique de Bertoua", day(34, 8), day(34, 18),
            "Le club robotique du lycée défend ses couleurs face aux établissements de la région de l'Est."),
        Event("Semaine culturelle et sportive", "Sportif", "Campus du lycée", day(-10, 8), day(-6, 17),
            "Tournois interclasses, concours de poésie, défilé et journée gastronomique ont rythmé une semaine très suivie."),
    ];

    private static SchoolActivity Activity(string name, string category, string schedule, string responsible, string description) => new()
    {
        Name = name, Category = category, Schedule = schedule, ResponsibleTeacher = responsible, Description = description, IsActive = true
    };

    public static IEnumerable<SchoolActivity> Activities() =>
    [
        Activity("Club robotique", "Science", "Mercredi 14h-17h", "M. MBARGA Serge",
            "Conception et programmation de robots : capteurs, moteurs, cartes Arduino. Les élèves préparent les compétitions régionales."),
        Activity("Laboratoire virtuel d'électricité", "Science", "En ligne, à tout moment", "M. MBARGA Serge",
            "Simulations de circuits sur la plateforme : loi d'Ohm, montages série et parallèle, mesures à l'oscilloscope. Idéal pour réviser avant les travaux pratiques."),
        Activity("Club mathématiques et olympiades", "Science", "Jeudi 15h-17h", "Mme. FOUDA Estelle",
            "Résolution de problèmes, préparation aux olympiades et entraînement au raisonnement pour le baccalauréat."),
        Activity("Football", "Sport", "Mardi et vendredi 15h30-17h30", "Service d'EPS",
            "Entraînements de l'équipe du lycée et tournoi interclasses. Ouvert aux filles et aux garçons."),
        Activity("Handball", "Sport", "Lundi 15h30-17h30", "Service d'EPS",
            "Initiation et perfectionnement, matchs amicaux contre les établissements voisins."),
        Activity("Club de lecture et d'écriture", "Art", "Samedi 9h-11h", "Mme. TCHOUA Brigitte",
            "Lectures partagées, ateliers d'écriture et préparation du concours de poésie de la semaine culturelle."),
        Activity("Atelier dessin technique assisté par ordinateur", "Club", "Mercredi 14h-16h, salle informatique", "M. NJOYA Ibrahim",
            "Initiation à la CAO : plans de pièces mécaniques, cotation et impression des dossiers techniques."),
    ];
}
