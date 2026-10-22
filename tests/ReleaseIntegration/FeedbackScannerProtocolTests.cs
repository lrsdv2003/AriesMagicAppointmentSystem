using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AriesMagicAppointmentSystem.Services;
using Microsoft.Extensions.Options;

static class FeedbackScannerProtocolTests
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try {
            var bytes=Encoding.UTF8.GetBytes("Harmless protocol fixture.");
            foreach(var sample in new[]{("stream: OK\0",MediaScan.Clean),("stream: Fixture-Virus FOUND\0",MediaScan.Infected),("stream: engine unavailable ERROR\0",MediaScan.Unavailable),("stream: OK",MediaScan.Unavailable)}) {
                var server=Task.Run(async()=>{
                    using var client=await listener.AcceptTcpClientAsync();await using var stream=client.GetStream();
                    var command=new byte[10];await stream.ReadExactlyAsync(command);
                    if(!command.SequenceEqual("zINSTREAM\0"u8.ToArray()))throw new Exception("Incorrect ClamAV command.");
                    using var received=new MemoryStream();var prefix=new byte[4];
                    while(true){await stream.ReadExactlyAsync(prefix);var length=BinaryPrimitives.ReadInt32BigEndian(prefix);if(length==0)break;if(length<0||length>65536)throw new Exception("Invalid scan chunk.");var part=new byte[length];await stream.ReadExactlyAsync(part);await received.WriteAsync(part);}
                    if(!received.ToArray().SequenceEqual(bytes))throw new Exception("Scan stream changed bytes.");
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(sample.Item1));
                });
                var scanner=new ClamAvFeedbackScanner(Options.Create(new FeedbackMediaOptions{ClamAvHost="127.0.0.1",ClamAvPort=((IPEndPoint)listener.LocalEndpoint).Port}));
                var result=await scanner.ScanAsync(bytes,CancellationToken.None);await server;
                check(result==sample.Item2,"ClamAV INSTREAM framing and fail-closed reply: "+sample.Item2+"/"+sample.Item1.EndsWith('\0'));
            }
        } finally {listener.Stop();}
    }
}
