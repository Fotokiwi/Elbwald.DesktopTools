using System.Runtime.InteropServices;
using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Core.FileOperations;

public sealed class WindowsFileTrashService : IFileTrashService
{
    private const uint FofSilent = 0x0004;
    private const uint FofNoConfirmation = 0x0010;
    private const uint FofNoErrorUi = 0x0400;
    private const uint FofxRecycleOnDelete = 0x00080000;

    private static readonly Guid ShellItemGuid = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid FileOperationClassGuid = new("3AD05575-8857-4850-9277-11B85BDB8E09");

    public Task<FileTrashResult> MoveToTrashAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new FileTrashResult(
                false,
                Path.GetFullPath(path),
                null,
                "Der Windows-Papierkorb ist auf diesem Betriebssystem nicht verfügbar. Die Datei wurde nicht verändert."));
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(new FileTrashResult(
                false,
                fullPath,
                null,
                "Die Datei ist vor dem Verschieben in den Papierkorb nicht mehr vorhanden."));
        }

        var completion = new TaskCompletionSource<FileTrashResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(MoveToTrashCore(fullPath));
            }
            catch (Exception exception)
            {
                completion.TrySetResult(new FileTrashResult(
                    false,
                    fullPath,
                    null,
                    "Windows-Papierkorb fehlgeschlagen: " + exception.GetBaseException().Message));
            }
        })
        {
            IsBackground = true,
            Name = "Elbwald.WindowsRecycleBin"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Sobald die Shell-Aktion gestartet wurde, wird sie bewusst nicht mehr
        // per CancellationToken abgebrochen. Sonst könnte die Datei bereits im
        // Papierkorb liegen, während der Aufrufer fälschlich von einem Abbruch
        // ohne Seiteneffekt ausgeht.
        return completion.Task;
    }

    private static FileTrashResult MoveToTrashCore(string fullPath)
    {
        IShellItem? shellItem = null;
        IFileOperation? operation = null;

        try
        {
            var iid = ShellItemGuid;
            var itemResult = SHCreateItemFromParsingName(fullPath, IntPtr.Zero, ref iid, out shellItem);
            Marshal.ThrowExceptionForHR(itemResult);

            var operationType = Type.GetTypeFromCLSID(FileOperationClassGuid, throwOnError: true)
                ?? throw new InvalidOperationException("Windows IFileOperation konnte nicht aktiviert werden.");
            operation = (IFileOperation)(Activator.CreateInstance(operationType)
                ?? throw new InvalidOperationException("Windows IFileOperation konnte nicht erstellt werden."));

            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(
                FofSilent
                | FofNoConfirmation
                | FofNoErrorUi
                | FofxRecycleOnDelete));
            Marshal.ThrowExceptionForHR(operation.DeleteItem(shellItem, IntPtr.Zero));
            Marshal.ThrowExceptionForHR(operation.PerformOperations());
            Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out var aborted));

            if (aborted || File.Exists(fullPath))
            {
                return new FileTrashResult(
                    false,
                    fullPath,
                    null,
                    "Windows hat die Papierkorb-Aktion nicht abgeschlossen. Die Datei bleibt am ursprünglichen Ort.");
            }

            return new FileTrashResult(
                true,
                fullPath,
                null,
                "Datei wurde in den Windows-Papierkorb verschoben.");
        }
        finally
        {
            if (operation is not null && Marshal.IsComObject(operation))
            {
                Marshal.FinalReleaseComObject(operation);
            }

            if (shellItem is not null && Marshal.IsComObject(shellItem))
            {
                Marshal.FinalReleaseComObject(shellItem);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler();
        void GetParent();
        void GetDisplayName();
        void GetAttributes();
        void Compare();
    }

    [ComImport]
    [Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr pfops, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOperationFlags(uint dwOperationFlags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        [PreserveSig] int SetProgressDialog(IntPtr popd);
        [PreserveSig] int SetProperties(IntPtr pproparray);
        [PreserveSig] int SetOwnerWindow(uint hwndOwner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem psiItem);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr punkItems);
        [PreserveSig] int RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        [PreserveSig] int RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        [PreserveSig] int MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IntPtr pfopsItem);
        [PreserveSig] int MoveItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        [PreserveSig] int CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IntPtr pfopsItem);
        [PreserveSig] int CopyItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        [PreserveSig] int DeleteItem(IShellItem psiItem, IntPtr pfopsItem);
        [PreserveSig] int DeleteItems(IntPtr punkItems);
        [PreserveSig] int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IntPtr pfopsItem);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }
}
