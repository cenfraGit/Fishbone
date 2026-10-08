using Antlr4.Runtime;
using Fishbone;
using Fishbone.DebugClient;
using SpineIDE.Services;
using SpineIDE.Views.Editor;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpineIDE.Win32;

internal static partial class Program
{
    // scintilla messages and constants, values from Scintilla.h
    private const uint SCI_GETTEXTLENGTH = 2183, SCI_GETTEXT = 2182, SCI_SETTEXT = 2181, SCI_SETCODEPAGE = 2037,
        SCI_GETCURRENTPOS = 2008, SCI_GOTOPOS = 2025, SCI_REPLACESEL = 2170, SCI_GETLINECOUNT = 2154,
        SCI_LINEFROMPOSITION = 2166, SCI_LINELENGTH = 2350, SCI_GETLINE = 2153, SCI_WORDSTARTPOSITION = 2266,
        SCI_STYLECLEARALL = 2050, SCI_STYLESETFORE = 2051, SCI_STYLESETSIZE = 2055, SCI_STYLESETFONT = 2056,
        SCI_STARTSTYLING = 2032, SCI_SETSTYLINGEX = 2073, SCI_SETTABWIDTH = 2036, SCI_SETUSETABS = 2124,
        SCI_SETINDENT = 2122, SCI_SETLINEINDENTATION = 2126, SCI_GETLINEINDENTATION = 2127,
        SCI_GETLINEINDENTPOSITION = 2128, SCI_BRACEMATCH = 2353,
        SCI_SETMARGINTYPEN = 2240, SCI_SETMARGINWIDTHN = 2242, SCI_SETMARGINMASKN = 2244,
        SCI_SETMARGINSENSITIVEN = 2246, SCI_SETCARETLINEVISIBLE = 2096, SCI_SETCARETLINEBACK = 2098,
        SCI_MARKERDEFINE = 2040, SCI_MARKERSETFORE = 2041, SCI_MARKERSETBACK = 2042, SCI_MARKERADD = 2043,
        SCI_MARKERDELETE = 2044, SCI_MARKERDELETEALL = 2045, SCI_MARKERGET = 2046, SCI_MARKERNEXT = 2047,
        SCI_SETFOLDLEVEL = 2222, SCI_SETAUTOMATICFOLD = 2663, SCI_ENSUREVISIBLEENFORCEPOLICY = 2234,
        SCI_AUTOCSHOW = 2100, SCI_AUTOCSETIGNORECASE = 2115, SCI_AUTOCSETORDER = 2660;
    private const int SCN_STYLENEEDED = 2000, SCN_CHARADDED = 2001, SCN_MARGINCLICK = 2010, STYLE_DEFAULT = 32;
    private const int SC_FOLDLEVELBASE = 0x400, SC_FOLDLEVELHEADERFLAG = 0x2000;
    private const byte StyleText = 0, StyleComment = 1, StyleKeyword = 2, StyleString = 3, StyleNumber = 4;
    private const int MarkerBreakpoint = 0, MarkerCurrentArrow = 1, MarkerCurrentLine = 2;
    private const int MarginNumbers = 0, MarginBreakpoints = 1, MarginFolding = 2, IndentSize = 4;

    // --------------------------------------------------------------------------------
    // scintilla
    // --------------------------------------------------------------------------------

    private static IntPtr Sci(uint msg, nint wParam = 0, nint lParam = 0) => SendMessageW(_editor, msg, wParam, lParam);

    private static IntPtr Sci(uint msg, nint wParam, byte[] lParam) => SendMessageW(_editor, msg, wParam, lParam);

    private static void SetupEditor()
    {
        Sci(SCI_SETCODEPAGE, 65001); // utf-8
        Sci(SCI_STYLESETFONT, STYLE_DEFAULT, Utf8("Consolas"));
        Sci(SCI_STYLESETSIZE, STYLE_DEFAULT, 11);
        Sci(SCI_STYLECLEARALL);

        // colors are 0x00bbggrr
        Sci(SCI_STYLESETFORE, StyleComment, 0x008000);
        Sci(SCI_STYLESETFORE, StyleKeyword, 0xFF0000);
        Sci(SCI_STYLESETFORE, StyleString, 0x1515A3);
        Sci(SCI_STYLESETFORE, StyleNumber, 0x588609);

        Sci(SCI_SETTABWIDTH, IndentSize);
        Sci(SCI_SETINDENT, IndentSize);
        Sci(SCI_SETUSETABS, 0);
        Sci(SCI_SETCARETLINEVISIBLE, 1);
        Sci(SCI_SETCARETLINEBACK, 0xF5F5F5);

        Sci(SCI_SETMARGINTYPEN, MarginNumbers, 1); // SC_MARGIN_NUMBER
        Sci(SCI_SETMARGINWIDTHN, MarginNumbers, 44);

        // breakpoint margin, clickable, shows the breakpoint dot and the current line arrow
        Sci(SCI_SETMARGINTYPEN, MarginBreakpoints, 0); // SC_MARGIN_SYMBOL
        Sci(SCI_SETMARGINWIDTHN, MarginBreakpoints, 16);
        Sci(SCI_SETMARGINMASKN, MarginBreakpoints, (1 << MarkerBreakpoint) | (1 << MarkerCurrentArrow));
        Sci(SCI_SETMARGINSENSITIVEN, MarginBreakpoints, 1);
        Sci(SCI_MARKERDEFINE, MarkerBreakpoint, 0); // SC_MARK_CIRCLE
        Sci(SCI_MARKERSETFORE, MarkerBreakpoint, 0x0000C0);
        Sci(SCI_MARKERSETBACK, MarkerBreakpoint, 0x0000E0);
        Sci(SCI_MARKERDEFINE, MarkerCurrentArrow, 4); // SC_MARK_SHORTARROW
        Sci(SCI_MARKERSETFORE, MarkerCurrentArrow, 0x000000);
        Sci(SCI_MARKERSETBACK, MarkerCurrentArrow, 0x00D7FF);
        Sci(SCI_MARKERDEFINE, MarkerCurrentLine, 22); // SC_MARK_BACKGROUND
        Sci(SCI_MARKERSETBACK, MarkerCurrentLine, 0xC0F8FF);

        // fold margin. scintilla draws the tree and handles clicks itself once fold levels are set
        Sci(SCI_SETMARGINTYPEN, MarginFolding, 0);
        Sci(SCI_SETMARGINWIDTHN, MarginFolding, 16);
        Sci(SCI_SETMARGINMASKN, MarginFolding, unchecked((int)0xFE000000)); // SC_MASK_FOLDERS
        Sci(SCI_SETMARGINSENSITIVEN, MarginFolding, 1);
        // fold marker numbers 25..31 paired with their box-tree symbols
        (int Marker, int Symbol)[] foldMarkers = [(31, 14), (30, 12), (29, 9), (28, 10), (27, 11), (26, 15), (25, 13)];
        foreach ((int marker, int symbol) in foldMarkers)
        {
            Sci(SCI_MARKERDEFINE, marker, symbol);
            Sci(SCI_MARKERSETFORE, marker, 0xFFFFFF);
            Sci(SCI_MARKERSETBACK, marker, 0x808080);
        }
        Sci(SCI_SETAUTOMATICFOLD, 7); // show, click, change

        Sci(SCI_AUTOCSETIGNORECASE, 1);
        Sci(SCI_AUTOCSETORDER, 1); // scintilla sorts the list for us
    }

    private static void OnEditorNotification(SCNotification notification)
    {
        switch (notification.code)
        {
            case SCN_STYLENEEDED:
                Highlight();
                break;
            case SCN_SAVEPOINTREACHED or SCN_SAVEPOINTLEFT:
                UpdateTitle();
                break;
            case SCN_MARGINCLICK when notification.margin == MarginBreakpoints:
                ToggleBreakpoint((int)Sci(SCI_LINEFROMPOSITION, notification.position));
                break;
            case SCN_CHARADDED when notification.ch == '\n':
                IndentNewLine();
                break;
            case SCN_CHARADDED when notification.ch == '}':
                DedentClosingBrace();
                break;
            case SCN_CHARADDED when char.IsLetter((char)notification.ch) || notification.ch == '_':
                ShowCompletion(forced: false);
                break;
        }
    }

    private static byte[] GetEditorBytes()
    {
        int length = (int)Sci(SCI_GETTEXTLENGTH);
        var buffer = new byte[length + 1];
        Sci(SCI_GETTEXT, buffer.Length, buffer);
        Array.Resize(ref buffer, length);
        return buffer;
    }

    private static string GetLine(int line)
    {
        int length = (int)Sci(SCI_LINELENGTH, line);
        var buffer = new byte[length + 1];
        Sci(SCI_GETLINE, line, buffer);
        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    private static int CaretLine() => (int)Sci(SCI_LINEFROMPOSITION, Sci(SCI_GETCURRENTPOS));

    // ponytail: relexes the whole document on every edit. measured about 2 ms per
    // keystroke at 5k lines, restyle from the edited line onwards if it ever lags
    private static void Highlight()
    {
        byte[] bytes = GetEditorBytes();
        string text = Encoding.UTF8.GetString(bytes);

        // antlr indexes by code point but scintilla by utf-8 byte, so map one to the other
        var byteOffsets = new List<int>(text.Length + 1);
        int offset = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            byteOffsets.Add(offset);
            offset += rune.Utf8SequenceLength;
        }
        byteOffsets.Add(offset);

        // the lexer skips comments and whitespace, so paint everything as a comment
        // first and let the tokens paint over it. whitespace has no visible color
        var styles = new byte[bytes.Length];
        Array.Fill(styles, StyleComment);

        // net braces opened per line, for folding. counting tokens rather than characters
        // means braces inside strings and comments are ignored
        int lineCount = (int)Sci(SCI_GETLINECOUNT);
        var braceDelta = new int[lineCount];

        var lexer = new FishboneLexer(CharStreams.fromString(text));
        lexer.RemoveErrorListeners();
        for (IToken token = lexer.NextToken(); token.Type != TokenConstants.EOF; token = lexer.NextToken())
        {
            int start = byteOffsets[token.StartIndex];
            int end = byteOffsets[token.StopIndex + 1];
            Array.Fill(styles, StyleFor(lexer.Vocabulary, token.Type), start, end - start);

            if (token.Text == "{")
                braceDelta[token.Line - 1]++;
            else if (token.Text == "}")
                braceDelta[token.Line - 1]--;
        }

        Sci(SCI_STARTSTYLING, 0);
        Sci(SCI_SETSTYLINGEX, styles.Length, styles);

        // a line sits at the depth it starts at, and is a fold header when it opens a block
        int depth = 0;
        for (int line = 0; line < lineCount; line++)
        {
            int level = SC_FOLDLEVELBASE + depth;
            if (braceDelta[line] > 0)
                level |= SC_FOLDLEVELHEADERFLAG;
            Sci(SCI_SETFOLDLEVEL, line, level);
            depth = Math.Max(0, depth + braceDelta[line]);
        }

    }

    private static byte StyleFor(IVocabulary vocabulary, int tokenType)
    {
        // keywords are the tokens whose literal is a word, like 'while'. operators are symbols like '+'
        string? literal = vocabulary.GetLiteralName(tokenType);
        if (literal is { Length: > 2 } && char.IsLetter(literal[1]))
            return StyleKeyword;

        return tokenType switch
        {
            FishboneLexer.STRING or FishboneLexer.RAW_STRING or FishboneLexer.INTERP_STRING => StyleString,
            FishboneLexer.INT or FishboneLexer.DOUBLE => StyleNumber,
            _ => StyleText,
        };
    }

    private static void IndentNewLine()
    {
        int line = CaretLine();
        if (line == 0)
            return;
        string indent = FishboneEditorIndentation.IndentForNewLine(GetLine(line - 1).TrimEnd('\r', '\n'), IndentSize);
        Sci(SCI_REPLACESEL, 0, Utf8(indent));
    }

    private static void DedentClosingBrace()
    {
        int bracePosition = (int)Sci(SCI_GETCURRENTPOS) - 1;
        int line = (int)Sci(SCI_LINEFROMPOSITION, bracePosition);
        string lineBeforeBrace = GetLine(line).TrimEnd('\r', '\n').TrimEnd('}');
        if (!FishboneEditorIndentation.ShouldDedentClosingBrace(lineBeforeBrace))
            return;

        // line the brace up with its opening brace. braces only match when their styles
        // match, so ones inside strings and comments are skipped
        int opening = (int)Sci(SCI_BRACEMATCH, bracePosition);
        int indent = opening >= 0
            ? (int)Sci(SCI_GETLINEINDENTATION, Sci(SCI_LINEFROMPOSITION, opening))
            : Math.Max(0, (int)Sci(SCI_GETLINEINDENTATION, line) - IndentSize);
        Sci(SCI_SETLINEINDENTATION, line, indent);
        Sci(SCI_GOTOPOS, Sci(SCI_GETLINEINDENTPOSITION, line) + 1);
    }

    private static void ShowCompletion(bool forced)
    {
        int caret = (int)Sci(SCI_GETCURRENTPOS);
        int wordStart = (int)Sci(SCI_WORDSTARTPOSITION, caret, 1);
        int typed = caret - wordStart;

        // open on the first letter of a word. after that scintilla narrows the list as you type
        if (!forced && typed != 1)
            return;

        byte[] bytes = GetEditorBytes();
        string prefix = Encoding.UTF8.GetString(bytes, wordStart, typed);
        FishboneCompletionCatalog catalog = FishboneCompletionCatalog.Shared;

        IEnumerable<FishboneCompletionItem> items = catalog.Keywords
            .Concat(FishboneLocalSymbolScanner.Scan(Encoding.UTF8.GetString(bytes, 0, caret)));
        items = items.Concat(prefix.Length > 0
            ? catalog.GlobalsByInitial.GetValueOrDefault(char.ToLowerInvariant(prefix[0]), [])
            : catalog.Globals);

        // scintilla only moves the selection as you type, it never hides entries, so keep the list to the prefix
        string list = string.Join(' ', items
            .Select(item => item.Text)
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !name.Contains(' '))
            .Distinct());
        if (list.Length > 0)
            Sci(SCI_AUTOCSHOW, typed, Utf8(list));
    }

    private static void ToggleBreakpoint(int line)
    {
        if (((int)Sci(SCI_MARKERGET, line) & (1 << MarkerBreakpoint)) != 0)
            Sci(SCI_MARKERDELETE, line, MarkerBreakpoint);
        else
            Sci(SCI_MARKERADD, line, MarkerBreakpoint);

        // the session ignores this when nothing is being debugged
        _ = _session.UpdateBreakpointsAsync(BreakpointLines());
    }

    // markers move with the text as lines are edited, so they are the source of truth
    private static List<int> BreakpointLines()
    {
        var lines = new List<int>();
        for (int line = (int)Sci(SCI_MARKERNEXT, 0, 1 << MarkerBreakpoint); line >= 0;
             line = (int)Sci(SCI_MARKERNEXT, line + 1, 1 << MarkerBreakpoint))
            lines.Add(line + 1); // dap lines are 1-based
        return lines;
    }

    private static void ShowCurrentLine(int line)
    {
        Sci(SCI_MARKERDELETEALL, MarkerCurrentArrow);
        Sci(SCI_MARKERDELETEALL, MarkerCurrentLine);
        if (line < 0)
            return;
        Sci(SCI_MARKERADD, line, MarkerCurrentArrow);
        Sci(SCI_MARKERADD, line, MarkerCurrentLine);
        Sci(SCI_ENSUREVISIBLEENFORCEPOLICY, line);
    }

    // scintilla takes nul-terminated utf-8 strings, not utf-16
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

}
