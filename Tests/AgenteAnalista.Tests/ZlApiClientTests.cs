using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZlApiMcp;

namespace AgenteAnalista.Tests;

/// <summary>
/// Regresión del bug real documentado en la skill mcp-tools-desarrollo: ZlApiClient llegó a
/// tragarse CUALQUIER error (401, 500, timeout) y devolver null/lista vacía — indistinguible de
/// "no encontrado". Solo un 404 real es "no encontrado"; cualquier otro error se tiene que propagar.
/// </summary>
public class ZlApiClientTests
{
    private static ZlApiClient CrearCliente(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var http = new HttpClient(new FakeHttpMessageHandler(responder));
        var cfg = Options.Create(new ZlApiConfig { BaseUrl = "http://fake-zl" });
        return new ZlApiClient(http, cfg, NullLogger<ZlApiClient>.Instance);
    }

    [Fact]
    public async Task Get_404Real_DevuelveNull()
    {
        var client = CrearCliente(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var tarea = await client.GetTareaAsync(123);

        Assert.Null(tarea);
    }

    [Fact]
    public async Task Get_Error500_PropagaExcepcion_NoLoConfundeConNoEncontrado()
    {
        var client = CrearCliente(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom"),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetTareaAsync(123));
    }

    [Fact]
    public async Task Get_Error401_PropagaExcepcion_NoLoConfundeConNoEncontrado()
    {
        var client = CrearCliente(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("credenciales invalidas"),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetIncidenteAsync(123));
    }

    [Fact]
    public async Task BuscarTareasPendientes_Error500_PropagaExcepcion_NoDevuelveListaVacia()
    {
        var client = CrearCliente(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom"),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.BuscarTareasPendientesAsync(["AALVAREZ"]));
    }

    [Fact]
    public async Task BuscarTareasPendientes_404_DevuelveListaVacia()
    {
        var client = CrearCliente(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var tareas = await client.BuscarTareasPendientesAsync(["AALVAREZ"]);

        Assert.Empty(tareas);
    }
}
