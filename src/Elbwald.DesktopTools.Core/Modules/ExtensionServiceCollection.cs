using System.Collections;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Core.Modules;

/// <summary>
/// Restricted view over the host service collection used while an extension
/// registers services. Existing host registrations cannot be removed or replaced
/// through list operations. Additions are tracked so they can be rolled back if
/// extension registration fails.
/// </summary>
internal sealed class ExtensionServiceCollection : IServiceCollection
{
    private readonly IServiceCollection _services;
    private readonly List<ServiceDescriptor> _added = [];

    public ExtensionServiceCollection(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public int Count => _services.Count;

    public bool IsReadOnly => false;

    public IReadOnlyList<ServiceDescriptor> AddedDescriptors => _added;

    public ServiceDescriptor this[int index]
    {
        get => _services[index];
        set => throw new NotSupportedException(
            "Extensions dürfen vorhandene Service-Registrierungen nicht ersetzen.");
    }

    public void Add(ServiceDescriptor item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _services.Add(item);
        _added.Add(item);
    }

    public void Clear()
    {
        throw new NotSupportedException(
            "Extensions dürfen vorhandene Service-Registrierungen nicht löschen.");
    }

    public bool Contains(ServiceDescriptor item) => _services.Contains(item);

    public void CopyTo(ServiceDescriptor[] array, int arrayIndex) =>
        _services.CopyTo(array, arrayIndex);

    public IEnumerator<ServiceDescriptor> GetEnumerator() => _services.GetEnumerator();

    public int IndexOf(ServiceDescriptor item) => _services.IndexOf(item);

    public void Insert(int index, ServiceDescriptor item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _services.Insert(index, item);
        _added.Add(item);
    }

    public bool Remove(ServiceDescriptor item)
    {
        throw new NotSupportedException(
            "Extensions dürfen vorhandene Service-Registrierungen nicht entfernen.");
    }

    public void RemoveAt(int index)
    {
        throw new NotSupportedException(
            "Extensions dürfen vorhandene Service-Registrierungen nicht entfernen.");
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Rollback()
    {
        for (var index = _added.Count - 1; index >= 0; index--)
        {
            _services.Remove(_added[index]);
        }

        _added.Clear();
    }
}
