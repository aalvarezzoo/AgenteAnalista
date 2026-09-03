using LogsMcp;

namespace AgenteAnalista.Tests;

public class SesionLogParserTests
{
    private static readonly string[] BloqueValido =
    [
        "02/09/2026, Base: 9DJ, Usuario: 9DEJULIO@REBMAN.COM.AR, Aplicación: Zoo Logic Dragonfish Color y Talle, Versión: 16.0004.14964, Serie: 113339",
        "            Estado del sistema: 2, Nombre de la PC: WIN-PKPNDAGCSII, Usuario de la PC: 9deJulioAF, Origen logueo: UI",
        "    09:06:36, SEGURIDAD, Menu -> Consultas -> Stock Y Precios Entre Locales",
        "",
    ];

    [Fact]
    public void Parsea_un_bloque_valido()
    {
        var eventos = SesionLogParser.Parsear(BloqueValido).ToList();

        var e = Assert.Single(eventos);
        Assert.Equal(new DateTime(2026, 9, 2, 9, 6, 36), e.Momento);
        Assert.Equal("9DJ", e.Base);
        Assert.Equal("113339", e.Serie);
        Assert.Equal("WIN-PKPNDAGCSII", e.NombrePc);
        // "Mensaje" es todo lo que sigue a la hora tal cual — no se intenta separar el módulo
        // (ej. "SEGURIDAD") del mensaje en sí, porque el formato varía (a veces son 3 campos
        // separados por coma, a veces 2 con "->" en el medio) y separarlos a la fuerza sería frágil.
        Assert.Equal("SEGURIDAD, Menu -> Consultas -> Stock Y Precios Entre Locales", e.Mensaje);
    }

    [Fact]
    public void Parsea_dos_bloques_pegados_sin_linea_en_blanco_entre_medio()
    {
        // BloqueValido[..^1] descarta la línea en blanco final — este test prueba justamente que
        // no hace falta esa línea en blanco para que arranque bien el próximo bloque.
        var lineas = BloqueValido[..^1].Concat(BloqueValido[..^1]).ToList();
        var eventos = SesionLogParser.Parsear(lineas).ToList();
        Assert.Equal(2, eventos.Count);
    }

    [Fact]
    public void Un_solo_encabezado_puede_tener_varias_lineas_de_mensaje()
    {
        // Confirmado en la práctica en ZOOSESSION.log: una importación deja varias líneas de
        // mensaje (uno por paso) bajo el mismo encabezado, sin repetirlo.
        string[] lineas =
        [
            "02/09/2026, Base: REBMAN, Usuario: X@EMINTEX.COM.AR, Aplicación: Zoo Logic Dragonfish Color y Talle, Versión: 16.0004.14964, Serie: 113364",
            "            Estado del sistema: 2, Nombre de la PC: WIN-PKPNDAGCSII, Usuario de la PC: expooferta, Origen logueo: UI",
            "    10:44:48,Importación diseño REMITO",
            "    10:44:48,Base de datos Seleccionada: Base de datos activa",
            "    10:45:00,La importación del diseño REMITO ha finalizado.",
        ];

        var eventos = SesionLogParser.Parsear(lineas).ToList();
        Assert.Equal(3, eventos.Count);
        Assert.All(eventos, e => Assert.Equal("REBMAN", e.Base));
        Assert.Equal("Importación diseño REMITO", eventos[0].Mensaje);
        Assert.Equal("La importación del diseño REMITO ha finalizado.", eventos[2].Mensaje);
    }

    [Fact]
    public void Base_vacia_no_rompe_el_parseo()
    {
        string[] lineas =
        [
            "02/09/2026, Base: , Usuario: MMAX@REBMAN.COM.AR, Aplicación: Zoo Logic Dragonfish Color y Talle, Versión: 16.0004.14964, Serie: 113356",
            "            Estado del sistema: 2, Nombre de la PC: WIN-PKPNDAGCSII, Usuario de la PC: mmax, Origen logueo: UI",
            "    09:05:59, SEGURIDAD, Menu -> Base De Datos -> Mmax",
        ];

        var e = Assert.Single(SesionLogParser.Parsear(lineas));
        Assert.Equal("", e.Base);
    }

    [Fact]
    public void Formato_de_mensaje_sin_espacio_despues_de_la_coma_tambien_parsea()
    {
        string[] lineas =
        [
            "01/09/2026, Base: EMINTEX, Usuario: ADMIN, Aplicación: Zoo Logic Dragonfish Color y Talle, Versión: 16.0004.14964, Serie: 113395",
            "            Estado del sistema: 2, Nombre de la PC: WIN-PKPNDAGCSII, Usuario de la PC: Administrador, Origen logueo: UI",
            "    16:06:20,DISENOIMPO -> Modificar: CLASEHOOK ART1, CODIGO ART 1.",
        ];

        var e = Assert.Single(SesionLogParser.Parsear(lineas));
        Assert.Equal("DISENOIMPO -> Modificar: CLASEHOOK ART1, CODIGO ART 1.", e.Mensaje);
    }

    [Fact]
    public void Bloque_incompleto_no_genera_evento()
    {
        string[] lineas =
        [
            "02/09/2026, Base: 9DJ, Usuario: X@REBMAN.COM.AR, Aplicación: Zoo Logic Dragonfish Color y Talle, Versión: 16.0004.14964, Serie: 113339",
            "            Estado del sistema: 2, Nombre de la PC: WIN-PKPNDAGCSII, Usuario de la PC: x, Origen logueo: UI",
            // sin línea de mensaje
        ];

        Assert.Empty(SesionLogParser.Parsear(lineas));
    }
}
