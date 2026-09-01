using GestionBackupsMcp;

namespace AgenteAnalista.Tests;

/// <summary>
/// Regresión del near-miss real: una primera versión de restaurar_backup llegó a restaurar un
/// backup de DRAGONFISH_ZOOLOGICMASTER que estaba en la misma carpeta que el backup pedido,
/// pisando datos de infraestructura. ElegirZip nunca debe tocar otro .zip que el pedido.
/// </summary>
public class NombreBackupTests
{
    [Fact]
    public void ElegirZip_IgnoraOtroBackupEnLaMismaCarpeta()
    {
        string[] zips =
        [
            @"C:\1694233\20260827-110000-Jueves-DRAGONFISH_ZOOLOGICMASTER-16.0004.14964.zip",
            @"C:\1694233\20260827-110500-Jueves-DRAGONFISH_NCENTRO-16.0004.14964.zip",
        ];

        var elegido = NombreBackup.ElegirZip(zips, "DRAGONFISH_NCENTRO");

        Assert.Equal(zips[1], elegido);
    }

    [Fact]
    public void ElegirZip_SinCoincidencia_DevuelveNull_NoElijeOtroPorDefecto()
    {
        string[] zips =
        [
            @"C:\1694233\20260827-110000-Jueves-DRAGONFISH_ZOOLOGICMASTER-16.0004.14964.zip",
        ];

        var elegido = NombreBackup.ElegirZip(zips, "DRAGONFISH_NCENTRO");

        Assert.Null(elegido);
    }

    [Fact]
    public void ElegirZip_ComparacionDeBaseEsCaseInsensitive()
    {
        string[] zips = [@"C:\x\20260827-110000-Jueves-dragonfish_ncentro-16.0004.14964.zip"];

        var elegido = NombreBackup.ElegirZip(zips, "DRAGONFISH_NCENTRO");

        Assert.Equal(zips[0], elegido);
    }

    [Fact]
    public void ElegirZip_NoMatcheaPorPrefijoParcial()
    {
        // "DRAGONFISH_NCENTRO2" no es lo mismo que "DRAGONFISH_NCENTRO" — match exacto, no Contains.
        string[] zips = [@"C:\x\20260827-110000-Jueves-DRAGONFISH_NCENTRO2-16.0004.14964.zip"];

        var elegido = NombreBackup.ElegirZip(zips, "DRAGONFISH_NCENTRO");

        Assert.Null(elegido);
    }
}
