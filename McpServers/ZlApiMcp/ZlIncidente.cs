using System.Text.Json.Serialization;

namespace ZlApiMcp;

/// <summary>
/// Entidad <c>mdaincmda</c> de la API de ZL (path <c>/mdaincmda/</c>): el registro que
/// se genera en MDA con la información del caso a relevar.
/// </summary>
public class ZlIncidente
{
    [JsonPropertyName("Numero")]           public int    Numero           { get; set; }
    [JsonPropertyName("RegPor")]           public string RegPor           { get; set; } = "";
    [JsonPropertyName("NroSerie")]         public string NroSerie         { get; set; } = "";
    [JsonPropertyName("Producto")]         public string Producto         { get; set; } = "";
    [JsonPropertyName("FechaInicio")]      public string FechaInicio      { get; set; } = "";
    [JsonPropertyName("HoraInicio")]       public string HoraInicio       { get; set; } = "";
    [JsonPropertyName("Cliente")]          public string Cliente          { get; set; } = "";
    [JsonPropertyName("Razonsocial")]      public string RazonSocial      { get; set; } = "";
    [JsonPropertyName("msjMDAserie")]      public string MsjMdaSerie      { get; set; } = "";
    [JsonPropertyName("msjMDAcliente")]    public string MsjMdaCliente    { get; set; } = "";
    [JsonPropertyName("TipoIncidente")]    public int    TipoIncidente    { get; set; }
    [JsonPropertyName("SubTipoIncidente")] public int    SubTipoIncidente { get; set; }

    /// <summary>No la usamos hoy pero no hay problema en implementarla.</summary>
    [JsonPropertyName("Prioridad")]        public string Prioridad        { get; set; } = "";

    /// <summary>
    /// Detalle completo del incidente. Al devolver el análisis hay que agregar al final
    /// <c>"MH: ..."</c> (ver <c>Devoluciones.md</c>). Es lectura-modificación-escritura:
    /// leer lo más cerca posible del PUT para minimizar la ventana de carrera con otras
    /// ediciones concurrentes del incidente.
    /// </summary>
    [JsonPropertyName("Consulta")]         public string Consulta         { get; set; } = "";

    [JsonPropertyName("DetalleTransacciones")]
    public List<DetalleTransaccion> DetalleTransacciones { get; set; } = [];

    /// <summary>
    /// Tareas vinculadas al incidente. Al cerrar, se agrega acá la tarea sucesora.
    /// Está pendiente de confirmar si mandar solo <see cref="DetalleTareaIncidente.Numero"/>
    /// alcanza para que ZL autocomplete el resto del registro.
    /// </summary>
    [JsonPropertyName("DetalleTareas")]
    public List<DetalleTareaIncidente> DetalleTareas { get; set; } = [];

    [JsonPropertyName("casoVinculadoQSR")]   public bool casoVinculadoQSR   { get; set; }
    [JsonPropertyName("documentarcasoDyC")]  public bool documentarcasoDyC  { get; set; }

    [JsonPropertyName("InformacionAdicional")]
    public InformacionAdicionalFw InformacionAdicional { get; set; } = new();
}

public class DetalleTransaccion
{
    [JsonPropertyName("Numero")]                 public int    Numero                 { get; set; }
    [JsonPropertyName("TipoContacto")]           public string TipoContacto           { get; set; } = "";
    [JsonPropertyName("RegPor")]                 public string RegPor                 { get; set; } = "";
    [JsonPropertyName("FechaInicio")]            public string FechaInicio            { get; set; } = "";
    [JsonPropertyName("HoraInicio")]             public string HoraInicio             { get; set; } = "";
    [JsonPropertyName("ContactoCliente")]        public string ContactoCliente        { get; set; } = "";
    [JsonPropertyName("Nota")]                   public string Nota                   { get; set; } = "";
    [JsonPropertyName("idTecnoTransaccionTVoz")] public string IdTecnoTransaccionTVoz  { get; set; } = "";
    [JsonPropertyName("NroItem")]                public int    NroItem                { get; set; }
}

public class DetalleTareaIncidente
{
    [JsonPropertyName("Codigo")]            public int    Codigo            { get; set; }
    [JsonPropertyName("Numero")]            public int    Numero            { get; set; }
    [JsonPropertyName("Numerodetalle")]     public string NumeroDetalle     { get; set; } = "";
    [JsonPropertyName("fechaini")]          public string FechaIni          { get; set; } = "";
    [JsonPropertyName("fechafin")]          public string FechaFin          { get; set; } = "";
    [JsonPropertyName("asignadoa")]         public string AsignadoA         { get; set; } = "";
    [JsonPropertyName("numCIERRE")]         public int    NumCierre         { get; set; }
    [JsonPropertyName("numCIERREDETALLE")]  public string NumCierreDetalle  { get; set; } = "";
    [JsonPropertyName("Cerrador")]          public string Cerrador          { get; set; } = "";
    [JsonPropertyName("NroItem")]           public int    NroItem           { get; set; }
}
