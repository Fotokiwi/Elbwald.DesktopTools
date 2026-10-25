using Elbwald.DesktopTools.Contracts.Media.Editing;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Contracts.Navigation;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Image.Editor.Basic.Operations;
using Elbwald.DesktopTools.Image.Editor.Basic.Preview;
using Elbwald.DesktopTools.Image.Editor.Basic.Rendering;
using Elbwald.DesktopTools.Image.Editor.Basic.ViewModels;
using Elbwald.DesktopTools.Image.Editor.Basic.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Image.Editor.Basic;

/// <summary>
/// Optional first-party image editor. The host only sees the generic module/navigation
/// contracts; all editor UI, edit persistence and rendering stay inside this assembly.
/// </summary>
public sealed class ImageEditorBasicExtensionModule : IToolModule
{
    public const string ExtensionId = "elbwald.image.editor.basic";

    public string Id => ExtensionId;

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(
            serviceProvider =>
            {
                var appPaths = serviceProvider.GetRequiredService<IAppPaths>();

                return new ImageEditStoreOptions
                {
                    DatabasePath = Path.Combine(
                        appPaths.DatabaseDirectory,
                        "image-editing.db")
                };
            });

        services.AddSingleton<IImageEditDocumentStore, SqliteImageEditDocumentStore>();
        services.AddSingleton<IImageEditOperationRegistry, ImageEditOperationRegistry>();

        services.AddImageEditOperationProcessor<RotateImageEditOperationProcessor>(Id);
        services.AddImageEditOperationProcessor<FlipImageEditOperationProcessor>(Id);
        services.AddImageEditOperationProcessor<CropImageEditOperationProcessor>(Id);
        services.AddImageEditOperationProcessor<ToneImageEditOperationProcessor>(Id);
        services.AddImageEditRenderer<ImageEditRenderer>(Id);
        services.AddImageEditPreviewService<ImageEditPreviewService>(Id);

        services.AddNavigationContribution(
            new NavigationContribution(
                ExtensionId: Id,
                Id: "image-editor",
                RouteId: Id,
                Placement: NavigationContributionPlacement.Section,
                SectionId: "edit",
                Title: "Bildbearbeitung",
                Description: "Bilder nicht-destruktiv bearbeiten – Originale bleiben unverändert.",
                Icon: "Image",
                Order: 10,
                ToolModuleId: Id));
    }

    public ToolModuleDescriptor GetDescriptor()
    {
        return new ToolModuleDescriptor(
            Id: Id,
            Name: "Bildbearbeitung",
            Description: "Nicht-destruktiver Bildeditor mit modularer Render-Pipeline.",
            Icon: "Image",
            ViewModelType: typeof(ImageEditorViewModel),
            ViewType: typeof(ImageEditorView));
    }
}
