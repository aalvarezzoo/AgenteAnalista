using System.ComponentModel;
using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace SqlDiagnosticoMcp;

[McpServerToolType]
public sealed class SqlDiagnosticoTools(IOptions<SqlDiagnosticoConfig> cfg)
{
    /// <summary>Todas las tools pasan por acá. El SDK de MCP sanitiza cualquier excepción que no
    /// sea <see cref="McpException"/> a un mensaje genérico ("An error occurred invoking...") antes
    /// de devolvérsela al modelo — así que un error de validación (perfil inexistente, tabla
    /// ambigua) o el mensaje real de SQL Server (columna inválida, sintaxis, etc.) se perdería si se
    /// dejara propagar tal cual. Envolver reconvierte cualquier excepción en McpException para que el
    /// texto descriptivo sí llegue a Claude — confirmado con una prueba real end-to-end contra el
    /// perfil TEST, no una suposición sobre el SDK.</summary>
    private static string Envolver(Func<string> accion)
    {
        try
        {
            return accion();
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "listar_perfiles")]
    [Description("Lista los perfiles (instancias SQL) configurados. No expone usuario ni password.")]
    public string ListarPerfiles() =>
        JsonSerializer.Serialize(cfg.Value.Perfiles.Keys);

    [McpServerTool(Name = "listar_bases")]
    [Description("Lista las bases de datos visibles para el login configurado en el perfil (solo las que tiene permiso de leer), con su estado (ONLINE/OFFLINE/etc).")]
    public string ListarBases(
        [Description("Nombre del perfil (ver listar_perfiles)")] string perfil) => Envolver(() =>
    {
        var p = ResolverPerfil(perfil);
        using var conn = ConexionHelper.AbrirConexion(p, "master");

        const string sql = """
            SELECT name, state_desc
            FROM sys.databases
            WHERE HAS_DBACCESS(name) = 1
            ORDER BY name
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        using var reader = cmd.ExecuteReader();

        var bases = new List<object>();
        while (reader.Read())
            bases.Add(new { nombre = reader.GetString(0), estado = reader.GetString(1) });

        return JsonSerializer.Serialize(bases);
    });

    [McpServerTool(Name = "buscar_en_esquema")]
    [Description("Busca tablas, vistas, procedimientos y columnas cuyo nombre contenga la palabra clave dada. Punto de partida para ubicar dónde vive un dato sin tener que indicarle la tabla de antemano (ej. buscar_en_esquema con \"precio\" para encontrar todo lo relacionado a precios).")]
    public string BuscarEnEsquema(
        [Description("Nombre del perfil (ver listar_perfiles)")] string perfil,
        [Description("Base de datos, ej. DRAGONFISH_DEMO")] string baseDeDatos,
        [Description("Palabra clave a buscar en nombres de tablas/vistas/procedimientos/columnas, ej. \"precio\"")] string palabraClave) => Envolver(() =>
    {
        const int limite = 100;
        var p = ResolverPerfil(perfil);
        using var conn = ConexionHelper.AbrirConexion(p, baseDeDatos);

        const string sqlObjetos = """
            SELECT TOP (@limite) s.name AS esquema, o.name AS objeto,
                   CASE o.type WHEN 'U' THEN 'tabla' WHEN 'V' THEN 'vista'
                                WHEN 'P' THEN 'procedimiento' ELSE o.type_desc END AS tipo
            FROM sys.objects o
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE o.type IN ('U','V','P') AND o.name LIKE '%' + @palabra + '%'
            ORDER BY tipo, esquema, objeto
            """;
        using var cmdObjetos = new SqlCommand(sqlObjetos, conn) { CommandTimeout = 15 };
        cmdObjetos.Parameters.AddWithValue("@limite", limite);
        cmdObjetos.Parameters.AddWithValue("@palabra", palabraClave);

        var objetos = new List<object>();
        using (var reader = cmdObjetos.ExecuteReader())
            while (reader.Read())
                objetos.Add(new { esquema = reader.GetString(0), objeto = reader.GetString(1), tipo = reader.GetString(2) });

        const string sqlColumnas = """
            SELECT TOP (@limite) s.name AS esquema, t.name AS tabla, c.name AS columna
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE c.name LIKE '%' + @palabra + '%'
            ORDER BY esquema, tabla, columna
            """;
        using var cmdColumnas = new SqlCommand(sqlColumnas, conn) { CommandTimeout = 15 };
        cmdColumnas.Parameters.AddWithValue("@limite", limite);
        cmdColumnas.Parameters.AddWithValue("@palabra", palabraClave);

        var columnas = new List<object>();
        using (var reader = cmdColumnas.ExecuteReader())
            while (reader.Read())
                columnas.Add(new { esquema = reader.GetString(0), tabla = reader.GetString(1), columna = reader.GetString(2) });

        return JsonSerializer.Serialize(new
        {
            objetos,
            columnas,
            nota = objetos.Count == limite || columnas.Count == limite
                ? $"Se truncó a {limite} resultados por categoría — afiná la palabra clave si hace falta más precisión."
                : null,
        });
    });

    [McpServerTool(Name = "describir_tabla")]
    [Description("Devuelve columnas (tipo, longitud, nullable, identity), clave primaria, claves foráneas (entrantes y salientes) e índices de una tabla. Si no se indica esquema, lo resuelve solo buscando en qué esquema existe esa tabla (falla con un mensaje claro si es ambigua).")]
    public string DescribirTabla(
        [Description("Nombre del perfil (ver listar_perfiles)")] string perfil,
        [Description("Base de datos, ej. DRAGONFISH_DEMO")] string baseDeDatos,
        [Description("Nombre de la tabla, ej. ART")] string tabla,
        [Description("Esquema, opcional — si no se indica se resuelve solo (ej. ZooLogic)")] string? esquema = null) => Envolver(() =>
    {
        var p = ResolverPerfil(perfil);
        using var conn = ConexionHelper.AbrirConexion(p, baseDeDatos);
        var esq = ConexionHelper.ResolverEsquemaDeTabla(conn, tabla, esquema);

        return JsonSerializer.Serialize(new
        {
            esquema = esq,
            tabla,
            columnas = ObtenerColumnas(conn, esq, tabla),
            clavePrimaria = ObtenerClavePrimaria(conn, esq, tabla),
            clavesForaneasSalientes = ObtenerForeignKeysSalientes(conn, esq, tabla),
            clavesForaneasEntrantes = ObtenerForeignKeysEntrantes(conn, esq, tabla),
            indices = ObtenerIndices(conn, esq, tabla),
        });
    });

    [McpServerTool(Name = "obtener_definicion_objeto")]
    [Description("Devuelve el código SQL real de una vista, procedimiento almacenado, función o trigger (sys.sql_modules vía OBJECT_DEFINITION) — clave cuando un reporte SSRS llama a un SP donde vive el cálculo real. Si no se indica esquema, lo resuelve solo (falla con un mensaje claro si es ambiguo). Se trunca a limiteCaracteres para no traer de una sola vez la definición completa de un objeto enorme — si queda truncado, volver a llamar con desde=<lo que indique la nota> para seguir leyendo desde ahí, no subir limiteCaracteres sin límite.")]
    public string ObtenerDefinicionObjeto(
        [Description("Nombre del perfil (ver listar_perfiles)")] string perfil,
        [Description("Base de datos, ej. DRAGONFISH_DEMO")] string baseDeDatos,
        [Description("Nombre del objeto (vista/SP/función/trigger), ej. SP_LISTADO_PRECIOS")] string nombreObjeto,
        [Description("Esquema, opcional — si no se indica se resuelve solo")] string? esquema = null,
        [Description("Desde qué caracter empezar a devolver (default 0) — usarlo para pedir el siguiente pedazo cuando la llamada anterior quedó truncada")] int desde = 0,
        [Description("Máximo de caracteres a devolver desde 'desde' (default 8000, tope 50000)")] int limiteCaracteres = 8000) => Envolver(() =>
    {
        desde = Math.Max(0, desde);
        limiteCaracteres = Math.Clamp(limiteCaracteres, 500, 50000);

        var p = ResolverPerfil(perfil);
        using var conn = ConexionHelper.AbrirConexion(p, baseDeDatos);
        var esq = ConexionHelper.ResolverEsquemaDeObjeto(conn, nombreObjeto, esquema);
        var nombreCompleto = $"{ConexionHelper.CorchetesSeguro(esq)}.{ConexionHelper.CorchetesSeguro(nombreObjeto)}";

        // Antes de pedir la definición, chequear tipo real + si está cifrado — así, si
        // OBJECT_DEFINITION da NULL, se puede decir la causa real en vez de listar sospechosos
        // ("puede ser una tabla, o estar cifrado") sin confirmar ninguno. Confirmado en la
        // práctica: la causa real más común no era ninguna de esas dos, era que al login le
        // faltaba el permiso VIEW DEFINITION (que db_datareader no incluye).
        string tipo;
        bool cifrado;
        using (var cmdInfo = new SqlCommand(
            "SELECT o.type, OBJECTPROPERTY(o.object_id, 'IsEncrypted') FROM sys.objects o WHERE o.object_id = OBJECT_ID(@nombreCompleto)",
            conn) { CommandTimeout = 10 })
        {
            cmdInfo.Parameters.AddWithValue("@nombreCompleto", nombreCompleto);
            using var readerInfo = cmdInfo.ExecuteReader();
            if (!readerInfo.Read())
                throw new McpException($"No se pudo resolver el objeto '{esq}.{nombreObjeto}' para chequear su tipo.");
            tipo = readerInfo.GetString(0).Trim();
            cifrado = !readerInfo.IsDBNull(1) && readerInfo.GetInt32(1) == 1;
        }

        if (tipo == "U")
            return $"'{esq}.{nombreObjeto}' es una tabla — las tablas no tienen definición SQL, no es un error.";

        using var cmd = new SqlCommand("SELECT OBJECT_DEFINITION(OBJECT_ID(@nombreCompleto))", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@nombreCompleto", nombreCompleto);
        var definicion = cmd.ExecuteScalar() as string;

        if (definicion is null)
        {
            return cifrado
                ? $"'{esq}.{nombreObjeto}' está creado con WITH ENCRYPTION — nadie puede ver su definición, ni siquiera un administrador."
                : $"'{esq}.{nombreObjeto}' no está cifrado pero no devolvió definición — el login del perfil probablemente no tiene el permiso VIEW DEFINITION en esta base (ver skill configurar-perfil-sql-diagnostico).";
        }

        if (desde >= definicion.Length)
            return $"-- '{esq}.{nombreObjeto}' tiene {definicion.Length} caracteres en total — 'desde' ({desde}) ya pasó el final, no hay más para mostrar.";

        var restante = definicion.Length - desde;
        var aDevolver = Math.Min(limiteCaracteres, restante);
        var recorte = definicion.Substring(desde, aDevolver);
        var prefijo = desde == 0 ? "" : $"-- [continúa desde el caracter {desde} de {definicion.Length}]\n";

        if (aDevolver >= restante)
            return prefijo + recorte;

        var proximoDesde = desde + aDevolver;
        return prefijo + recorte
            + $"\n\n-- [TRUNCADO: quedan {definicion.Length - proximoDesde} caracteres más de {definicion.Length} totales. Para seguir, llamá de nuevo con desde={proximoDesde}.]";
    });

    [McpServerTool(Name = "consultar_sql")]
    [Description("Ejecuta una consulta SELECT (o WITH/CTE) de solo lectura contra la base indicada. Cualquier sentencia que no sea de lectura (INSERT/UPDATE/DELETE/DROP/ALTER/EXEC/etc.) se rechaza antes de llegar a SQL Server — aunque la protección real es que el login del perfil solo tiene permiso db_datareader. Los resultados se truncan a limiteFilas (y además a un tope total de celdas, que se achica solo en tablas anchas) para no gastar de más en columnas que no hacen falta — preferir columnas explícitas en vez de SELECT * si la tabla es ancha y solo hacen falta un par de datos.")]
    public string ConsultarSql(
        [Description("Nombre del perfil (ver listar_perfiles)")] string perfil,
        [Description("Base de datos, ej. DRAGONFISH_DEMO")] string baseDeDatos,
        [Description("Sentencia SQL — debe empezar con SELECT o WITH")] string sql,
        [Description("Máximo de filas a devolver (default 50, tope 1000) — preferir seleccionar columnas puntuales antes que subir esto en tablas anchas")] int limiteFilas = 50,
        [Description("Timeout en segundos (default 5, tope 30)")] int timeoutSegundos = 5) => Envolver(() =>
    {
        ConsultaSqlValidator.ValidarOTirar(sql);
        limiteFilas = Math.Clamp(limiteFilas, 1, 1000);
        timeoutSegundos = Math.Clamp(timeoutSegundos, 1, 30);

        var p = ResolverPerfil(perfil);
        using var conn = ConexionHelper.AbrirConexion(p, baseDeDatos);
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = timeoutSegundos };
        using var reader = cmd.ExecuteReader();

        var limiteEfectivo = LimiteEfectivoPorCeldas(limiteFilas, reader.FieldCount);

        var filas = new List<Dictionary<string, object?>>();
        var truncado = false;
        while (reader.Read())
        {
            if (filas.Count >= limiteEfectivo) { truncado = true; break; }
            filas.Add(FilaComoDiccionario(reader));
        }

        var nota = !truncado ? null
            : limiteEfectivo < limiteFilas
                ? $"Se cortó en {limiteEfectivo} filas — el resultado tiene {reader.FieldCount} columnas, así que se redujo el límite pedido ({limiteFilas}) para no gastar de más. Para ver más filas, seleccioná menos columnas en el SELECT en vez de subir limiteFilas."
                : $"Se cortó en {limiteFilas} filas — puede haber más. Subí limiteFilas o acotá la consulta.";

        return JsonSerializer.Serialize(new { filas, cantidad = filas.Count, truncado, nota });
    });

    [McpServerTool(Name = "buscar_valor")]
    [Description("Busca un valor exacto (ej. un CUIT, un número de comprobante) en las columnas de texto/numéricas compatibles de las tablas indicadas. Requiere la lista de tablas candidatas (usá buscar_en_esquema primero) — no hace un barrido ciego de toda la base por rendimiento.")]
    public string BuscarValor(
        [Description("Nombre del perfil (ver listar_perfiles)")] string perfil,
        [Description("Base de datos, ej. DRAGONFISH_DEMO")] string baseDeDatos,
        [Description("Valor exacto a buscar, ej. \"30123456789\"")] string valor,
        [Description("Tablas candidatas donde buscar, formato \"Esquema.Tabla\" (ver buscar_en_esquema), ej. [\"ZooLogic.CLIENTES\", \"ZooLogic.COMPROBANTEV\"]")] string[] tablas,
        [Description("Máximo de filas a devolver por tabla (default 10, tope 50)")] int limitePorTabla = 10) => Envolver(() =>
    {
        if (tablas is null || tablas.Length == 0)
            throw new McpException("Hace falta indicar al menos una tabla candidata — usá buscar_en_esquema para encontrarlas primero.");

        limitePorTabla = Math.Clamp(limitePorTabla, 1, 50);
        var p = ResolverPerfil(perfil);
        using var conn = ConexionHelper.AbrirConexion(p, baseDeDatos);
        var esNumero = decimal.TryParse(valor, out _);

        var resultados = new List<object>();
        foreach (var entrada in tablas)
        {
            var partes = entrada.Split('.', 2);
            if (partes.Length != 2)
            {
                resultados.Add(new { tabla = entrada, error = "Formato inválido — usá \"Esquema.Tabla\"." });
                continue;
            }
            var (esq, tabla) = (partes[0], partes[1]);

            try
            {
                resultados.Add(BuscarValorEnTabla(conn, esq, tabla, entrada, valor, esNumero, limitePorTabla));
            }
            catch (Exception ex)
            {
                resultados.Add(new { tabla = entrada, error = ex.Message });
            }
        }

        return JsonSerializer.Serialize(resultados);
    });

    [McpServerTool(Name = "comparar_esquemas")]
    [Description("Compara el esquema de dos bases de datos en la misma instancia (mismo perfil): tablas que están solo en una, y para las tablas en común, diferencias de columnas. Útil para el caso típico \"funciona en Demo pero no en la base del cliente\".")]
    public string CompararEsquemas(
        [Description("Nombre del perfil (ver listar_perfiles)")] string perfil,
        [Description("Primera base de datos, ej. DRAGONFISH_DEMO")] string baseA,
        [Description("Segunda base de datos, ej. DRAGONFISH_CLIENTE")] string baseB) => Envolver(() =>
    {
        var p = ResolverPerfil(perfil);
        using var connA = ConexionHelper.AbrirConexion(p, baseA);
        using var connB = ConexionHelper.AbrirConexion(p, baseB);

        var tablasA = ObtenerTodasLasTablas(connA);
        var tablasB = ObtenerTodasLasTablas(connB);

        var clavesA = tablasA.Keys.ToHashSet();
        var clavesB = tablasB.Keys.ToHashSet();

        var soloEnA = clavesA.Except(clavesB).OrderBy(x => x).ToList();
        var soloEnB = clavesB.Except(clavesA).OrderBy(x => x).ToList();
        var comunes = clavesA.Intersect(clavesB).OrderBy(x => x).ToList();

        var diferenciasColumnas = new List<object>();
        foreach (var clave in comunes)
        {
            var soloColsA = tablasA[clave].Except(tablasB[clave]).OrderBy(x => x).ToList();
            var soloColsB = tablasB[clave].Except(tablasA[clave]).OrderBy(x => x).ToList();
            if (soloColsA.Count > 0 || soloColsB.Count > 0)
                diferenciasColumnas.Add(new { tabla = clave, soloEnA = soloColsA, soloEnB = soloColsB });
        }

        return JsonSerializer.Serialize(new
        {
            tablasSoloEnA = soloEnA,
            tablasSoloEnB = soloEnB,
            diferenciasDeColumnasEnComunes = diferenciasColumnas,
        });
    });

    // ── helpers privados ──────────────────────────────────────────────────────

    private SqlDiagnosticoPerfil ResolverPerfil(string perfil) =>
        cfg.Value.Perfiles.TryGetValue(perfil, out var p)
            ? p
            : throw new McpException($"No existe el perfil '{perfil}'. Usá listar_perfiles para ver los configurados.");

    /// <summary>Tope total de celdas (filas × columnas) para no gastar de más en tablas anchas.
    /// El JSON de salida es por fila (repite el nombre de cada columna en cada fila, no es
    /// columnar) — un SELECT * en una tabla de 80+ columnas con el límite de filas de siempre
    /// multiplica el gasto de tokens varias veces sin necesidad. Achica el límite de filas pedido
    /// solo cuando hace falta (tablas angostas no se ven afectadas).</summary>
    private const int MaxCeldasPorConsulta = 300;

    private static int LimiteEfectivoPorCeldas(int limiteFilasPedido, int cantidadColumnas) =>
        cantidadColumnas <= 0 ? limiteFilasPedido : Math.Min(limiteFilasPedido, Math.Max(1, MaxCeldasPorConsulta / cantidadColumnas));

    private static Dictionary<string, object?> FilaComoDiccionario(SqlDataReader reader)
    {
        var fila = new Dictionary<string, object?>();
        for (int i = 0; i < reader.FieldCount; i++)
            fila[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        return fila;
    }

    private static List<object> ObtenerColumnas(SqlConnection conn, string esquema, string tabla)
    {
        const string sql = """
            SELECT c.name, ty.name AS tipo, c.max_length, c.precision, c.scale,
                   c.is_nullable, c.is_identity
            FROM sys.columns c
            JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @esquema AND t.name = @tabla
            ORDER BY c.column_id
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@esquema", esquema);
        cmd.Parameters.AddWithValue("@tabla", tabla);
        using var reader = cmd.ExecuteReader();

        var columnas = new List<object>();
        while (reader.Read())
        {
            columnas.Add(new
            {
                nombre = reader.GetString(0),
                tipo = reader.GetString(1),
                longitud = reader.GetInt16(2),
                precision = reader.GetByte(3),
                escala = reader.GetByte(4),
                nullable = reader.GetBoolean(5),
                identity = reader.GetBoolean(6),
            });
        }
        return columnas;
    }

    private static List<string> ObtenerClavePrimaria(SqlConnection conn, string esquema, string tabla)
    {
        const string sql = """
            SELECT c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @esquema AND t.name = @tabla AND i.is_primary_key = 1
            ORDER BY ic.key_ordinal
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@esquema", esquema);
        cmd.Parameters.AddWithValue("@tabla", tabla);
        using var reader = cmd.ExecuteReader();

        var pk = new List<string>();
        while (reader.Read()) pk.Add(reader.GetString(0));
        return pk;
    }

    private static List<object> ObtenerForeignKeysSalientes(SqlConnection conn, string esquema, string tabla)
    {
        const string sql = """
            SELECT fk.name, cCol.name AS columna, sRef.name AS esquemaRef, tRef.name AS tablaRef, cRef.name AS columnaRef
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.tables t ON t.object_id = fk.parent_object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.columns cCol ON cCol.object_id = fkc.parent_object_id AND cCol.column_id = fkc.parent_column_id
            JOIN sys.tables tRef ON tRef.object_id = fk.referenced_object_id
            JOIN sys.schemas sRef ON tRef.schema_id = sRef.schema_id
            JOIN sys.columns cRef ON cRef.object_id = fkc.referenced_object_id AND cRef.column_id = fkc.referenced_column_id
            WHERE s.name = @esquema AND t.name = @tabla
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@esquema", esquema);
        cmd.Parameters.AddWithValue("@tabla", tabla);
        using var reader = cmd.ExecuteReader();

        var fks = new List<object>();
        while (reader.Read())
        {
            fks.Add(new
            {
                nombre = reader.GetString(0),
                columna = reader.GetString(1),
                referenciaEsquema = reader.GetString(2),
                referenciaTabla = reader.GetString(3),
                referenciaColumna = reader.GetString(4),
            });
        }
        return fks;
    }

    private static List<object> ObtenerForeignKeysEntrantes(SqlConnection conn, string esquema, string tabla)
    {
        const string sql = """
            SELECT fk.name, sOrig.name AS esquemaOrigen, tOrig.name AS tablaOrigen, cOrig.name AS columnaOrigen, cCol.name AS columna
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.tables t ON t.object_id = fk.referenced_object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.columns cCol ON cCol.object_id = fkc.referenced_object_id AND cCol.column_id = fkc.referenced_column_id
            JOIN sys.tables tOrig ON tOrig.object_id = fk.parent_object_id
            JOIN sys.schemas sOrig ON tOrig.schema_id = sOrig.schema_id
            JOIN sys.columns cOrig ON cOrig.object_id = fkc.parent_object_id AND cOrig.column_id = fkc.parent_column_id
            WHERE s.name = @esquema AND t.name = @tabla
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@esquema", esquema);
        cmd.Parameters.AddWithValue("@tabla", tabla);
        using var reader = cmd.ExecuteReader();

        var fks = new List<object>();
        while (reader.Read())
        {
            fks.Add(new
            {
                nombre = reader.GetString(0),
                origenEsquema = reader.GetString(1),
                origenTabla = reader.GetString(2),
                origenColumna = reader.GetString(3),
                columna = reader.GetString(4),
            });
        }
        return fks;
    }

    private static List<object> ObtenerIndices(SqlConnection conn, string esquema, string tabla)
    {
        const string sql = """
            SELECT i.name, i.is_unique, i.type_desc,
                   STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @esquema AND t.name = @tabla AND i.name IS NOT NULL
            GROUP BY i.name, i.is_unique, i.type_desc
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@esquema", esquema);
        cmd.Parameters.AddWithValue("@tabla", tabla);
        using var reader = cmd.ExecuteReader();

        var indices = new List<object>();
        while (reader.Read())
        {
            indices.Add(new
            {
                nombre = reader.GetString(0),
                unico = reader.GetBoolean(1),
                tipo = reader.GetString(2),
                columnas = reader.GetString(3),
            });
        }
        return indices;
    }

    private static object BuscarValorEnTabla(SqlConnection conn, string esquema, string tabla, string etiqueta, string valor, bool esNumero, int limitePorTabla)
    {
        var columnas = ObtenerColumnasCompatibles(conn, esquema, tabla, esNumero);
        if (columnas.Count == 0)
            return new { tabla = etiqueta, filas = Array.Empty<object>(), nota = "No tiene columnas de texto/numéricas compatibles con el valor buscado." };

        var condiciones = string.Join(" OR ", columnas.Select(c => $"{ConexionHelper.CorchetesSeguro(c)} = @valor"));
        var sql = $"SELECT TOP (@limite) * FROM {ConexionHelper.CorchetesSeguro(esquema)}.{ConexionHelper.CorchetesSeguro(tabla)} WHERE {condiciones}";

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@limite", limitePorTabla);
        cmd.Parameters.Add("@valor", SqlDbType.NVarChar, 4000).Value = valor;

        var filas = new List<Dictionary<string, object?>>();
        using (var reader = cmd.ExecuteReader())
        {
            var limiteEfectivo = LimiteEfectivoPorCeldas(limitePorTabla, reader.FieldCount);
            while (reader.Read())
            {
                if (filas.Count >= limiteEfectivo) break;
                filas.Add(FilaComoDiccionario(reader));
            }
        }

        return new { tabla = etiqueta, columnasComparadas = columnas, filas };
    }

    private static List<string> ObtenerColumnasCompatibles(SqlConnection conn, string esquema, string tabla, bool esNumero)
    {
        var tiposTexto = new[] { "char", "varchar", "nchar", "nvarchar" };
        var tiposNumero = new[] { "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "money", "smallmoney" };
        var tiposAceptados = (esNumero ? tiposTexto.Concat(tiposNumero) : tiposTexto)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        const string sql = """
            SELECT c.name, ty.name AS tipo
            FROM sys.columns c
            JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @esquema AND t.name = @tabla
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@esquema", esquema);
        cmd.Parameters.AddWithValue("@tabla", tabla);
        using var reader = cmd.ExecuteReader();

        var columnas = new List<string>();
        while (reader.Read())
            if (tiposAceptados.Contains(reader.GetString(1)))
                columnas.Add(reader.GetString(0));
        return columnas;
    }

    private static Dictionary<string, HashSet<string>> ObtenerTodasLasTablas(SqlConnection conn)
    {
        const string sql = """
            SELECT s.name AS esquema, t.name AS tabla, c.name AS columna
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        using var reader = cmd.ExecuteReader();

        var tablas = new Dictionary<string, HashSet<string>>();
        while (reader.Read())
        {
            var clave = $"{reader.GetString(0)}.{reader.GetString(1)}";
            if (!tablas.TryGetValue(clave, out var cols))
                tablas[clave] = cols = new HashSet<string>();
            cols.Add(reader.GetString(2));
        }
        return tablas;
    }
}
