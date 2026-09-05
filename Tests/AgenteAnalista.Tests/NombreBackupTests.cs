using GestionBackupsMcp;

namespace AgenteAnalista.Tests;

/// <summary>
/// Regresión del near-miss real: una primera versión de restaurar_backup llegó a restaurar un
/// backup de DRAGONFISH_ZOOLOGICMASTER que estaba en la misma carpeta que el backup pedido,
/// pisando datos de infraestructura. ElegirArchivo nunca debe tocar otro archivo que el pedido.
/// </summary>
public class NombreBackupTests
{
    [Fact]
    public void ElegirArchivo_IgnoraOtroBackupEnLaMismaCarpeta()
    {
        string[] archivos =
        [
            @"C:\1694233\20260827-110000-Jueves-DRAGONFISH_ZOOLOGICMASTER-16.0004.14964.zip",
            @"C:\1694233\20260827-110500-Jueves-DRAGONFISH_NCENTRO-16.0004.14964.zip",
        ];

        var elegido = NombreBackup.ElegirArchivo(archivos, "DRAGONFISH_NCENTRO");

        Assert.Equal(archivos[1], elegido);
    }

    [Fact]
    public void ElegirArchivo_SinCoincidencia_DevuelveNull_NoElijeOtroPorDefecto()
    {
        string[] archivos =
        [
            @"C:\1694233\20260827-110000-Jueves-DRAGONFISH_ZOOLOGICMASTER-16.0004.14964.zip",
        ];

        var elegido = NombreBackup.ElegirArchivo(archivos, "DRAGONFISH_NCENTRO");

        Assert.Null(elegido);
    }

    [Fact]
    public void ElegirArchivo_ComparacionDeBaseEsCaseInsensitive()
    {
        string[] archivos = [@"C:\x\20260827-110000-Jueves-dragonfish_ncentro-16.0004.14964.zip"];

        var elegido = NombreBackup.ElegirArchivo(archivos, "DRAGONFISH_NCENTRO");

        Assert.Equal(archivos[0], elegido);
    }

    [Fact]
    public void ElegirArchivo_NoMatcheaPorPrefijoParcial()
    {
        // "DRAGONFISH_NCENTRO2" no es lo mismo que "DRAGONFISH_NCENTRO" — match exacto, no Contains.
        string[] archivos = [@"C:\x\20260827-110000-Jueves-DRAGONFISH_NCENTRO2-16.0004.14964.zip"];

        var elegido = NombreBackup.ElegirArchivo(archivos, "DRAGONFISH_NCENTRO");

        Assert.Null(elegido);
    }

    [Fact]
    public void ElegirArchivo_ReconoceUnSnapshotDeZNube()
    {
        // Caso real: incidente 1697789, cliente Iair Gabriel Tawil, base "I AM".
        string[] archivos = [@"C:\1697789\Iair_Gabriel_Tawil_I_AM_20260903112430.exe"];

        var elegido = NombreBackup.ElegirArchivo(archivos, "I AM");

        Assert.Equal(archivos[0], elegido);
    }

    [Fact]
    public void ElegirArchivo_SnapshotMatcheaConPrefijoDragonfishAunqueElArchivoNoLoTenga()
    {
        // El snapshot de zNube nunca lleva el prefijo DRAGONFISH_ en el nombre — el llamador tiene
        // que poder pedirlo con o sin prefijo indistintamente, igual que con un .zip de ZooBkp.
        string[] archivos = [@"C:\1697789\Iair_Gabriel_Tawil_I_AM_20260903112430.exe"];

        var elegido = NombreBackup.ElegirArchivo(archivos, "DRAGONFISH_I AM");

        Assert.Equal(archivos[0], elegido);
    }

    [Fact]
    public void ElegirArchivo_SnapshotDeOtraBaseEnLaMismaCarpetaNoSeToca()
    {
        string[] archivos =
        [
            @"C:\1697789\Iair_Gabriel_Tawil_I_AM_20260903112430.exe",
            @"C:\1697789\Iair_Gabriel_Tawil_ZOOLOGICMASTER_20260903112430.exe",
        ];

        var elegido = NombreBackup.ElegirArchivo(archivos, "I AM");

        Assert.Equal(archivos[0], elegido);
    }

    [Theory]
    [InlineData("Iair_Gabriel_Tawil_I_AM_20260903112430.exe", "I AM", true)]
    [InlineData("Iair_Gabriel_Tawil_I_AM_20260903112430.exe", "DRAGONFISH_I AM", true)]
    [InlineData("Iair_Gabriel_Tawil_I_AM_20260903112430.exe", "OTRA", false)]
    [InlineData("Iair_Gabriel_Tawil_I_AM_2026090311243.exe", "I AM", false)] // timestamp de 13 dígitos, no 14
    [InlineData("Iair_Gabriel_Tawil_I_AM_20260903112430.zip", "I AM", false)] // no es .exe
    public void EsSnapshotDe_CasosPuntuales(string nombreArchivo, string nombreBase, bool esperado)
    {
        Assert.Equal(esperado, NombreBackup.EsSnapshotDe(nombreArchivo, nombreBase));
    }
}
