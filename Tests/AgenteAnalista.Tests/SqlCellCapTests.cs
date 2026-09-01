using SqlDiagnosticoMcp;

namespace AgenteAnalista.Tests;

/// <summary>
/// Protege el tope de ~300 celdas totales (filas × columnas) de consultar_sql/buscar_valor: en
/// tablas anchas el límite de filas se achica para no gastar de más; en tablas angostas no se
/// toca el límite pedido.
/// </summary>
public class SqlCellCapTests
{
    [Theory]
    [InlineData(50, 3, 50)]     // tabla angosta (3 columnas): no hace falta recortar.
    [InlineData(50, 80, 3)]     // tabla ancha (80 columnas): 300/80 = 3 filas como mucho.
    [InlineData(1000, 1, 300)]  // 1 columna: el tope de celdas (300) igual gana si se pide de más.
    public void LimiteEfectivoPorCeldas_AchicaSoloCuandoHaceFalta(int limiteFilasPedido, int columnas, int esperado)
    {
        var resultado = SqlDiagnosticoTools.LimiteEfectivoPorCeldas(limiteFilasPedido, columnas);

        Assert.Equal(esperado, resultado);
    }

    [Fact]
    public void LimiteEfectivoPorCeldas_SinColumnas_DevuelveElLimitePedidoTalCual()
    {
        Assert.Equal(50, SqlDiagnosticoTools.LimiteEfectivoPorCeldas(50, 0));
    }
}
