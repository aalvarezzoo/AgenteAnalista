// LogsMcp — servidor MCP de solo lectura para explorar logs de Dragonfish (y el Visor de eventos
// de Windows) durante el análisis de un incidente. No "sabe" de negocio: parsea la estructura real
// de cada formato de log conocido y da una línea de tiempo unificada, en vez de tener que grepear
// texto crudo a mano cada vez.
//
// Corre local, sobre archivos ya presentes en disco (ej. la carpeta de un incidente ya descargada)
// — no busca ni descarga nada de una PC remota, y no necesita credenciales ni perfiles.
//
// Formatos soportados hoy (se suman más incidente a incidente, no de entrada):
//   - operaciones.log (+ rotados .1..N)
//   - OperacionesDelBuscador.log (+ rotados .1..N)
//   - .evtx (Visor de eventos de Windows)

using LogsMcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdout queda reservado para el protocolo MCP (stdio transport) — todo log va a stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<LogsTools>();

await builder.Build().RunAsync();
