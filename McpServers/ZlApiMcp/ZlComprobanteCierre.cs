using System.Text.Json.Serialization;

namespace ZlApiMcp;

/// <summary>
/// Entidad <c>Mdacompcierretareas</c> de la API de ZL (path <c>/Mdacompcierretareas/</c>):
/// comprobante de cierre de tareas. Solo se genera y se guarda el número
/// (<see cref="NumCierre"/>) para usarlo en <see cref="ZlTarea.NumCierre"/>.
/// Manualmente no hay campos editables al crearlo (<see cref="FechaCierre"/> y
/// <see cref="Cerrador"/> se autocompletan, <see cref="Obs"/> nunca se carga) —
/// falta confirmar por API si admiten override.
/// </summary>
public class ZlComprobanteCierre
{
    [JsonPropertyName("numCIERRE")]   public int    NumCierre   { get; set; }
    [JsonPropertyName("fechaCierre")] public string FechaCierre { get; set; } = "";
    [JsonPropertyName("Cerrador")]    public string Cerrador    { get; set; } = "";
    [JsonPropertyName("Obs")]         public string Obs         { get; set; } = "";

    [JsonPropertyName("InformacionAdicional")]
    public InformacionAdicionalFw InformacionAdicional { get; set; } = new();
}
