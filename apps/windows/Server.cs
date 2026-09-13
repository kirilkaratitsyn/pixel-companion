using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PixelCompanion;

public sealed class Server(Pairing pairing, MediaBridge media, BrowserController browser, int port, bool loopback) : IAsyncDisposable
{
    private WebApplication? app;
    private readonly CancellationTokenSource stop = new();
    private Task? polling, discovery;
    private int clients;
    public int ClientCount => Volatile.Read(ref clients);
    public static string[] Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a)).Select(a => a.ToString()).Distinct().ToArray();
    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        byte[] b = address.MapToIPv4().GetAddressBytes();
        return b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31;
    }
    public async Task Start()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(o => { o.Limits.MaxRequestBodySize = 8 * 1024 * 1024; o.Listen(loopback ? IPAddress.Loopback : IPAddress.Any, port); });
        app = builder.Build();
        app.Use(async (ctx, next) =>
        {
            if (ctx.Connection.RemoteIpAddress is not { } address || !IsPrivate(address)) { ctx.Response.StatusCode = 403; return; }
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self' ws:; frame-ancestors 'none'; base-uri 'none'";
            await next(ctx);
        });
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        app.MapGet("/health", () => Results.Json(new { name = "Pixel Companion", version = 1 }));
        app.MapGet("/api/diagnostics", (HttpContext ctx) =>
        {
            var remote = ctx.Connection.RemoteIpAddress;
            if (remote == null || !IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote)) return Results.StatusCode(403);
            var state = media.Latest;
            var art = media.Artwork;
            int? width = null, height = null;
            if (art != null && art.ContentType is "image/jpeg" or "image/png")
            {
                try
                {
                    using var stream = new MemoryStream(art.Bytes, false);
                    using var image = Image.FromStream(stream, false, true);
                    width = image.Width; height = image.Height;
                }
                catch { }
            }
            return Results.Json(new
            {
                state.Media.Source,
                state.Media.Title,
                state.Media.Artist,
                state.Media.Status,
                state.Media.ArtworkHash,
                artworkBytes = art?.Bytes.Length,
                artworkType = art?.ContentType,
                artworkWidth = width,
                artworkHeight = height,
                queueItems = state.Queue.Length,
                error = state.Error
            });
        });
        app.MapMethods("/api/extension-health", ["OPTIONS"], (HttpContext ctx) =>
        {
            if (!BrowserExtensionRequest(ctx)) return Results.StatusCode(403);
            AddExtensionCors(ctx); return Results.NoContent();
        });
        app.MapGet("/api/extension-health", (HttpContext ctx) =>
        {
            if (!BrowserExtensionRequest(ctx)) return Results.StatusCode(403);
            AddExtensionCors(ctx); return Results.Json(new { ok = true, version = "0.4.1" });
        });
        app.MapPost("/api/pair", async (HttpContext ctx) =>
        {
            if (!SameOrigin(ctx)) return Results.StatusCode(403);
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<PairRequest>();
                var result = pairing.Pair(body?.Code);
                return result.Token == null ? Results.Json(new { error = result.Error }, statusCode: 401) : Results.Json(new { token = result.Token });
            }
            catch (JsonException) { return Results.BadRequest(); }
        });
        app.MapMethods("/api/browser-artwork", ["OPTIONS"], (HttpContext ctx) =>
        {
            if (!BrowserExtensionRequest(ctx)) return Results.StatusCode(403);
            AddExtensionCors(ctx); return Results.NoContent();
        });
        app.MapPost("/api/browser-artwork", async (HttpContext ctx) =>
        {
            if (!BrowserExtensionRequest(ctx) || ctx.Request.Headers["X-Pixel-Companion"] != "browser-artwork-v1") return Results.StatusCode(403);
            AddExtensionCors(ctx);
            try
            {
                string? title = DecodeHeader(ctx.Request.Headers["X-Pixel-Companion-Title"]);
                string? artist = DecodeHeader(ctx.Request.Headers["X-Pixel-Companion-Artist"]);
                if (title == null || ctx.Request.ContentLength is null or <= 0 or > 8 * 1024 * 1024) return Results.BadRequest();
                using var output = new MemoryStream((int)ctx.Request.ContentLength.Value);
                await ctx.Request.Body.CopyToAsync(output, ctx.RequestAborted);
                var payload = MediaBridge.CreateArtwork(output.ToArray());
                return payload != null && await media.SetBrowserArtwork(title, artist, payload) ? Results.Ok(new { hash = payload.Hash }) : Results.BadRequest();
            }
            catch (Exception e) when (e is FormatException or DecoderFallbackException or IOException or TaskCanceledException) { return Results.BadRequest(); }
        });
        app.MapPost("/api/browser-state", async (HttpContext ctx) =>
        {
            if (!BrowserExtensionRequest(ctx) || ctx.Request.Headers["X-Pixel-Companion"] != "browser-state-v1") return Results.StatusCode(403);
            AddExtensionCors(ctx);
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<BrowserStateRequest>();
                if (body == null) return Results.BadRequest();
                return Results.Ok(new { command = browser.Update(body.Tracks, body.AfterSequence) });
            }
            catch (JsonException) { return Results.BadRequest(); }
        });
        app.Map("/ws", Socket);
        foreach (var (name, contentType) in new[] { ("index.html", "text/html; charset=utf-8"), ("app.js", "text/javascript; charset=utf-8"), ("style.css", "text/css; charset=utf-8"), ("mixer.css", "text/css; charset=utf-8") })
        {
            string file = Path.Combine(AppContext.BaseDirectory, "web", name);
            app.MapGet(name == "index.html" ? "/" : "/" + name, () => Results.File(file, contentType));
        }
        string apk = Path.Combine(AppContext.BaseDirectory, "PixelCompanion.apk");
        app.MapGet("/PixelCompanion.apk", () => File.Exists(apk) ? Results.File(apk, "application/vnd.android.package-archive", "PixelCompanion.apk") : Results.NotFound());
        await app.StartAsync();
        polling = Task.Run(async () => { using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500)); while (await timer.WaitForNextTickAsync(stop.Token)) await media.Refresh(); }, stop.Token);
        if (!loopback) discovery = Task.Run(Discovery, stop.Token);
    }
    private static bool SameOrigin(HttpContext ctx)
    {
        string? origin = ctx.Request.Headers.Origin;
        // Native clients may omit Origin; browser clients must use the server's own origin.
        return string.IsNullOrEmpty(origin) || origin == $"http://{ctx.Request.Host}";
    }
    private static bool BrowserExtensionRequest(HttpContext ctx)
    {
        var address = ctx.Connection.RemoteIpAddress;
        if (address == null || !IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)) return false;
        string origin = ctx.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || origin.StartsWith("chrome-extension://", StringComparison.Ordinal) || origin.StartsWith("moz-extension://", StringComparison.Ordinal);
    }
    private static void AddExtensionCors(HttpContext ctx)
    {
        string origin = ctx.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin)) ctx.Response.Headers.AccessControlAllowOrigin = origin;
        ctx.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
        ctx.Response.Headers.AccessControlAllowHeaders = "Content-Type, X-Pixel-Companion, X-Pixel-Companion-Title, X-Pixel-Companion-Artist";
        if (string.Equals(ctx.Request.Headers["Access-Control-Request-Private-Network"], "true", StringComparison.OrdinalIgnoreCase))
            ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        ctx.Response.Headers["Private-Network-Access-Name"] = "pixel-companion";
        ctx.Response.Headers["Private-Network-Access-ID"] = "50:58:43:4f:4d:50";
    }
    private static string? DecodeHeader(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return null;
        byte[] bytes = Convert.FromBase64String(encoded);
        return bytes.Length is > 0 and <= 2048 ? new UTF8Encoding(false, true).GetString(bytes) : null;
    }
    private async Task Socket(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest || !SameOrigin(ctx)) { ctx.Response.StatusCode = 403; return; }
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, ctx.RequestAborted);
        var ct = lifetime.Token;
        try
        {
            using var authenticationTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct); authenticationTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            var raw = await Receive(ws, authenticationTimeout.Token);
            using var auth = JsonDocument.Parse(raw);
            if (!auth.RootElement.TryGetProperty("type", out var type) || type.GetString() != "auth" || !auth.RootElement.TryGetProperty("token", out var tokenNode)) return;
            string? token = tokenNode.GetString();
            if (!pairing.Valid(token)) { await Send(ws, new { type = "auth_error" }, ct); await ws.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Pair again", ct); return; }
            Interlocked.Increment(ref clients);
            try
            {
                var sendGate = new SemaphoreSlim(1, 1);
                async Task Emit(object message) { await sendGate.WaitAsync(ct); try { await Send(ws, message, ct); } finally { sendGate.Release(); } }
                await Emit(new { type = "ready", version = 1 });
                var updates = Task.Run(async () =>
                {
                    string? lastArt = null;
                    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        if (!pairing.Valid(token)) { await Emit(new { type = "auth_error" }); lifetime.Cancel(); break; }
                        var state = media.Latest;
                        await Emit(state);
                        if (state.Media.ArtworkHash != lastArt)
                        {
                            var artwork = media.Artwork;
                            // A metadata update can swap image bytes between these two reads.
                            // Send only a matching image/hash pair, or retry on the next snapshot.
                            if (state.Media.ArtworkHash == null || artwork != null && artwork.Hash == state.Media.ArtworkHash)
                            {
                                await Emit(new { type = "artwork", hash = state.Media.ArtworkHash, data = artwork == null ? null : $"data:{artwork.ContentType};base64," + Convert.ToBase64String(artwork.Bytes) });
                                lastArt = state.Media.ArtworkHash;
                            }
                        }
                        await Task.Delay(500, ct);
                    }
                }, ct);
                var seen = new Dictionary<string, object>();
                var commandTimes = new Queue<DateTimeOffset>();
                try
                {
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var message = await Receive(ws, ct);
                        if (!pairing.Valid(token)) break;
                        var command = JsonSerializer.Deserialize<ClientCommand>(message, Wire.Json);
                        if (command == null || string.IsNullOrWhiteSpace(command.Id) || command.Id.Length > 80 || string.IsNullOrEmpty(command.Name)) continue;
                        if (seen.TryGetValue(command.Id, out var prior)) { await Emit(prior); continue; }
                        var now = DateTimeOffset.UtcNow;
                        while (commandTimes.TryPeek(out var at) && now - at > TimeSpan.FromSeconds(1)) commandTimes.Dequeue();
                        (bool Ok, string? Error) result;
                        if (commandTimes.Count >= 12) result = (false, "Слишком много команд");
                        else { commandTimes.Enqueue(now); result = await media.Command(command); }
                        object reply = new { type = "result", id = command.Id, ok = result.Ok, error = result.Error };
                        if (seen.Count >= 128) seen.Remove(seen.Keys.First());
                        seen[command.Id] = reply; await Emit(reply); await media.Refresh();
                    }
                }
                finally { lifetime.Cancel(); try { await updates; } catch (OperationCanceledException) { } catch (WebSocketException) { } }
            }
            finally { Interlocked.Decrement(ref clients); }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or JsonException or InvalidOperationException or ArgumentException) { }
        finally { ws.Abort(); }
    }
    private static Task Send(WebSocket ws, object value, CancellationToken ct) => ws.SendAsync(Encoding.UTF8.GetBytes(Wire.Serialize(value)), WebSocketMessageType.Text, true, ct);
    private static async Task<string> Receive(WebSocket ws, CancellationToken ct)
    {
        var bytes = new byte[4096]; int offset = 0;
        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(bytes, offset, bytes.Length - offset), ct);
            if (result.MessageType != WebSocketMessageType.Text) throw new WebSocketException("Text messages only");
            offset += result.Count;
            if (result.EndOfMessage) return Encoding.UTF8.GetString(bytes, 0, offset);
            if (offset == bytes.Length) throw new WebSocketException("Message too large");
        }
    }
    private async Task Discovery()
    {
        try
        {
            using var udp = new UdpClient(8764);
            var lastReply = DateTimeOffset.MinValue;
            while (!stop.IsCancellationRequested)
            {
                var packet = await udp.ReceiveAsync(stop.Token);
                if (!IsPrivate(packet.RemoteEndPoint.Address) || Encoding.UTF8.GetString(packet.Buffer) != "PIXEL_COMPANION_DISCOVER/1") continue;
                if (DateTimeOffset.UtcNow - lastReply < TimeSpan.FromMilliseconds(100)) continue;
                lastReply = DateTimeOffset.UtcNow;
                byte[] response = Encoding.UTF8.GetBytes(Wire.Serialize(new { name = Environment.MachineName, port, version = 1 }));
                await udp.SendAsync(response, packet.RemoteEndPoint, stop.Token);
            }
        }
        catch (SocketException) { /* Manual IP connection remains available if discovery is unavailable. */ }
        catch (OperationCanceledException) { }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        if (app != null) await app.DisposeAsync();
        foreach (var task in new[] { polling, discovery }) if (task != null) try { await task; } catch (OperationCanceledException) { }
        stop.Dispose();
    }
    private record PairRequest(string Code);
}
