using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace SqlDiagnosticoMcp;

/// <summary>
/// Validación de defensa en profundidad para consultar_sql. La protección real es que el login
/// configurado en el perfil solo tiene permiso db_datareader en SQL Server (no puede escribir
/// nada aunque este validador tuviera un agujero) — esto agrega una capa extra en el propio MCP
/// y, sobre todo, da un error claro y rápido antes de siquiera llegar a SQL Server.
/// </summary>
public static partial class ConsultaSqlValidator
{
    private static readonly string[] PalabrasBloqueadas =
    [
        "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "ALTER", "TRUNCATE", "CREATE",
        "GRANT", "REVOKE", "DENY", "BACKUP", "RESTORE", "SHUTDOWN", "EXEC", "EXECUTE",
        "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "BULK", "sp_", "xp_",
    ];

    public static void ValidarOTirar(string sql)
    {
        var limpio = sql.Trim();
        if (limpio.EndsWith(';')) limpio = limpio[..^1].TrimEnd();

        if (limpio.Contains(';'))
            throw new McpException(
                "Solo se permite un único statement (no se aceptan varios separados por ';').");

        if (!InicioPermitido().IsMatch(limpio))
            throw new McpException(
                "La consulta debe empezar con SELECT o WITH — esta tool es exclusivamente de lectura.");

        foreach (var palabra in PalabrasBloqueadas)
        {
            if (ContienePalabra(limpio, palabra))
                throw new McpException(
                    $"La consulta contiene '{palabra.TrimEnd('_')}', no permitido en consultar_sql (herramienta de solo lectura).");
        }
    }

    private static bool ContienePalabra(string sql, string palabra)
    {
        // sp_/xp_ son prefijos (sp_who, xp_cmdshell) — sin \b de cierre; el resto son palabras
        // completas (para no bloquear, por ejemplo, una columna llamada "CREATED").
        var patron = palabra.EndsWith('_')
            ? $@"\b{Regex.Escape(palabra)}"
            : $@"\b{Regex.Escape(palabra)}\b";
        return Regex.IsMatch(sql, patron, RegexOptions.IgnoreCase);
    }

    [GeneratedRegex(@"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InicioPermitido();
}
