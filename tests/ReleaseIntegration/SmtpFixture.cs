using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MimeKit;

// Loopback SMTP contract server. Only generated, nonproduction credentials/messages are accepted.
sealed class SmtpFixture : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stop = new();
    readonly List<Task> clients = new();
    Task? loop;
    public readonly ConcurrentQueue<MimeMessage> Messages = new();
    public bool RejectAuthentication { get; set; }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public void Start() { listener.Start(); loop = AcceptAsync(); }
    async Task AcceptAsync()
    {
        try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); clients.Add(HandleAsync(client)); } }
        catch (OperationCanceledException) { }
    }
    async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 loopback.test ESMTP");
            while (!stop.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(stop.Token);
                if (line == null) break;
                var verb = line.Split(' ')[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO": await writer.WriteAsync("250-loopback.test\r\n250-AUTH PLAIN\r\n250 SIZE 20000000\r\n"); break;
                    case "AUTH": await writer.WriteLineAsync(RejectAuthentication ? "535 Authentication rejected" : "235 Authenticated"); break;
                    case "MAIL": case "RCPT": case "RSET": case "NOOP": await writer.WriteLineAsync("250 OK"); break;
                    case "DATA":
                        await writer.WriteLineAsync("354 End with dot");
                        var data = new StringBuilder();
                        while ((line = await reader.ReadLineAsync(stop.Token)) != null && line != ".") data.Append(line.StartsWith("..") ? line[1..] : line).Append("\r\n");
                        using (var memory = new MemoryStream(Encoding.ASCII.GetBytes(data.ToString()))) Messages.Enqueue(await MimeMessage.LoadAsync(memory, stop.Token));
                        await writer.WriteLineAsync("250 Accepted"); break;
                    case "QUIT": await writer.WriteLineAsync("221 Bye"); return;
                    default: await writer.WriteLineAsync("500 Unsupported"); break;
                }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop();
        if (loop != null) await loop;
        try { await Task.WhenAll(clients); } catch (OperationCanceledException) { }
        stop.Dispose();
    }
}
