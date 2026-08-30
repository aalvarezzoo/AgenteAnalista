using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using AgenteAnalista.Secrets;

namespace DragonfishApiMcp;

/// <summary>
/// Comando "agregar-perfil": registra un perfil de DragonfishApiMcp a partir del token que
/// Dragonfish ya entrega con el botón "Obtener Token" de la pantalla Cliente REST API — no lo
/// reimplementa (una versión anterior firmaba el JWT a mano, replicando la herramienta getJWT
/// previa a ese botón; se sacó por frágil: cualquier detalle de la firma que no coincidiera
/// byte a byte con lo que Dragonfish esperaba producía un 401 sin pista real del motivo).
///
/// El "Cliente REST API" y el "Servicio REST API" siguen creándose a mano en Dragonfish (eso no
/// se automatiza) — este comando solo evita transcribir host/puerto/base de datos a mano: los
/// busca por SQL en DRAGONFISH_ZOOLOGICMASTER usando el Código del Cliente REST API.
///
/// Uso: dotnet DragonfishApiMcp.dll agregar-perfil &lt;sqlInstance&gt; &lt;perfil&gt; &lt;idCliente&gt; &lt;token&gt;
/// </summary>
public static class AgregarPerfilTool
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Uso: agregar-perfil <sqlInstance> <perfil> <idCliente> <token>");
            return 1;
        }

        var sqlInstance = args[0];
        var perfil      = args[1];
        var idCliente   = args[2];
        var token       = args[3];

        var (baseUrl, baseDeDatos) = await BuscarConexionAsync(sqlInstance, idCliente);
        if (baseUrl is null)
        {
            Console.Error.WriteLine(
                $"No se encontró un Cliente REST API con Código '{idCliente}' en {sqlInstance}\\DRAGONFISH_ZOOLOGICMASTER.");
            return 1;
        }

        GuardarPerfil(perfil, idCliente, token, baseUrl, baseDeDatos);

        Console.WriteLine($"✓ Perfil '{perfil}' guardado en appsettings.secrets.enc.");
        Console.WriteLine($"  IdCliente:   {idCliente}");
        Console.WriteLine($"  BaseUrl:     {baseUrl}");
        Console.WriteLine($"  BaseDeDatos: {(string.IsNullOrEmpty(baseDeDatos) ? "(vacío)" : baseDeDatos)}");
        return 0;
    }

    private static async Task<(string? BaseUrl, string BaseDeDatos)> BuscarConexionAsync(string sqlInstance, string idCliente)
    {
        var connStr = $"Server={sqlInstance};Database=DRAGONFISH_ZOOLOGICMASTER;Integrated Security=true;TrustServerCertificate=true;";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        const string sql = """
            SELECT sr.PUERTO, sr.PUESTO, sr.BASEDATOS
            FROM [ORGANIZACION].[SECRETREST] cr
            JOIN [ORGANIZACION].[SERVREST] sr ON sr.CODIGO = cr.SERVICIO
            WHERE cr.CODIGO = @idCliente
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@idCliente", idCliente);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (null, "");

        var puerto    = reader["PUERTO"].ToString() ?? "";
        var puesto    = reader["PUESTO"].ToString()?.Trim() ?? "";
        var baseDatos = reader["BASEDATOS"].ToString()?.Trim() ?? "";

        // El puesto que Dragonfish guarda es el nombre de la máquina — si es la misma desde
        // donde se corre esto, "localhost" es lo que realmente funciona (confirmado con TEST).
        var host = string.Equals(puesto, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? "localhost"
            : puesto;

        return ($"http://{host}:{puerto}/api.Dragonfish", baseDatos);
    }

    private static void GuardarPerfil(string perfil, string idCliente, string token, string baseUrl, string baseDeDatos)
    {
        var keyHex = Environment.GetEnvironmentVariable("PANELMH_SECRET_KEY")
            ?? throw new InvalidOperationException("PANELMH_SECRET_KEY no está definida.");

        const string encPath  = "appsettings.secrets.enc";
        const string jsonPath = "appsettings.secrets.json";

        JsonObject root;
        if (File.Exists(encPath))
        {
            SecretsEncryptor.Decrypt(encPath, jsonPath, keyHex);
            root = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsObject();
        }
        else
        {
            root = new JsonObject();
        }

        var dragonfishApi = root["DragonfishApi"] as JsonObject ?? new JsonObject();
        var perfiles      = dragonfishApi["Perfiles"] as JsonObject ?? new JsonObject();

        perfiles[perfil] = new JsonObject
        {
            ["BaseUrl"]       = baseUrl,
            ["IdCliente"]     = idCliente,
            ["Authorization"] = token,
            ["BaseDeDatos"]   = baseDeDatos,
        };

        dragonfishApi["Perfiles"] = perfiles;
        root["DragonfishApi"]     = dragonfishApi;

        File.WriteAllText(jsonPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        SecretsEncryptor.Encrypt(jsonPath, encPath, keyHex);
        File.Delete(jsonPath);
    }
}
