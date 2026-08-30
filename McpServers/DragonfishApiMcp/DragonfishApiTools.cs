using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DragonfishApiMcp;

[McpServerToolType]
public sealed class DragonfishApiTools(HttpClient http, IOptions<DragonfishApiConfig> cfg, SwaggerCatalog swagger)
{
    /// <summary>Ver el mismo helper en SqlDiagnosticoTools.cs — el SDK de MCP sanitiza cualquier
    /// excepción que no sea McpException a un mensaje genérico antes de devolvérsela al modelo.
    /// Confirmado con una prueba real (perfil TEST vacío devolvía "An error occurred invoking..."
    /// en vez del motivo real).</summary>
    private static async Task<string> Envolver(Func<Task<string>> accion)
    {
        try
        {
            return await accion();
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

    [McpServerTool(Name = "listar_perfiles")]
    [Description("Lista los perfiles (instalaciones de Dragonfish) configurados. No expone credenciales.")]
    public string ListarPerfiles() =>
        JsonSerializer.Serialize(cfg.Value.Perfiles.Keys);

    [McpServerTool(Name = "listar_entidades")]
    [Description("Lista los recursos/entidades disponibles en la API de Dragonfish de un perfil, opcionalmente filtrando por texto en el path o la descripción.")]
    public Task<string> ListarEntidades(
        [Description("Nombre del perfil (ver listar_perfiles), ej. \"TEST\"")] string perfil,
        [Description("Texto opcional para filtrar (ej. \"factura\")")] string? filtro = null) => Envolver(async () =>
    {
        var p = ResolverPerfil(perfil);
        var doc = await swagger.GetAsync(p.BaseUrl);
        var paths = doc["paths"]?.AsObject() ?? throw new McpException("El swagger no tiene 'paths'.");

        var entidades = paths
            .Select(kv => new
            {
                path = kv.Key,
                metodos = kv.Value?.AsObject().Select(m => m.Key).ToArray() ?? [],
                resumen = kv.Value?.AsObject().FirstOrDefault().Value?["summary"]?.GetValue<string>(),
            })
            .Where(e => filtro is null
                || e.path.Contains(filtro, StringComparison.OrdinalIgnoreCase)
                || (e.resumen?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        return JsonSerializer.Serialize(entidades);
    });

    [McpServerTool(Name = "describir_entidad")]
    [Description("Devuelve el esquema real (parámetros GET o campos del body POST) de una entidad de Dragonfish, para saber qué mandar sin adivinar campos.")]
    public Task<string> DescribirEntidad(
        [Description("Nombre del perfil (ver listar_perfiles)")] string perfil,
        [Description("Nombre de la entidad sin barras, ej. \"Factura\"")] string entidad) => Envolver(async () =>
    {
        var p = ResolverPerfil(perfil);
        var doc = await swagger.GetAsync(p.BaseUrl);
        var path = SwaggerCatalog.PathParaEntidad(entidad);
        var operaciones = doc["paths"]?[path]
            ?? throw new McpException($"No existe la entidad '{entidad}' (esperaba el path '{path}'). Usá listar_entidades para ver los nombres válidos.");

        var resuelto = SwaggerCatalog.ResolverRefs(doc, operaciones.DeepClone());
        return resuelto!.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    });

    [McpServerTool(Name = "consultar")]
    [Description("GET contra una entidad de Dragonfish (ej. \"ConsultaStockYPrecios\", \"Articulo\", \"Cliente\"). filtros son query params opcionales tal como los describe describir_entidad (ej. {\"query\":\"R016\"}).")]
    public Task<string> Consultar(
        [Description("Nombre del perfil (ver listar_perfiles)")] string perfil,
        [Description("Nombre de la entidad sin barras, ej. \"Articulo\"")] string entidad,
        [Description("Query params opcionales, ej. {\"query\":\"R016\",\"limit\":\"5\"}")] Dictionary<string, string>? filtros = null) => Envolver(async () =>
    {
        var p = ResolverPerfil(perfil);
        var path = SwaggerCatalog.PathParaEntidad(entidad);
        var url = $"{p.BaseUrl.TrimEnd('/')}{path}";
        if (filtros is { Count: > 0 })
            url += "?" + string.Join("&", filtros.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        using var req = ConHeaders(new HttpRequestMessage(HttpMethod.Get, url), p);
        using var resp = await http.SendAsync(req);
        return await LeerRespuesta(resp);
    });

    [McpServerTool(Name = "crear")]
    [Description("POST (alta) contra una entidad de Dragonfish (ej. \"Factura\"). bodyJson debe respetar el esquema que devuelve describir_entidad. Dragonfish valida a nivel entidad igual que una carga manual — un body inválido vuelve con el error real del servidor.")]
    public Task<string> Crear(
        [Description("Nombre del perfil (ver listar_perfiles)")] string perfil,
        [Description("Nombre de la entidad sin barras, ej. \"Factura\"")] string entidad,
        [Description("Body en JSON crudo, según el esquema de describir_entidad")] string bodyJson) => Envolver(async () =>
    {
        var p = ResolverPerfil(perfil);
        var path = SwaggerCatalog.PathParaEntidad(entidad);
        var url = $"{p.BaseUrl.TrimEnd('/')}{path}";

        using var req = ConHeaders(new HttpRequestMessage(HttpMethod.Post, url), p);
        req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req);
        return await LeerRespuesta(resp);
    });

    private DragonfishPerfil ResolverPerfil(string perfil) =>
        cfg.Value.Perfiles.TryGetValue(perfil, out var p)
            ? p
            : throw new McpException($"No existe el perfil '{perfil}'. Usá listar_perfiles para ver los configurados.");

    private static HttpRequestMessage ConHeaders(HttpRequestMessage req, DragonfishPerfil p)
    {
        req.Headers.TryAddWithoutValidation("IdCliente", p.IdCliente);
        req.Headers.TryAddWithoutValidation("Authorization", p.Authorization);
        if (!string.IsNullOrEmpty(p.BaseDeDatos))
            req.Headers.TryAddWithoutValidation("BaseDeDatos", p.BaseDeDatos);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    private static async Task<string> LeerRespuesta(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadAsStringAsync();
        var estado = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
        return string.IsNullOrEmpty(body) ? estado : $"{estado}\n{body}";
    }
}
