using Microsoft.Data.SqlClient;
using ModelContextProtocol;

namespace GestionBackupsMcp;

/// <summary>
/// Acceso a la tabla Emp de DRAGONFISH_ZOOLOGICMASTER — el registro de bases que Dragonfish usa
/// tanto en la pantalla de restauración como en el restore silencioso (ZooBkp.exe) para decidir
/// si una base "existe" o no. Confirmado leyendo el código fuente real de Dragonfish
/// (C:\IADragon2028): ProveedorBD.cs (consulta/columnas) y ent_basededatos.PRG (alta cuando la
/// pantalla de restauración pregunta "la base no existe, ¿desea darla de alta?").
///
/// Solo se conocen con certeza 5 columnas reales (las que aparecen en una consulta SQL real):
/// empcod, epath, RutaBack, crutamdf, replica. El resto de las columnas de Emp no están
/// confirmadas por nombre — nunca se adivinan (mismo criterio que para los campos de la API REST
/// de Dragonfish). Por eso el alta clona una fila existente como plantilla en vez de armar un
/// INSERT con una lista de columnas inventada.
/// </summary>
public static class EmpHelper
{
    public sealed record Columna(string Nombre, bool EsIdentity, bool EsComputada);

    public static string LimpiarCodigo(string nombreBase) =>
        nombreBase.Trim().ToUpperInvariant().Replace("DRAGONFISH_", "");

    public static SqlConnection AbrirConexion(string instanciaSql)
    {
        var connStr = $"Server={instanciaSql};Database=DRAGONFISH_ZOOLOGICMASTER;Integrated Security=true;TrustServerCertificate=true;";
        var conn = new SqlConnection(connStr);
        conn.Open();
        return conn;
    }

    /// <summary>El esquema de Emp no está hardcodeado — Dragonfish mismo lo resuelve en runtime
    /// buscando qué esquema tiene una tabla llamada 'emp' (mismo criterio que obteneresquemaemp.sql
    /// en el código fuente de Dragonfish).</summary>
    public static string ResolverEsquemaEmp(SqlConnection conn)
    {
        const string sql = """
            SELECT s.name AS esquema
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.name = 'Emp'
            """;

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new McpException("No se encontró ninguna tabla 'Emp' en DRAGONFISH_ZOOLOGICMASTER.");

        return reader.GetString(0);
    }

    public static bool ExisteEnEmp(SqlConnection conn, string esquema, string codigo)
    {
        using var cmd = new SqlCommand($"SELECT COUNT(*) FROM [{esquema}].[Emp] WHERE empcod = @codigo", conn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    public static List<Columna> ObtenerColumnasEmp(SqlConnection conn, string esquema)
    {
        const string sql = """
            SELECT c.name, c.is_identity, c.is_computed
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @esquema AND t.name = 'Emp'
            ORDER BY c.column_id
            """;

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@esquema", esquema);
        using var reader = cmd.ExecuteReader();

        var columnas = new List<Columna>();
        while (reader.Read())
        {
            columnas.Add(new Columna(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2)));
        }
        return columnas;
    }

    /// <summary>Lee una fila real de Emp para usar como plantilla — así cualquier columna que no
    /// conocemos por nombre queda con un valor válido (respeta NOT NULL/defaults reales) en vez de
    /// que nosotros tengamos que adivinarla. Se prefiere una fila con replica=0 para no heredar
    /// valores pensados para una base réplica.</summary>
    public static (Dictionary<string, object?>? Fila, string BaseTemplate) LeerFilaTemplate(SqlConnection conn, string esquema, List<Columna> columnas)
    {
        var listaColumnas = string.Join(", ", columnas.Select(c => $"[{c.Nombre}]"));

        using var cmd = new SqlCommand(
            $"SELECT TOP 1 {listaColumnas} FROM [{esquema}].[Emp] WHERE replica = 0 ORDER BY empcod", conn);
        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
            return (null, "");

        var fila = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        string baseTemplate = "";
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var nombreCol = reader.GetName(i);
            var valor = reader.IsDBNull(i) ? null : reader.GetValue(i);
            fila[nombreCol] = valor;
            if (string.Equals(nombreCol, "empcod", StringComparison.OrdinalIgnoreCase))
                baseTemplate = valor?.ToString() ?? "";
        }

        return (fila, baseTemplate);
    }

    /// <summary>Solo pisa las columnas cuyo nombre y valor real confirmamos comparando contra una
    /// base creada de verdad por Dragonfish (RECOLETA, 2026-08-29) — el resto de la fila queda tal
    /// cual vino de la plantilla. CRUTAMDF no es "" como se asumió al principio: en una creación real
    /// queda el placeholder literal "[Ruta predeterminada del servidor SQL]" (RutaCompleta, no
    /// RutaMDF, es lo que se compara contra el default en Organic — para SQL Server RutaCompleta
    /// nunca se usa, así que RutaMDF nunca se limpia). Los campos de auditoría (FALTAFW/HALTAFW/
    /// UALTAFW/BDALTAFW/etc.) se dejan sin tocar a propósito — no hay forma honesta de completar
    /// usuario/base de alta sin estar logueado en Organic.</summary>
    public static void AplicarOverrides(Dictionary<string, object?> fila, string codigo, string ruta)
    {
        SetSiExiste(fila, "empcod", codigo);
        SetSiExiste(fila, "epath", ruta);
        SetSiExiste(fila, "descrip", codigo);
        SetSiExiste(fila, "RutaBack", "");
        SetSiExiste(fila, "crutamdf", "[Ruta predeterminada del servidor SQL]");
        SetSiExiste(fila, "replica", false);
    }

    private static void SetSiExiste(Dictionary<string, object?> fila, string columna, object valor)
    {
        if (fila.ContainsKey(columna))
            fila[columna] = valor;
    }

    public static void InsertarFila(SqlConnection conn, string esquema, List<Columna> columnas, Dictionary<string, object?> fila)
    {
        // Identity/computadas no se pueden (ni hace falta) insertarlas explícitamente.
        var insertables = columnas.Where(c => !c.EsIdentity && !c.EsComputada).ToList();

        var nombresCols = string.Join(", ", insertables.Select(c => $"[{c.Nombre}]"));
        var nombresParams = string.Join(", ", insertables.Select((c, i) => $"@p{i}"));

        using var cmd = new SqlCommand($"INSERT INTO [{esquema}].[Emp] ({nombresCols}) VALUES ({nombresParams})", conn);
        for (int i = 0; i < insertables.Count; i++)
        {
            var valor = fila.TryGetValue(insertables[i].Nombre, out var v) ? v : DBNull.Value;
            cmd.Parameters.AddWithValue($"@p{i}", valor ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }
}
