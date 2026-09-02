using System.Net;
using System.Text;
using System.Text.Json;
using DragonfishApiMcp;
using Microsoft.Extensions.Options;

namespace AgenteAnalista.Tests;

/// <summary>
/// Regresión del cap agregado a listar_entidades (2026-09-01): sin filtro, antes devolvía las
/// ~600 entidades del swagger completo sin ningún tope. Ahora corta a 30 y avisa con "nota".
/// </summary>
public class DragonfishListarEntidadesTests
{
    private const int TotalPathsFixture = 40;
    private const int LimiteEntidades = 30;

    private static DragonfishApiTools CrearTools(string swaggerJson)
    {
        var http = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(swaggerJson, Encoding.UTF8, "application/json"),
        }));
        var cfg = Options.Create(new DragonfishApiConfig
        {
            Perfiles = new Dictionary<string, DragonfishPerfil>
            {
                ["TEST"] = new() { BaseUrl = "http://fake-dragon" },
            },
        });
        var swagger = new SwaggerCatalog(http);
        var autenticador = new AutenticadorDragonfish(http);
        return new DragonfishApiTools(http, cfg, swagger, autenticador);
    }

    private static string SwaggerFixture(int cantidadPaths)
    {
        var paths = new Dictionary<string, object>();
        for (int i = 0; i < cantidadPaths; i++)
        {
            paths[$"/Entidad{i}/"] = new Dictionary<string, object>
            {
                ["get"] = new Dictionary<string, object> { ["summary"] = $"Entidad número {i}" },
            };
        }
        return JsonSerializer.Serialize(new { paths });
    }

    [Fact]
    public async Task SinFiltro_MasDe30_CortaA30YAvisaConNota()
    {
        var tools = CrearTools(SwaggerFixture(TotalPathsFixture));

        var json = await tools.ListarEntidades("TEST", null);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(TotalPathsFixture, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(LimiteEntidades, doc.RootElement.GetProperty("entidades").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, doc.RootElement.GetProperty("nota").ValueKind);
    }

    [Fact]
    public async Task ConFiltro_MenosDe30_NoTruncaYNotaEsNull()
    {
        var tools = CrearTools(SwaggerFixture(TotalPathsFixture));

        // El filtro "Entidad1" matchea Entidad1, Entidad10-19 (11 resultados) — bien por debajo de 30.
        var json = await tools.ListarEntidades("TEST", "Entidad1");
        using var doc = JsonDocument.Parse(json);

        var total = doc.RootElement.GetProperty("total").GetInt32();
        Assert.True(total <= LimiteEntidades);
        Assert.Equal(total, doc.RootElement.GetProperty("entidades").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("nota").ValueKind);
    }
}
