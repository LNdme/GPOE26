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
        UserRole.Admin => "Administrateur",
        _ => ""
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
