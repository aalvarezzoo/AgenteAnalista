using System.Net;
using System.Text;
using DragonfishApiMcp;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace AgenteAnalista.Tests;

/// <summary>
/// Regresión del gap real encontrado el 2026-09-02: la API de Dragonfish exige POST /Autenticar
/// antes de aceptar IdCliente/Authorization en cualquier otra llamada — sin esto, cualquier perfil
/// con credenciales válidas y no vencidas devolvía 401 "Cliente no autenticado", indistinguible de
/// un token realmente vencido o mal configurado.
/// </summary>
public class AutenticadorDragonfishTests
{
    private static DragonfishPerfil Perfil() => new() { BaseUrl = "http://fake-dragon", IdCliente = "CLI", Authorization = "tok" };

    [Fact]
    public async Task AsegurarAutenticadoAsync_SoloLlamaAutenticarUnaVezPorPerfil()
    {
        var llamadas = 0;
        var http = new HttpClient(new FakeHttpMessageHandler(_ =>
        {
            llamadas++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var autenticador = new AutenticadorDragonfish(http);
        var perfil = Perfil();

        await autenticador.AsegurarAutenticadoAsync(perfil);
        await autenticador.AsegurarAutenticadoAsync(perfil);
        await autenticador.AsegurarAutenticadoAsync(perfil);

        Assert.Equal(1, llamadas);
    }

    [Fact]
    public async Task Invalidar_FuerzaUnAutenticarNuevoEnElProximoIntento()
    {
        var llamadas = 0;
        var http = new HttpClient(new FakeHttpMessageHandler(_ =>
        {
            llamadas++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var autenticador = new AutenticadorDragonfish(http);
        var perfil = Perfil();

        await autenticador.AsegurarAutenticadoAsync(perfil);
        autenticador.Invalidar(perfil);
        await autenticador.AsegurarAutenticadoAsync(perfil);

        Assert.Equal(2, llamadas);
    }

    [Fact]
    public async Task AsegurarAutenticadoAsync_ErrorHttp_TiraMcpExceptionConElIdCliente()
    {
        var http = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var autenticador = new AutenticadorDragonfish(http);

        var ex = await Assert.ThrowsAsync<McpException>(() => autenticador.AsegurarAutenticadoAsync(Perfil()));

        Assert.Contains("CLI", ex.Message);
    }

    [Fact]
    public async Task Consultar_Con401InesperadoDeLaEntidad_ReautenticaYReintentaUnaVez()
    {
        var llamadasAutenticar = 0;
        var llamadasEntidad = 0;
        var http = new HttpClient(new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/Autenticar"))
            {
                llamadasAutenticar++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            llamadasEntidad++;
            return llamadasEntidad == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json") };
        }));

        var cfg = Options.Create(new DragonfishApiConfig
        {
            Perfiles = new Dictionary<string, DragonfishPerfil> { ["TEST"] = Perfil() },
        });
        var swagger = new SwaggerCatalog(http);
        var autenticador = new AutenticadorDragonfish(http);
        var tools = new DragonfishApiTools(http, cfg, swagger, autenticador);

        var resultado = await tools.Consultar("TEST", "Articulo", null);

        Assert.Contains("ok", resultado);
        Assert.Equal(2, llamadasAutenticar); // la inicial + la forzada tras el 401 inesperado
        Assert.Equal(2, llamadasEntidad);    // la que fallo con 401 + el reintento que si funciono
    }
}
