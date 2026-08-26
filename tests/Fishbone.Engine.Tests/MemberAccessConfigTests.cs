using Fishbone.Core;

namespace Fishbone.Engine.Tests;

public class MemberAccessConfigTests
{
    [Fact]
    public void Run_MemberAccessDisabled_PropertyReadIsRuntimeError()
    {
        var config = new FishboneConfiguration { EnableMemberAccess = false };

        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("""
let xs = [1, 2, 3];
let n = xs.Count;
""", config));

        Assert.Contains("disabled by the host configuration", exception.Message);
        Assert.Equal(2, exception.Line);
    }

    [Fact]
    public void Run_MemberAccessDisabled_MethodCallIsRuntimeError()
    {
        var config = new FishboneConfiguration { EnableMemberAccess = false };

        var exception = Assert.Throws<FishboneRuntimeException>(
            () => FishboneProgram.Run("""let s = "abc".ToUpper();""", config));

        Assert.Contains("disabled by the host configuration", exception.Message);
    }

    [Fact]
    public void Run_MemberAccessDisabled_CuratedSurfaceStillWorks()
    {
        // indexing, loops, operators, and registered functions are the intended surface
        var config = new FishboneConfiguration { EnableMemberAccess = false }
            .AddBuiltIn("count", new Func<List<object>, int>(xs => xs.Count));

        var env = FishboneProgram.Run("""
let xs = [10, 20, 30];
let total = 0;
foreach (x in xs) { total += x; }
let n = count(xs);
let first = xs[0];
let msg = $"n={n} total={total}";
""", config);

        Assert.Equal(60, env.GetValue("total"));
        Assert.Equal(3, env.GetValue("n"));
        Assert.Equal(10, env.GetValue("first"));
        Assert.Equal("n=3 total=60", env.GetValue("msg"));
    }

    [Fact]
    public void Clone_CopiesEnableMemberAccess()
    {
        var config = new FishboneConfiguration { EnableMemberAccess = false };
        Assert.False(config.Clone().EnableMemberAccess);
    }
}
