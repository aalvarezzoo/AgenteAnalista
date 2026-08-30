namespace SqlDiagnosticoMcp;

/// <summary>
/// Config de SqlDiagnosticoMcp (sección "SqlDiagnostico" de appsettings.secrets.enc). Cada perfil
/// es una instancia de SQL Server distinta, identificada por un nombre corto (mismo patrón de
/// "perfil" que DragonfishApi, pero acá el perfil apunta a una instancia SQL, no a una
/// instalación de Dragonfish vía API REST).
///
/// El usuario/password SIEMPRE deben ser los de un login SQL dedicado con el rol db_datareader
/// únicamente (nunca sa, nunca una cuenta con permisos de escritura, nunca Integrated Security
/// con la cuenta Windows del analista) — la protección real de este MCP es ese permiso a nivel
/// motor de SQL Server, no la validación de texto que hace ConsultaSqlValidator. Ver CLAUDE.md,
/// sección SqlDiagnosticoMcp, para cómo crear ese login.
/// </summary>
public class SqlDiagnosticoConfig
{
    public Dictionary<string, SqlDiagnosticoPerfil> Perfiles { get; set; } = [];
}

public class SqlDiagnosticoPerfil
{
    /// <summary>Instancia de SQL Server, ej. ".\\SQLEXPRESS2022" o "localhost".</summary>
    public string Instancia { get; set; } = "";

    /// <summary>Login SQL dedicado de solo lectura (rol db_datareader). Nunca sa ni una cuenta
    /// con permisos de escritura.</summary>
    public string Usuario { get; set; } = "";

    public string Password { get; set; } = "";
}
