using LogsMcp;

namespace AgenteAnalista.Tests;

public class BuscadorLogParserTests
{
    [Fact]
    public void Parsea_una_entrada_con_stack_trace()
    {
        string[] lineas =
        [
            "-------------------------------------------------------------",
            "19/8/2026 15:06:36 - ERROR: System.Data.SqlClient.SqlException: No se encuentra la columna 'funciones'...",
            "   en System.Data.SqlClient.SqlConnection.OnError(...)",
            "   en ZooLogicSA.Buscador.Colorytalle.Generados.Din_Busqueda101OB.ObtenerCodigoBarraAlternativo(String txt)",
            "-------------------------------------------------------------",
        ];

        var e = Assert.Single(BuscadorLogParser.Parsear(lineas));
        Assert.Equal(new DateTime(2026, 8, 19, 15, 6, 36), e.Momento);
        Assert.StartsWith("ERROR: System.Data.SqlClient.SqlException", e.Mensaje);
        Assert.Contains("Din_Busqueda101OB.ObtenerCodigoBarraAlternativo", e.Detalle);
    }

    [Fact]
    public void Parsea_varias_entradas_seguidas()
    {
        string[] lineas =
        [
            "19/8/2026 15:06:36 - ERROR: primer error",
            "   detalle 1",
            "2/9/2026 10:24:37 - ERROR: segundo error",
            "   detalle 2",
        ];

        var eventos = BuscadorLogParser.Parsear(lineas);
        Assert.Equal(2, eventos.Count);
        Assert.Equal("ERROR: primer error", eventos[0].Mensaje);
        Assert.Equal("ERROR: segundo error", eventos[1].Mensaje);
        Assert.Contains("detalle 1", eventos[0].Detalle);
    }

    [Fact]
    public void Formato_de_fecha_sin_cero_relleno_tambien_parsea()
    {
        // "2/9/2026" (día y mes sin cero a la izquierda) — visto en la práctica en Emintex.
        string[] lineas = ["2/9/2026 8:56:40 - ERROR: algo"];
        var e = Assert.Single(BuscadorLogParser.Parsear(lineas));
        Assert.Equal(new DateTime(2026, 9, 2, 8, 56, 40), e.Momento);
    }

    [Fact]
    public void Archivo_vacio_o_solo_separadores_no_genera_eventos()
    {
        string[] lineas = ["---------------", "---------------"];
        Assert.Empty(BuscadorLogParser.Parsear(lineas));
    }
}
