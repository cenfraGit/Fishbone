using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111.Tests;

/// <summary>A test that needs HALCON itself, and skips where it isn't installed, like CI.</summary>
public sealed class HalconFactAttribute : FactAttribute
{
    private static readonly bool Available = CheckAvailable();

    public HalconFactAttribute()
    {
        if (!Available)
            Skip = "HALCON is not installed";
    }

    private static bool CheckAvailable()
    {
        try
        {
            new HDevEngine().Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// <see cref="HalconProcedurePlugin"/> against the real engine, with procedures written into a
/// temporary folder per test.
/// </summary>
public sealed class HalconProcedurePluginTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fishbone-hdvp-").FullName;

    // HALCON keeps loaded procedures by name for the whole process, and the tests reuse names
    public HalconProcedurePluginTests()
    {
        if (new HalconFactAttribute().Skip is null)
            HalconProcedurePlugin.Reload();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [HalconFact]
    public void Procedure_TakesInputsAndWritesOutputs()
    {
        string folder = Folder("a");
        Procedure(folder, "add_one", ["X"], ["Y"], "<l>Y := X + 1</l>");

        Assert.Equal("42", Run(folder, "let y = 0; add_one(41, out y);", "y"));
    }

    // registering again is cheap because it reuses what HALCON loaded, so it doesn't see edits
    [HalconFact]
    public void ReplacedFile_IsKeptUntilReload()
    {
        string folder = Folder("a");
        Procedure(folder, "foo", [], ["Result"], "<l>Result := 1</l>");
        Assert.Equal("1", Run(folder, "let r = 0; foo(out r);", "r"));

        Procedure(folder, "foo", [], ["Result"], "<l>Result := 2</l>");
        Assert.Equal("1", Run(folder, "let r = 0; foo(out r);", "r"));

        HalconProcedurePlugin.Reload(folder);
        Assert.Equal("2", Run(folder, "let r = 0; foo(out r);", "r"));
    }

    [HalconFact]
    public void ReplacedHelper_IsReadFreshAfterReload()
    {
        string folder = Folder("a");
        Procedure(folder, "helper", [], ["Result"], "<l>Result := 1</l>");
        Procedure(folder, "uses_helper", [], ["Result"], "<l>helper (R)</l>\n<l>Result := R + 10</l>");
        Assert.Equal("11", Run(folder, "let r = 0; uses_helper(out r);", "r"));

        Procedure(folder, "helper", [], ["Result"], "<l>Result := 2</l>");
        HalconProcedurePlugin.Reload(folder);

        Assert.Equal("12", Run(folder, "let r = 0; uses_helper(out r);", "r"));
    }

    [HalconFact]
    public void AddedFile_IsFoundAfterReload()
    {
        string folder = Folder("a");
        Procedure(folder, "foo", [], ["Result"], "<l>Result := 1</l>");
        Run(folder, "let r = 0; foo(out r);", "r");

        Procedure(folder, "bar", [], ["Result"], "<l>Result := 3</l>");
        HalconProcedurePlugin.Reload(folder);

        Assert.Equal("3", Run(folder, "let r = 0; bar(out r);", "r"));
    }

    [HalconFact]
    public void RegisteringAgain_IsFasterThanTheFirstTime()
    {
        string folder = Folder("a");
        for (int i = 0; i < 10; i++)
            Procedure(folder, $"proc_{i}", ["X"], ["Y"], "<l>Y := X + 1</l>");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        new FishboneConfiguration().AddPlugin(new HalconProcedurePlugin(folder));
        TimeSpan first = clock.Elapsed;
        clock.Restart();
        for (int i = 0; i < 5; i++)
            new FishboneConfiguration().AddPlugin(new HalconProcedurePlugin(folder));
        TimeSpan again = clock.Elapsed / 5;

        // the first loads every procedure, the others reuse them
        Assert.True(again < first, $"first {first.TotalMilliseconds} ms, again {again.TotalMilliseconds} ms");
    }

    [HalconFact]
    public void TwoFolders_EachConfigurationCallsItsOwn_WithAReloadBetween()
    {
        string a = Folder("a"), b = Folder("b");
        Procedure(a, "foo", [], ["Result"], "<l>Result := 1</l>");
        Procedure(b, "foo", [], ["Result"], "<l>Result := 9</l>");

        var first = new FishboneConfiguration().AddPlugin(new HalconProcedurePlugin(a));
        // HALCON knows foo by name, so the other folder's foo needs a reload first
        HalconProcedurePlugin.Reload(b);
        var second = new FishboneConfiguration().AddPlugin(new HalconProcedurePlugin(b));

        // the first configuration still calls its own copy after the second one was built
        Assert.Equal("1", FishboneProgram.Run("let r = 0; foo(out r);", first).GetValue("r").ToString());
        Assert.Equal("9", FishboneProgram.Run("let r = 0; foo(out r);", second).GetValue("r").ToString());
    }

    [HalconFact]
    public void BrokenFile_FailsWhenCalled_AndTheOthersStillLoad()
    {
        string folder = Folder("a");
        Procedure(folder, "good", [], ["Result"], "<l>Result := 1</l>");
        File.WriteAllText(Path.Combine(folder, "broken.hdvp"), "<not xml");
        var config = new FishboneConfiguration().AddPlugin(new HalconProcedurePlugin(folder));

        Assert.Equal("1", FishboneProgram.Run("let r = 0; good(out r);", config).GetValue("r").ToString());
        var error = Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("broken();", config));
        Assert.Contains("'broken' could not be loaded", error.Message);
        Assert.Contains("broken.hdvp", error.Message);
    }

    // checked before HALCON is used, so it runs without it
    [Fact]
    public void NameClash_ThrowsBeforeRegisteringAnything()
    {
        string folder = Folder("a");
        Procedure(folder, "println", [], ["Result"], "<l>Result := 1</l>");
        Procedure(folder, "other", [], ["Result"], "<l>Result := 2</l>");
        var config = new FishboneConfiguration().AddBuiltIn("println", (Action<object?>)(_ => { }));

        var error = Assert.Throws<FishboneConfigurationException>(() => config.AddPlugin(new HalconProcedurePlugin(folder)));

        Assert.Contains("println", error.Message);
        Assert.Equal(["println"], error.Names);
        Assert.False(config.BuiltIns.ContainsKey("other"));
    }

    // checked before HALCON is used, so it runs without it
    [Fact]
    public void ProcedureNamedLikeAMathBuiltIn_Throws()
    {
        string folder = Folder("a");
        Procedure(folder, "round", [], ["Result"], "<l>Result := 1</l>");
        var config = new FishboneConfiguration().AddPlugin(new Fishbone.Plugins.Math.MathPlugin());

        var error = Assert.Throws<FishboneConfigurationException>(() => config.AddPlugin(new HalconProcedurePlugin(folder)));

        Assert.Contains("round", error.Message);
        Assert.Equal(["round"], error.Names);
    }

    // checked before HALCON is used, so it runs without it
    [Fact]
    public void MissingFolder_Throws()
    {
        string missing = Path.Combine(_root, "missing");

        var error = Assert.Throws<FishboneConfigurationException>(() => new FishboneConfiguration().AddPlugin(new HalconProcedurePlugin(missing)));

        Assert.Contains(missing, error.Message);
        Assert.Empty(error.Names);
    }

    [HalconFact]
    public void ProtectedProcedure_RunsWithoutThePassword_LikeItsPlainCopy()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Procedures");
        HOperatorSet.GenImageGrayRamp(out HObject ramp, 1, 1, 128, 128, 128, 256, 256);
        HOperatorSet.ConvertImageType(ramp, out HObject image, "byte");

        int Area(string folder, out FishboneSignature signature)
        {
            var config = new FishboneConfiguration().AddPlugin(new HalconProcedurePlugin(Path.Combine(fixtures, folder)));
            config.AddValue("image", image);
            signature = Assert.Single(Assert.Single(config.Describe().Symbols, symbol => symbol.Name == "myTestProcedure").Signatures);
            var region = (HObject)FishboneProgram.Run("let region = null; myTestProcedure(image, out region, 200);", config).GetValue("region");
            HOperatorSet.AreaCenter(region, out HTuple area, out _, out _);
            return area.I;
        }

        int plain = Area("plain", out var plainSignature);
        int locked = Area("protected", out var lockedSignature);

        Assert.True(plain > 0);
        Assert.Equal(plain, locked);
        Assert.Equal(
            plainSignature.Parameters.Select(parameter => (parameter.Name, parameter.Direction)),
            lockedSignature.Parameters.Select(parameter => (parameter.Name, parameter.Direction)));
    }

    [HalconFact]
    public void Procedure_CanCallAProcedureFromAnotherFolder()
    {
        string library = Folder("library"), project = Folder("project");
        Procedure(library, "lib_helper", [], ["Result"], "<l>Result := 100</l>");
        Procedure(project, "caller", [], ["Result"], "<l>lib_helper (R)</l>\n<l>Result := R + 1</l>");

        var config = new FishboneConfiguration().AddPlugin(new HalconProcedurePlugin(library, project));

        Assert.Equal("101", FishboneProgram.Run("let r = 0; caller(out r);", config).GetValue("r").ToString());
    }

    // checked before HALCON is used, so it runs without it
    [Fact]
    public void SameNameInTwoFolders_Throws()
    {
        string a = Folder("a"), b = Folder("b");
        Procedure(a, "foo", [], ["Result"], "<l>Result := 1</l>");
        Procedure(b, "foo", [], ["Result"], "<l>Result := 2</l>");

        var error = Assert.Throws<FishboneConfigurationException>(() => new FishboneConfiguration().AddPlugin(new HalconProcedurePlugin(a, b)));

        Assert.Contains("foo", error.Message);
        Assert.Equal(["foo"], error.Names);
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private static string Run(string folder, string code, string variable)
    {
        var config = new FishboneConfiguration().AddPlugin(new HalconProcedurePlugin(folder));
        return FishboneProgram.Run(code, config).GetValue(variable).ToString()!;
    }

    // writes a procedure with control inputs and outputs, in the format HDevelop saves
    private static void Procedure(string folder, string name, string[] inputs, string[] outputs, string body)
    {
        static string Bucket(string tag, string[] names) => names.Length == 0 ? "" :
            $"<{tag}>\n" + string.Concat(names.Select(n => $"<par name=\"{n}\" base_type=\"ctrl\" dimension=\"0\"/>\n")) + $"</{tag}>\n";

        File.WriteAllText(Path.Combine(folder, name + ".hdvp"), $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <hdevelop file_version="1.2" halcon_version="24.11.1.0">
            <procedure name="{name}">
            <interface>
            {Bucket("ic", inputs)}{Bucket("oc", outputs)}</interface>
            <body>
            {body}
            <l>return ()</l>
            </body>
            <docu id="{name}">
            <parameters>
            {string.Concat(inputs.Concat(outputs).Select(n => $"<parameter id=\"{n}\"/>\n"))}</parameters>
            </docu>
            </procedure>
            </hdevelop>
            """);
    }
}
