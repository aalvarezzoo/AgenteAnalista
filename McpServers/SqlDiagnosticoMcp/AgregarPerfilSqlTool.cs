using System.Text.Json;
using System.Text.Json.Nodes;
using AgenteAnalista.Secrets;

namespace SqlDiagnosticoMcp;

/// <summary>
/// Comando "agregar-perfil": registra un perfil de SqlDiagnosticoMcp (instancia SQL + login de
/// solo lectura) en appsettings.secrets.enc. A diferencia del "agregar-perfil" de
/// DragonfishApiMcp, acá no hace falta buscar nada por SQL — la persona ya sabe la instancia, y
/// el usuario/password son los del login que ella misma creó con rol db_datareader ÚNICAMENTE
/// (ver CLAUDE.md, sección SqlDiagnosticoMcp, para el script de creación de ese login). Nunca
/// pasar acá un login con permisos de escritura ni sa.
///
/// Uso: dotnet SqlDiagnosticoMcp.dll agregar-perfil &lt;perfil&gt; &lt;instancia&gt; &lt;usuario&gt; &lt;password&gt;
/// </summary>
public static class AgregarPerfilSqlTool
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Uso: agregar-perfil <perfil> <instancia> <usuario> <password>");
            return Task.FromResult(1);
        }

        var (perfil, instancia, usuario, password) = (args[0], args[1], args[2], args[3]);

        GuardarPerfil(perfil, instancia, usuario, password);

        Console.WriteLine($"✓ Perfil '{perfil}' guardado en appsettings.secrets.enc.");
        Console.WriteLine($"  Instancia: {instancia}");
        Console.WriteLine($"  Usuario:   {usuario}");
        Console.WriteLine("  (password no se muestra)");
        Console.WriteLine();
        Console.WriteLine("Antes de probarlo: rebuildear este proyecto y recargar la ventana de VS Code");
        Console.WriteLine("(el MCP ya corriendo lee su propia copia del .enc en bin/ hasta que se rebuildea).");
        return Task.FromResult(0);
    }

    private static void GuardarPerfil(string perfil, string instancia, string usuario, string password)
    {
        var keyHex = Environment.GetEnvironmentVariable("AGENTEANALISTA_SECRET_KEY")
            ?? throw new InvalidOperationException("AGENTEANALISTA_SECRET_KEY no está definida.");

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

        var sqlDiagnostico = root["SqlDiagnostico"] as JsonObject ?? new JsonObject();
        var perfiles       = sqlDiagnostico["Perfiles"] as JsonObject ?? new JsonObject();

        perfiles[perfil] = new JsonObject
        {
            ["Instancia"] = instancia,
            ["Usuario"]   = usuario,
            ["Password"]  = password,
        };

        sqlDiagnostico["Perfiles"] = perfiles;
        root["SqlDiagnostico"]     = sqlDiagnostico;

        File.WriteAllText(jsonPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        SecretsEncryptor.Encrypt(jsonPath, encPath, keyHex);
        File.Delete(jsonPath);
    }
}
