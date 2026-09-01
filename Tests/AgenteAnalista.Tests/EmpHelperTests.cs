using GestionBackupsMcp;

namespace AgenteAnalista.Tests;

/// <summary>
/// Regresión del bug real de dar_alta_base_para_restore: se asumió que crutamdf debía quedar ""
/// (vacío), pero comparado contra una base creada de verdad por Dragonfish (RECOLETA, 2026-08-29)
/// el valor real es el placeholder literal "[Ruta predeterminada del servidor SQL]" — el error
/// vino de leer mal un "&amp;&amp;" de VFP (delimitador de comentario, no AND) en ent_basededatos.PRG.
/// </summary>
public class EmpHelperTests
{
    private static Dictionary<string, object?> FilaTemplate() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["empcod"] = "VIEJA",
        ["epath"] = "DRAGONFISH_VIEJA",
        ["descrip"] = "VIEJA",
        ["RutaBack"] = @"C:\algun\path\viejo",
        ["crutamdf"] = "",
        ["replica"] = true,
        ["otracolumnadesconocida"] = "no tocar",
    };

    [Fact]
    public void AplicarOverrides_Crutamdf_QuedaConPlaceholderLiteral_NuncaVacio()
    {
        var fila = FilaTemplate();

        EmpHelper.AplicarOverrides(fila, "NUEVA", "DRAGONFISH_NUEVA");

        Assert.Equal("[Ruta predeterminada del servidor SQL]", fila["crutamdf"]);
    }

    [Fact]
    public void AplicarOverrides_PisaSoloLasColumnasConfirmadas()
    {
        var fila = FilaTemplate();

        EmpHelper.AplicarOverrides(fila, "NUEVA", "DRAGONFISH_NUEVA");

        Assert.Equal("NUEVA", fila["empcod"]);
        Assert.Equal("DRAGONFISH_NUEVA", fila["epath"]);
        Assert.Equal("NUEVA", fila["descrip"]);
        Assert.Equal("", fila["RutaBack"]);
        Assert.Equal(false, fila["replica"]);
        // Columna no confirmada por nombre: queda tal cual vino de la plantilla, no se inventa.
        Assert.Equal("no tocar", fila["otracolumnadesconocida"]);
    }

    [Fact]
    public void AplicarOverrides_NuncaAgregaUnaColumnaQueNoExistiaEnLaPlantilla()
    {
        var fila = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        EmpHelper.AplicarOverrides(fila, "NUEVA", "DRAGONFISH_NUEVA");

        Assert.Empty(fila);
    }
}
