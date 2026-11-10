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
}
