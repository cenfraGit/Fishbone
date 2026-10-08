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

    // ponytail: copies the whole script on every caret move inside a call. read only the text
    // before the caret if big scripts make the caret lag
    private static void UpdateSignatureHelp()
    {
        int caret = (int)Sci(SCI_GETCURRENTPOS);
        string before = Encoding.UTF8.GetString(GetEditorBytes(), 0, caret);
        if (FindCall(before) is not var (open, argument))
        {
            HideSignatureHelp();
            return;
        }

        int openByte = Encoding.UTF8.GetByteCount(before.AsSpan(0, open));
        if (openByte == _tipCall)
        {
            // the same call. scintilla shows only one of the tip and the completion list, so the
            // tip waits while the list is open, and comes back after unless Escape closed it
            if (argument != _tipArgument)
                _tipDismissed = false;
            if (Sci(SCI_AUTOCACTIVE) != 0)
                return;
            if (argument != _tipArgument || (!_tipDismissed && Sci(SCI_CALLTIPACTIVE) == 0))
            {
                _tipArgument = argument;
                ShowSignature();
            }
            return;
        }

        if (SignaturesFor(before[..open]) is not { Count: > 0 } signatures)
        {
            HideSignatureHelp();
            return;
        }
        _tipCall = openByte;
        _tipDismissed = false;
        _tipSignatures = signatures;
        _tipArgument = argument;
        // start on the first overload that has room for the argument the caret is on
        _tipOverload = Math.Max(0, signatures.ToList().FindIndex(signature => signature.Parameters.Count > argument));
        ShowSignature();
    }

    // the '(' of the call the caret is inside, and how many arguments come before the caret.
    // a call can't hold a ';', so the search stops at one
    private static (int Open, int Argument)? FindCall(string text)
    {
        int depth = 0, open = -1;
        for (int i = text.Length - 1; i >= 0 && open < 0; i--)
        {
            char c = text[i];
            if (c is ')' or ']' or '}')
                depth++;
            else if (c is '(' or '[' or '{')
            {
                // an open list, dictionary or block means the caret isn't on a call's argument
                if (depth > 0)
                    depth--;
                else if (c == '(')
                    open = i;
                else
                    return null;
            }
            else if (depth == 0 && c == ';')
                return null;
        }
        if (open < 0)
            return null;

        int nesting = 0, argument = 0;
        foreach (char c in text.AsSpan(open + 1))
        {
            if (c is '(' or '[' or '{')
                nesting++;
            else if (c is ')' or ']' or '}')
                nesting--;
            else if (c == ',' && nesting == 0)
                argument++;
        }
        return (open, argument);
    }

    // the overloads of whatever is called: a method on a known type, a script function, or a
    // function or type from the configuration. a script name hides the configuration's
    private static IReadOnlyList<FishboneSignature>? SignaturesFor(string text)
    {
        string trimmed = text.TrimEnd();
        string callee = ExpressionBefore(trimmed);
        if (callee.Length == 0 || char.IsDigit(callee[0]))
            return null;

        int start = trimmed.Length - callee.Length;
        int line = trimmed.AsSpan(0, start).Count('\n') + 1;
        int column = start - (trimmed.LastIndexOf('\n', Math.Max(0, start - 1)) + 1) + 1;
        FishboneCompletionCatalog catalog = FishboneCompletionCatalog.Shared;
        _analysis ??= Analyze(catalog);

        int dot = callee.LastIndexOf('.');
        if (dot >= 0)
        {
            string name = callee[(dot + 1)..];
            if (catalog.Description is not { } description || _analysis?.TypeOf(callee[..dot], line, column) is not { } type)
                return null;
            return description.Members(type.Type, type.IsStatic)
                .FirstOrDefault(member => member.Name == name && member.Kind == FishboneMemberKind.Method)?
                .Signatures.Select(signature => FishboneCompletionCatalog.ToDisplay(name, signature))
                .ToList();
        }

        if (_analysis?.VisibleAt(line, column).FirstOrDefault(variable => variable.Name == callee) is { } script)
            return script.Parameters is null ? null :
            [
                new FishboneSignature(callee, script.Parameters
                    .Select(parameter => new FishboneParameter(parameter, "", FishboneParamDirection.In)).ToList(), null)
            ];
        return catalog.Signatures.GetValueOrDefault(callee);
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
