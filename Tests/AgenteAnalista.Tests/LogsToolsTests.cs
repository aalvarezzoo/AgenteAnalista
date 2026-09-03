using LogsMcp;

namespace AgenteAnalista.Tests;

public class LogsToolsTests
{
    [Theory]
    [InlineData("operaciones.log", "operaciones")]
    [InlineData("OPERACIONES.LOG", "operaciones")]
    [InlineData("operaciones.log.1", "operaciones")]
    [InlineData("operaciones.log.10", "operaciones")]
    [InlineData("OperacionesDelBuscador.log", "buscador")]
    [InlineData("OperacionesDelBuscador.log.3", "buscador")]
    [InlineData("visor.evtx", "eventosWindows")]
    [InlineData("VISOR.EVTX", "eventosWindows")]
    [InlineData("ZooBkp.log", "desconocido")]
    [InlineData("log.err", "desconocido")]
    public void Clasifica_el_archivo_segun_su_nombre(string nombre, string tipoEsperado)
    {
        Assert.Equal(tipoEsperado, LogsTools.ClasificarArchivo(nombre));
    }

    [Theory]
    [InlineData("operaciones.log", "operaciones.log", true)]
    [InlineData("operaciones.log.1", "operaciones.log", true)]
    [InlineData("operaciones.log.10", "operaciones.log", true)]
    [InlineData("OPERACIONES.LOG.2", "operaciones.log", true)]
    [InlineData("operaciones.log.viejo", "operaciones.log", false)]
    [InlineData("otracosa.log", "operaciones.log", false)]
    [InlineData("operaciones.log.err", "operaciones.log", false)]
    public void Reconoce_el_archivo_base_y_sus_rotaciones(string nombreArchivo, string nombreBase, bool esperado)
    {
        Assert.Equal(esperado, ArchivosLog.EsArchivoDeLog(nombreArchivo, nombreBase));
    }
}
