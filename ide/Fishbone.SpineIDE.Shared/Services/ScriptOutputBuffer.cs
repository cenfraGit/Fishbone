using System;
using System.Text;

namespace SpineIDE.Panels;

public sealed class ScriptOutputBuffer
{
    private readonly object _sync = new();
    private readonly StringBuilder _pending = new();

    public void Append(object? value)
    {
        AppendText(value?.ToString() ?? string.Empty);
    }

    public void AppendLine(object? value)
    {
        AppendText((value?.ToString() ?? string.Empty) + Environment.NewLine);
    }

    public string DrainPending()
    {
        lock (_sync)
        {
            if (_pending.Length == 0)
                return string.Empty;

            string output = _pending.ToString();
            _pending.Clear();
            return output;
        }
    }

    private void AppendText(string text)
    {
        if (text.Length == 0)
            return;

        lock (_sync)
            _pending.Append(text);
    }
}