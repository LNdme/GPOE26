namespace GPOE26.ApiService.Model
{
    /// <summary>
    /// Formule d'abonnement : prix, catégorie et fonctionnalités.
    /// C'est la référence de la plateforme : le site l'affiche et le service User en déduit les droits.
    /// </summary>
    public class SubscriptionPlan
    {
        public int Id { get; set; }

        /// <summary>Identifiant stable : free, student-premium, family, teacher, organization-license</summary>
        public required string Code { get; set; }

        public required string Name { get; set; }

        /// <summary>Public visé : Élève, Famille, Enseignant, Établissement</summary>
        public required string Category { get; set; }

        public string? Description { get; set; }

        /// <summary>Prix mensuel ; vide pour une formule sur devis</summary>
        public int? PriceMonthly { get; set; }

        /// <summary>Prix annuel ; vide pour une formule sur devis</summary>
        public int? PriceYearly { get; set; }

        public string Currency { get; set; } = "FCFA";

        /// <summary>Nombre de comptes couverts par une clé</summary>
        public int Seats { get; set; } = 1;

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public List<PlanFeature> Features { get; set; } = new();
    }

    /// <summary>Fonctionnalité d'une formule. Limit : quota mensuel, vide si illimité.</summary>
    public class PlanFeature
    {
        public required string Code { get; set; }
        public required string Label { get; set; }
        public int? Limit { get; set; }
    }
}
