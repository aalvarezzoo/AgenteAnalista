using LogsMcp;

namespace AgenteAnalista.Tests;

public class ErrorLogParserTests
{
    [Fact]
    public void Parsea_un_bloque_de_error_con_stack_multilinea()
    {
        string[] lineas =
        [
            "01/09/2026, Base: ARKA9DAF, Usuario: 9DEJULIOARKA@GMAIL.COM, Aplicación: Zoo Logic Dragonfish Color y Talle, Versión: 16.0004.14964, Serie: 113340",
            "            Estado del sistema: 2, Nombre de la PC: WIN-PKPNDAGCSII, Usuario de la PC: arka9dj, Origen logueo: UI",
            "     19:01:53,",
            "********************************************************************************",
            "Programa: ",
            "Procedimiento: dibujanteimpresion.dibujar",
            "Nº Linea: 0",
            "",
            "*********** ERROR ***********",
            "Nº Error: 1426",
            "Message: Código de error OLE 0x80070057: The parameter is incorrect.",
            "StackLevel: 31",
        ];

        var e = Assert.Single(ErrorLogParser.Parsear(lineas));
        Assert.Equal(new DateTime(2026, 9, 1, 19, 1, 53), e.Momento);
        Assert.Equal("ARKA9DAF", e.Base);
        Assert.Equal("113340", e.Serie);
        Assert.Equal("WIN-PKPNDAGCSII", e.NombrePc);
        Assert.Contains("Nº Error: 1426", e.Detalle);
        Assert.Contains("dibujanteimpresion.dibujar", e.Detalle);
    }

    [Fact]
    public void Dos_bloques_de_error_seguidos_no_mezclan_el_detalle()
    {
        string[] lineas =
        [
            "01/09/2026, Base: A, Usuario: U, Aplicación: X, Versión: 1, Serie: 111",
            "            Estado del sistema: 2, Nombre de la PC: PC1, Usuario de la PC: u1, Origen logueo: UI",
            "     19:01:53,",
            "Nº Error: 1426",
            "01/09/2026, Base: B, Usuario: U2, Aplicación: X, Versión: 1, Serie: 222",
            "            Estado del sistema: 2, Nombre de la PC: PC2, Usuario de la PC: u2, Origen logueo: UI",
            "     19:05:00,",
            "Nº Error: 11",
        ];

        var eventos = ErrorLogParser.Parsear(lineas);
        Assert.Equal(2, eventos.Count);
        Assert.Equal("A", eventos[0].Base);
        Assert.Contains("1426", eventos[0].Detalle);
        Assert.DoesNotContain("222", eventos[0].Detalle); // no se coló nada del segundo bloque
        Assert.Equal("B", eventos[1].Base);
        Assert.Contains("Nº Error: 11", eventos[1].Detalle);
    }

    [Fact]
    public void Bloque_sin_lineas_de_detalle_igual_genera_evento_con_detalle_vacio()
    {
        string[] lineas =
        [
            "01/09/2026, Base: A, Usuario: U, Aplicación: X, Versión: 1, Serie: 111",
            "            Estado del sistema: 2, Nombre de la PC: PC1, Usuario de la PC: u1, Origen logueo: UI",
            "     19:01:53,",
        ];

        var e = Assert.Single(ErrorLogParser.Parsear(lineas));
        Assert.Equal("", e.Detalle);
    }
}
