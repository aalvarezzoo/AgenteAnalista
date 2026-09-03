using System.Text.RegularExpressions;

namespace LogsMcp;

/// <summary>Junta un log con sus rotaciones (<c>nombre.log</c>, <c>nombre.log.1</c>,
/// <c>nombre.log.2</c>...) como si fueran un solo stream. A propósito NO asume cuál es más nueva
/// por el número de rotación (no está confirmado que Dragonfish numere siempre igual) — junta todas
/// las líneas de todos los archivos y deja que el timestamp real de cada evento (no el nombre de
/// archivo) decida el orden final, en las tools que ordenan por fecha.</summary>
public static partial class ArchivosLog
{
    [GeneratedRegex(@"\.\d+$")]
    private static partial Regex SufijoRotacion();

    /// <summary>Es el archivo base o una rotación suya (ej. "operaciones.log" o
    /// "operaciones.log.3" para nombreBase "operaciones.log") — extraído a función pura (no toca
    /// disco) para poder testear la regla de matching sin crear archivos reales.</summary>
    internal static bool EsArchivoDeLog(string nombreArchivo, string nombreBase)
    {
        if (string.Equals(nombreArchivo, nombreBase, StringComparison.OrdinalIgnoreCase))
            return true;
        return nombreArchivo.StartsWith(nombreBase, StringComparison.OrdinalIgnoreCase) && SufijoRotacion().IsMatch(nombreArchivo);
    }

    /// <summary>Busca en <paramref name="carpeta"/> el archivo <paramref name="nombreBase"/> y sus
    /// rotaciones, sin distinguir mayúsculas/minúsculas en el nombre.</summary>
    public static List<string> EncontrarArchivos(string carpeta, string nombreBase)
    {
        if (!Directory.Exists(carpeta))
            return [];

        return Directory.EnumerateFiles(carpeta)
            .Where(f => EsArchivoDeLog(Path.GetFileName(f), nombreBase))
            .ToList();
    }

    public static IEnumerable<string> LeerTodasLasLineas(string carpeta, string nombreBase) =>
        EncontrarArchivos(carpeta, nombreBase).SelectMany(File.ReadLines);
}
