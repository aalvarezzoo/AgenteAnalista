using System.Text.Json;
using System.Text.Json.Nodes;
using AgenteAnalista.Secrets;

namespace ZNubeEcommerceMcp;

/// <summary>
/// Comando "agregar-perfil": registra el storeId de Mercado Libre de un cliente en
/// appsettings.secrets.enc. A diferencia de los demás MCP de este repo, acá el "perfil" es un
/// cliente real (no un ambiente de prueba interno) y solo guarda el storeId — nunca un token,
/// que se pide fresco en cada incidente porque rota y lo tiene MDA.
///
/// Uso: dotnet ZNubeEcommerceMcp.dll agregar-perfil &lt;perfil&gt; &lt;storeId&gt;
/// </summary>
public static class AgregarPerfilTool
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Uso: agregar-perfil <perfil> <storeId>");
            return Task.FromResult(1);
        }

        var (perfil, storeId) = (args[0], args[1]);

        GuardarPerfil(perfil, storeId);

        Console.WriteLine($"✓ Perfil '{perfil}' guardado en appsettings.secrets.enc.");
        Console.WriteLine($"  StoreId: {storeId}");
        Console.WriteLine();
        Console.WriteLine("Recordá: rebuildear este proyecto y recargar la ventana de VS Code antes de probar el perfil");
        Console.WriteLine("(el MCP ya corriendo lee la copia vieja del .enc en su propio bin hasta que se rebuildea).");
        return Task.FromResult(0);
    }

    private static void GuardarPerfil(string perfil, string storeId)
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

        var znube    = root["ZNubeEcommerce"] as JsonObject ?? new JsonObject();
        var perfiles = znube["Perfiles"] as JsonObject ?? new JsonObject();

        perfiles[perfil] = new JsonObject
        {
            ["StoreId"] = storeId,
        };

        znube["Perfiles"]         = perfiles;
        root["ZNubeEcommerce"]    = znube;

        File.WriteAllText(jsonPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        SecretsEncryptor.Encrypt(jsonPath, encPath, keyHex);
        File.Delete(jsonPath);
    }
}
