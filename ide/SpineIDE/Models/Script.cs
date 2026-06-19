using System.IO;

namespace SpineIDE.Models;

public class Script
{
    public string Name { get; set; }
    public string? Path { get; set; }
    public string Code { get; set; }
    public string? Directory => System.IO.Path.GetDirectoryName(this.Path);

    public Script(string name, string? path, string code)
    {
        this.Name = name;
        this.Path = path;
        this.Code = code;
    }
}