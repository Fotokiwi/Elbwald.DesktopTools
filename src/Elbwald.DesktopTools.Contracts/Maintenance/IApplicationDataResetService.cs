namespace Elbwald.DesktopTools.Contracts.Maintenance;

public interface IApplicationDataResetService
{
    Task<ApplicationDataResetResult> ResetVolatileDataAsync(
        CancellationToken cancellationToken = default);
}
