using System.Runtime.InteropServices;
using System.Text;
using SpineIDE.Services;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint SCI_SETSAVEPOINT = 2014, SCI_GETMODIFY = 2159, SCI_EMPTYUNDOBUFFER = 2175;
    private const int SCN_SAVEPOINTREACHED = 2002, SCN_SAVEPOINTLEFT = 2003;
    private const uint MB_YESNOCANCEL = 0x3, MB_ICONWARNING = 0x30;
    private const int IDYES = 6, IDNO = 7;
    private const string FileFilter = "Fishbone scripts (*.fb)\0*.fb\0All files (*.*)\0*.*\0\0";

    // null until the script is saved or opened from disk
    private static string? _filePath;

    // set while attached, to the name of the host's script
    private static string? _remoteName;

    private static bool IsModified => Sci(SCI_GETMODIFY) != 0;

    private static void LoadDocument(string text, string? path)
    {
        _analysis = null;
        Sci(SCI_SETTEXT, 0, Utf8(text));
        Sci(SCI_EMPTYUNDOBUFFER);
        Sci(SCI_SETSAVEPOINT);
        _filePath = path;
        UpdateTitle();
    }

    private static void UpdateTitle()
    {
        if (_remoteName is not null)
        {
            SetWindowTextW(_window, $"[Remote] {_remoteName} - SpineIDE");
            return;
        }
        string name = _filePath is null ? "untitled" : Path.GetFileName(_filePath);
        SetWindowTextW(_window, $"{name}{(IsModified ? "*" : "")} - SpineIDE");
    }

    private static void NewFile()
    {
        if (ConfirmDiscard())
            LoadDocument("", null);
    }

    private static void OpenFile()
    {
        if (!ConfirmDiscard())
            return;
        string? path = ShowFileDialog(save: false);
        if (path is not null)
            LoadDocument(File.ReadAllText(path), path);
    }

    private static void OpenSample(int index)
    {
        if (ConfirmDiscard())
            LoadDocument(SampleCatalog.Load(SampleCatalog.Samples[index].FileName), null);
    }

    /// <summary>Saves to the current file, or asks for one. False when the user cancels.</summary>
    private static bool Save() => _filePath is null ? SaveAs() : WriteTo(_filePath);

    private static bool SaveAs()
    {
        string? path = ShowFileDialog(save: true);
        return path is not null && WriteTo(path);
    }

    private static bool WriteTo(string path)
    {
        File.WriteAllBytes(path, GetEditorBytes());
        _filePath = path;
        Sci(SCI_SETSAVEPOINT);
        UpdateTitle();
        return true;
    }

    // asks before throwing away unsaved changes. false means stay
    private static bool ConfirmDiscard()
    {
        if (!IsModified)
            return true;
        string name = _filePath is null ? "the new script" : Path.GetFileName(_filePath);
        int answer = MessageBoxW(_window, $"Save the changes to {name}?", "SpineIDE", MB_YESNOCANCEL | MB_ICONWARNING);
        return answer == IDNO || (answer == IDYES && Save());
    }

    private static string? ShowFileDialog(bool save)
    {
        const int bufferChars = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(bufferChars * 2);
        try
        {
            // the dialog starts from the buffer's text, so start it empty
            Marshal.WriteInt16(buffer, 0);
            var dialog = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = _window,
                lpstrFilter = FileFilter,
                lpstrFile = buffer,
                nMaxFile = bufferChars,
                lpstrDefExt = "fb",
                // OFN_EXPLORER, plus OFN_OVERWRITEPROMPT to save, or OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST to open
                Flags = 0x80000 | (save ? 0x2 : 0x800 | 0x1000)
            };
            bool chosen = save ? GetSaveFileNameW(ref dialog) : GetOpenFileNameW(ref dialog);
            return chosen ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
