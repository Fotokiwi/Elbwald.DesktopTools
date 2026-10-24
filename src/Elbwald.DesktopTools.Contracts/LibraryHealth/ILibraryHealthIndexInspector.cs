namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public interface ILibraryHealthIndexInspector
{
    Task<LibraryHealthIndexSummary> InspectAsync(
        IReadOnlyList<LibraryHealthIndexEndpointState> endpointStates,
        CancellationToken cancellationToken = default);

    Task<LibraryHealthIndexSummary> InspectAsync(
        IReadOnlyList<LibraryHealthIndexEndpointState> endpointStates,
        CancellationToken cancellationToken,
        IProgress<LibraryHealthProgress>? progress)
        => InspectAsync(endpointStates, cancellationToken);
}
