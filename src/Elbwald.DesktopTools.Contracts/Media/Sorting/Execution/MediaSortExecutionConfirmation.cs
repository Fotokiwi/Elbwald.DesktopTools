using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public sealed record MediaSortExecutionConfirmation(
    string Fingerprint,
    string ConfirmationText,
    bool ApproveDateReviews,
    bool ApproveSourceDeletion = false)
{
    public const string CopyRequiredText =
        "AUSFÜHREN";

    public const string MoveRequiredText =
        "VERSCHIEBEN";

    // Kompatibilität für bestehende Copy-Aufrufer.
    public const string RequiredText =
        CopyRequiredText;

    public bool HasRequiredText =>
        HasRequiredTextFor(
            FileOperationKind.Copy);

    public bool HasRequiredTextFor(
        FileOperationKind operationKind)
    {
        if (operationKind == FileOperationKind.Copy)
        {
            return true;
        }

        return string.Equals(
            ConfirmationText?.Trim(),
            MoveRequiredText,
            StringComparison.Ordinal);
    }
}
