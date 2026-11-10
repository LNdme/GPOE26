namespace GPOE26.UI.Models;

// ─── Administration de la plateforme (service User, droit d'administrateur) ─────

/// <summary>Abonnement individuel ou familial, avec son titulaire.</summary>
public record AdminSubscription(SubscriptionInfo Subscription, DateTime CreatedAt, string? OwnerName, string? OwnerEmail, UserRole? OwnerRole);

public enum OrganizationType { School, Institution }

/// <summary>Établissement ou institution, et ses licences (chacune avec sa clé à transmettre).</summary>
public record Organization(Guid Id, string Name, OrganizationType Type, string? City, List<SubscriptionInfo> Licenses);

public record CreateOrganizationRequest(string Name, OrganizationType Type, string? City);

public record CreateLicenseRequest(int Seats, int Months);
