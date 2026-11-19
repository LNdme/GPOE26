using System.Text.Json;
using System.Text.RegularExpressions;

namespace GPOE26.Agent.Abstractions;

public static partial class AgentExtensionManifestValidator
{
    public const int CurrentManifestVersion = 1;
    private const int MaxIdentifierLength = 128;
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2000;

    [GeneratedRegex(@"^[a-zA-Z0-9](?:[a-zA-Z0-9._-]{0,126}[a-zA-Z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public static IReadOnlyList<string> Validate(AgentExtensionManifest? manifest)
    {
        var errors = new List<string>();
        if (manifest is null)
            return ["Manifest must not be null."];

        if (manifest.ManifestVersion != CurrentManifestVersion)
            errors.Add($"Unsupported manifest version '{manifest.ManifestVersion}'.");
        ValidateIdentifier(manifest.Id, "Extension ID", errors);
        if (string.IsNullOrWhiteSpace(manifest.Version) || !VersionPattern().IsMatch(manifest.Version))
            errors.Add("Extension version must be a semantic version.");
        ValidateText(manifest.Name, "Extension name", MaxNameLength, errors);
        ValidateText(manifest.Description, "Extension description", MaxDescriptionLength, errors);

        if (!Enum.IsDefined(manifest.Kind))
            errors.Add("Extension kind is invalid.");

        if (manifest.Publisher is null)
        {
            errors.Add("Publisher must be specified.");
        }
        else
        {
            ValidateIdentifier(manifest.Publisher.Id, "Publisher ID", errors);
            ValidateText(manifest.Publisher.Name, "Publisher name", MaxNameLength, errors);
            if (manifest.Publisher.SourceUri is { } sourceUri &&
                (!Uri.TryCreate(sourceUri, UriKind.Absolute, out var uri) ||
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add("Publisher source URI must be an absolute HTTPS URI.");
            }
        }

        if (manifest.Capabilities is null)
        {
            errors.Add("Capabilities must be specified.");
        }
        else
        {
            var capabilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var capability in manifest.Capabilities)
            {
                if (capability is null)
                {
                    errors.Add("Capabilities must not contain null entries.");
                    continue;
                }

                ValidateIdentifier(capability.Id, "Capability ID", errors);
                if (!capabilityIds.Add(capability.Id.Trim()))
                    errors.Add($"Duplicate capability ID '{capability.Id}'.");
                ValidateText(capability.Description, $"Capability '{capability.Id}' description", MaxDescriptionLength, errors);

                if (capability.InputSchemaJson is { } schema)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(schema);
                        if (document.RootElement.ValueKind != JsonValueKind.Object)
                            errors.Add($"Capability '{capability.Id}' input schema must be a JSON object.");
                    }
                    catch (JsonException)
                    {
                        errors.Add($"Capability '{capability.Id}' input schema is invalid JSON.");
                    }
                }
            }
        }

        var permissionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (manifest.RequestedPermissions is null)
        {
            errors.Add("Requested permissions must be specified.");
        }
        else
        {
            foreach (var permission in manifest.RequestedPermissions)
            {
                if (permission is null)
                {
                    errors.Add("Requested permissions must not contain null entries.");
                    continue;
                }

                ValidateIdentifier(permission.Id, "Permission ID", errors);
                if (!permissionIds.Add(permission.Id.Trim()))
                    errors.Add($"Duplicate permission ID '{permission.Id}'.");
                ValidateText(permission.Description, $"Permission '{permission.Id}' description", MaxDescriptionLength, errors);
            }
        }

        if (manifest.Capabilities is not null)
        {
            foreach (var capability in manifest.Capabilities.Where(capability => capability is not null))
            {
                foreach (var permissionId in capability.RequiredPermissionIds ?? [])
                {
                    if (!permissionIds.Contains(permissionId))
                        errors.Add($"Capability '{capability.Id}' references undeclared permission '{permissionId}'.");
                }
            }
        }

        if (manifest.Parameters is null)
        {
            errors.Add("Parameters must be specified.");
        }
        else
        {
            var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in manifest.Parameters)
            {
                if (parameter is null)
                {
                    errors.Add("Parameters must not contain null entries.");
                    continue;
                }

                ValidateIdentifier(parameter.Name, "Parameter name", errors);
                if (!parameterNames.Add(parameter.Name.Trim()))
                    errors.Add($"Duplicate parameter name '{parameter.Name}'.");
                ValidateText(parameter.Description, $"Parameter '{parameter.Name}' description", MaxDescriptionLength, errors);
                if (!Enum.IsDefined(parameter.Type))
                    errors.Add($"Parameter '{parameter.Name}' type is invalid.");
            }
        }

        return errors;
    }

    private static void ValidateIdentifier(string? value, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaxIdentifierLength ||
            !IdentifierPattern().IsMatch(value))
        {
            errors.Add($"{label} is invalid.");
        }
    }

    private static void ValidateText(string? value, string label, int maxLength, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maxLength ||
            value.Any(char.IsControl))
        {
            errors.Add($"{label} is invalid.");
        }
    }
}
