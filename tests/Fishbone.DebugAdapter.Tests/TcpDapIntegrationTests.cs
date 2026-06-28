using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Fishbone.Dap;

namespace Fishbone.DebugAdapter.Tests;

[Collection("DebugServer")]
public class TcpDapIntegrationTests
{
    [Fact]
    public async Task ClientCanAttachBreakInspectContinueAndReceiveOutput()
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"fishbone-dap-{Guid.NewGuid():N}.fb");
        await File.WriteAllTextAsync(scriptPath, "let x = 1;\nprint(x); println(x);\nx = x + 1;");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var endpoint = new EndpointWriter();
        Task<int> host = new FishboneDapHost().RunAsync(scriptPath, 0, endpoint, timeout.Token);

        string address = await endpoint.Endpoint.WaitAsync(timeout.Token);
        int port = int.Parse(address[(address.LastIndexOf(':') + 1)..]);
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port, timeout.Token);
        using NetworkStream stream = client.GetStream();
        var dap = new RawDapClient(stream, timeout.Token);

        JsonElement initialize = await dap.RequestAsync("initialize", new
        {
            adapterID = "fishbone",
            linesStartAt1 = true,
            columnsStartAt1 = true,
            pathFormat = "path"
        });
        Assert.True(initialize.GetProperty("body").GetProperty("supportsConfigurationDoneRequest").GetBoolean());
        await dap.ReadUntilAsync(message => message.TryGetProperty("event", out var name) && name.GetString() == "initialized");
        await dap.RequestAsync("attach", new { });
        JsonElement setBreakpoints = await dap.RequestAsync("setBreakpoints", new
        {
            source = new { name = Path.GetFileName(scriptPath), path = scriptPath },
            breakpoints = new[] { new { line = 2 } }
        });
        Assert.True(setBreakpoints.GetProperty("body").GetProperty("breakpoints")[0].GetProperty("verified").GetBoolean());
        await dap.RequestAsync("configurationDone", new { });

        JsonElement stopped = await dap.ReadUntilAsync(message =>
            message.TryGetProperty("event", out var name) && name.GetString() == "stopped");
        Assert.Equal("breakpoint", stopped.GetProperty("body").GetProperty("reason").GetString());

        JsonElement stack = await dap.RequestAsync("stackTrace", new { threadId = 1 });
        long frameId = stack.GetProperty("body").GetProperty("stackFrames")[0].GetProperty("id").GetInt64();
        JsonElement scopes = await dap.RequestAsync("scopes", new { frameId });
        long visibleReference = scopes.GetProperty("body").GetProperty("scopes")
            .EnumerateArray().Single(scope => scope.GetProperty("name").GetString() == "Visible Variables")
            .GetProperty("variablesReference").GetInt64();
        JsonElement variables = await dap.RequestAsync("variables", new { variablesReference = visibleReference });
        JsonElement x = variables.GetProperty("body").GetProperty("variables")
            .EnumerateArray().Single(variable => variable.GetProperty("name").GetString() == "x");
        Assert.Equal("1", x.GetProperty("value").GetString());

        await dap.RequestAsync("continue", new { threadId = 1 });
        var events = new List<JsonElement>();
        await dap.ReadUntilAsync(message =>
        {
            events.Add(message);
            return message.TryGetProperty("event", out var name) && name.GetString() == "terminated";
        });
        Assert.Contains(events, message =>
            message.TryGetProperty("event", out var name) && name.GetString() == "output" &&
            message.GetProperty("body").GetProperty("output").GetString() == "1");
        Assert.Contains(events, message =>
            message.TryGetProperty("event", out var name) && name.GetString() == "output" &&
            message.GetProperty("body").GetProperty("output").GetString() == "1" + Environment.NewLine);
        Assert.Equal(0, await host.WaitAsync(timeout.Token));
        File.Delete(scriptPath);
    }

    private sealed class EndpointWriter : StringWriter
    {
        private readonly TaskCompletionSource<string> _endpoint = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> Endpoint => _endpoint.Task;
        public override Task WriteLineAsync(string? value)
        {
            _endpoint.TrySetResult(value!);
            return Task.CompletedTask;
        }
    }

    private sealed class RawDapClient(NetworkStream stream, CancellationToken cancellationToken)
    {
        private int _sequence;
        private readonly List<JsonElement> _pending = [];

        public async Task<JsonElement> RequestAsync(string command, object arguments)
        {
            int sequence = Interlocked.Increment(ref _sequence);
            await WriteAsync(new { seq = sequence, type = "request", command, arguments });
            return await ReadUntilAsync(message =>
                message.TryGetProperty("type", out var type) && type.GetString() == "response" &&
                message.GetProperty("request_seq").GetInt32() == sequence);
        }

        public async Task<JsonElement> ReadUntilAsync(Func<JsonElement, bool> predicate)
        {
            int existing = _pending.FindIndex(message => predicate(message));
            if (existing >= 0)
            {
                JsonElement match = _pending[existing];
                _pending.RemoveAt(existing);
                return match;
            }

            while (true)
            {
                JsonElement message = await ReadAsync();
                if (predicate(message)) return message;
                _pending.Add(message);
            }
        }

        private async Task WriteAsync(object message)
        {
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(message);
            byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private async Task<JsonElement> ReadAsync()
        {
            var header = new List<byte>();
            while (header.Count < 4 || !header.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray()))
            {
                int value = stream.ReadByte();
                if (value < 0) throw new EndOfStreamException();
                header.Add((byte)value);
            }

            string headerText = Encoding.ASCII.GetString(header.ToArray());
            string lengthText = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                .Split(':', 2)[1].Trim();
            int length = int.Parse(lengthText);
            byte[] body = new byte[length];
            await stream.ReadExactlyAsync(body, cancellationToken);
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
    }
}