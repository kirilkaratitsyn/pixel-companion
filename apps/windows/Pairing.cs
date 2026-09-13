using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PixelCompanion;

public sealed class Pairing
{
    private readonly object gate = new();
    private readonly string file;
    private HashSet<string> hashes = [];
    private readonly Queue<DateTimeOffset> failures = new();
    private string code = "";
    private DateTimeOffset expires;
    public string Code { get { lock (gate) return DateTimeOffset.UtcNow < expires ? code : "Истёк"; } }
    public int DeviceCount { get { lock (gate) return hashes.Count; } }
    public Pairing(string directory)
    {
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, "devices.json");
        if (File.Exists(file))
            hashes = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(file)) ?? [];
        Renew();
    }
    public void Renew() { lock (gate) { code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); expires = DateTimeOffset.UtcNow.AddMinutes(5); } }
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public bool Valid(string? token) { lock (gate) return token is { Length: 64 } && hashes.Contains(Hash(token)); }
    public (string? Token, string Error) Pair(string? candidate)
    {
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            while (failures.TryPeek(out var time) && now - time > TimeSpan.FromMinutes(1)) failures.Dequeue();
            if (failures.Count >= 5) return (null, "Слишком много попыток. Подождите минуту.");
            if (now >= expires || candidate != code) { failures.Enqueue(now); return (null, "Неверный или истёкший код."); }
            if (hashes.Count >= 8) return (null, "Лимит 8 устройств. Сбросьте привязки на компьютере.");
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            hashes.Add(Hash(token)); Save(); Renew(); // every code is single-use
            return (token, "");
        }
    }
    public void Revoke() { lock (gate) { hashes.Clear(); Save(); Renew(); } }
    private void Save()
    {
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(hashes));
        File.Move(file + ".tmp", file, true);
    }
}
