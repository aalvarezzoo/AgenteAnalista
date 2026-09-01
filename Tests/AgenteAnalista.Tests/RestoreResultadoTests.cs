using GestionBackupsMcp;

namespace AgenteAnalista.Tests;

/// <summary>
/// Regresión de dos bugs reales de detección de éxito en restaurar_backup:
/// 1) una restauración con error real de ADN Implant quedó marcada como éxito porque un paso
///    intermedio del log ("herramientas exportadas con éxito") contenía la frase suelta.
/// 2) una base no registrada quedó marcada "success" siendo en realidad un no-op, porque ZooBkp
///    puede reportar éxito general sin haber llegado a invocar la restauración real.
/// </summary>
public class RestoreResultadoTests
{
    [Fact]
    public void ExitoRealConRestauracion_ExitoYRestauracionReal()
    {
        var log = "Preparando...\nInvocando al componente SQLDmoWrapper...\nProceso finalizado con éxito.\n";

        var r = RestoreResultado.Evaluar(0, log);

        Assert.True(r.Exito);
        Assert.True(r.HuboRestauracionReal);
    }

    [Fact]
    public void FraseDeExitoSueltaEnPasoIntermedio_PeroTerminaConError_NoEsExito()
    {
        var log = "Herramientas exportadas con éxito.\nProceso finalizado con errores.\n";

        var r = RestoreResultado.Evaluar(0, log);

        Assert.False(r.Exito);
    }

    [Fact]
    public void RetornoErroneo_NoEsExitoAunqueDigaConExitoAntes()
    {
        var log = "Herramientas exportadas con éxito.\nretorno erróneo del proceso.\n";

        var r = RestoreResultado.Evaluar(0, log);

        Assert.False(r.Exito);
    }

    [Fact]
    public void ExitoGeneral_SinInvocarSQLDmoWrapper_NoEsRestauracionReal_BaseNoRegistrada()
    {
        var log = "Proceso finalizado con éxito.\n";

        var r = RestoreResultado.Evaluar(0, log);

        Assert.True(r.Exito);
        Assert.False(r.HuboRestauracionReal);
    }

    [Fact]
    public void ExitCodeDistintoDeCero_NoEsExitoAunqueElLogDigaExito()
    {
        var log = "Proceso finalizado con éxito.\n";

        var r = RestoreResultado.Evaluar(1, log);

        Assert.False(r.Exito);
    }
}
