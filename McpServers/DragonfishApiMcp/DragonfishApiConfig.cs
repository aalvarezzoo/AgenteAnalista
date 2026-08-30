namespace DragonfishApiMcp;

/// <summary>
/// Config de la API de Dragonfish (sección "DragonfishApi" de appsettings.secrets.enc).
/// A diferencia de ZlApi (una sola API interna de ZooLogic), Dragonfish es multi-tenant a
/// nivel instalación: cada cliente (base DRAGONFISH_*) tiene su propio host:puerto e
/// IdCliente/Authorization propios. Cada entrada de <see cref="Perfiles"/> es una de esas
/// instalaciones, identificada por un nombre corto (ej. "TEST") — no confundir con el
/// "IdCliente" de la API (identifica al cliente REST dentro de esa instalación) ni con
/// "Cliente" como entidad de negocio de Dragonfish (un cliente/comprador).
/// </summary>
public class DragonfishApiConfig
{
    public Dictionary<string, DragonfishPerfil> Perfiles { get; set; } = [];
}

public class DragonfishPerfil
{
    /// <summary>Ej. "http://localhost:9009/api.Dragonfish" — sin barra final.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Header "IdCliente" — código configurado en "Cliente REST API" de Dragonfish.</summary>
    public string IdCliente { get; set; } = "";

    /// <summary>Header "Authorization" — JWT tal cual, sin prefijo "Bearer".</summary>
    public string Authorization { get; set; } = "";

    /// <summary>Header "BaseDeDatos", opcional según el swagger.</summary>
    public string? BaseDeDatos { get; set; }
}
