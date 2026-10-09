using Fishbone.Debugging;
using System.IO.Pipes;
using System.Net;

namespace Fishbone.DebugAdapter.Tests;

/// <summary>An idle SpineIDE that an earlier run opened attaches to the next run, instead of a new one starting.</summary>
[Collection("SpineIdeAttachPipe")]
public class SpineIdeLauncherTests
{
    [Fact]
    public async Task AnIdleSpineIde_IsAskedToAttach_AndNothingStarts()
    {
        await using var ide = new NamedPipeServerStream(SpineIdeAttachPipe.Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task<string?> request = AnswerAsync(ide, "ok");

        // a missing executable would throw if it were started
        var started = SpineIdeLauncher.Launch(new IPEndPoint(IPAddress.Loopback, 4321), "no-such-spineide.exe");

        Assert.Null(started);
        Assert.Equal("attach 4321", await request);
    }

    [Fact]
    public async Task ABusySpineIde_LeavesItToANewOne()
    {
        await using var ide = new NamedPipeServerStream(SpineIdeAttachPipe.Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task<string?> request = AnswerAsync(ide, "busy");

        Assert.ThrowsAny<Exception>(() => SpineIdeLauncher.Launch(new IPEndPoint(IPAddress.Loopback, 4321), "no-such-spineide.exe"));
        Assert.Equal("attach 4321", await request);
    }

    private static async Task<string?> AnswerAsync(NamedPipeServerStream pipe, string answer)
    {
        await pipe.WaitForConnectionAsync();
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        string? request = await reader.ReadLineAsync();
        await writer.WriteLineAsync(answer);
        return request;
    }
}
