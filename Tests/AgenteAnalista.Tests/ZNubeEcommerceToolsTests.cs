using System.Net;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ZNubeEcommerceMcp;

namespace AgenteAnalista.Tests;

/// <summary>
/// Regresión de dos gaps encontrados en la auditoría del 2026-09-01: 1) "limit" se mandaba tal
/// cual a zNube sin ningún tope local; 2) un error HTTP de zNube (token vencido, 500, etc.) se
/// devolvía como un string de resultado normal en vez de McpException.
/// </summary>
public class ZNubeEcommerceToolsTests
{
    private static ZNubeEcommerceTools CrearTools(out Func<Uri?> ultimaUrl, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        HttpRequestMessage? ultimaRequest = null;
        var http = new HttpClient(new FakeHttpMessageHandler(req =>
        {
            ultimaRequest = req;
            return responder(req);
        }));
        var cfg = Options.Create(new ZNubeEcommerceConfig
        {
            Perfiles = new Dictionary<string, ZNubeEcommercePerfil>
            {
                ["CLIENTE"] = new() { StoreId = "12345" },
            },
        });
        ultimaUrl = () => ultimaRequest?.RequestUri;
        return new ZNubeEcommerceTools(http, cfg);
    }

    [Fact]
    public async Task BuscarOrdenes_LimitGigante_SeClampeaAntesDeMandarloAZNube()
    {
        var tools = CrearTools(out var ultimaUrl, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        });

        await tools.BuscarOrdenes("CLIENTE", "token-fresco", fromOrderId: 1, limit: 999_999);

        Assert.Contains("limit=100", ultimaUrl()!.Query);
        Assert.DoesNotContain("limit=999999", ultimaUrl()!.Query);
    }

    [Fact]
    public async Task ObtenerOrden_ErrorHttp_TiraMcpException_NoLoDevuelveComoResultadoNormal()
    {
        var tools = CrearTools(out _, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("token vencido"),
        });

        var ex = await Assert.ThrowsAsync<McpException>(
            () => tools.ObtenerOrden("CLIENTE", "token-viejo", orderId: 1));

        Assert.Contains("401", ex.Message);
    }
}
