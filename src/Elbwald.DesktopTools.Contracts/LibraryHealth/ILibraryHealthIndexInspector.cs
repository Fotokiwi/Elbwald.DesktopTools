namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public interface ILibraryHealthIndexInspector
{
    Task<LibraryHealthIndexSummary> InspectAsync(
        CancellationToken cancellationToken = default);
}
