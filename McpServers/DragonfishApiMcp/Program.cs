// DragonfishApiMcp — servidor MCP genérico para la API REST de Dragonfish.
//
// A diferencia de ZlApiMcp (una sola API interna, con modelos tipados), la API de
// Dragonfish tiene 607 endpoints/754 definitions — un tool por endpoint es inviable. Este
// MCP no tiene tools fijas por entidad: carga el swagger.json que sirve en vivo cada
// instalación (SwaggerCatalog) y expone tools genéricas que lo recorren por demanda
// (listar_entidades/describir_entidad/consultar/crear en DragonfishApiTools).
//
// Multi-tenant: cada instalación de Dragonfish (cada cliente DRAGONFISH_*) tiene su propio
// host:puerto e IdCliente/Authorization. Cada una se registra como un "perfil" con nombre
// corto en la sección "DragonfishApi:Perfiles" de appsettings.secrets.enc — cifrado propio
// de este repo (ver Shared/Secrets), independiente del que usa PanelMasterHelp.
//
// Dragonfish valida a nivel entidad en cada alta igual que una carga manual, así que el
// MCP no reimplementa reglas de negocio — un body inválido vuelve con el error real del
// servidor.

using AgenteAnalista.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DragonfishApiMcp;

// ── CLI: dotnet DragonfishApiMcp.dll encrypt / decrypt / generate-key ────────
// Manejo del archivo de secretos propio de este repo (ver Shared/Secrets/EncryptedJsonConfig.cs).
if (args.Contains("generate-key"))
{
    var key = SecretsEncryptor.GenerateKey();
    Console.WriteLine("Nueva clave generada (guardar en lugar seguro):");
    Console.WriteLine(key);
    Console.WriteLine();
    Console.WriteLine("Setear con:");
    Console.WriteLine($"  [System.Environment]::SetEnvironmentVariable('AGENTEANALISTA_SECRET_KEY', '{key}', 'User')");
    return 0;
}

if (args.Contains("encrypt"))
{
    var keyHex = Environment.GetEnvironmentVariable("AGENTEANALISTA_SECRET_KEY")
        ?? throw new InvalidOperationException(
            "AGENTEANALISTA_SECRET_KEY no está definida. Setearla con: $env:AGENTEANALISTA_SECRET_KEY = '<64-char-hex>'");
    SecretsEncryptor.Encrypt("appsettings.secrets.json", "appsettings.secrets.enc", keyHex);
    return 0;
}

if (args.Contains("decrypt"))
{
    var keyHex = Environment.GetEnvironmentVariable("AGENTEANALISTA_SECRET_KEY")
        ?? throw new InvalidOperationException(
            "AGENTEANALISTA_SECRET_KEY no está definida. Setearla con: $env:AGENTEANALISTA_SECRET_KEY = '<64-char-hex>'");
    SecretsEncryptor.Decrypt("appsettings.secrets.enc", "appsettings.secrets.json", keyHex);
    return 0;
}

// ── CLI: dotnet DragonfishApiMcp.dll agregar-perfil <sqlInstance> <perfil> <idCliente> <token>
// Registra un perfil a partir del token que da el botón "Obtener Token" de Dragonfish. Ver AgregarPerfilTool.
if (args.Length > 0 && args[0] == "agregar-perfil")
    return await AgregarPerfilTool.RunAsync(args[1..]);

var builder = Host.CreateApplicationBuilder(args);

var secretsPath = Environment.GetEnvironmentVariable("AGENTEANALISTA_SECRETS_PATH") ?? "appsettings.secrets.enc";
((IConfigurationBuilder)builder.Configuration).Sources.Insert(0, new EncryptedJsonConfigSource
{
    FilePath  = secretsPath,
    KeyEnvVar = "AGENTEANALISTA_SECRET_KEY",
    Optional  = false,
});

builder.Services.Configure<DragonfishApiConfig>(builder.Configuration.GetSection("DragonfishApi"));

// SwaggerCatalog es singleton a propósito: su cache en memoria (un swagger por perfil)
// solo sirve si sobrevive entre llamadas a tools — DragonfishApiTools se resuelve nuevo
// por cada invocación, SwaggerCatalog no.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<SwaggerCatalog>(sp =>
    new SwaggerCatalog(sp.GetRequiredService<IHttpClientFactory>().CreateClient()));

builder.Services.AddHttpClient<DragonfishApiTools>();

// stdout queda reservado para el protocolo MCP (stdio transport) — todo log va a stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DragonfishApiTools>();

await builder.Build().RunAsync();
return 0;
