using System.Diagnostics;

namespace Fishbone.Tests;

/// <summary>The <c>spine</c> command line runner, started as its own process like a shell would.</summary>
public class CliTests
{
    [Theory]
    [InlineData("println(1 + 1);\nlet x = missing;", "line 2")]
    [InlineData("let x = ;", "line 1")]
    public async Task FailingScript_ExitsWithOne_AndSaysWhere(string code, string where)
    {
        var (exitCode, _, error) = await Spine(code);

        Assert.Equal(1, exitCode);
        Assert.Contains(where, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkingScript_ExitsWithZero_AndPrints()
    {
        var (exitCode, output, _) = await Spine("println(\"hi \" + (40 + 2));");

        Assert.Equal(0, exitCode);
        Assert.Equal("hi 42", output.Trim());
    }

    private static async Task<(int ExitCode, string Output, string Error)> Spine(string code)
    {
        string script = Path.Combine(Path.GetTempPath(), $"spine-{Guid.NewGuid():N}.fb");
        File.WriteAllText(script, code);
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "spine.dll"));
            start.ArgumentList.Add(script);

            using var process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            File.Delete(script);
        }
    }
}
