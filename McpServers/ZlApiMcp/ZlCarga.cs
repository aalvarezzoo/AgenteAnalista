namespace ZlApiMcp;

/// <summary>
/// Modelos en memoria del panel lateral de carga de Bug/Requerimiento (ver
/// <c>Components/Shared/ZlCargaPanel.razor</c>). Viven en <c>DetTab</c> mientras dura la
/// tab de detalle del incidente — no se persisten en SQL. No llevan <c>[JsonPropertyName]</c>
/// todavía porque no hay schema real confirmado de estas entidades en la API de ZL
/// (a diferencia de <see cref="ZlTarea"/>/<see cref="ZlIncidente"/>/<see cref="ZlComprobanteCierre"/>).
/// </summary>
public enum BugSeveridad { Alta, AltaRecurrente, Media, Baja }

public enum BugOcurrencia { Siempre, Esporadico }

/// <summary>Producto ZooLogic + versión donde se declara el bug. Catálogo real a relevar.</summary>
public class ProductoAfectado
{
    public string Producto { get; set; } = "";
    public string Version  { get; set; } = "";
}

/// <summary>
/// Datos del formulario de carga de Bug. Campos "de uso raro" (Issues, Sistema Operativo,
/// Edición, Plataforma — ver <c>CargaDeBugs.md</c>) quedan ocultos por defecto en la UI.
/// </summary>
public class BugCarga
{
    public string Titulo      { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public BugSeveridad  Severidad  { get; set; } = BugSeveridad.Media;
    public BugOcurrencia Ocurrencia { get; set; } = BugOcurrencia.Siempre;

    /// <summary>Catálogo real a relevar contra ZL — campo libre por ahora.</summary>
    public string Clasificacion { get; set; } = "";

    /// <summary>Una sola línea por defecto; la UI ofrece agregar más con "+".</summary>
    public List<ProductoAfectado> Productos { get; set; } = [new()];

    public string Issues           { get; set; } = "";
    public string SistemaOperativo { get; set; } = "";
    public string Edicion          { get; set; } = "";
    public string Plataforma       { get; set; } = "";

    /// <summary>Número asignado por ZL al guardar. Null mientras no se guardó.</summary>
    public int? NumeroAsignado { get; set; }

    /// <summary>
    /// True cuando el bug se vinculó al incidente vía "Asignar bug" (bug ya existente en ZL,
    /// se ingresa el número a mano — ver ventana "Asignación de bugs a incidentes" de ZL) en
    /// vez de crearse con el formulario completo de carga. En este caso el resto de los
    /// campos queda vacío: no tenemos forma de traer los datos del bug existente todavía.
    /// </summary>
    public bool EsAsignacionExistente { get; set; }

    public bool Guardado => NumeroAsignado is not null;
}

/// <summary>
/// Datos del formulario de carga de Requerimiento. Cliente/Razón Social/Código de cliente no
/// se guardan acá: la UI los muestra leyendo directamente el incidente abierto (no se cargan
/// ni se buscan). Contacto y Producto ZL sí son campos propios del form — son obligatorios en
/// la carga real de ZL y todavía no hay forma de buscarlos/autocompletarlos, así que quedan
/// como texto libre.
/// </summary>
public class RequerimientoCarga
{
    public string Titulo                  { get; set; } = "";
    public string Necesidad                { get; set; } = "";
    public string ImplementacionSugerida   { get; set; } = "";
    public string Contacto                 { get; set; } = "";
    public string ProductoZl               { get; set; } = "";

    public int? NumeroAsignado { get; set; }

    public bool Guardado => NumeroAsignado is not null;
}
