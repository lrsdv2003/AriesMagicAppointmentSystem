using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Models;
using Microsoft.Extensions.Options;
using SkiaSharp;
namespace AriesMagicAppointmentSystem.Services;

public class FeedbackMediaOptions
{
    public string? Root { get; set; }
    public int MaxImages { get; set; } = 5;
    public long MaxImageBytes { get; set; } = 10*1024*1024;
    public int MaxDimension { get; set; } = 6000;
    public long MaxPixels { get; set; } = 16_000_000;
    public string? ClamAvHost { get; set; }
    public int ClamAvPort { get; set; } = 3310;
}
public enum MediaScan { Unavailable, Clean, Infected }
public interface IFeedbackMalwareScanner { Task<MediaScan> ScanAsync(byte[] content,CancellationToken ct); }
public interface IFeedbackVideoProcessor
{
    bool Available { get; }
    Task<byte[]> ProcessAsync(byte[] content,CancellationToken ct);
}
public sealed class DisabledFeedbackVideoProcessor : IFeedbackVideoProcessor
{
    public bool Available => false;
    public Task<byte[]> ProcessAsync(byte[] content,CancellationToken ct) => throw new InvalidOperationException("Secure video processing is unavailable.");
}
public sealed class ClamAvFeedbackScanner(IOptions<FeedbackMediaOptions> options) : IFeedbackMalwareScanner
{
    public async Task<MediaScan> ScanAsync(byte[] content,CancellationToken ct)
    {
        var settings=options.Value;
        if(string.IsNullOrWhiteSpace(settings.ClamAvHost)) return MediaScan.Unavailable;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try {
            using var tcp=new TcpClient();
            await tcp.ConnectAsync(settings.ClamAvHost,settings.ClamAvPort,timeout.Token);
            await using var stream=tcp.GetStream();
            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(),timeout.Token);
            var prefix=new byte[4];
            for(int offset=0;offset<content.Length;offset+=65536) {
                var length=Math.Min(65536,content.Length-offset);
                BinaryPrimitives.WriteInt32BigEndian(prefix,length);
                await stream.WriteAsync(prefix,timeout.Token);
                await stream.WriteAsync(content.AsMemory(offset,length),timeout.Token);
            }
            await stream.WriteAsync(new byte[4],timeout.Token);
            var reply=new List<byte>();var terminated=false;
            var one=new byte[1];
            while(reply.Count<4096 && await stream.ReadAsync(one,timeout.Token)>0) {
                if(one[0]==0){terminated=true;break;}
                reply.Add(one[0]);
            }
            if(!terminated)return MediaScan.Unavailable;
            var value=Encoding.UTF8.GetString(reply.ToArray()).Trim();
            return value is "stream: OK" or "instream: OK" ? MediaScan.Clean
                : value.EndsWith(" FOUND",StringComparison.Ordinal) ? MediaScan.Infected : MediaScan.Unavailable;
        } catch(Exception ex) when(ex is IOException or SocketException or OperationCanceledException) {
            ct.ThrowIfCancellationRequested();
            return MediaScan.Unavailable;
        }
    }
}
public sealed class FeedbackUploadException(string message) : Exception(message);

public sealed class FeedbackMediaService
{
    private readonly FeedbackMediaOptions options;
    private readonly IFeedbackMalwareScanner scanner;
    private readonly string root;
    private static readonly SemaphoreSlim DecoderGate=new(1,1);
    public FeedbackMediaService(IOptions<FeedbackMediaOptions> options,IFeedbackMalwareScanner scanner,IWebHostEnvironment environment)
    {
        this.options=options.Value;this.scanner=scanner;
        root=Path.GetFullPath(this.options.Root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AriesMagic","FeedbackMedia"));
        var applicationRoot=Path.GetFullPath(environment.ContentRootPath).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!Path.IsPathFullyQualified(root)||root.StartsWith(applicationRoot,StringComparison.OrdinalIgnoreCase)||root==applicationRoot.TrimEnd(Path.DirectorySeparatorChar))
            throw new InvalidOperationException("Feedback media requires private storage outside the application directory.");
        if(!string.IsNullOrWhiteSpace(environment.WebRootPath)) {
            var webRoot=Path.GetFullPath(environment.WebRootPath).TrimEnd(Path.DirectorySeparatorChar);
            if(root==webRoot||root.StartsWith(webRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Feedback media cannot be stored in a web-served directory.");
        }
    }
    private string Resolve(string key)
    {
        if(!Regex.IsMatch(key,"^[0-9a-f]{64}\\.jpg$")) throw new InvalidOperationException("Invalid media identifier.");
        return Path.Combine(root,key);
    }
    public async Task<FeedbackMedia> PrepareAsync(IFormFile file,CancellationToken ct)
    {
        if(file.FileName.Length>120||file.FileName.Contains("..")||file.FileName.IndexOfAny(['/', '\\', ':'])>=0||file.FileName.Any(char.IsControl))
            throw new FeedbackUploadException("Use a simple filename without folders or special path characters.");
        if(Path.GetExtension(file.FileName).Equals(".mp4",StringComparison.OrdinalIgnoreCase))
            throw new FeedbackUploadException("Video uploads are unavailable until the isolated video processor is operational. You can still submit photos and feedback.");
        if(file.Length<=0||file.Length>options.MaxImageBytes) throw new FeedbackUploadException($"Each photo must be between 1 byte and {options.MaxImageBytes/1024/1024} MB.");
        if(!await ImageUploadValidation.HasMatchingSignatureAsync(file)) throw new FeedbackUploadException("Use a valid JPEG, PNG, or WebP photo with a matching file type.");
        await using var source=file.OpenReadStream();
        using var buffer=new MemoryStream();
        var chunk=new byte[65536];int read;
        while((read=await source.ReadAsync(chunk,ct))>0) {
            if(buffer.Length+read>options.MaxImageBytes) throw new FeedbackUploadException("The photo exceeds the upload limit.");
            await buffer.WriteAsync(chunk.AsMemory(0,read),ct);
        }
        if(buffer.Length!=file.Length) throw new FeedbackUploadException("The upload was interrupted. Select the file again.");
        var bytes=buffer.ToArray();
        if(await scanner.ScanAsync(bytes,ct)==MediaScan.Infected) throw new FeedbackUploadException("The file failed its security check.");
        if(!await DecoderGate.WaitAsync(TimeSpan.FromSeconds(10),ct)) throw new FeedbackUploadException("Photo processing is busy. Please try again.");
        byte[] encoded;int width,height;
        try {
            using var data=SKData.CreateCopy(bytes);
            using var codec=SKCodec.Create(data);
            var expected=Path.GetExtension(file.FileName).ToLowerInvariant() switch {".png"=>SKEncodedImageFormat.Png,".webp"=>SKEncodedImageFormat.Webp,_=>SKEncodedImageFormat.Jpeg};
            if(codec==null||codec.EncodedFormat!=expected||codec.FrameCount>1) throw new FeedbackUploadException("The photo is malformed, animated, or does not match its file type.");
            width=codec.Info.Width;height=codec.Info.Height;
            if(width<1||height<1||width>options.MaxDimension||height>options.MaxDimension||(long)width*height>options.MaxPixels)
                throw new FeedbackUploadException("The photo dimensions are too large. Use a photo up to 16 megapixels and 6,000 pixels per side.");
            using var bitmap=new SKBitmap(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Premul));
            if(codec.GetPixels(bitmap.Info,bitmap.GetPixels())!=SKCodecResult.Success) throw new FeedbackUploadException("The photo could not be fully decoded.");
            using var image=SKImage.FromBitmap(bitmap);
            using var output=image.Encode(SKEncodedImageFormat.Jpeg,88);
            encoded=output?.ToArray()??throw new FeedbackUploadException("The photo could not be processed.");
            if(encoded.Length>options.MaxImageBytes) throw new FeedbackUploadException("The processed photo is too large. Use a smaller image.");
        } catch(FeedbackUploadException){throw;}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException) {throw new FeedbackUploadException("The photo could not be processed safely.");}
        catch(Exception ex) when(ex is DllNotFoundException or TypeInitializationException) {throw new FeedbackUploadException("The photo processor is unavailable. You can submit feedback without photos or try again later.");}
        finally {DecoderGate.Release();}
        var scan=await scanner.ScanAsync(encoded,ct);
        if(scan==MediaScan.Infected) throw new FeedbackUploadException("The processed photo failed its security check.");
        var key=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()+".jpg";
        if(OperatingSystem.IsWindows())Directory.CreateDirectory(root);
        else Directory.CreateDirectory(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        await using(var destination=new FileStream(Resolve(key),FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)) {
            try {await destination.WriteAsync(encoded,ct);}
            catch {destination.Close();File.Delete(Resolve(key));throw;}
        }
        return new FeedbackMedia {StorageKey=key,DisplayFilename=file.FileName,Size=encoded.Length,Width=width,Height=height,
            ProcessingStatus=scan==MediaScan.Clean?"Ready":"Quarantined",ScannedAtUtc=scan==MediaScan.Clean?DateTime.UtcNow:null};
    }
    public Task<byte[]> ReadAsync(string key,CancellationToken ct) => File.ReadAllBytesAsync(Resolve(key),ct);
    public void Delete(string key) {var path=Resolve(key);if(File.Exists(path))File.Delete(path);}
}
