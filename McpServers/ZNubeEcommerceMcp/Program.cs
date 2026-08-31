// ZNubeEcommerceMcp — servidor MCP para consultar la API "ECommerceIntegration" de zNube
// (api.znube.com.ar), donde queda registrada cada orden de venta de Mercado Libre antes de que
// Dragonfish la descargue como "operación" (tabla OPECOM). Pensado para trazabilidad: dado un
// incidente de "la venta no bajó" / "bajó con datos mal" / "cliente incorrecto", ver qué vio
// zNube de esa orden sin necesitar acceso al Portal de DevOps de zNube.
//
// Mismo contrato ya probado en producción en PanelMasterHelp (Services/ZNubeService.cs) — se
// copió tal cual, no se reinventó el request.
//
// El storeId de cada cliente se guarda como perfil (estable, no cambia). El token de zNube-token
// NUNCA se guarda — rota, lo tiene MDA, se pide como parámetro en cada llamada.

using AgenteAnalista.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZNubeEcommerceMcp;

// ── CLI: dotnet ZNubeEcommerceMcp.dll agregar-perfil <perfil> <storeId>
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

builder.Services.Configure<ZNubeEcommerceConfig>(builder.Configuration.GetSection("ZNubeEcommerce"));

builder.Services.AddHttpClient<ZNubeEcommerceTools>();

// stdout queda reservado para el protocolo MCP (stdio transport) — todo log va a stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ZNubeEcommerceTools>();

await builder.Build().RunAsync();
return 0;
