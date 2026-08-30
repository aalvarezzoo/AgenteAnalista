using Microsoft.Data.SqlClient;
using ModelContextProtocol;

namespace SqlDiagnosticoMcp;

/// <summary>
/// Apertura de conexión y resolución de esquema/objeto compartidas por todas las tools. La
/// conexión siempre usa SQL Authentication con las credenciales del perfil (nunca Integrated
/// Security) — el login configurado debe tener solo el rol db_datareader en las bases que se
/// vayan a consultar. Nada de lo que hace este helper (ni el resto del MCP) depende de la
/// validación de texto para estar a salvo de escrituras: esa es responsabilidad del permiso real
/// otorgado en SQL Server.
/// </summary>
public static class ConexionHelper
{
    public static SqlConnection AbrirConexion(SqlDiagnosticoPerfil perfil, string baseDeDatos)
    {
        var connStr = $"Server={perfil.Instancia};Database={baseDeDatos};User Id={perfil.Usuario};Password={perfil.Password};TrustServerCertificate=true;Connect Timeout=10;";
        var conn = new SqlConnection(connStr);
        conn.Open();
        return conn;
    }

    /// <summary>Escapa un identificador para uso seguro entre corchetes ([nombre]) — duplica los
    /// ']' internos (escape estándar de T-SQL) para que no se pueda "romper" el bracket con un
    /// nombre de tabla/columna armado a propósito.</summary>
    public static string CorchetesSeguro(string nombre) => "[" + nombre.Replace("]", "]]") + "]";

    /// <summary>Si no se especifica esquema, lo resuelve buscando qué esquema(s) tienen una
    /// tabla con ese nombre — mismo criterio que EmpHelper.ResolverEsquemaEmp en
    /// GestionBackupsMcp. Tira si no encuentra ninguna coincidencia o si el nombre es ambiguo
    /// entre varios esquemas (nunca asume cuál "es la correcta").</summary>
    public static string ResolverEsquemaDeTabla(SqlConnection conn, string tabla, string? esquema)
    {
        if (!string.IsNullOrWhiteSpace(esquema))
            return esquema;

        const string sql = """
            SELECT s.name
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.name = @tabla
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@tabla", tabla);
        using var reader = cmd.ExecuteReader();

        var esquemas = new List<string>();
        while (reader.Read()) esquemas.Add(reader.GetString(0));

        return esquemas.Count switch
        {
            0 => throw new McpException(
                $"No se encontró ninguna tabla '{tabla}' en esta base. Usá buscar_en_esquema para ubicarla."),
            1 => esquemas[0],
            _ => throw new McpException(
                $"La tabla '{tabla}' existe en varios esquemas ({string.Join(", ", esquemas)}) — especificá cuál con el parámetro 'esquema'."),
        };
    }

    /// <summary>Igual que <see cref="ResolverEsquemaDeTabla"/> pero para cualquier tipo de
    /// objeto (tabla, vista, procedimiento, función, trigger) — usado por
    /// obtener_definicion_objeto, que no está limitado a tablas.</summary>
    public static string ResolverEsquemaDeObjeto(SqlConnection conn, string objeto, string? esquema)
    {
        if (!string.IsNullOrWhiteSpace(esquema))
            return esquema;

        const string sql = """
            SELECT s.name
            FROM sys.objects o
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE o.name = @objeto AND o.type IN ('U','V','P','FN','IF','TF','TR')
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@objeto", objeto);
        using var reader = cmd.ExecuteReader();

        var esquemas = new List<string>();
        while (reader.Read()) esquemas.Add(reader.GetString(0));

        return esquemas.Count switch
        {
            0 => throw new McpException($"No se encontró ningún objeto '{objeto}' en esta base."),
            1 => esquemas[0],
            _ => throw new McpException(
                $"El objeto '{objeto}' existe en varios esquemas ({string.Join(", ", esquemas)}) — especificá cuál con el parámetro 'esquema'."),
        };
    }
}
