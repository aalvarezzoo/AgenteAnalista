using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ZlApiMcp;

/// <summary>
/// Cliente HTTP para la API de ZL/BBRIF (Tareas, Incidentes/<c>mdaincmda</c>,
/// Cierre de tareas/<c>Mdacompcierretareas</c>). Circuito armado pero todavía no
/// integrado al flujo real del panel — no hay URL/token reales para probarlo en runtime.
/// Headers <c>IdCliente</c>/<c>Authorization</c>/<c>BaseDeDatos</c> se setean una sola vez
/// en <c>Program.cs</c> vía <see cref="HttpClient.DefaultRequestHeaders"/>.
/// </summary>
public class ZlApiClient(HttpClient http, IOptions<ZlApiConfig> cfg, ILogger<ZlApiClient> log)
{
    private const string PathTareas    = "/Tareas/";
    private const string PathIncidente = "/mdaincmda/";
    private const string PathCierre    = "/Mdacompcierretareas/";

    private static readonly JsonSerializerOptions JOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _baseUrl = cfg.Value.BaseUrl.TrimEnd('/');

    // ── Tareas ───────────────────────────────────────────────────

    public Task<ZlTarea?> GetTareaAsync(int numero, CancellationToken ct = default) =>
        GetAsync<ZlTarea>($"{PathTareas}{numero}", ct);

    /// <summary>
    /// Busca tareas asignadas a alguno de los <paramref name="owners"/> (analistas MH)
    /// sin comprobante de cierre. Usado por el botón de refresco manual (no hay
    /// reconciliación automática contra la API).
    /// PENDIENTE DE PROBAR: se intenta filtro server-side por query params; si la API
    /// no lo soporta, hay que traer todo y filtrar client-side acá mismo.
    /// </summary>
    public async Task<List<ZlTarea>> BuscarTareasPendientesAsync(IEnumerable<string> owners, CancellationToken ct = default)
    {
        var ownerList = owners.ToList();
        var url = $"{_baseUrl}{PathTareas}?numCIERRE=0";
        var tareas = await GetListAsync<ZlTarea>(url, ct);
        return tareas.Where(t => ownerList.Contains(t.Owner, StringComparer.OrdinalIgnoreCase)
                                  && t.NumCierre == 0)
                      .ToList();
    }

    /// <summary>PUT full-replace (asumido hasta confirmar si la API soporta patch parcial).</summary>
    public Task<ZlTarea?> PutTareaAsync(ZlTarea tarea, CancellationToken ct = default) =>
        PutAsync($"{PathTareas}{tarea.Numero}", tarea, ct);

    public Task<ZlTarea?> PostTareaAsync(ZlTarea nueva, CancellationToken ct = default) =>
        PostAsync($"{PathTareas}", nueva, ct);

    // ── Incidentes ───────────────────────────────────────────────

    public Task<ZlIncidente?> GetIncidenteAsync(int numero, CancellationToken ct = default) =>
        GetAsync<ZlIncidente>($"{PathIncidente}{numero}", ct);

    /// <summary>PUT full-replace (asumido). Leer el incidente lo más cerca posible de este PUT.</summary>
    public Task<ZlIncidente?> PutIncidenteAsync(ZlIncidente incidente, CancellationToken ct = default) =>
        PutAsync($"{PathIncidente}{incidente.Numero}", incidente, ct);

    // ── Cierre de tareas ─────────────────────────────────────────

    /// <summary>
    /// Genera un comprobante de cierre. Manualmente no hay campos editables al crearlo
    /// (fechaCierre/Cerrador se autocompletan, Obs nunca se carga) — <paramref name="obs"/>
    /// queda como parámetro por si la API sí lo acepta.
    /// </summary>
    public Task<ZlComprobanteCierre?> PostComprobanteCierreAsync(string? obs = null, CancellationToken ct = default) =>
        PostAsync(PathCierre, new ZlComprobanteCierre { Obs = obs ?? "" }, ct);

    // ── Bugs / Requerimientos ──────────────────────────────────────

    /// <summary>
    /// STUB — falta el schema real (path y campos JSON) de la entidad Bug de la API de ZL.
    /// A diferencia de Tareas/Incidente/Cierre, todavía no se relevó ningún JSON de ejemplo.
    /// Reemplazar por la llamada real (siguiendo el patrón de <see cref="PostTareaAsync"/>)
    /// en cuanto se confirme el contrato.
    /// </summary>
    public Task<int> PostBugAsync(BugCarga bug, CancellationToken ct = default)
    {
        log.LogWarning("PostBugAsync invocado sin schema real de la API de ZL todavía (bug: {Titulo})", bug.Titulo);
        throw new NotImplementedException(
            "Falta el schema real de Bug de la API de ZL (path, campos JSON) para poder cargarlo.");
    }

    /// <summary>
    /// STUB — falta el schema real (path y campos JSON) de la entidad Requerimiento de la
    /// API de ZL. Ver <see cref="PostBugAsync"/>.
    /// </summary>
    public Task<int> PostRequerimientoAsync(RequerimientoCarga req, CancellationToken ct = default)
    {
        log.LogWarning("PostRequerimientoAsync invocado sin schema real de la API de ZL todavía (req: {Titulo})", req.Titulo);
        throw new NotImplementedException(
            "Falta el schema real de Requerimiento de la API de ZL (path, campos JSON) para poder cargarlo.");
    }

    /// <summary>
    /// STUB — vincula un bug ya existente en ZL al incidente (ventana "Asignación de bugs
    /// a incidentes"). Falta el schema real (path y campos JSON) de esa entidad — se sabe
    /// que lleva Incidente + Bug + Asistentes + Observaciones, pero no se relevó el JSON.
    /// </summary>
    public Task AsignarBugExistenteAsync(int numeroIncidente, int numeroBug, CancellationToken ct = default)
    {
        log.LogWarning(
            "AsignarBugExistenteAsync invocado sin schema real de la API de ZL todavía (incidente #{Incidente}, bug #{Bug})",
            numeroIncidente, numeroBug);
        throw new NotImplementedException(
            "Falta el schema real de Asignación de bugs a incidentes de la API de ZL (path, campos JSON).");
    }

    // ── HTTP helpers ─────────────────────────────────────────────

    /// <summary>Un 404 real es "no encontrado" (devuelve default) — cualquier otro código de error
    /// (401, 500, etc.) o excepción de red se propaga tal cual, nunca se pisa con default. Antes
    /// este método atrapaba TODO y devolvía default/lista vacía en cualquier error, lo cual hacía
    /// indistinguible "no existe" de "credenciales inválidas" o "sin conexión" — un bug real, no
    /// solo el de sanitización de excepciones del SDK de MCP (ver skill mcp-tools-desarrollo).</summary>
    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        var url = $"{_baseUrl}{path}";
        var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return default;
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync(ct);
            log.LogError("ZL API GET {Url} respondió {Status}: {Detail}", url, resp.StatusCode, detail);
            throw new InvalidOperationException($"ZL API GET {url} respondió {(int)resp.StatusCode} {resp.ReasonPhrase}: {detail}");
        }
        return await resp.Content.ReadFromJsonAsync<T>(JOpts, ct);
    }

    private async Task<List<T>> GetListAsync<T>(string url, CancellationToken ct)
    {
        var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return [];
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync(ct);
            log.LogError("ZL API GET {Url} respondió {Status}: {Detail}", url, resp.StatusCode, detail);
            throw new InvalidOperationException($"ZL API GET {url} respondió {(int)resp.StatusCode} {resp.ReasonPhrase}: {detail}");
        }
        return await resp.Content.ReadFromJsonAsync<List<T>>(JOpts, ct) ?? [];
    }

    private async Task<T?> PutAsync<T>(string path, T body, CancellationToken ct)
    {
        var url = $"{_baseUrl}{path}";
        var resp = await http.PutAsJsonAsync(url, body, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync(ct);
            log.LogError("ZL API PUT {Url} respondió {Status}: {Detail}", url, resp.StatusCode, detail);
            resp.EnsureSuccessStatusCode();
        }
        return await resp.Content.ReadFromJsonAsync<T>(JOpts, ct);
    }

    private async Task<T?> PostAsync<T>(string path, T body, CancellationToken ct)
    {
        var url = $"{_baseUrl}{path}";
        var resp = await http.PostAsJsonAsync(url, body, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync(ct);
            log.LogError("ZL API POST {Url} respondió {Status}: {Detail}", url, resp.StatusCode, detail);
            resp.EnsureSuccessStatusCode();
        }
        return await resp.Content.ReadFromJsonAsync<T>(JOpts, ct);
    }
}
