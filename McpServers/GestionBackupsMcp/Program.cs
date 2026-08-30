// GestionBackupsMcp — servidor MCP para gestionar backups de clientes de Dragonfish: hoy
// restaura en modo silencioso (ZooBkp.exe, sin abrir ninguna ventana); más adelante también
// se va a encargar de bajarlos de SharePoint (hoy bloqueado por política de consentimiento de
// administrador del tenant — ver memoria del proyecto). Por eso el nombre no quedó atado solo
// a "restore": va a terminar haciendo más que eso.
//
// Corre local contra la instalación de Dragonfish de esta misma máquina — no necesita
// credenciales ni el cifrado de secretos del portal.
//
// La instancia SQL destino NO es un parámetro: ZooBkp.exe restaura contra la instancia que ya
// usa la instalación de Dragonfish local (confirmado con el usuario, no varía por llamada).
//
// Este MCP asume que el/los .zip del backup ya están en disco, en la carpeta que se le indique
// — la descarga automática se suma después.

using GestionBackupsMcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdout queda reservado para el protocolo MCP (stdio transport) — todo log va a stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<GestionBackupsTools>();

await builder.Build().RunAsync();
