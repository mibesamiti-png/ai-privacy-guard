using System.Net;
using System.Net.Sockets;
using PrivacyGuard.Core;
using PrivacyGuard.LiveObs;
using Xunit;

namespace PrivacyGuard.LiveObs.Tests;

public class BrowserSourceTests
{
    [Fact]
    public async Task BrowserSourcePublishesOnlyWatchdogSnapshotsAndBlackout()
    {
        var frameBytes = new byte[] { 33, 44, 55, 255, 66, 77, 88, 255 };
        var watchdog = new LiveOutputWatchdog(2, 1, TimeSpan.FromSeconds(2));
        int port;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        await using var server = new LocalObsBrowserSource(watchdog, port);
        await server.StartAsync();
        using var client = new HttpClient();
        string baseUrl = server.BrowserSourceUrl;
        Assert.StartsWith("http://127.0.0.1:", baseUrl);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"http://127.0.0.1:{port}/")).StatusCode);
        var page = await client.GetStringAsync(baseUrl);
        Assert.Contains("frame.bmp", page);

        var first = await client.GetByteArrayAsync(baseUrl + "frame.bmp");
        Assert.Equal((byte)'B', first[0]);
        Assert.Equal((byte)0, first[54]); // disarmed black
        await watchdog.WriteAsync(new LiveFrameResult(0,
            new PixelBuffer(2, 1, frameBytes), LiveFrameDisposition.Masked, "fixture"),
            CancellationToken.None);
        var visible = await client.GetByteArrayAsync(baseUrl + "frame.bmp");
        Assert.Equal((byte)33, visible[54]); // only gated synthetic output
        watchdog.ForceBlackout();
        var after = await client.GetByteArrayAsync(baseUrl + "frame.bmp");
        Assert.Equal((byte)0, after[54]);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"http://127.0.0.1:{port}/wrong-token/frame.bmp")).StatusCode);
    }
}
