namespace GPOE26.UI.Models;

// ─── Administration de la plateforme (service User, droit d'administrateur) ─────

/// <summary>Abonnement individuel ou familial, avec son titulaire.</summary>
public record AdminSubscription(SubscriptionInfo Subscription, DateTime CreatedAt, string? OwnerName, string? OwnerEmail, UserRole? OwnerRole);

// OrganizationType : voir SpaceModels.cs (établissement, institution, enseignant indépendant)

/// <summary>Responsable du compte d'un établissement.</summary>
public record SpaceOwner(Guid UserId, string Username, string? Email);

/// <summary>
/// Établissement ou institution, ses licences et ses responsables. Une licence d'établissement n'a plus de clé
/// partagée : ses élèves et enseignants reçoivent un code personnel.
/// </summary>
public record Organization(Guid Id, string Name, OrganizationType Type, string? City, List<SubscriptionInfo> Licenses, List<SpaceOwner>? Owners = null);

public record CreateOrganizationRequest(string Name, OrganizationType Type, string? City);

public record CreateLicenseRequest(int Seats, int Months);
