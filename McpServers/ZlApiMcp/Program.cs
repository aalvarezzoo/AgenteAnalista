// ZlApiMcp — servidor MCP que expone la API de ZL (Tareas/Incidentes) como tools de Claude.
//
// ZlApiClient y los modelos (ZlTarea/ZlIncidente/ZlComprobanteCierre/ZlApiConfig) son copia
// propia de este repo — ver el resto de los .cs de esta carpeta. El cifrado de secretos
// (EncryptedJsonConfigSource) es el propio de AgenteAnalista (ver Shared/Secrets).
//
// Solo lectura por ahora (GetTarea/GetIncidente/BuscarTareasPendientes) — los PUT/POST de
// ZlApiClient quedan afuera hasta probar el circuito contra la API real.
//
// Requiere:
//   - AGENTEANALISTA_SECRET_KEY: clave AES-256 para descifrar appsettings.secrets.enc.
//   - Un appsettings.secrets.enc con la sección "ZlApi" (BaseUrl/IdCliente/Authorization/
//     BaseDeDatos). Por defecto se busca junto al ejecutable; AGENTEANALISTA_SECRETS_PATH
//     permite apuntar a otra ruta si hiciera falta.
//
// Todavía no hay URL/token reales de la API de ZL (mismo estado que ZlApiClient) — este
// servidor compila y queda listo, pero no se probó contra la API real.

using AgenteAnalista.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZlApiMcp;

var builder = Host.CreateApplicationBuilder(args);

var secretsPath = Environment.GetEnvironmentVariable("AGENTEANALISTA_SECRETS_PATH") ?? "appsettings.secrets.enc";
((IConfigurationBuilder)builder.Configuration).Sources.Insert(0, new EncryptedJsonConfigSource
{
    FilePath  = secretsPath,
    KeyEnvVar = "AGENTEANALISTA_SECRET_KEY",
    Optional  = false,
});

var zlCfg = builder.Configuration.GetSection("ZlApi").Get<ZlApiConfig>() ?? new ZlApiConfig();
builder.Services.AddHttpClient<ZlApiClient>(http =>
{
    http.DefaultRequestHeaders.TryAddWithoutValidation("IdCliente",     zlCfg.IdCliente);
    http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", zlCfg.Authorization);
    http.DefaultRequestHeaders.TryAddWithoutValidation("BaseDeDatos",   zlCfg.BaseDeDatos);
});

// stdout queda reservado para el protocolo MCP (stdio transport) — todo log va a stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ZlApiTools>();

await builder.Build().RunAsync();
