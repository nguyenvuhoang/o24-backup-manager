using System.Security.Cryptography;
using System.Text;
using BackupManager.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BackupManager.SecurityTests;

public sealed class SecretVaultTests
{
    private const string ConfigName = "BackupManager:Security:MasterKey";
    private static SecretVault Create(string? key, string? legacy = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { [ConfigName] = key, ["BackupManager:MasterKey"] = legacy }).Build();
        return new SecretVault(new TestEnvironment(), config);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("invalid-base64-sensitive-marker")]
    public void RejectsMissingOrInvalidKeyAtConstruction(string? value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Create(value));
        Assert.Contains(ConfigName, error.Message);
        if (!string.IsNullOrWhiteSpace(value)) Assert.DoesNotContain(value, error.ToString());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(33)]
    public void RejectsWrongDecodedLength(int length)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));
        var error = Assert.Throws<InvalidOperationException>(() => Create(key));
        Assert.Contains("32", error.Message); Assert.DoesNotContain(key, error.ToString());
    }

    [Fact]
    public void SameProvisionedKeyDecryptsAfterNewVaultInstance()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(25));
        var cipher = Create(key).EncryptPassword(secret);
        Assert.Equal(secret, Create(key).DecryptPassword(cipher));
        Assert.DoesNotContain(secret, cipher);
        Assert.NotEqual(cipher, Create(key).EncryptPassword(secret));
    }

    [Fact]
    public void WrongValidKeyCannotDecrypt()
    {
        var cipher = Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).EncryptPassword("test-only");
        Assert.ThrowsAny<CryptographicException>(() => Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).DecryptPassword(cipher));
    }

    [Fact]
    public void LegacyConfigurationRequiresExplicitConversion()
    {
        var old = Convert.ToBase64String(RandomNumberGenerator.GetBytes(40));
        var error = Assert.Throws<InvalidOperationException>(() => Create(null, old));
        Assert.Contains("legacy", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(old, error.ToString());
    }

    [Fact]
    public void ConvertedLegacyKeyDecryptsExistingUnversionedAesPayload()
    {
        var old = Convert.ToBase64String(RandomNumberGenerator.GetBytes(40));
        var derived = SHA256.HashData(Encoding.UTF8.GetBytes(old));
        var plain = Encoding.UTF8.GetBytes("legacy-test-only");
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(derived, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        Assert.Equal("legacy-test-only", Create(Convert.ToBase64String(derived)).DecryptPassword(Convert.ToBase64String([.. nonce, .. tag, .. cipher])));
    }
}

internal sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "BackupManager.SecurityTests";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
