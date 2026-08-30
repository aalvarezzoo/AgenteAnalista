using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace DragonfishApiMcp;

/// <summary>
/// Carga y cachea en memoria el swagger.json que sirve en vivo cada instalación de
/// Dragonfish (uno distinto por perfil, pueden estar en versiones distintas). Se pide una
/// sola vez por perfil y se reusa mientras el proceso del MCP esté vivo — evita 607 tools
/// fijas: el mapa completo de la API se consulta por demanda desde acá.
/// </summary>
public sealed class SwaggerCatalog(HttpClient http)
{
    private readonly ConcurrentDictionary<string, Task<JsonNode>> _cache = new();

    public Task<JsonNode> GetAsync(string baseUrl) =>
        _cache.GetOrAdd(baseUrl, url => LoadAsync(url));

    private async Task<JsonNode> LoadAsync(string baseUrl)
    {
        var swagger = await http.GetFromJsonAsync<JsonNode>($"{baseUrl.TrimEnd('/')}/swagger.json")
            ?? throw new McpException($"swagger.json vacío en {baseUrl}");
        return swagger;
    }

    /// <summary>Nombre de entidad ("Factura") → clave real de path en el swagger ("/Factura/").</summary>
    public static string PathParaEntidad(string entidad) => "/" + entidad.Trim('/') + "/";

    /// <summary>Resuelve "$ref": "#/definitions/X" contra el propio documento, hasta <paramref name="maxDepth"/> niveles.</summary>
    public static JsonNode? ResolverRefs(JsonNode raiz, JsonNode? nodo, int maxDepth = 2, int profundidad = 0)
    {
        switch (nodo)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("$ref", out var refNode) && refNode?.GetValue<string>() is string refPath)
                {
                    if (profundidad >= maxDepth)
                        return new JsonObject { ["$ref"] = refPath };
                    var destino = ResolverPuntero(raiz, refPath);
                    return ResolverRefs(raiz, destino?.DeepClone(), maxDepth, profundidad + 1);
                }
                var resultado = new JsonObject();
                foreach (var kv in obj)
                    resultado[kv.Key] = ResolverRefs(raiz, kv.Value?.DeepClone(), maxDepth, profundidad);
                return resultado;
            case JsonArray arr:
                var lista = new JsonArray();
                foreach (var item in arr)
                    lista.Add(ResolverRefs(raiz, item?.DeepClone(), maxDepth, profundidad));
                return lista;
            default:
                return nodo?.DeepClone();
        }
    }

    private static JsonNode? ResolverPuntero(JsonNode raiz, string puntero)
    {
        JsonNode? actual = raiz;
        foreach (var parte in puntero.TrimStart('#', '/').Split('/'))
            actual = actual?[parte];
        return actual;
    }
}
