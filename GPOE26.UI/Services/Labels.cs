using System.Globalization;
using GPOE26.UI.Models;

namespace GPOE26.UI.Services;

/// <summary>Libellés français affichés dans l'interface.</summary>
public static class Labels
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Of(AssessmentKind k) => k switch
    {
        AssessmentKind.Practice => "Entraînement",
        AssessmentKind.Homework => "Devoir",
        AssessmentKind.GradedExam => "Épreuve notée",
        AssessmentKind.MockExam => "Épreuve blanche",
        _ => k.ToString()
    };

    public static string Of(AssessmentStatus s) => s switch
    {
        AssessmentStatus.Draft => "Brouillon",
        AssessmentStatus.Published => "Publiée",
        AssessmentStatus.Closed => "Clôturée",
        _ => s.ToString()
    };

    public static string Of(QuestionType t) => t switch
    {
        QuestionType.SingleChoice => "QCM",
        QuestionType.MultipleChoice => "Choix multiples",
        QuestionType.TrueFalse => "Vrai / Faux",
        QuestionType.ShortAnswer => "Réponse courte",
        QuestionType.Numeric => "Numérique",
        QuestionType.OpenEnded => "Réponse ouverte",
        _ => t.ToString()
    };

    public static string Describe(QuestionType t) => t switch
    {
        QuestionType.SingleChoice => "Une seule bonne réponse",
        QuestionType.MultipleChoice => "Plusieurs bonnes réponses (tout ou rien)",
        QuestionType.TrueFalse => "Affirmation à juger",
        QuestionType.ShortAnswer => "Un mot ou une expression",
        QuestionType.Numeric => "Un nombre, avec tolérance",
        QuestionType.OpenEnded => "Rédaction, démonstration, copie photographiée",
        _ => ""
    };

    public static string Of(SubmissionStatus? s) => s switch
    {
        SubmissionStatus.InProgress => "En cours",
        SubmissionStatus.Submitted => "À corriger",
        SubmissionStatus.Graded => "Corrigée",
        _ => "Non rendue"
    };

    public static string Of(SubscriptionPlan? p) => p switch
    {
        SubscriptionPlan.StudentPremium => "Premium élève",
        SubscriptionPlan.Family => "Formule familiale",
        SubscriptionPlan.Teacher => "Abonnement enseignant",
        SubscriptionPlan.OrganizationLicense => "Licence établissement",
        _ => "Gratuit"
    };

    public static string Of(SubscriptionStatus s) => s switch
    {
        SubscriptionStatus.PendingPayment => "En attente de paiement",
        SubscriptionStatus.Trial => "Essai",
        SubscriptionStatus.Active => "Actif",
        SubscriptionStatus.Expired => "Expiré",
        SubscriptionStatus.Cancelled => "Annulé",
        _ => s.ToString()
    };

    public static string Of(UserRole? r) => r switch
    {
        UserRole.Student => "Élève",
        UserRole.Teacher => "Enseignant",
        UserRole.Parent => "Parent",
        _ => ""
    };

    public static string Of(OrganizationRole r) => r switch
    {
        OrganizationRole.Owner => "Responsable",
        OrganizationRole.Manager => "Gestionnaire",
        _ => "Enseignant"
    };

    public static string Of(StudentStatus s) => s switch
    {
        StudentStatus.Pending => "En attente",
        StudentStatus.Active => "Compte activé",
        StudentStatus.Revoked => "Retiré",
        _ => "Parti"
    };

    public static string Tone(StudentStatus s) => s switch
    {
        StudentStatus.Active => "green",
        StudentStatus.Pending => "amber",
        _ => "grey"
    };

    public static string LongDate(DateTime? d) => d is null ? "—" : AppTime.ToLocal(d.Value).ToString("d MMMM yyyy", Fr);

    /// <summary>
    /// Dernier jour de validité d'un code : il expire en fin de journée UTC, ce qui tombe déjà le lendemain
    /// à Douala (UTC+1). On affiche donc le jour UTC, celui qu'a fixé l'établissement (fin de l'année scolaire).
    /// </summary>
    public static string ValidUntil(DateTime utc) => utc.ToString("d MMMM yyyy", Fr);

    /// <summary>D'où vient un accès Premium, en clair (Label : établissement, espace de l'enseignant ou parent payeur).</summary>
    public static string Of(EntitlementSource s) => s.Kind switch
    {
        EntitlementSourceKind.Trial => "Période d'essai",
        EntitlementSourceKind.Personal => "Abonnement personnel",
        EntitlementSourceKind.Family => s.Label is { Length: > 0 } payer ? $"Formule Famille de {payer}" : "Formule Famille",
        EntitlementSourceKind.School => s.Label is { Length: > 0 } school ? $"Licence de {school}" : "Licence de l'établissement",
        _ => s.Label is { Length: > 0 } space ? $"Offert par l'enseignant ({space})" : "Offert par l'enseignant"
    };

    public static string Date(DateTime? d) => d is null ? "—" : AppTime.ToLocal(d.Value).ToString("ddd d MMM", Fr);
    public static string When(DateTime? d) => d is null ? "—" : AppTime.ToLocal(d.Value).ToString("ddd d MMM · HH'h'mm", Fr);
    public static string Today() => AppTime.Now.ToString("dddd d MMMM", Fr);

    public static string Score(decimal? value, int scaleTo) =>
        value is null ? "—" : $"{value.Value.ToString("0.##", Fr)}/{scaleTo}";

    public static string Number(decimal value) => value.ToString("0.##", Fr);
    public static string Number(double? value) => value is null ? "—" : value.Value.ToString("0.##", Fr);

    public static string Initial(string? name) => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();
}
