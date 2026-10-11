using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Modules;

/// <summary>
/// Declares that an extension provides a concrete implementation for a named
/// capability. The provider instance itself is resolved lazily from the host DI
/// container through <see cref="IExtensionRegistry"/>.
/// </summary>
public sealed record ExtensionCapabilityProviderRegistration(
    string ExtensionId,
    string CapabilityId,
    Type ContractType,
    Type ImplementationType,
    ServiceLifetime Lifetime);
