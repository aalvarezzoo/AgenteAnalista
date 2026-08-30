using System.Text.Json.Serialization;

namespace ZlApiMcp;

/// <summary>
/// Entidad <c>Tareas</c> de la API de ZL (path <c>/Tareas/</c>): acción asignada a un
/// técnico o área. Las tareas de MasterHelp tienen <see cref="Owner"/> con el usuario
/// individual del analista (JINIGUEZ/AALVAREZ/DPIERCAMILLI), no un valor de equipo.
/// </summary>
public class ZlTarea
{
    /// <summary>Título de la tarea. Al cerrar y generar la tarea sucesora se reutiliza el mismo título.</summary>
    [JsonPropertyName("Titulo")]          public string Titulo          { get; set; } = "";

    /// <summary>Tipo de tarea. A MasterHelp siempre le llega y devuelve "0139".</summary>
    [JsonPropertyName("tipotarea")]       public string TipoTarea       { get; set; } = "";

    [JsonPropertyName("Numero")]          public int    Numero          { get; set; }

    /// <summary>Quién asignó la tarea. A quién se le devuelve al cerrar el incidente.</summary>
    [JsonPropertyName("registradopor")]   public string RegistradoPor   { get; set; } = "";

    [JsonPropertyName("fechaini")]        public string FechaIni        { get; set; } = "";
    [JsonPropertyName("fechafin")]        public string FechaFin        { get; set; } = "";
    [JsonPropertyName("prospecto")]       public string Prospecto       { get; set; } = "";
    [JsonPropertyName("RazonSocial")]     public string RazonSocial     { get; set; } = "";
    [JsonPropertyName("cliente")]         public string Cliente         { get; set; } = "";
    [JsonPropertyName("PRODUCTOZOOLOGIC")] public string ProductoZooLogic { get; set; } = "";

    /// <summary>A quién está asignada la tarea (usuario individual; MASTERHELP = uno de nuestros analistas).</summary>
    [JsonPropertyName("Owner")]           public string Owner           { get; set; } = "";

    [JsonPropertyName("bonifica")]        public string Bonifica        { get; set; } = "";

    [JsonPropertyName("DetalleHojasServ")]
    public List<DetalleHojaServ> DetalleHojasServ { get; set; } = [];

    /// <summary>Número del comprobante de cierre (<see cref="ZlComprobanteCierre.NumCierre"/>) asignado a esta tarea al cerrarla.</summary>
    [JsonPropertyName("numCIERRE")]       public int    NumCierre       { get; set; }

    /// <summary>Usuario que cerró la tarea. Se autocompleta con el usuario de ZL que ejecuta el cierre.</summary>
    [JsonPropertyName("Cerrador")]        public string Cerrador        { get; set; } = "";

    [JsonPropertyName("DetalleContactos")]
    public List<DetalleContacto> DetalleContactos { get; set; } = [];

    [JsonPropertyName("DetalleUsuariosDePantera")]
    public List<DetalleUsuarioPantera> DetalleUsuariosDePantera { get; set; } = [];

    /// <summary>
    /// Entidades asociadas a la tarea (ej: el incidente del que proviene). Filtrar por
    /// <c>Entidad == "incidente"</c> para resolver el vínculo tarea→incidente.
    /// No se completa en la tarea sucesora: el vínculo se establece editando el incidente.
    /// </summary>
    [JsonPropertyName("DetalleTareasAsociadas")]
    public List<DetalleTareaAsociada> DetalleTareasAsociadas { get; set; } = [];

    [JsonPropertyName("DetalleInteraccionesTareas")]
    public List<DetalleInteraccionTarea> DetalleInteraccionesTareas { get; set; } = [];

    [JsonPropertyName("notas")]           public string Notas           { get; set; } = "";

    [JsonPropertyName("InformacionAdicional")]
    public InformacionAdicionalFw InformacionAdicional { get; set; } = new();
}

public class DetalleHojaServ
{
    [JsonPropertyName("Codigo")]             public int    Codigo             { get; set; }
    [JsonPropertyName("NumeroHdeS")]         public int    NumeroHdeS         { get; set; }
    [JsonPropertyName("fecha")]              public string Fecha              { get; set; } = "";
    [JsonPropertyName("registradopor")]      public string RegistradoPor      { get; set; } = "";
    [JsonPropertyName("fechaProgramacion")]  public string FechaProgramacion  { get; set; } = "";
    [JsonPropertyName("asignadoa")]          public string AsignadoA          { get; set; } = "";
    [JsonPropertyName("NroItem")]            public int    NroItem            { get; set; }
}

public class DetalleContacto
{
    [JsonPropertyName("codigo")]    public int    Codigo    { get; set; }
    [JsonPropertyName("Contact")]   public string Contact   { get; set; } = "";
    [JsonPropertyName("Nombre")]    public string Nombre    { get; set; } = "";
    [JsonPropertyName("Apellido")]  public string Apellido  { get; set; } = "";
    [JsonPropertyName("NroItem")]   public int    NroItem   { get; set; }
}

public class DetalleUsuarioPantera
{
    [JsonPropertyName("codigo")]          public int    Codigo          { get; set; }
    [JsonPropertyName("UsuarioPantera")]  public int    UsuarioPantera  { get; set; }
    [JsonPropertyName("Email")]           public string Email           { get; set; } = "";
    [JsonPropertyName("NombreUsuario")]   public string NombreUsuario   { get; set; } = "";
    [JsonPropertyName("Nombre")]          public string Nombre          { get; set; } = "";
    [JsonPropertyName("Apellido")]        public string Apellido        { get; set; } = "";
    [JsonPropertyName("Telefono")]        public string Telefono        { get; set; } = "";
    [JsonPropertyName("Creado")]          public string Creado          { get; set; } = "";
    [JsonPropertyName("Activo")]          public bool   Activo          { get; set; }
    [JsonPropertyName("Administrador")]   public bool   Administrador   { get; set; }
    [JsonPropertyName("NroItem")]         public int    NroItem         { get; set; }
}

public class DetalleTareaAsociada
{
    [JsonPropertyName("codigo")]  public int    Codigo  { get; set; }
    [JsonPropertyName("Numero")]  public int    Numero  { get; set; }

    /// <summary>Tipo de entidad asociada. Nos interesa filtrar por <c>"incidente"</c>.</summary>
    [JsonPropertyName("Entidad")] public string Entidad { get; set; } = "";
    [JsonPropertyName("Fecha")]   public string Fecha   { get; set; } = "";
    [JsonPropertyName("NroItem")] public int    NroItem { get; set; }
}

public class DetalleInteraccionTarea
{
    [JsonPropertyName("Codigo")]        public int    Codigo        { get; set; }
    [JsonPropertyName("Numero")]        public int    Numero        { get; set; }
    [JsonPropertyName("Fecha")]         public string Fecha         { get; set; } = "";
    [JsonPropertyName("Tipo")]          public string Tipo          { get; set; } = "";
    [JsonPropertyName("Contacto")]      public string Contacto      { get; set; } = "";
    [JsonPropertyName("IdTecnovoz")]    public string IdTecnovoz     { get; set; } = "";
    [JsonPropertyName("Ualtafw")]       public string Ualtafw        { get; set; } = "";
    [JsonPropertyName("Notas")]         public string Notas         { get; set; } = "";
    [JsonPropertyName("NroItem")]       public int    NroItem       { get; set; }
}
