using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using BackupManager.Application.Connections;

namespace BackupManager.Infrastructure.Services;

public sealed class SecretVault(IHostEnvironment environment, IConfiguration configuration) : ISecretVault, IDisposable
{
    // Load once; configuration reloads must never silently change an active vault's key.
    private readonly byte[] _key = ReadMasterKey(configuration);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string VaultPath => Path.Combine(environment.ContentRootPath, "data", "secrets.json");
    public string EncryptPassword(string value) => Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(value)));
    public string? DecryptPassword(string? value) => value is null ? null : Encoding.UTF8.GetString(Decrypt(Convert.FromBase64String(value)));

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

    private static byte[] ReadMasterKey(IConfiguration configuration)
    {
        const string setting = "BackupManager:Security:MasterKey";
        var value = configuration[setting];
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!string.IsNullOrWhiteSpace(configuration["BackupManager:MasterKey"]))
                throw new InvalidOperationException($"SecretVault configuration error: legacy BackupManager:MasterKey detected. Convert the existing key as documented in README to {setting}; do not generate a replacement for existing ciphertext.");
            throw new InvalidOperationException($"SecretVault configuration error: {setting} is missing. Provision a Base64-encoded 32-byte key using User Secrets or BackupManager__Security__MasterKey before startup.");
        }
        byte[] key;
        try { key = Convert.FromBase64String(value); }
        catch (FormatException) { throw new InvalidOperationException($"SecretVault configuration error: {setting} must be valid Base64 encoding exactly 32 bytes."); }
        if (key.Length == 32) return key;
        CryptographicOperations.ZeroMemory(key);
        throw new InvalidOperationException($"SecretVault configuration error: {setting} must decode to exactly 32 bytes (AES-256).");
    }

    public void Dispose() { CryptographicOperations.ZeroMemory(_key); _gate.Dispose(); }

    private byte[] Encrypt(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return [.. nonce, .. tag, .. cipher];
    }

    private byte[] Decrypt(byte[] packed)
    {
        if (packed.Length < 28) throw new CryptographicException("Invalid SecretVault encrypted payload.");
        var plain = new byte[packed.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(packed[..12], packed[28..], packed[12..28], plain);
        return plain;
    }
}
