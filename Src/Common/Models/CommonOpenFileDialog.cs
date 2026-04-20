#pragma warning disable
using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Diagnostics;
using System.Linq; // Keep for .NET 4.6
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Interop;

namespace BcToolsC.Models
{
    public class CommonOpenFileDialog
    {
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

        // Závislosti cesty a jména
        private readonly List<string> _resultPaths = new List<string>();
        private readonly List<string> _resultNames = new List<string>();
        public IReadOnlyList<string> ResultPaths => _resultPaths;
        public IReadOnlyList<string> ResultNames => _resultNames;
        public string ResultPath => ResultPaths.FirstOrDefault();
        public string ResultName => ResultNames.FirstOrDefault();

        // Nastavení
        public virtual string InputPath { get; set; }
        public virtual bool ForceFileSystem { get; set; }
        public virtual bool Multiselect { get; set; }
        public virtual string Title { get; set; }
        public virtual string OkButtonLabel { get; set; }
        public virtual string FileNameLabel { get; set; }

        // Pøímá kompatibilita s WPF
        public bool? ShowDialog(Window hwndOwner = null, bool throwOnError = false)
        {
            hwndOwner = hwndOwner ?? Application.Current?.MainWindow;
            return ShowDialog(hwndOwner != null ? new WindowInteropHelper(hwndOwner).Handle : IntPtr.Zero, throwOnError);
        }

        public virtual bool? ShowDialog(IntPtr hwndOwner, bool throwOnError = false)
        {
            IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialog();
            if (!string.IsNullOrEmpty(InputPath))
            {
                if (CheckHr(SHCreateItemFromParsingName(InputPath, null, typeof(IShellItem).GUID, out IShellItem item), throwOnError) != 0)
                    return null;
                dialog.SetFolder(item);
            }
            FILEOPENDIALOGOPTIONS options = FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS;
            options = (FILEOPENDIALOGOPTIONS)SetOptions((int)options);
            dialog.SetOptions(options);
            if (Title != null)
                dialog.SetTitle(Title);
            if (OkButtonLabel != null)
                dialog.SetOkButtonLabel(OkButtonLabel);
            if (FileNameLabel != null)
                dialog.SetFileName(FileNameLabel);
            if (hwndOwner == IntPtr.Zero)
            {
                hwndOwner = Process.GetCurrentProcess().MainWindowHandle;
                if (hwndOwner == IntPtr.Zero)
                    hwndOwner = GetDesktopWindow();
            }
            int HRESULT_FROM_WIN32 = dialog.Show(hwndOwner);
            // HRESULT_FROM_WIN32(ERROR_CANCELLED)
            // The user closed the window by cancelling the operation. 
            if (HRESULT_FROM_WIN32 == ERROR_CANCELLED)
                return null;
            if (CheckHr(HRESULT_FROM_WIN32, throwOnError) != 0)
                return null;
            if (CheckHr(dialog.GetResults(out IShellItemArray items), throwOnError) != 0)
                return null;
            items.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                items.GetItemAt(i, out IShellItem item);
                CheckHr(item.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, out string path), throwOnError);
                CheckHr(item.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEEDITING, out string name), throwOnError);
                if (path != null || name != null)
                {
                    _resultPaths.Add(path);
                    _resultNames.Add(name);
                }
            }
            return true;
        }

        protected virtual int SetOptions(int fos)
        {
            if (ForceFileSystem)
                fos |= (int)FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM;
            if (Multiselect)
                fos |= (int)FILEOPENDIALOGOPTIONS.FOS_ALLOWMULTISELECT;
            return fos;
        }
        #region PRIVATE
        private static int CheckHr(int errorCode, bool throwOnError)
        {
            if (errorCode != 0 && throwOnError) Marshal.ThrowExceptionForHR(errorCode);
            return errorCode;
        }
        #region WIN32_CALLS
        [DllImport("shell32")]
        static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IBindCtx pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem ppv);
        [DllImport("user32")]
        static extern IntPtr GetDesktopWindow();
        #endregion
        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifiledialog
        [ComImport]
        [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-imodalwindow-show
            // Launches the modal window.
            // If the method succeeds, it returns S_OK. Otherwise, it returns an HRESULT error code.
            [PreserveSig] int Show(IntPtr hwndOwner);
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int SetFileTypes();
            [PreserveSig] int SetFileTypeIndex(int iFileType);
            [PreserveSig] int GetFileTypeIndex(out int piFileType);
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int Advise();
            [PreserveSig] int Unadvise();
            [PreserveSig] int SetOptions(FILEOPENDIALOGOPTIONS fos);
            [PreserveSig] int GetOptions(out FILEOPENDIALOGOPTIONS pfos);
            [PreserveSig] int SetDefaultFolder(IShellItem psi);
            [PreserveSig] int SetFolder(IShellItem psi);
            [PreserveSig] int GetFolder(out IShellItem ppsi);
            [PreserveSig] int GetCurrentSelection(out IShellItem ppsi);
            [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            [PreserveSig] int GetResult(out IShellItem ppsi);
            // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifiledialog-addplace
            [PreserveSig] int AddPlace(IShellItem psi, FDAP fdap);
            // Sets the default extension to be added to file names.
            // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifiledialog-setdefaultextension
            [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            // Closes the dialog.
            // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifiledialog-close
            [PreserveSig] int Close(int hr);
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int SetClientGuid();
            // Instructs the dialog to clear all persisted state information.
            // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifiledialog-clearclientdata
            [PreserveSig] int ClearClientData();
            [PreserveSig] int SetFilter([MarshalAs(UnmanagedType.IUnknown)] object pFilter);
            [PreserveSig] int GetResults(out IShellItemArray ppenum);
            [PreserveSig] int GetSelectedItems([MarshalAs(UnmanagedType.IUnknown)] out object ppsai);
        }

        // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellitem
        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int BindToHandler();
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int GetParent();
            [PreserveSig] int GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int GetAttributes();
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int Compare();
        }

        // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellitemarray
        [ComImport]
        [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int BindToHandler();
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int GetPropertyStore();
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int GetPropertyDescriptionList();
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int GetAttributes();
            [PreserveSig] int GetCount(out int pdwNumItems);
            [PreserveSig] int GetItemAt(int dwIndex, out IShellItem ppsi);
            /// <remarks>
            /// <para><b>Warning:</b> This method is intentionally not implemented
            /// in this interop definition. Calling it will result in undefined behavior.</para>
            /// </remarks>
            [PreserveSig] int EnumItems();
        }

        // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-fdap
        private enum FDAP : int
        {
            /// <summary>
            /// The place is added to the bottom of the default list.
            /// </summary>
            FDAP_BOTTOM = 0,
            /// <summary>
            /// The place is added to the top of the default list.
            /// </summary>
            FDAP_TOP = 1
        }

        // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-sigdn
        private enum SIGDN : uint
        {
            /// <summary>
            /// Returns the editing name relative to the desktop.
            /// In UI, this name is suitable for display to the user.
            /// </summary>
            SIGDN_DESKTOPABSOLUTEEDITING = 0x8004c000,
            /// <summary>
            /// Returns the parsing name relative to the desktop.
            /// This name is not suitable for display in the UI.
            /// </summary>
            SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000,
            /// <summary>
            /// Returns the item's file system path, if it has one.
            /// Only items that report SFGAO_FILESYSTEM have a file system path.
            /// If the item does not have a file system path, the call fails.
            /// In UI, this name may be suitable for display in some cases.
            /// </summary>
            SIGDN_FILESYSPATH = 0x80058000,
            /// <summary>
            /// Returns the display name relative to the parent folder.
            /// In UI, this name is generally ideal for display to the user.
            /// </summary>
            SIGDN_NORMALDISPLAY = 0,
            /// <summary>
            /// Returns the path relative to the parent folder.
            /// </summary>
            SIGDN_PARENTRELATIVE = 0x80080001,
            /// <summary>
            /// Returns the editing name relative to the parent folder.
            /// In UI, this name is suitable for display to the user.
            /// </summary>
            SIGDN_PARENTRELATIVEEDITING = 0x80031001,
            /// <summary>
            /// Returns the path relative to the parent folder in a friendly format,
            /// as displayed in an address bar. This name is suitable for display
            /// to the user.
            /// </summary>
            SIGDN_PARENTRELATIVEFORADDRESSBAR = 0x8007c001,
            /// <summary>
            /// Returns the parsing name relative to the parent folder.
            /// This name is not suitable for display in the UI.
            /// </summary>
            SIGDN_PARENTRELATIVEPARSING = 0x80018001,
            /// <summary>
            /// Returns the item's URL, if it has one.
            /// Some items do not have a URL, and in those cases the call fails.
            /// This name may be suitable for display in some cases.
            /// </summary>
            SIGDN_URL = 0x80068000,
            /// <summary>
            /// Returns the path relative to the parent folder in a format
            /// suitable for display in the UI.
            /// </summary>
            /// <remarks>
            /// <para><b>Warning:</b> Introduced in Windows 8. This value is not supported
            /// on earlier versions of Windows.</para>
            /// </remarks>
            [Obsolete("Introduced in Windows 8. Not supported on earlier versions.", false)]
            SIGDN_PARENTRELATIVEFORUI = 0x80094001
        }

        // https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_fileopendialogoptions
        [Flags]
        private enum FILEOPENDIALOGOPTIONS
        {
            /// <summary>
            /// When saving a file, prompts the user before overwriting an existing file
            /// of the same name. This is the default behavior for Save dialogs.
            /// </summary>
            FOS_OVERWRITEPROMPT = 0x2,
            /// <summary>
            /// In the Save dialog, restricts the user to selecting only file types
            /// whose extensions were specified using IFileDialog::SetFileTypes.
            /// </summary>
            FOS_STRICTFILETYPES = 0x4,
            /// <summary>
            /// Prevents the dialog from changing the application's current working directory.
            /// </summary>
            FOS_NOCHANGEDIR = 0x8,
            /// <summary>
            /// Displays an Open dialog that allows the user to select folders
            /// instead of files.
            /// </summary>
            FOS_PICKFOLDERS = 0x20,
            /// <summary>
            /// Ensures that returned items are file system items
            /// (SFGAO_FILESYSTEM).
            /// </summary>
            FOS_FORCEFILESYSTEM = 0x40,
            /// <summary>
            /// Allows selection of any item in the Shell namespace, including
            /// non-file-system items. This option cannot be combined with
            /// <see cref="FOS_FORCEFILESYSTEM"/>.
            /// </summary>
            FOS_ALLNONSTORAGEITEMS = 0x80,
            /// <summary>
            /// Disables validation of the selected item, such as checking for
            /// sharing violations or access denied errors.
            /// </summary>
            FOS_NOVALIDATE = 0x100,
            /// <summary>
            /// Enables the user to select multiple items in the Open dialog.
            /// When this option is set, results must be retrieved using
            /// IFileOpenDialog.
            /// </summary>
            FOS_ALLOWMULTISELECT = 0x200,
            /// <summary>
            /// Requires that the selected item be located in an existing folder.
            /// This is the default behavior.
            /// </summary>
            FOS_PATHMUSTEXIST = 0x800,
            /// <summary>
            /// Requires that the selected item already exists.
            /// This is the default behavior for Open dialogs.
            /// </summary>
            FOS_FILEMUSTEXIST = 0x1000,
            /// <summary>
            /// Prompts the user to create the item if it does not exist.
            /// This option does not actually create the item.
            /// </summary>
            FOS_CREATEPROMPT = 0x2000,
            /// <summary>
            /// Enables callback to the application through OnShareViolation
            /// when a sharing violation occurs, unless
            /// <see cref="FOS_NOVALIDATE"/> is specified.
            /// </summary>
            FOS_SHAREAWARE = 0x4000,
            /// <summary>
            /// Prevents read-only items from being returned.
            /// This is the default behavior for Save dialogs.
            /// </summary>
            FOS_NOREADONLYRETURN = 0x8000,
            /// <summary>
            /// Prevents testing whether the item can be successfully created.
            /// The calling application must handle any creation errors.
            /// </summary>
            FOS_NOTESTFILECREATE = 0x10000,
            /// <summary>
            /// Hides the list of recently used locations.
            /// This option is not supported on Windows 7 and later.
            /// </summary>
            FOS_HIDEMRUPLACES = 0x20000,
            /// <summary>
            /// Hides standard navigation pane locations.
            /// On Windows 7 and later, this includes Favorites, Libraries,
            /// Computer, and Network.
            /// </summary>
            FOS_HIDEPINNEDPLACES = 0x40000,
            /// <summary>
            /// Prevents shortcut (.lnk) files from being dereferenced to their targets,
            /// allowing the shortcut file itself to be selected.
            /// </summary>
            FOS_NODEREFERENCELINKS = 0x100000,
            /// <summary>
            /// Disables the OK button until the user navigates within the dialog
            /// or edits the file name.
            /// </summary>
            FOS_OKBUTTONNEEDSINTERACTION = 0x200000,
            /// <summary>
            /// Prevents the selected item from being added to the Recent Documents list.
            /// </summary>
            FOS_DONTADDTORECENT = 0x2000000,
            /// <summary>
            /// Forces hidden and system items to be displayed in the dialog.
            /// </summary>
            FOS_FORCESHOWHIDDEN = 0x10000000,
            /// <summary>
            /// Indicates that the Save As dialog should open in expanded mode.
            /// This option is not supported on Windows 7 and later.
            /// </summary>
            FOS_DEFAULTNOMINIMODE = 0x20000000,
            /// <summary>
            /// Forces the preview pane to be shown in the Open dialog.
            /// </summary>
            FOS_FORCEPREVIEWPANEON = 0x40000000,
            /// <summary>
            /// Indicates that the caller is opening the file as a stream
            /// (BHID_Stream), so the file does not need to be downloaded.
            /// </summary>
            FOS_SUPPORTSTREAMABLEITEMS = unchecked((int)0x80000000)
        }
        #endregion
    }
}