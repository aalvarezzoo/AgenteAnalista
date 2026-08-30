namespace ZlApiMcp;

/// <summary>
/// Configuración de la API de ZL/BBRIF (sección <c>"ZlApi"</c> de appsettings.json).
/// Circuito armado pero todavía no integrado al flujo real del panel.
/// </summary>
public class ZlApiConfig
{
    /// <summary>URL base de la API. Vacío hasta que se defina el entorno real.</summary>
    public string BaseUrl        { get; set; } = "";

    /// <summary>Header <c>IdCliente</c> compartido por todos los endpoints.</summary>
    public string IdCliente      { get; set; } = "";

    /// <summary>Header <c>Authorization</c> compartido por todos los endpoints.</summary>
    public string Authorization  { get; set; } = "";

    /// <summary>Header <c>BaseDeDatos</c>. Es un valor estático (no varía por incidente/cliente).</summary>
    public string BaseDeDatos    { get; set; } = "";

    /// <summary>Intervalo entre reintentos de <c>ZlTareaLinkResolver</c> para resolver tarea→incidente.</summary>
    public int    LinkPollIntervalSeconds { get; set; } = 10;

    /// <summary>Tiempo máximo de polling de <c>ZlTareaLinkResolver</c> antes de abandonar.</summary>
    public int    LinkPollTimeoutMinutes  { get; set; } = 5;

    /// <summary>
    /// Feriados nacionales (formato <c>yyyy-MM-dd</c>) usados por <c>DiaHabilCalculator</c>.
    /// Mantenimiento manual: actualizar todos los años.
    /// </summary>
    public List<string> Feriados { get; set; } = [];
}
