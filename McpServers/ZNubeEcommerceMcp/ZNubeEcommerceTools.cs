using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ZNubeEcommerceMcp;

/// <summary>
/// Wrapper de la API "ECommerceIntegration" de zNube (host real: api.znube.com.ar, confirmado en
/// el código fuente de Dragonfish — App.config de ZooLogicSA.Framework.zNube). Mismo contrato que
/// ya está probado en producción en PanelMasterHelp (Services/ZNubeService.cs) — se copia tal
/// cual, no se reinventa el request. Devuelve el JSON crudo (pretty-printed) tal como lo entrega
/// zNube, sin tipar a modelos — mismo criterio que `consultar`/`crear` de DragonfishApiMcp: dejar
/// que el modelo interprete la respuesta real en vez de mantener POCOs a mano.
///
/// `eCommerceType=1` está hardcodeado (Mercado Libre) — es el único que se pidió cubrir por
/// ahora. Si en el futuro hace falta Tienda Nube u otra plataforma, agregar un parámetro nuevo,
/// no asumir el valor.
/// </summary>
[McpServerToolType]
public sealed class ZNubeEcommerceTools(HttpClient http, IOptions<ZNubeEcommerceConfig> cfg)
{
    private const string BaseUrl = "https://api.znube.com.ar/ECommerceIntegration";
    private const int ECommerceTypeMercadoLibre = 1;

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
    [Description("Lista los clientes (perfiles) con storeId de Mercado Libre ya guardado. No expone ningún token.")]
    public string ListarPerfiles() =>
        JsonSerializer.Serialize(cfg.Value.Perfiles.Keys);

    [McpServerTool(Name = "obtener_orden")]
    [Description("Trae una orden de venta puntual de Mercado Libre tal como la tiene zNube (JSON crudo). Requiere el token vigente de zNube-token para ese cliente — pedirlo siempre en el momento, nunca asumir uno guardado.")]
    public Task<string> ObtenerOrden(
        [Description("Nombre del cliente (perfil, ver listar_perfiles) — resuelve el storeId guardado")] string perfil,
        [Description("Token vigente de zNube-token para este cliente (lo tiene MDA, rota — pedirlo siempre fresco)")] string token,
        [Description("ID de la orden en Mercado Libre")] long orderId) => Envolver(async () =>
    {
        var storeId = ResolverStoreId(perfil);
        var url = $"{BaseUrl}/GetOrder/?eCommerceType={ECommerceTypeMercadoLibre}&storeId={storeId}&orderId={orderId}";
        return await LlamarZNube(token, url);
    });

    [McpServerTool(Name = "buscar_ordenes")]
    [Description("Trae un rango de órdenes de venta de Mercado Libre desde un ID dado (JSON crudo de zNube).")]
    public Task<string> BuscarOrdenes(
        [Description("Nombre del cliente (perfil, ver listar_perfiles)")] string perfil,
        [Description("Token vigente de zNube-token para este cliente")] string token,
        [Description("ID de orden desde el cual empezar a traer")] long fromOrderId,
        [Description("Cantidad máxima de órdenes a traer")] int limit) => Envolver(async () =>
    {
        var storeId = ResolverStoreId(perfil);
        var url = $"{BaseUrl}/GetOrders?storeId={storeId}&eCommerceType={ECommerceTypeMercadoLibre}&fromOrderId={fromOrderId}&limit={limit}";
        return await LlamarZNube(token, url);
    });

    [McpServerTool(Name = "historial_ordenes")]
    [Description("Historial de eventos de un rango de órdenes de Mercado Libre desde un ID dado (JSON crudo de zNube) — útil para ver la secuencia de estados por los que pasó cada orden, no solo el estado final.")]
    public Task<string> HistorialOrdenes(
        [Description("Nombre del cliente (perfil, ver listar_perfiles)")] string perfil,
        [Description("Token vigente de zNube-token para este cliente")] string token,
        [Description("ID de orden desde el cual empezar a traer")] long fromOrderId,
        [Description("Cantidad máxima de órdenes a traer")] int limit) => Envolver(async () =>
    {
        var storeId = ResolverStoreId(perfil);
        var url = $"{BaseUrl}/GetOrdersHistory?storeId={storeId}&eCommerceType={ECommerceTypeMercadoLibre}&fromOrderId={fromOrderId}&limit={limit}";
        return await LlamarZNube(token, url);
    });

    [McpServerTool(Name = "historial_orden")]
    [Description("Historial de eventos de UNA orden puntual de Mercado Libre (JSON crudo de zNube) — la secuencia completa de estados de esa orden, no solo el estado final.")]
    public Task<string> HistorialOrden(
        [Description("Nombre del cliente (perfil, ver listar_perfiles)")] string perfil,
        [Description("Token vigente de zNube-token para este cliente")] string token,
        [Description("ID de la orden en Mercado Libre")] long orderId) => Envolver(async () =>
    {
        var storeId = ResolverStoreId(perfil);
        var url = $"{BaseUrl}/GetOrderHistory?eCommerceType={ECommerceTypeMercadoLibre}&storeId={storeId}&orderId={orderId}";
        return await LlamarZNube(token, url);
    });

    [McpServerTool(Name = "historial_reclamos")]
    [Description("Historial de reclamos de un rango de órdenes de Mercado Libre (JSON crudo de zNube).")]
    public Task<string> HistorialReclamos(
        [Description("Nombre del cliente (perfil, ver listar_perfiles)")] string perfil,
        [Description("Token vigente de zNube-token para este cliente")] string token,
        [Description("ID de orden desde el cual empezar a traer")] long fromOrderId,
        [Description("Cantidad máxima de órdenes a traer")] int limit) => Envolver(async () =>
    {
        var storeId = ResolverStoreId(perfil);
        var url = $"{BaseUrl}/GetClaimsHistory?storeId={storeId}&eCommerceType={ECommerceTypeMercadoLibre}&fromOrderId={fromOrderId}&limit={limit}";
        return await LlamarZNube(token, url);
    });

    private string ResolverStoreId(string perfil) =>
        cfg.Value.Perfiles.TryGetValue(perfil, out var p)
            ? p.StoreId
            : throw new McpException($"No hay storeId guardado para el cliente '{perfil}'. Usá listar_perfiles para ver los configurados, o pedilo y agregalo con el comando 'agregar-perfil'.");

    private async Task<string> LlamarZNube(string token, string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("zNube-token", token);
        using var resp = await http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}\n{PrettyJson(raw)}";

        return PrettyJson(raw);
    }

    private static string PrettyJson(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            return raw;
        }
    }
}
