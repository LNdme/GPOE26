using GPOE26.UI.Models;

namespace GPOE26.UI.Services;

/// <summary>
/// Administration de la plateforme : abonnements à activer, établissements et licences.
/// Le service User vérifie le droit d'administrateur en base à chaque appel.
/// </summary>
public interface IPlatformAdminApi
{
    Task<ApiResult<List<AdminSubscription>>> SubscriptionsAsync(SubscriptionStatus status);
    Task<ApiResult<SubscriptionInfo>> ActivateAsync(Guid subscriptionId, int? months);
    Task<ApiResult<List<Organization>>> OrganizationsAsync();
    Task<ApiResult<Organization>> CreateOrganizationAsync(CreateOrganizationRequest request);
    Task<ApiResult<SubscriptionInfo>> CreateLicenseAsync(Guid organizationId, CreateLicenseRequest request);
    Task<ApiResult<SpaceOwner>> SetOwnerAsync(Guid organizationId, string email);
    Task<ApiResult<bool>> RemoveOwnerAsync(Guid organizationId, Guid userId);
    /// <summary>Code responsable à usage unique, pour une personne qui n'a pas encore de compte.</summary>
    Task<ApiResult<AccessCodeInfo>> OwnerCodeAsync(Guid organizationId);
    /// <summary>Code de l'établissement pour la connexion par matricule (4 à 10 lettres ou chiffres).</summary>
    Task<ApiResult<bool>> SetShortCodeAsync(Guid organizationId, string code);
}

public class PlatformAdminApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.User), IPlatformAdminApi
{
    public Task<ApiResult<List<AdminSubscription>>> SubscriptionsAsync(SubscriptionStatus status) =>
        GetAsync<List<AdminSubscription>>($"/admin/subscriptions?status={status}");

    public Task<ApiResult<SubscriptionInfo>> ActivateAsync(Guid subscriptionId, int? months) =>
        PostAsync<SubscriptionInfo>($"/admin/subscriptions/{subscriptionId}/activate", new { months });

    public Task<ApiResult<List<Organization>>> OrganizationsAsync() => GetAsync<List<Organization>>("/admin/organizations");

    public Task<ApiResult<Organization>> CreateOrganizationAsync(CreateOrganizationRequest request) =>
        PostAsync<Organization>("/admin/organizations", request);

    public Task<ApiResult<SubscriptionInfo>> CreateLicenseAsync(Guid organizationId, CreateLicenseRequest request) =>
        PostAsync<SubscriptionInfo>($"/admin/organizations/{organizationId}/licenses", request);

    public Task<ApiResult<SpaceOwner>> SetOwnerAsync(Guid organizationId, string email) =>
        PutAsync<SpaceOwner>($"/admin/organizations/{organizationId}/owners", new { email });

    public Task<ApiResult<bool>> RemoveOwnerAsync(Guid organizationId, Guid userId) =>
        DeleteAsync<bool>($"/admin/organizations/{organizationId}/owners/{userId}");

    public Task<ApiResult<AccessCodeInfo>> OwnerCodeAsync(Guid organizationId) =>
        PostAsync<AccessCodeInfo>($"/admin/organizations/{organizationId}/owner-code");

    public Task<ApiResult<bool>> SetShortCodeAsync(Guid organizationId, string code) =>
        PutAsync<bool>($"/admin/organizations/{organizationId}/short-code", new { code });
}
