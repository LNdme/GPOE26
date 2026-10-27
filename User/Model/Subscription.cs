namespace User.Model
{
    /// <summary>
    /// Abonnement Premium (modèle freemium : les cours partagés restent gratuits).
    ///
    /// - StudentPremium / Teacher : payé par l'utilisateur lui-même (OwnerUserId).
    /// - Family : payé par un parent ; une clé est générée, que ses enfants activent.
    /// - OrganizationLicense : acheté par un établissement ou une institution ;
    ///   une clé est générée, que ses enseignants (et élèves) activent.
    ///
    /// Le paiement réel arrive en phase 5 : en attendant, un abonnement acheté reste
    /// en PendingPayment jusqu'à son activation par un administrateur.
    /// </summary>
    public class Subscription
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public SubscriptionPlan Plan { get; set; }

        public BillingPeriod Period { get; set; } = BillingPeriod.Monthly;

        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.PendingPayment;

        /// <summary>Payeur (élève, enseignant ou parent) — null pour une licence d'organisation</summary>
        public Guid? OwnerUserId { get; set; }

        public Guid? OrganizationId { get; set; }

        /// <summary>Nombre de places : enfants couverts (Family) ou comptes couverts (licence)</summary>
        public int Seats { get; set; } = 1;

        public DateTime? StartsAt { get; set; }

        public DateTime? EndsAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsCurrent(DateTime now) =>
            Status is SubscriptionStatus.Active or SubscriptionStatus.Trial
            && EndsAt is not null && EndsAt > now;
    }

    /// <summary>
    /// Clé d'abonnement à partager (ex: "FAM-7Q2K-9XRM").
    /// Chaque activation consomme une place jusqu'à MaxUses.
    /// </summary>
    public class SubscriptionKey
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public required string Code { get; set; }

        public Guid SubscriptionId { get; set; }
        public Subscription Subscription { get; set; } = null!;

        public int MaxUses { get; set; } = 1;

        public int UsedCount { get; set; }

        public bool IsRevoked { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Activation d'une clé par un utilisateur — c'est elle qui lui donne l'accès Premium</summary>
    public class KeyRedemption
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid KeyId { get; set; }
        public SubscriptionKey Key { get; set; } = null!;

        public Guid UserId { get; set; }

        public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;
    }

    public enum SubscriptionPlan
    {
        StudentPremium,
        Family,
        Teacher,
        OrganizationLicense
    }

    public enum BillingPeriod
    {
        Monthly,
        Yearly
    }

    public enum SubscriptionStatus
    {
        PendingPayment,
        Trial,
        Active,
        Expired,
        Cancelled
    }
}
