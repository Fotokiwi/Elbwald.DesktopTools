namespace Elbwald.DesktopTools.Contracts.Diagnostics;

public interface IDiagnosticEventStore
{
    ValueTask<bool> TryWriteAsync(
        DiagnosticEventWrite diagnosticEvent,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiagnosticEventEntry>> QueryAsync(
        DiagnosticEventQuery query,
        CancellationToken cancellationToken = default);
}
