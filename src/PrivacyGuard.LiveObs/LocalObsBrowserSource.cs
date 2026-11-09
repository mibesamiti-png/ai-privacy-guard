using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using PrivacyGuard.Core;

namespace PrivacyGuard.LiveObs;

/// <summary>
/// Local OBS Browser Source transport. Only LiveOutputWatchdog snapshots are
/// exposed; raw capture is never a web endpoint. This is an experimental
/// transport, not a certified stream-censorship solution.
/// </summary>
public sealed class LocalObsBrowserSource : IAsyncDisposable
{
    private readonly LiveOutputWatchdog _watchdog;
    private readonly int _port;
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private WebApplication? _app;

    public LocalObsBrowserSource(LiveOutputWatchdog watchdog, int port = 18731)
    {
        _watchdog = watchdog ?? throw new ArgumentNullException(nameof(watchdog));
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
    }

    public string BrowserSourceUrl => $"http://127.0.0.1:{_port}/{_token}/";

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_app is not null) throw new InvalidOperationException("Already started.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.WebHost.UseUrls($"http://127.0.0.1:{_port}");
        var app = builder.Build();
        string prefix = "/" + _token;
        app.MapGet(prefix + "/", (HttpContext ctx) =>
        {
            ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.Content(BuildPage(_watchdog.Width, _watchdog.Height),
                "text/html; charset=utf-8");
        });
        app.MapGet(prefix + "/frame.bmp", (HttpContext ctx) =>
        {
            ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            // Snapshot blackouts when processing stalls, even if input stopped.
            return Results.File(BmpFrameEncoder.Encode(_watchdog.Snapshot()), "image/bmp");
        });
        // Other paths return 404, without advertising the bearer-style URL token.
        try
        {
            await app.StartAsync(ct);
            _app = app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var app = _app;
        _app = null;
        _watchdog.ForceBlackout();
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static string BuildPage(int width, int height) => $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>html,body{margin:0;width:100%;height:100%;background:#000;overflow:hidden}
canvas{display:block;width:100%;height:100%;background:#000}</style>
</head>
<body><canvas id="live" width="{{width}}" height="{{height}}"></canvas>
<script>
"use strict";
const canvas = document.getElementById("live");
const ctx = canvas.getContext("2d", {alpha:false});
const black = () => { ctx.fillStyle = "#000"; ctx.fillRect(0,0,canvas.width,canvas.height); };
black();
let inFlight = false;
let started = 0;
let generation = 0;
function poll(){
  const now = performance.now();
  if(inFlight && now - started < 700) return;
  if(inFlight){ generation++; inFlight=false; black(); }
  inFlight=true; started=now;
  const current=++generation;
  const image=new Image();
  image.onload=()=>{
    if(current!==generation)return;
    inFlight=false;
    ctx.drawImage(image,0,0,canvas.width,canvas.height);
  };
  image.onerror=()=>{
    if(current!==generation)return;
    inFlight=false; black();
  };
  image.src="frame.bmp?seq="+current+"_"+Date.now();
}
setInterval(poll,100);
poll();
</script></body></html>
""";
}
