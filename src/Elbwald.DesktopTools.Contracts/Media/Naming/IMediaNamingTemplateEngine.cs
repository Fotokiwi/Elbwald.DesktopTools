namespace Elbwald.DesktopTools.Contracts.Media.Naming;

public interface IMediaNamingTemplateEngine
{
    string RenderFileStem(string pattern, MediaNamingContext context);
    void Validate(string pattern);
}
