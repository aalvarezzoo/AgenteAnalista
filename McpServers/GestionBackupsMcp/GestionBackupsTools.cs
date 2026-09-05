using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using ModelContextProtocol.Server;

namespace GestionBackupsMcp;

/// <summary>
/// Dos formatos de backup reconocidos, ambos se le pasan igual a ZooBkp.exe (mismo argumento -f) —
/// pero adentro ZooBkp los restaura con mecanismos completamente distintos, confirmado leyendo el
/// código fuente real (`RestoreBase.GetRestoreMode` en
/// ZooLogicSA.RecoveryManager.Core\Managers\RestoreBase.cs:793 — enruta explícitamente por
/// extensión del archivo; ver detalle completo en mapa-codigo-dragonfish.md, sección "Restore de
/// snapshots de zNube"):
/// - **Backup de ZooBkp** (`.zip`): "&lt;fecha&gt;-&lt;hora&gt;-&lt;frecuencia&gt;-&lt;NombreBase&gt;-&lt;version&gt;.zip"
///   (ej. "20260827-110000-Jueves-DRAGONFISH_DEMO-16.0004.14964.zip"). NombreBase ya trae el
///   prefijo DRAGONFISH_ si corresponde (confirmado contra ADNIMPLANT..BasesDeDatos) — no agregarlo.
///   Se restaura con `RESTORE DATABASE` nativo de SQL Server (`RestoreFromRecovery.cs` →
///   `SqlDmoWrapper.RestoreDatabase`).
/// - **Snapshot de zNube** (`.exe`): "&lt;RazonSocial_con_guiones_bajos&gt;_&lt;NombreBase&gt;_&lt;yyyyMMddHHmmss&gt;.exe"
///   (ej. "Iair_Gabriel_Tawil_I_AM_20260903112430.exe" para la base "I AM"). Es un autoextraíble
///   (DotNetZip SFX firmado por Zoo Logic) que adentro trae un `Snapshot.zsnp`, y no hace falta la
///   herramienta de restore propia (`zNube.RestoreSnapshot.exe`, GUI) — ZooBkp.exe reconoce la
///   extensión `.exe` a propósito y usa una estrategia dedicada (`RestoreFromSnapshot.cs`) que NO
///   es un `RESTORE DATABASE`: reconstruye la base desde cero (crea esquema desde scripts .sql
///   extraídos + bulk copy de datos), y por eso dispara después un paso de adecuación exclusivo en
///   ADNImplant que regenera PKs/índices (el bulk copy no los recrea solo). A diferencia del
///   backup de ZooBkp, el nombre de la base ACÁ NO lleva el prefijo DRAGONFISH_ — por eso el
///   matching lo saca antes de comparar, para que el llamador pueda seguir pasando el nombre con o
///   sin prefijo indistintamente.
/// </summary>
public static partial class NombreBackup
{
    [GeneratedRegex(@"^\d{8}-\d{6}-[^-]+-(?<base>.+)-\d+\.\d+\.\d+\.zip$", RegexOptions.IgnoreCase)]
    public static partial Regex PatronZip();

    /// <summary>Es un snapshot de zNube cuyo nombre de base coincide con <paramref name="nombreBase"/>
    /// (con o sin el prefijo DRAGONFISH_, y con espacios o guiones bajos indistintamente).</summary>
    public static bool EsSnapshotDe(string nombreArchivo, string nombreBase)
    {
        var sinPrefijo = nombreBase.StartsWith("DRAGONFISH_", StringComparison.OrdinalIgnoreCase)
            ? nombreBase["DRAGONFISH_".Length..]
            : nombreBase;
        var conGuiones = sinPrefijo.Replace(' ', '_');
        var patron = $@"^.+_{Regex.Escape(conGuiones)}_\d{{14}}\.exe$";
        return Regex.IsMatch(nombreArchivo, patron, RegexOptions.IgnoreCase);
    }

    /// <summary>Elige, entre los archivos de una carpeta (.zip de ZooBkp o .exe de snapshot de
    /// zNube), el único cuyo nombre de base coincida con <paramref name="nombreBase"/> — nunca toca
    /// otro archivo que pueda haber al lado (ej. un backup de DRAGONFISH_ZOOLOGICMASTER en la misma
    /// carpeta). Extraído a función pura para poder testear esta regla sin tocar el sistema de
    /// archivos real.</summary>
    public static string? ElegirArchivo(IEnumerable<string> archivos, string nombreBase) =>
        archivos.FirstOrDefault(a =>
        {
            var nombre = Path.GetFileName(a);
            var m = PatronZip().Match(nombre);
            if (m.Success && string.Equals(m.Groups["base"].Value, nombreBase, StringComparison.OrdinalIgnoreCase))
                return true;
            return EsSnapshotDe(nombre, nombreBase);
        });
}

/// <summary>Interpreta el log de ZooBkp.exe para una corrida puntual — separado de
/// <see cref="GestionBackupsTools"/> para poder testear esta lógica sin depender de Process.Start
/// ni del registro de Windows que esa clase resuelve en sus campos estáticos.</summary>
public static class RestoreResultado
{
    public readonly record struct Evaluacion(bool Exito, bool HuboRestauracionReal);

    /// <summary>Frase textual y específica de ZooBkp para el resultado FINAL del proceso — no
    /// alcanza con buscar "con éxito" suelto: pasos intermedios ("herramientas exportadas con
    /// éxito") pueden contenerlo aunque el proceso termine mal. "Invocando al componente
    /// SQLDmoWrapper" confirma que hubo restauración real — ZooBkp puede reportar éxito general
    /// sin haber tocado nada si la base no está registrada en Emp (ver CLAUDE.md).</summary>
    public static Evaluacion Evaluar(int exitCode, string logNuevo)
    {
        var huboExito = logNuevo.Contains("finalizado con éxito", StringComparison.OrdinalIgnoreCase);
        var huboError = logNuevo.Contains("finalizado con errores", StringComparison.OrdinalIgnoreCase)
            || logNuevo.Contains("retorno erróneo", StringComparison.OrdinalIgnoreCase);
        var exito = exitCode == 0 && huboExito && !huboError;
        var huboRestauracionReal = logNuevo.Contains("Invocando al componente SQLDmoWrapper", StringComparison.OrdinalIgnoreCase);
        return new Evaluacion(exito, huboRestauracionReal);
    }
}

[McpServerToolType]
public sealed class GestionBackupsTools
{
    private static readonly string DragonfishInstallDir = ResolverInstallDir();

    private static readonly string ZooBkpExe =
        Environment.GetEnvironmentVariable("ZOOBKP_EXE_PATH") ?? Path.Combine(DragonfishInstallDir, "BIN", "ZooBkp.exe");

    private static readonly string ZooBkpLog =
        Environment.GetEnvironmentVariable("ZOOBKP_LOG_PATH") ?? Path.Combine(DragonfishInstallDir, "Log", "ZooBkp.log");

    /// <summary>La carpeta de instalación de Dragonfish no está fija en C:\ — cada máquina puede
    /// tenerla en otro disco/carpeta (ej. Archivos de Programa). Se lee de la clave de registro que
    /// el instalador de Dragonfish escribe (confirmado en la máquina de AALVAREZ:
    /// HKLM\SOFTWARE\Zoo Logic\Dragonfish Color y Talle\InstallDir = C:\Dragonfish). El nombre de la
    /// subclave varía según la edición de Dragonfish instalada (Color y Talle, Comercios, etc.), por
    /// eso se busca por "contiene Dragonfish" en vez de por nombre exacto.</summary>
    private static string ResolverInstallDir()
    {
        foreach (var vista in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, vista);
                using var zooLogicKey = hklm.OpenSubKey(@"SOFTWARE\Zoo Logic");
                if (zooLogicKey is null) continue;

                foreach (var nombreSubclave in zooLogicKey.GetSubKeyNames())
                {
                    if (!nombreSubclave.Contains("Dragonfish", StringComparison.OrdinalIgnoreCase)) continue;

                    using var subclave = zooLogicKey.OpenSubKey(nombreSubclave);
                    if (subclave?.GetValue("InstallDir") is string installDir && !string.IsNullOrWhiteSpace(installDir))
                        return installDir;
                }
            }
            catch
            {
                // Si el registro no está accesible por algún motivo, se sigue con el fallback de abajo.
            }
        }

        return @"C:\Dragonfish";
    }

    /// <summary>Las 3 bases "por defecto" de cualquier instalación de Dragonfish (confirmado en
    /// el código fuente, DatosEstructuraAdnPorDefecto.xml) no son todas iguales: DEMO es de tipo
    /// Sucursal (una base de cliente más, por eso sí pasa por el alta en Emp), pero ADNIMPLANT y
    /// ZOOLOGICMASTER tienen Ubicaciones distintas (ADNIMPLANT/infraestructura) — Dragonfish nunca
    /// las da de alta vía Emp como si fueran una sucursal de cliente. Restaurarlas no debería
    /// depender de ese chequeo. Confirmado en la práctica: forzar el alta de ADNIMPLANT vía
    /// dar_alta_base_para_restore falla (EMP.EMPCOD es más corto que "ADNIMPLANT") — la falla en
    /// sí ya era una señal de que ese camino no correspondía para estas dos bases.</summary>
    private static readonly HashSet<string> BasesSinChequeoDeEmp = new(StringComparer.OrdinalIgnoreCase)
    {
        "ADNIMPLANT",
        "ZOOLOGICMASTER",
    };

    [McpServerTool(Name = "restaurar_backup")]
    [Description("Restaura de forma silenciosa (sin abrir ninguna ventana) el backup de UNA base puntual, buscando en la carpeta dada el archivo cuyo nombre de base coincida — un .zip de ZooBkp o un .exe autoextraíble de snapshot de zNube (ej. \"Cliente_NombreBase_20260903112430.exe\"), los dos se restauran igual, pasándolos tal cual a ZooBkp.exe. Restaura SOLO la base pedida — si la carpeta tiene backups/snapshots de otras bases, no se tocan. Antes de restaurar un .zip de ZooBkp chequea si la base ya está registrada en Dragonfish (tabla Emp): si no lo está, NO restaura — hace falta darla de alta primero con 'dar_alta_base_para_restore' (y confirmar explícitamente con la persona antes de llamarlo). Excepciones que nunca pasan por este chequeo: ADNIMPLANT y ZOOLOGICMASTER (no son bases de tipo Sucursal, Dragonfish no las da de alta en Emp), y cualquier snapshot de zNube (.exe) — restaurar un snapshot no pregunta por el alta en Emp, a diferencia de un .zip.")]
    public string RestaurarBackup(
        [Description("Carpeta donde buscar el backup/snapshot, ej. C:\\1690552")] string carpeta,
        [Description("Nombre exacto de la base a restaurar (como aparece en el nombre del archivo, con o sin el prefijo DRAGONFISH_), ej. DRAGONFISH_NCENTRO o I AM")] string nombreBase)
    {
        // Envuelve TODO el cuerpo, no solo el chequeo de Emp — Directory.GetFiles y lo que
        // dispare RestaurarUno (WaitForExit/lectura de log) también pueden tirar, y sin este
        // try/catch el SDK de MCP sanitiza esa excepción a un mensaje genérico inútil (mismo
        // gotcha ya documentado en la skill mcp-tools-desarrollo para Process.Start).
        try
        {
            if (!Directory.Exists(carpeta))
                return $"La carpeta '{carpeta}' no existe.";

            var candidatos = Directory.GetFiles(carpeta, "*.zip").Concat(Directory.GetFiles(carpeta, "*.exe"));
            var zip = NombreBackup.ElegirArchivo(candidatos, nombreBase);

            if (zip is null)
                return $"No se encontró ningún backup (.zip) ni snapshot (.exe) para la base '{nombreBase}' en '{carpeta}'. (Otros archivos que pueda haber en la carpeta no se tocan salvo que se pidan explícitamente.)";

            var codigo = EmpHelper.LimpiarCodigo(nombreBase);

            // Confirmado en el código fuente de Dragonfish (2026-09-04, ver mapa-codigo-dragonfish.md,
            // sección "Restore de snapshots de zNube"): en modo consola, TANTO para .zip como para
            // .exe, ZooBkp arma la lista de bases a restaurar consultando Emp — si la base pedida no
            // está ahí, la restauración se salta en silencio (sin excepción) reportando éxito igual.
            // Un snapshot NO está exento de este chequeo — probado empíricamente: restaurar un
            // snapshot de una base no registrada en Emp no creó ninguna base real, aunque el log de
            // ZooBkp dijera "finalizado con éxito".
            if (!BasesSinChequeoDeEmp.Contains(codigo))
            {
                try
                {
                    var instanciaSql = ResolverInstanciaSql();
                    using var conn = EmpHelper.AbrirConexion(instanciaSql);
                    var esquema = EmpHelper.ResolverEsquemaEmp(conn);

                    if (!EmpHelper.ExisteEnEmp(conn, esquema, codigo))
                    {
                        return $"La base '{codigo}' no está registrada en esta instalación de Dragonfish (no aparece en Emp) — no se restauró nada. "
                             + "Si corresponde crearla, confirmá con la persona y después llamá a 'dar_alta_base_para_restore'; recién ahí se puede reintentar este restore.";
                    }
                }
                catch (Exception ex)
                {
                    return $"No se pudo verificar en SQL si la base '{codigo}' está registrada — no se restauró nada por las dudas. Detalle: {ex.Message}";
                }
            }

            // El -bdr que espera ZooBkp en modo consola se compara contra epath en Emp, que
            // siempre lleva el prefijo DRAGONFISH_ (confirmado en ProveedorBD.cs — .Nombre sale de
            // epath, no de empcod) — así que para cualquier base que sí pase por Emp, se normaliza
            // acá, sin importar si el llamador pasó el nombre corto o con prefijo. ADNIMPLANT y
            // ZOOLOGICMASTER (que no están en Emp) se dejan tal cual llegó nombreBase, sin tocar un
            // comportamiento ya probado que no pasa por este chequeo.
            var bdr = BasesSinChequeoDeEmp.Contains(codigo) ? nombreBase : $"DRAGONFISH_{codigo}";

            return RestaurarUno(zip, nombreBase, bdr);
        }
        catch (Exception ex)
        {
            return $"✗ No se pudo procesar la restauración de '{nombreBase}' en '{carpeta}'. Detalle: {ex.Message}";
        }
    }

    [McpServerTool(Name = "dar_alta_base_para_restore")]
    [Description("Registra una base nueva en la tabla Emp de Dragonfish (DRAGONFISH_ZOOLOGICMASTER) para que después restaurar_backup pueda restaurar un backup sobre ella. Replica el mismo alta que hace la pantalla de restauración de Dragonfish cuando la base no existe — NO crea el archivo físico de la base (eso lo hace restaurar_backup después). Usar SOLO tras confirmar explícitamente con la persona que corresponde crear esta base — nunca de forma automática dentro de restaurar_backup.")]
    public string DarAltaBaseParaRestore(
        [Description("Nombre corto de la base a registrar, con o sin el prefijo DRAGONFISH_, ej. NCENTRO")] string nombreBase)
    {
        var codigo = EmpHelper.LimpiarCodigo(nombreBase);
        var ruta = $"DRAGONFISH_{codigo}";

        try
        {
            var instanciaSql = ResolverInstanciaSql();
            using var conn = EmpHelper.AbrirConexion(instanciaSql);
            var esquema = EmpHelper.ResolverEsquemaEmp(conn);

            if (EmpHelper.ExisteEnEmp(conn, esquema, codigo))
                return $"La base '{codigo}' ya estaba registrada en Emp — no se hizo ningún cambio.";

            var columnas = EmpHelper.ObtenerColumnasEmp(conn, esquema);
            var (template, baseTemplate) = EmpHelper.LeerFilaTemplate(conn, esquema, columnas);

            if (template is null)
            {
                return "No se encontró ninguna fila existente en Emp para usar como plantilla de columnas — "
                     + "no se puede dar de alta de forma segura sin saber qué otras columnas requiere la tabla. No se insertó nada.";
            }

            EmpHelper.AplicarOverrides(template, codigo, ruta);
            EmpHelper.InsertarFila(conn, esquema, columnas, template);

            return $"✓ Base '{codigo}' registrada en Emp (epath={ruta}), usando '{baseTemplate}' como plantilla para el resto de las columnas. "
                 + "Ahora se puede correr restaurar_backup para restaurar el backup sobre ella.";
        }
        catch (Exception ex)
        {
            return $"✗ No se pudo dar de alta la base '{codigo}'. Detalle: {ex.Message}";
        }
    }

    /// <summary>La instancia SQL de esta instalación de Dragonfish no se pide por parámetro ni se
    /// guarda: se lee de dataconfig.ini (mismo archivo que usa Dragonfish/ZooBkp.exe), ubicado al
    /// lado de ZooBkp.exe. Overrideable con ZOOBKP_SQL_INSTANCE si en alguna máquina no aplica.</summary>
    private static string ResolverInstanciaSql()
    {
        var overrideEnv = Environment.GetEnvironmentVariable("ZOOBKP_SQL_INSTANCE");
        if (!string.IsNullOrWhiteSpace(overrideEnv))
            return overrideEnv;

        var binDir = Path.GetDirectoryName(ZooBkpExe)
            ?? throw new InvalidOperationException($"No se pudo determinar la carpeta contenedora de '{ZooBkpExe}'.");
        var appPath = Directory.GetParent(binDir)?.FullName
            ?? throw new InvalidOperationException($"No se pudo subir un nivel desde '{binDir}' para encontrar dataconfig.ini.");
        var dataconfigPath = Path.Combine(appPath, "dataconfig.ini");

        if (!File.Exists(dataconfigPath))
            throw new InvalidOperationException(
                $"No se encontró '{dataconfigPath}' para determinar la instancia SQL de Dragonfish. Definí la variable de entorno ZOOBKP_SQL_INSTANCE manualmente.");

        bool enSeccionSql = false;
        foreach (var linea in File.ReadAllLines(dataconfigPath))
        {
            var t = linea.Trim();
            if (t.Equals("[SQL]", StringComparison.OrdinalIgnoreCase)) { enSeccionSql = true; continue; }
            if (t.StartsWith('[') && t.EndsWith(']')) { enSeccionSql = false; continue; }

            if (enSeccionSql && t.StartsWith("Servidor", StringComparison.OrdinalIgnoreCase))
            {
                var idx = t.IndexOf('=');
                if (idx >= 0)
                {
                    var valor = t[(idx + 1)..].Trim();
                    if (!string.IsNullOrEmpty(valor))
                        return valor;
                }
            }
        }

        throw new InvalidOperationException(
            $"No se encontró 'Servidor=' en la sección [SQL] de '{dataconfigPath}'. Definí la variable de entorno ZOOBKP_SQL_INSTANCE manualmente.");
    }

    private static string RestaurarUno(string zipPath, string nombreBase, string bdr)
    {
        var nombreArchivo = Path.GetFileName(zipPath);

        if (!File.Exists(ZooBkpExe))
            return $"✗ {nombreArchivo}: no se encontró ZooBkp.exe en '{ZooBkpExe}'.";

        // Posición del log antes de correr — así después leemos solo lo que agregó esta corrida,
        // no el historial acumulado de restauraciones anteriores.
        long posicionPrevia = File.Exists(ZooBkpLog) ? new FileInfo(ZooBkpLog).Length : 0;

        var psi = new ProcessStartInfo
        {
            FileName               = ZooBkpExe,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WindowStyle            = ProcessWindowStyle.Hidden,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add("-hp");
        psi.ArgumentList.Add($"-f{zipPath}");
        psi.ArgumentList.Add($"-bdr{bdr}");
        psi.ArgumentList.Add("-ejecutormantenimiento");

        // Process.Start puede tirar (Win32Exception por permisos, etc.) además de devolver null —
        // se captura acá y se devuelve como texto, igual que el resto de este método, en vez de
        // dejarlo escapar como excepción cruda (el SDK de MCP sanitiza cualquier excepción que no
        // sea McpException a un mensaje genérico antes de que el modelo la vea).
        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start devolvió null.");
        }
        catch (Exception ex)
        {
            return $"✗ {nombreArchivo}: no se pudo iniciar ZooBkp.exe. Detalle: {ex.Message}";
        }

        using (proc)
        {
            try
            {
                proc.WaitForExit();

                var logNuevo = LeerLogNuevo(posicionPrevia);
                var (exito, huboRestauracionReal) = RestoreResultado.Evaluar(proc.ExitCode, logNuevo);

                if (exito && !huboRestauracionReal)
                    return $"⚠ {nombreArchivo} → {nombreBase}: ZooBkp reportó éxito, pero no se detectó que haya restaurado datos de verdad — probablemente '{nombreBase}' no está registrada en esta instalación de Dragonfish (no crea bases nuevas). Revisar antes de asumir que quedó restaurada.";

                var estado = exito ? "✓" : "✗";
                var detalle = exito
                    ? "restaurada con éxito"
                    : $"código de salida {proc.ExitCode}, revisar log — {(logNuevo.Length == 0 ? "no se pudo leer el log nuevo" : ResumenLog(logNuevo))}";

                return $"{estado} {nombreArchivo} → {nombreBase}: {detalle}";
            }
            catch (Exception ex)
            {
                // WaitForExit/LeerLogNuevo pueden tirar (log bloqueado/rotado justo en ese
                // instante, etc.) — ZooBkp ya arrancó, así que no se puede asumir que no pasó
                // nada; se avisa que el resultado quedó sin confirmar en vez de perder el detalle
                // real en un mensaje genérico del SDK.
                return $"✗ {nombreArchivo} → {nombreBase}: ZooBkp se inició pero no se pudo confirmar el resultado (no se pudo esperar a que termine o leer su log). Revisar manualmente. Detalle: {ex.Message}";
            }
        }
    }

    private static string LeerLogNuevo(long posicionPrevia)
    {
        if (!File.Exists(ZooBkpLog)) return "";
        using var fs = new FileStream(ZooBkpLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (posicionPrevia > fs.Length) posicionPrevia = 0; // el log pudo haberse rotado
        fs.Seek(posicionPrevia, SeekOrigin.Begin);
        using var reader = new StreamReader(fs, Encoding.GetEncoding("ISO-8859-1"));
        return reader.ReadToEnd();
    }

    /// <summary>Últimas líneas no vacías del log nuevo — para no devolver miles de líneas si falla.</summary>
    private static string ResumenLog(string logNuevo) =>
        string.Join(" | ", logNuevo
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(5));
}
