namespace ZNubeEcommerceMcp;

/// <summary>
/// Config de ZNubeEcommerceMcp (sección "ZNubeEcommerce" de appsettings.secrets.enc). Cada perfil
/// es un CLIENTE (no un ambiente de prueba como en los demás MCP) — guarda únicamente su
/// `StoreId` de Mercado Libre (el `IDVINC` que Dragonfish guarda en `ZooLogic.ECOM` de la base de
/// ese cliente, y que le pasa a la API de zNube). Es estable en el tiempo, por eso vale la pena
/// persistirlo.
///
/// El token de zNube-token NUNCA se guarda acá ni en ningún lado — rota, lo tiene MDA, y se pide
/// como parámetro en cada llamada a cada tool (decisión explícita, confirmada con el usuario:
/// restaurar una base solo para leer el storeId de `ECOM` no es eficiente para un triage rápido,
/// pero el storeId en sí no cambia, así que ese sí se persiste una vez pedido).
/// </summary>
public class ZNubeEcommerceConfig
{
    public Dictionary<string, ZNubeEcommercePerfil> Perfiles { get; set; } = [];
}

public class ZNubeEcommercePerfil
{
    /// <summary>StoreId de la cuenta de Mercado Libre de este cliente (mismo valor que
    /// ZooLogic.ECOM.IDVINC en la base de ese cliente).</summary>
    public string StoreId { get; set; } = "";
}
