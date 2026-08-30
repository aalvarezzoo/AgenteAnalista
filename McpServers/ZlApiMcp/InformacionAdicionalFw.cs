using System.Text.Json.Serialization;

namespace ZlApiMcp;

/// <summary>
/// Bloque de auditoría común a las tres entidades de la API de ZL (Tareas, Incidentes
/// y Cierre de tareas). Mismo shape repetido igual en los tres JSON de ejemplo.
/// </summary>
public class InformacionAdicionalFw
{
    [JsonPropertyName("FechaTransferencia")]        public string FechaTransferencia        { get; set; } = "";
    [JsonPropertyName("EstadoTransferencia")]       public string EstadoTransferencia       { get; set; } = "";
    [JsonPropertyName("FechaAltaFW")]               public string FechaAltaFW               { get; set; } = "";
    [JsonPropertyName("HoraAltaFW")]                public string HoraAltaFW                { get; set; } = "";
    [JsonPropertyName("FechaModificacionFW")]       public string FechaModificacionFW       { get; set; } = "";
    [JsonPropertyName("HoraModificacionFW")]        public string HoraModificacionFW        { get; set; } = "";
    [JsonPropertyName("FechaImpo")]                 public string FechaImpo                 { get; set; } = "";
    [JsonPropertyName("HoraImpo")]                  public string HoraImpo                  { get; set; } = "";
    [JsonPropertyName("FechaExpo")]                 public string FechaExpo                 { get; set; } = "";
    [JsonPropertyName("HoraExpo")]                  public string HoraExpo                  { get; set; } = "";
    [JsonPropertyName("UsuarioAltaFW")]             public string UsuarioAltaFW             { get; set; } = "";
    [JsonPropertyName("UsuarioModificacionFW")]     public string UsuarioModificacionFW     { get; set; } = "";
    [JsonPropertyName("SerieAltaFW")]               public string SerieAltaFW               { get; set; } = "";
    [JsonPropertyName("SerieModificacionFW")]       public string SerieModificacionFW       { get; set; } = "";
    [JsonPropertyName("BaseDeDatosAltaFW")]         public string BaseDeDatosAltaFW         { get; set; } = "";
    [JsonPropertyName("BaseDeDatosModificacionFW")] public string BaseDeDatosModificacionFW { get; set; } = "";
    [JsonPropertyName("VersionAltaFW")]             public string VersionAltaFW             { get; set; } = "";
    [JsonPropertyName("VersionModificacionFW")]     public string VersionModificacionFW     { get; set; } = "";
    [JsonPropertyName("ZADSFW")]                    public string ZADSFW                    { get; set; } = "";
}
