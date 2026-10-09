namespace Elbwald.DesktopTools.Contracts.Diagnostics;

public sealed record DiagnosticEventQuery(
    DiagnosticEventSeverity? MinimumSeverity = null,
    DiagnosticEventCategory? Category = null,
    string? SearchText = null,
    int Limit = 250);
