using Fishbone;
using SpineIDE.Views.Editor;
using System.Text;

using FishboneSignature = SpineIDE.Views.Editor.FishboneSignature;

namespace SpineIDE.Win32;

// the parameter tip shown while the caret is inside a call, like the Avalonia SpineIDE's.
// it marks the argument the caret is on, and whether that one needs out or ref
internal static partial class Program
{
    private const uint SCI_AUTOCACTIVE = 2102, SCI_CALLTIPACTIVE = 2202, SCI_CALLTIPSETHLT = 2204, SCI_CALLTIPSETFOREHLT = 2207;
    private const int SCN_UPDATEUI = 2007, SCN_CALLTIPCLICK = 2021;

    // the call the tip shows: the byte position of its '(' (-1 for none), its overloads, which
    // one is shown, and the argument the caret is on
    private static int _tipCall = -1;
    private static IReadOnlyList<FishboneSignature> _tipSignatures = [];
    private static int _tipOverload, _tipArgument;
    // Escape closes the tip until the caret reaches another argument or call
    private static bool _tipDismissed;
    // the diagnostic hover uses scintilla's one call tip too
    private static bool _tipIsDiagnostic;

    // ponytail: copies and lexes the script on every caret move. read only the text before
    // the caret if big scripts make the caret lag
    private static void UpdateSignatureHelp()
    {
        int caret = (int)Sci(SCI_GETCURRENTPOS);
        byte[] bytes = GetEditorBytes();
        string text = Encoding.UTF8.GetString(bytes);
        _analysis ??= Analyze(FishboneCompletionCatalog.Shared);
        if (_analysis?.CallAt(text, Encoding.UTF8.GetCharCount(bytes, 0, caret)) is not { } call)
        {
            HideSignatureHelp();
            return;
        }

        int openByte = Encoding.UTF8.GetByteCount(text.AsSpan(0, call.OpenParen));
        if (openByte == _tipCall)
        {
            // the same call. scintilla shows only one of the tip and the completion list, so the
            // tip waits while the list is open, and comes back after unless Escape closed it
            if (call.Argument != _tipArgument)
                _tipDismissed = false;
            if (Sci(SCI_AUTOCACTIVE) != 0)
                return;
            if (call.Argument != _tipArgument || (!_tipDismissed && Sci(SCI_CALLTIPACTIVE) == 0))
            {
                _tipArgument = call.Argument;
                ShowSignature();
            }
            return;
        }

        _tipCall = openByte;
        _tipDismissed = false;
        _tipSignatures = call.Signatures.Select(signature => FishboneCompletionCatalog.ToDisplay(call.Name, signature)).ToList();
        _tipArgument = call.Argument;
        // start on the first overload that has room for the argument the caret is on
        _tipOverload = Math.Max(0, _tipSignatures.ToList().FindIndex(signature => signature.Parameters.Count > call.Argument));
        ShowSignature();
    }

    private static void ShowSignature()
    {
        FishboneSignature signature = _tipSignatures[_tipOverload];
        var text = new StringBuilder();
        // \u0001 and \u0002 draw scintilla's up and down arrows, clicking them changes the overload
        if (_tipSignatures.Count > 1)
            text.Append($"\u0001 {_tipOverload + 1} of {_tipSignatures.Count} \u0002 ");
        text.Append(signature.Name).Append('(');

        int highlightStart = 0, highlightEnd = 0;
        for (int i = 0; i < signature.Parameters.Count; i++)
        {
            if (i > 0)
                text.Append(", ");
            FishboneParameter parameter = signature.Parameters[i];
            if (i == _tipArgument)
                highlightStart = Encoding.UTF8.GetByteCount(text.ToString());
            if (parameter.Direction != FishboneParamDirection.In)
                text.Append(parameter.DirectionKeyword).Append(' ');
            if (parameter.Type.Length > 0)
                text.Append(parameter.Type).Append(' ');
            text.Append(parameter.Name);
            if (i == _tipArgument)
                highlightEnd = Encoding.UTF8.GetByteCount(text.ToString());
        }
        text.Append(')');
        if (signature.ReturnType is { } returnType)
            text.Append(" : ").Append(returnType);

        // the current argument is blue, or orange when it has to be passed with out or ref
        bool byReference = _tipArgument < signature.Parameters.Count
            && signature.Parameters[_tipArgument].Direction != FishboneParamDirection.In;
        _tipIsDiagnostic = false;
        Sci(SCI_CALLTIPSETFOREHLT, byReference ? 0x0060E0 : 0xC05000); // 0x00bbggrr
        Sci(SCI_CALLTIPSHOW, _tipCall, Utf8(text.ToString()));
        Sci(SCI_CALLTIPSETHLT, highlightStart, highlightEnd);
    }

    // position 1 is the up arrow, 2 the down arrow
    private static void CycleOverload(int arrow)
    {
        if (_tipCall < 0 || _tipSignatures.Count < 2)
            return;
        _tipOverload = (_tipOverload + (arrow == 1 ? -1 : 1) + _tipSignatures.Count) % _tipSignatures.Count;
        ShowSignature();
    }

    private static void HideSignatureHelp()
    {
        if (_tipCall < 0)
            return;
        _tipCall = -1;
        if (!_tipIsDiagnostic)
            Sci(SCI_CALLTIPCANCEL);
    }
}
