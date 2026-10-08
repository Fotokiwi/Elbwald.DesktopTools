namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record MediaCacheStatistics(
    string Location,
    long EntryCount,
    long SizeBytes,
    bool IsAvailable = true,
    string? Message = null);
