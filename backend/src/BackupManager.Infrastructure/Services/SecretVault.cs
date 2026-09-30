using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using BackupManager.Application.Connections;

namespace BackupManager.Infrastructure.Services;

public sealed class SecretVault(IHostEnvironment environment, IConfiguration configuration) : ISecretVault
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string VaultPath => Path.Combine(environment.ContentRootPath, "data", "secrets.json");

    public async Task StoreAsync(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(value))
            throw new ArgumentException("Secret name and value are required.");
        await _gate.WaitAsync();
        try
        {
            var values = await ReadAsync();
            values[name] = Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(value)));
            await WriteAsync(values);
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> ResolveAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        await _gate.WaitAsync();
        try
        {
            var values = await ReadAsync();
            return values.TryGetValue(name, out var cipher)
                ? Encoding.UTF8.GetString(Decrypt(Convert.FromBase64String(cipher))) : null;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string name)
    {
        await _gate.WaitAsync();
        try { var values = await ReadAsync(); values.Remove(name); await WriteAsync(values); }
        finally { _gate.Release(); }
    }
    private async Task WriteAsync(Dictionary<string, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(VaultPath)!);
        var temporary = VaultPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(values));
        File.Move(temporary, VaultPath, true);
    }

    private async Task<Dictionary<string, string>> ReadAsync() => File.Exists(VaultPath)
        ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(VaultPath)) ?? []
        : [];

    private byte[] Key
    {
        get
        {
            var masterKey = configuration["BackupManager:MasterKey"];
            if (string.IsNullOrWhiteSpace(masterKey) || masterKey.Length < 32)
                throw new InvalidOperationException("BackupManager:MasterKey must contain at least 32 characters.");
            return SHA256.HashData(Encoding.UTF8.GetBytes(masterKey));
        }
    }

    private byte[] Encrypt(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(Key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return [.. nonce, .. tag, .. cipher];
    }

    private byte[] Decrypt(byte[] packed)
    {
        var plain = new byte[packed.Length - 28];
        using var aes = new AesGcm(Key, 16);
        aes.Decrypt(packed[..12], packed[28..], packed[12..28], plain);
        return plain;
    }
}
