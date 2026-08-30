using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace AgenteAnalista.Secrets;

// ── Configuration source ──────────────────────────────────────────────────────

/// <summary>
/// Fuente de configuración que lee un archivo JSON cifrado con AES-256-GCM.
/// La clave de descifrado se lee de la variable de entorno indicada en <see cref="KeyEnvVar"/>.
/// </summary>
public sealed class EncryptedJsonConfigSource : IConfigurationSource
{
    public string FilePath  { get; init; } = "appsettings.secrets.enc";
    public string KeyEnvVar { get; init; } = "AGENTEANALISTA_SECRET_KEY";
    public bool   Optional  { get; init; } = false;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new EncryptedJsonConfigProvider(this);
}

// ── Configuration provider ────────────────────────────────────────────────────

public sealed class EncryptedJsonConfigProvider : ConfigurationProvider
{
    private readonly EncryptedJsonConfigSource _src;

    public EncryptedJsonConfigProvider(EncryptedJsonConfigSource src) => _src = src;

    public override void Load()
    {
        var filePath = Path.IsPathRooted(_src.FilePath)
            ? _src.FilePath
            : Path.Combine(AppContext.BaseDirectory, _src.FilePath);

        if (!File.Exists(filePath))
        {
            if (_src.Optional) return;
            throw new FileNotFoundException(
                $"Archivo de secretos no encontrado: {filePath}. " +
                $"Generarlo con: dotnet run -- encrypt");
        }

        var keyHex = Environment.GetEnvironmentVariable(_src.KeyEnvVar);
        if (string.IsNullOrWhiteSpace(keyHex))
            throw new InvalidOperationException(
                $"Variable de entorno '{_src.KeyEnvVar}' no está definida. " +
                $"No se puede arrancar sin la clave de descifrado. " +
                $"Setearla con: $env:{_src.KeyEnvVar} = '<clave>'");

        try
        {
            var key      = Convert.FromHexString(keyHex);
            var raw      = File.ReadAllText(filePath);
            var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(raw)
                           ?? throw new InvalidDataException("Archivo de secretos vacío o inválido.");

            var nonce  = Convert.FromBase64String(envelope.Nonce);
            var tag    = Convert.FromBase64String(envelope.Tag);
            var cipher = Convert.FromBase64String(envelope.Data);
            var plain  = new byte[cipher.Length];

            using var aes = new AesGcm(key, tagSizeInBytes: 16);
            aes.Decrypt(nonce, cipher, tag, plain);

            var json = Encoding.UTF8.GetString(plain);

            // Parsear el JSON descifrado usando el pipeline estándar de configuración
            var temp = new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
                .Build();

            Data = new Dictionary<string, string?>(
                temp.AsEnumerable(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            throw new InvalidOperationException(
                $"No se pudo descifrar '{_src.FilePath}'. " +
                $"Verificar que '{_src.KeyEnvVar}' tenga la clave correcta.", ex);
        }
    }
}

// ── Utilidad de cifrado ───────────────────────────────────────────────────────

/// <summary>
/// Herramienta para generar la clave y cifrar el archivo de secretos.
/// Invocada desde el CLI de cada MCP que la necesite: <c>dotnet run -- encrypt</c>
/// </summary>
public static class SecretsEncryptor
{
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { WriteIndented = true };

    /// <summary>Genera una clave AES-256 aleatoria en formato hex (64 caracteres).</summary>
    public static string GenerateKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return Convert.ToHexString(key).ToLowerInvariant();
    }

    /// <summary>
    /// Cifra <paramref name="inputPath"/> (JSON plano) con AES-256-GCM
    /// y escribe el resultado en <paramref name="outputPath"/>.
    /// </summary>
    public static void Encrypt(string inputPath, string outputPath, string keyHex)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"No se encontró el archivo de secretos: {inputPath}");

        var key    = Convert.FromHexString(keyHex);
        var plain  = File.ReadAllBytes(inputPath);
        var nonce  = new byte[12];
        var tag    = new byte[16];
        var cipher = new byte[plain.Length];

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plain, cipher, tag);

        var envelope = new EncryptedEnvelope
        {
            Nonce = Convert.ToBase64String(nonce),
            Tag   = Convert.ToBase64String(tag),
            Data  = Convert.ToBase64String(cipher),
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(envelope, _jsonOpts));
        Console.WriteLine($"✓ {inputPath} → {outputPath} (AES-256-GCM)");
    }

    /// <summary>
    /// Descifra <paramref name="inputPath"/> (generado por <see cref="Encrypt"/>) y escribe
    /// el JSON plano en <paramref name="outputPath"/> — para poder editar los secretos.
    /// </summary>
    public static void Decrypt(string inputPath, string outputPath, string keyHex)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"No se encontró el archivo cifrado: {inputPath}");

        var key      = Convert.FromHexString(keyHex);
        var raw      = File.ReadAllText(inputPath);
        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(raw)
                       ?? throw new InvalidDataException("Archivo cifrado vacío o inválido.");

        var nonce  = Convert.FromBase64String(envelope.Nonce);
        var tag    = Convert.FromBase64String(envelope.Tag);
        var cipher = Convert.FromBase64String(envelope.Data);
        var plain  = new byte[cipher.Length];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Decrypt(nonce, cipher, tag, plain);

        File.WriteAllBytes(outputPath, plain);
        Console.WriteLine($"✓ {inputPath} → {outputPath} (descifrado)");
    }
}

// ── Envelope (formato del .enc) ───────────────────────────────────────────────

internal sealed class EncryptedEnvelope
{
    [JsonPropertyName("nonce")] public string Nonce { get; init; } = "";
    [JsonPropertyName("tag")]   public string Tag   { get; init; } = "";
    [JsonPropertyName("data")]  public string Data  { get; init; } = "";
}
