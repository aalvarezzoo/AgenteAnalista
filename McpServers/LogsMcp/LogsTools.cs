using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LogsMcp;

[McpServerToolType]
public sealed class LogsTools
{
    private const int LimiteMaximoEventos = 1000;
    private const int LimiteDefaultEventos = 200;

    private const string NombreOperaciones = "operaciones.log";
    private const string NombreBuscador = "OperacionesDelBuscador.log";
    private const string NombreZooSession = "ZOOSESSION.log";
    private const string NombreLogErr = "log.err";

    /// <summary>Todas las tools pasan por acá. El SDK de MCP sanitiza cualquier excepción que no
    /// sea <see cref="McpException"/> a un mensaje genérico antes de devolvérsela al modelo — mismo
    /// criterio que el resto de los MCP de este repo (ver skill mcp-tools-desarrollo).</summary>
    private static string Envolver(Func<string> accion)
    {
        try
        {
            return accion();
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "listar_logs")]
    [Description("Lista los archivos de una carpeta (ej. la carpeta de un incidente) e indica qué formato reconocido tiene cada uno (operaciones/buscador/eventosWindows/desconocido) y su tamaño. Punto de partida antes de leer nada — para saber qué tools puntuales usar.")]
    public string ListarLogs(
        [Description("Carpeta donde buscar, ej. C:\\1697224 o C:\\1697224\\Log")] string carpeta) => Envolver(() =>
    {
        if (!Directory.Exists(carpeta))
            throw new McpException($"No existe la carpeta '{carpeta}'.");

        var archivos = Directory.EnumerateFiles(carpeta).Select(f =>
        {
            var nombre = Path.GetFileName(f);
            var tipo = ClasificarArchivo(nombre);
            var info = new FileInfo(f);
            return new { nombre, tipo, bytes = info.Length };
        }).OrderBy(a => a.nombre).ToList();

        return JsonSerializer.Serialize(archivos);
    });

    [McpServerTool(Name = "leer_operaciones")]
    [Description("Parsea operaciones.log (+ sus rotaciones .1..N si existen en la misma carpeta) y devuelve los eventos (fecha/hora, Base, Usuario, Serie, PC, acción) como estructura, no texto crudo. Filtra por rango de fecha/hora y/o texto libre (busca en el texto de la acción). No hace falta indicar el nombre del archivo, se busca solo en la carpeta dada.")]
    public string LeerOperaciones(
        [Description("Carpeta donde está operaciones.log, ej. C:\\1697224\\Log")] string carpeta,
        [Description("Desde qué fecha/hora incluir (ej. \"2026-09-02 10:00:00\"), opcional")] string? desde = null,
        [Description("Hasta qué fecha/hora incluir, opcional")] string? hasta = null,
        [Description("Filtrar solo acciones que contengan este texto (ej. \"Stock Y Precios Entre Locales\"), opcional")] string? texto = null,
        [Description("Máximo de eventos a devolver (default 200, tope 1000)")] int limite = LimiteDefaultEventos) => Envolver(() =>
        LeerBloqueSesion(carpeta, NombreOperaciones, desde, hasta, texto, limite));

    [McpServerTool(Name = "leer_zoosession")]
    [Description("Parsea ZOOSESSION.log (+ rotaciones) y devuelve los eventos (fecha/hora, Base, Usuario, Serie, PC, mensaje) como estructura. Acá se loguean cosas como scripts ejecutados, marcas de entrada/salida e importaciones — no navegación de menú como operaciones.log. Un mismo encabezado puede traer varias líneas de mensaje (ej. los pasos de una importación). Filtra por rango de fecha/hora y/o texto libre.")]
    public string LeerZooSession(
        [Description("Carpeta donde está ZOOSESSION.log, ej. C:\\1697224\\Log")] string carpeta,
        [Description("Desde qué fecha/hora incluir, opcional")] string? desde = null,
        [Description("Hasta qué fecha/hora incluir, opcional")] string? hasta = null,
        [Description("Filtrar solo mensajes que contengan este texto, opcional")] string? texto = null,
        [Description("Máximo de eventos a devolver (default 200, tope 1000)")] int limite = LimiteDefaultEventos) => Envolver(() =>
        LeerBloqueSesion(carpeta, NombreZooSession, desde, hasta, texto, limite));

    [McpServerTool(Name = "leer_log_err")]
    [Description("Parsea log.err (+ rotaciones) y devuelve los errores (fecha/hora, Base, Usuario, Serie, PC, detalle con Programa/Procedimiento/Nº Error/Message/Stack tal cual vienen) como estructura. El detalle no se descompone campo por campo — el formato interno varía — pero queda como texto para leer o buscar. Filtra por rango de fecha/hora y/o texto libre (busca en el detalle).")]
    public string LeerLogErr(
        [Description("Carpeta donde está log.err, ej. C:\\1697224\\Log")] string carpeta,
        [Description("Desde qué fecha/hora incluir, opcional")] string? desde = null,
        [Description("Hasta qué fecha/hora incluir, opcional")] string? hasta = null,
        [Description("Filtrar solo errores cuyo detalle contenga este texto (ej. \"Nº Error: 1426\"), opcional")] string? texto = null,
        [Description("Máximo de eventos a devolver (default 200, tope 1000)")] int limite = LimiteDefaultEventos) => Envolver(() =>
    {
        var (fDesde, fHasta) = ParsearRango(desde, hasta);
        limite = Math.Clamp(limite, 1, LimiteMaximoEventos);

        var lineas = ArchivosLog.LeerTodasLasLineas(carpeta, NombreLogErr);
        var eventos = ErrorLogParser.Parsear(lineas)
            .Where(e => (fDesde is null || e.Momento >= fDesde) && (fHasta is null || e.Momento <= fHasta))
            .Where(e => texto is null || e.Detalle.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Momento)
            .ToList();

        return SerializarConTruncado(eventos, limite);
    });

    [McpServerTool(Name = "leer_buscador")]
    [Description("Parsea OperacionesDelBuscador.log (+ rotaciones) y devuelve los eventos de error (fecha/hora, mensaje, detalle/stack) como estructura. Filtra por rango de fecha/hora y/o texto libre (busca en mensaje y detalle).")]
    public string LeerBuscador(
        [Description("Carpeta donde está OperacionesDelBuscador.log, ej. C:\\1697224\\Log")] string carpeta,
        [Description("Desde qué fecha/hora incluir, opcional")] string? desde = null,
        [Description("Hasta qué fecha/hora incluir, opcional")] string? hasta = null,
        [Description("Filtrar solo entradas que contengan este texto en el mensaje o el detalle, opcional")] string? texto = null,
        [Description("Máximo de eventos a devolver (default 200, tope 1000)")] int limite = LimiteDefaultEventos) => Envolver(() =>
    {
        var (fDesde, fHasta) = ParsearRango(desde, hasta);
        limite = Math.Clamp(limite, 1, LimiteMaximoEventos);

        var lineas = ArchivosLog.LeerTodasLasLineas(carpeta, NombreBuscador);
        var eventos = BuscadorLogParser.Parsear(lineas)
            .Where(e => (fDesde is null || e.Momento >= fDesde) && (fHasta is null || e.Momento <= fHasta))
            .Where(e => texto is null
                || e.Mensaje.Contains(texto, StringComparison.OrdinalIgnoreCase)
                || e.Detalle.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Momento)
            .ToList();

        return SerializarConTruncado(eventos, limite);
    });

    [McpServerTool(Name = "leer_eventos_windows")]
    [Description("Lee un archivo .evtx (export del Visor de eventos de Windows) y devuelve los eventos como estructura (fecha/hora, nivel, proveedor, id, mensaje). Filtra por rango de fecha/hora y/o nivel (ej. \"Error\", \"Warning\").")]
    public string LeerEventosWindows(
        [Description("Ruta al archivo .evtx, ej. C:\\1697224\\visor.evtx")] string rutaEvtx,
        [Description("Desde qué fecha/hora incluir, opcional")] string? desde = null,
        [Description("Hasta qué fecha/hora incluir, opcional")] string? hasta = null,
        [Description("Filtrar solo este nivel (ej. \"Error\", \"Advertencia\"), opcional")] string? nivel = null,
        [Description("Máximo de eventos a devolver (default 200, tope 1000)")] int limite = LimiteDefaultEventos) => Envolver(() =>
    {
        if (!File.Exists(rutaEvtx))
            throw new McpException($"No existe el archivo '{rutaEvtx}'.");

        var (fDesde, fHasta) = ParsearRango(desde, hasta);
        limite = Math.Clamp(limite, 1, LimiteMaximoEventos);

        var eventos = EventosWindowsReader.Leer(rutaEvtx, fDesde, fHasta, nivel, limite);
        return SerializarConTruncado(eventos, limite);
    });

    [McpServerTool(Name = "linea_de_tiempo")]
    [Description("Combina operaciones.log, ZOOSESSION.log, log.err, OperacionesDelBuscador.log (y opcionalmente un .evtx) de una carpeta en UNA sola línea de tiempo, ordenada por fecha/hora — para no tener que cruzar a mano varios logs para entender qué pasó y en qué orden. Filtra por rango de fecha/hora, que conviene siempre acotar (basarse en los horarios que menciona el incidente) para no traer de más.")]
    public string LineaDeTiempo(
        [Description("Carpeta con los logs, ej. C:\\1697224\\Log")] string carpeta,
        [Description("Desde qué fecha/hora incluir (recomendado siempre indicarlo)")] string? desde = null,
        [Description("Hasta qué fecha/hora incluir (recomendado siempre indicarlo)")] string? hasta = null,
        [Description("Ruta a un .evtx para sumar a la línea de tiempo, opcional")] string? rutaEvtx = null,
        [Description("Máximo de eventos a devolver (default 300, tope 1000)")] int limite = 300) => Envolver(() =>
    {
        var (fDesde, fHasta) = ParsearRango(desde, hasta);
        limite = Math.Clamp(limite, 1, LimiteMaximoEventos);

        var eventos = new List<EventoLog>();

        eventos.AddRange(SesionLogParser.Parsear(ArchivosLog.LeerTodasLasLineas(carpeta, NombreOperaciones))
            .Select(e => new EventoLog(e.Momento, "operaciones", e.Mensaje, $"Base={e.Base}, Usuario={e.Usuario}, Serie={e.Serie}, PC={e.NombrePc}")));

        eventos.AddRange(SesionLogParser.Parsear(ArchivosLog.LeerTodasLasLineas(carpeta, NombreZooSession))
            .Select(e => new EventoLog(e.Momento, "zoosession", e.Mensaje, $"Base={e.Base}, Usuario={e.Usuario}, Serie={e.Serie}, PC={e.NombrePc}")));

        eventos.AddRange(ErrorLogParser.Parsear(ArchivosLog.LeerTodasLasLineas(carpeta, NombreLogErr))
            .Select(e => new EventoLog(e.Momento, "log.err", $"Error en Base={e.Base}, Serie={e.Serie}", e.Detalle)));

        eventos.AddRange(BuscadorLogParser.Parsear(ArchivosLog.LeerTodasLasLineas(carpeta, NombreBuscador))
            .Select(e => new EventoLog(e.Momento, "buscador", e.Mensaje, e.Detalle)));

        if (rutaEvtx is not null)
        {
            if (!File.Exists(rutaEvtx))
                throw new McpException($"No existe el archivo '{rutaEvtx}'.");
            eventos.AddRange(EventosWindowsReader.Leer(rutaEvtx, fDesde, fHasta, null, LimiteMaximoEventos)
                .Select(e => new EventoLog(e.Momento, "eventosWindows", $"[{e.Nivel}] {e.Proveedor} (Id {e.Id})", e.Mensaje)));
        }

        var filtrados = eventos
            .Where(e => (fDesde is null || e.Momento >= fDesde) && (fHasta is null || e.Momento <= fHasta))
            .OrderBy(e => e.Momento)
            .ToList();

        return SerializarConTruncado(filtrados, limite);
    });

    // ── helpers privados ──────────────────────────────────────────────────────

    /// <summary>internal (no private) a propósito — testeada directamente desde AgenteAnalista.Tests
    /// vía InternalsVisibleTo (ver LogsMcp.csproj), sin necesidad de instanciar la tool completa.</summary>
    internal static string ClasificarArchivo(string nombre)
    {
        if (ArchivosLog.EsArchivoDeLog(nombre, NombreOperaciones)) return "operaciones";
        if (ArchivosLog.EsArchivoDeLog(nombre, NombreBuscador)) return "buscador";
        if (ArchivosLog.EsArchivoDeLog(nombre, NombreZooSession)) return "zoosession";
        if (ArchivosLog.EsArchivoDeLog(nombre, NombreLogErr)) return "logErr";
        if (nombre.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase)) return "eventosWindows";
        return "desconocido";
    }

    private static string LeerBloqueSesion(string carpeta, string nombreArchivo, string? desde, string? hasta, string? texto, int limite)
    {
        var (fDesde, fHasta) = ParsearRango(desde, hasta);
        limite = Math.Clamp(limite, 1, LimiteMaximoEventos);

        var lineas = ArchivosLog.LeerTodasLasLineas(carpeta, nombreArchivo);
        var eventos = SesionLogParser.Parsear(lineas)
            .Where(e => (fDesde is null || e.Momento >= fDesde) && (fHasta is null || e.Momento <= fHasta))
            .Where(e => texto is null || e.Mensaje.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Momento)
            .ToList();

        return SerializarConTruncado(eventos, limite);
    }

    private static (DateTime? desde, DateTime? hasta) ParsearRango(string? desde, string? hasta) =>
        (ParsearFecha(desde, nameof(desde)), ParsearFecha(hasta, nameof(hasta)));

    private static DateTime? ParsearFecha(string? texto, string nombreParametro)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        if (DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)) return f;
        if (DateTime.TryParse(texto, CultureInfo.GetCultureInfo("es-AR"), DateTimeStyles.None, out f)) return f;
        throw new McpException($"No se pudo interpretar '{texto}' como fecha/hora para '{nombreParametro}' — probá formato \"yyyy-MM-dd HH:mm:ss\" o \"dd/MM/yyyy HH:mm:ss\".");
    }

    private static string SerializarConTruncado<T>(List<T> eventos, int limite)
    {
        var truncado = eventos.Count > limite;
        var aDevolver = truncado ? eventos.Take(limite).ToList() : eventos;
        return JsonSerializer.Serialize(new
        {
            eventos = aDevolver,
            cantidad = aDevolver.Count,
            truncado,
            nota = truncado ? $"Se cortó en {limite} eventos — hay más. Acotá el rango de fecha/hora o subí el límite." : null,
        });
    }
}
