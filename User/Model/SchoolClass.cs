using System.Security.Cryptography;

namespace User.Model;

/// <summary>
/// Une classe : un enseignant, une matière, et les élèves qui l'ont rejointe.
///
/// C'est par elle que passe le suivi enseignant. Un parent se rattache à trois enfants
/// nommément ; un enseignant en suit cent cinquante, et les désigner un par un n'aurait
/// aucun sens — ni pour lui, ni pour un jeton qui devrait les porter.
/// </summary>
public class SchoolClass
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeacherId { get; set; }
    public AppUser Teacher { get; set; } = null!;

    /// <summary>« Terminale C », « 1ère D2 ».</summary>
    public required string Name { get; set; }

    public required string Subject { get; set; }

    public string? Level { get; set; }

    /// <summary>« 2025-2026 ». Une classe ne se réutilise pas d'une année sur l'autre.</summary>
    public required string SchoolYear { get; set; }

    /// <summary>
    /// Le code que les élèves saisissent pour rejoindre.
    ///
    /// ⚠️ Rien à voir avec <see cref="StudentLinkCode"/>, et les confondre serait une
    /// erreur. Un code parent est à usage unique et vaut trente minutes, parce qu'il donne
    /// accès à UN enfant. Un code de classe est saisi par TRENTE élèves et vit une année
    /// scolaire — il sera écrit au tableau, dicté, recopié. Il est donc réutilisable et
    /// sans expiration courte, mais en contrepartie il doit pouvoir être **coupé ou
    /// régénéré en une action** : un code affiché en classe finit toujours par sortir de
    /// la classe.
    /// </summary>
    public required string Code { get; set; }

    /// <summary>Coupe l'entrée sans supprimer la classe ni ses élèves.</summary>
    public bool JoinEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ClassEnrollment> Enrollments { get; set; } = [];

    /// <summary>
    /// Alphabet sans 0/O ni 1/I/L : un code de classe se dicte à voix haute devant
    /// trente élèves, et une seule ambiguïté coûte trente mains levées.
    /// </summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Longueur d'un code de classe.</summary>
    public const int CodeLength = 6;

    /// <summary>
    /// Six caractères : assez pour ne pas se deviner, assez court pour se recopier depuis
    /// le fond de la salle.
    ///
    /// <c>GetString</c> plutôt qu'un modulo sur des octets : l'alphabet compte 31 lettres,
    /// qui ne divise pas 256, donc <c>octet % 31</c> rendrait les huit premières lettres
    /// plus fréquentes que les autres. Le biais est petit, mais l'éviter ne coûte rien.
    /// </summary>
    public static string NewCode() =>
        RandomNumberGenerator.GetString(Alphabet, CodeLength);

    public bool AcceptsJoin => JoinEnabled;
}

/// <summary>L'appartenance d'un élève à une classe.</summary>
public class ClassEnrollment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ClassId { get; set; }
    public SchoolClass Class { get; set; } = null!;

    public Guid StudentId { get; set; }
    public AppUser Student { get; set; } = null!;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
