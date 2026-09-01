using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using ModelContextProtocol.Server;

namespace GestionBackupsMcp;

/// <summary>
/// Nombre de backup: "&lt;fecha&gt;-&lt;hora&gt;-&lt;frecuencia&gt;-&lt;NombreBase&gt;-&lt;version&gt;.zip"
/// (ej. "20260827-110000-Jueves-DRAGONFISH_DEMO-16.0004.14964.zip"). NombreBase ya trae el
/// prefijo DRAGONFISH_ si corresponde (confirmado contra ADNIMPLANT..BasesDeDatos) — no agregarlo.
/// </summary>
public static partial class NombreBackup
{
    [GeneratedRegex(@"^\d{8}-\d{6}-[^-]+-(?<base>.+)-\d+\.\d+\.\d+\.zip$", RegexOptions.IgnoreCase)]
    public static partial Regex Patron();
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
    [Description("Restaura de forma silenciosa (sin abrir ninguna ventana) el backup de UNA base puntual, buscando en la carpeta dada el .zip cuyo nombre de base coincida. Restaura SOLO la base pedida — si la carpeta tiene backups de otras bases, no se tocan. Antes de restaurar chequea si la base ya está registrada en Dragonfish (tabla Emp): si no lo está, NO restaura — hace falta darla de alta primero con 'dar_alta_base_para_restore' (y confirmar explícitamente con la persona antes de llamarlo). Excepción: ADNIMPLANT y ZOOLOGICMASTER nunca pasan por este chequeo — no son bases de tipo Sucursal, Dragonfish no las da de alta en Emp.")]
    public string RestaurarBackup(
        [Description("Carpeta donde buscar el .zip de backup, ej. C:\\1690552")] string carpeta,
        [Description("Nombre exacto de la base a restaurar (como aparece en el nombre del .zip), ej. DRAGONFISH_NCENTRO")] string nombreBase)
    {
        if (!Directory.Exists(carpeta))
            return $"La carpeta '{carpeta}' no existe.";

        var zips = Directory.GetFiles(carpeta, "*.zip");
        var zip = zips.FirstOrDefault(z =>
        {
            var m = NombreBackup.Patron().Match(Path.GetFileName(z));
            return m.Success && string.Equals(m.Groups["base"].Value, nombreBase, StringComparison.OrdinalIgnoreCase);
        });

        if (zip is null)
            return $"No se encontró ningún .zip para la base '{nombreBase}' en '{carpeta}'. (Otros .zip que pueda haber en la carpeta no se tocan salvo que se pidan explícitamente.)";

        var codigo = EmpHelper.LimpiarCodigo(nombreBase);

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

        return RestaurarUno(zip, nombreBase);
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

    private static string RestaurarUno(string zipPath, string nombreBase)
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
        psi.ArgumentList.Add($"-bdr{nombreBase}");
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
            proc.WaitForExit();

            var logNuevo = LeerLogNuevo(posicionPrevia);

            // Frase textual y específica de ZooBkp para el resultado FINAL del proceso — no alcanza
            // con buscar "con éxito" suelto: pasos intermedios ("herramientas exportadas con éxito")
            // pueden contenerlo aunque el proceso termine mal. Ya nos pasó: una restauración con un
            // error real de ADN Implant quedó marcada como éxito por este motivo.
            var huboExito = logNuevo.Contains("finalizado con éxito", StringComparison.OrdinalIgnoreCase);
            var huboError = logNuevo.Contains("finalizado con errores", StringComparison.OrdinalIgnoreCase)
                || logNuevo.Contains("retorno erróneo", StringComparison.OrdinalIgnoreCase);
            var exito = proc.ExitCode == 0 && huboExito && !huboError;

            // ZooBkp puede reportar "éxito" sin haber restaurado nada de verdad si la base no está
            // registrada en esta instalación (no crea bases nuevas de la nada) — el paso real de
            // restauración ("Invocando al componente SQLDmoWrapper...") nunca llega a ejecutarse.
            // Nos pasó con una base inexistente: quedó "success" siendo en realidad un no-op.
            var huboRestauracionReal = logNuevo.Contains("Invocando al componente SQLDmoWrapper", StringComparison.OrdinalIgnoreCase);

            if (exito && !huboRestauracionReal)
                return $"⚠ {nombreArchivo} → {nombreBase}: ZooBkp reportó éxito, pero no se detectó que haya restaurado datos de verdad — probablemente '{nombreBase}' no está registrada en esta instalación de Dragonfish (no crea bases nuevas). Revisar antes de asumir que quedó restaurada.";

            var estado = exito ? "✓" : "✗";
            var detalle = exito
                ? "restaurada con éxito"
                : $"código de salida {proc.ExitCode}, revisar log — {(logNuevo.Length == 0 ? "no se pudo leer el log nuevo" : ResumenLog(logNuevo))}";

            return $"{estado} {nombreArchivo} → {nombreBase}: {detalle}";
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
