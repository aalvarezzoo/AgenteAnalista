// SqlDiagnosticoMcp — servidor MCP de diagnóstico SQL de solo lectura contra instalaciones de
// Dragonfish. No es "Claude con SSMS": expone tools chicas y específicas para explorar el
// esquema, describir tablas, ver la definición real de vistas/SPs, consultar, buscar un valor
// puntual y comparar el esquema entre dos bases — en vez de una única execute_sql genérica.
//
// Seguridad en dos capas:
//   1) Real: cada perfil usa un login SQL dedicado (SQL Authentication, nunca Integrated
//      Security ni sa) que en SQL Server solo tiene el rol db_datareader en las bases que se
//      vayan a consultar — así ni un bug de este código puede escribir nada.
//   2) Defensa en profundidad: ConsultaSqlValidator bloquea en el propio MCP cualquier sentencia
//      que no sea SELECT/WITH antes de mandarla a SQL Server.
//
// A propósito NO expone backup/restore (eso ya lo hace GestionBackupsMcp, con sus propios
// privilegios) ni ninguna operación de escritura — si en el futuro hiciera falta, tendría que
// ser una tool nueva y explícita, nunca una ampliación de consultar_sql.

using AgenteAnalista.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlDiagnosticoMcp;

// ── CLI: dotnet SqlDiagnosticoMcp.dll agregar-perfil <perfil> <instancia> <usuario> <password>
if (args.Length > 0 && args[0] == "agregar-perfil")
    return await AgregarPerfilSqlTool.RunAsync(args[1..]);

var builder = Host.CreateApplicationBuilder(args);

var secretsPath = Environment.GetEnvironmentVariable("AGENTEANALISTA_SECRETS_PATH") ?? "appsettings.secrets.enc";
((IConfigurationBuilder)builder.Configuration).Sources.Insert(0, new EncryptedJsonConfigSource
{
    FilePath  = secretsPath,
    KeyEnvVar = "AGENTEANALISTA_SECRET_KEY",
    Optional  = false,
});

builder.Services.Configure<SqlDiagnosticoConfig>(builder.Configuration.GetSection("SqlDiagnostico"));

// stdout queda reservado para el protocolo MCP (stdio transport) — todo log va a stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SqlDiagnosticoTools>();

await builder.Build().RunAsync();
return 0;
