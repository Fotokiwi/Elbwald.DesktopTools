namespace Elbwald.DesktopTools.Contracts.Media.Dates;

public interface IMediaDateContextAnalyzer
{
    IReadOnlyList<MediaDateContextHint> Analyze(
        IEnumerable<MediaDateContextEntry> entries,
        CancellationToken cancellationToken = default);
}
