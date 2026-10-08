namespace Elbwald.DesktopTools.Contracts.Media.Dates;

public interface IMediaDateResolver
{
    MediaDateResolution Resolve(
        MediaAnalyzedFile file);
}
