using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrivacyGuard.LiveObs;

/// <summary>
/// An explicit manual OBS audio-offset adjustment through obs-websocket v5.
/// Does NOT capture audio or automatically measure audiovisual synchrony.
/// Only loopback OBS websocket is allowed. Password comes from an environment
/// variable; never put it in command-line arguments or repository files.
/// </summary>
public static class ObsAudioSyncProtocol
{
    public static void Validate(string inputName, int offsetMs)
    {
        if (string.IsNullOrWhiteSpace(inputName) || inputName.Length > 256 ||
            inputName.Any(char.IsControl))
            throw new ArgumentException("OBS audio input name is invalid.", nameof(inputName));
        if (offsetMs < -950 || offsetMs > 20000)
            throw new ArgumentOutOfRangeException(nameof(offsetMs),
                "OBS audio offset must be in [-950, 20000] milliseconds.");
    }

    public static string Authentication(string password, string salt, string challenge)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(challenge);
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(password + salt));
        var secret = Convert.ToBase64String(secretHash);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    public static string SetOffsetRequest(string requestId, string inputName, int offsetMs)
    {
        Validate(inputName, offsetMs);
        if (!Guid.TryParse(requestId, out _))
            throw new ArgumentException("Request ID must be a GUID.", nameof(requestId));
        return JsonSerializer.Serialize(new
        {
            op = 6,
            d = new {
                requestType = "SetInputAudioSyncOffset",
                requestId,
                requestData = new { inputName, inputAudioSyncOffset = offsetMs }
            }
        });
    }
}

public sealed class ObsAudioSyncClient
{
    public async Task SetInputOffsetAsync(string inputName, int offsetMs,
        string? password, CancellationToken cancellationToken = default)
    {
        ObsAudioSyncProtocol.Validate(inputName, offsetMs);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri("ws://127.0.0.1:4455"), timeout.Token);

        using var hello = JsonDocument.Parse(await ReadMessage(socket, timeout.Token));
        if (hello.RootElement.GetProperty("op").GetInt32() != 0)
            throw new InvalidDataException("Expected obs-websocket Hello.");
        var helloData = hello.RootElement.GetProperty("d");
        string? auth = null;
        if (helloData.TryGetProperty("authentication", out var challengeData))
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException(
                    "OBS authentication is enabled; set APG_OBS_WS_PASSWORD locally.");
            auth = ObsAudioSyncProtocol.Authentication(password,
                challengeData.GetProperty("salt").GetString()!,
                challengeData.GetProperty("challenge").GetString()!);
        }
        object identify = auth is null
            ? new { op = 1, d = (object)new { rpcVersion = 1 } }
            : new { op = 1, d = (object)new { rpcVersion = 1, authentication = auth } };
        await Send(socket, JsonSerializer.Serialize(identify), timeout.Token);

        using var identified = JsonDocument.Parse(await ReadMessage(socket, timeout.Token));
        if (identified.RootElement.GetProperty("op").GetInt32() != 2)
            throw new InvalidDataException("OBS did not accept Identify.");

        string requestId = Guid.NewGuid().ToString();
        await Send(socket, ObsAudioSyncProtocol.SetOffsetRequest(requestId, inputName, offsetMs),
            timeout.Token);
        for (int i = 0; i < 20; i++)
        {
            using var response = JsonDocument.Parse(await ReadMessage(socket, timeout.Token));
            if (response.RootElement.GetProperty("op").GetInt32() != 7) continue;
            var data = response.RootElement.GetProperty("d");
            if (data.GetProperty("requestId").GetString() != requestId) continue;
            if (!data.GetProperty("requestStatus").GetProperty("result").GetBoolean())
                throw new InvalidOperationException(
                    "OBS rejected SetInputAudioSyncOffset. Check input name and permissions.");
            return;
        }
        throw new TimeoutException("No matching OBS request response received.");
    }

    private static async Task Send(ClientWebSocket socket, string message, CancellationToken ct)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(data, WebSocketMessageType.Text, true, ct);
    }

    private static async Task<string> ReadMessage(ClientWebSocket socket, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        byte[] bytes = new byte[4096];
        while (true)
        {
            var result = await socket.ReceiveAsync(bytes, ct);
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("Expected OBS websocket text message.");
            buffer.Write(bytes, 0, result.Count);
            if (buffer.Length > 65536) throw new InvalidDataException("OBS message too large.");
            if (result.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
