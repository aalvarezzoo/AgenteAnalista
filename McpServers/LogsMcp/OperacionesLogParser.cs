using System.Globalization;
using System.Text.RegularExpressions;

namespace LogsMcp;

/// <summary>Parsea el formato de <c>operaciones.log</c> de Dragonfish: bloques de 3 líneas
/// (cabecera con fecha/Base/Usuario/Serie, línea de PC, línea de hora+acción) separados por línea
/// en blanco. Extraído a función pura (recibe líneas de texto, no un path) para poder testear el
/// parseo sin depender de archivos reales — mismo criterio que <c>NombreBackup.ElegirZip</c> en
/// GestionBackupsMcp.</summary>
public static partial class OperacionesLogParser
{
    [GeneratedRegex(@"^(?<fecha>\d{1,2}/\d{1,2}/\d{4}),\s*Base:\s*(?<base>[^,]*),\s*Usuario:\s*(?<usuario>[^,]*),\s*Aplicaci[oó]n:\s*(?<app>[^,]*),\s*Versi[oó]n:\s*(?<version>[^,]*),\s*Serie:\s*(?<serie>.*)$")]
    private static partial Regex Cabecera();

    [GeneratedRegex(@"Nombre de la PC:\s*(?<pc>[^,]*),")]
    private static partial Regex LineaPc();

    [GeneratedRegex(@"^\s*(?<hora>\d{1,2}:\d{2}:\d{2}),\s*(?<resto>.*)$")]
    private static partial Regex LineaAccion();

    public sealed record Evento(DateTime Momento, string Base, string Usuario, string Serie, string NombrePc, string Accion);

    /// <summary>Un evento por bloque de 3 líneas — si la línea de hora no aparece justo después de
    /// la de PC (formato inesperado), el bloque se descarta en silencio en vez de tirar una
    /// excepción de parseo, para no cortar la lectura del resto del archivo por un bloque raro.</summary>
    public static IEnumerable<Evento> Parsear(IEnumerable<string> lineas)
    {
        string? fecha = null;
        string @base = "", usuario = "", serie = "", nombrePc = "";
        var estado = Estado.EsperandoCabecera;

        foreach (var linea in lineas)
        {
            var mc = Cabecera().Match(linea);
            if (mc.Success)
            {
                fecha = mc.Groups["fecha"].Value;
                @base = mc.Groups["base"].Value.Trim();
                usuario = mc.Groups["usuario"].Value.Trim();
                serie = mc.Groups["serie"].Value.Trim();
                estado = Estado.EsperandoPc;
                continue;
            }

            if (estado == Estado.EsperandoPc)
            {
                var mp = LineaPc().Match(linea);
                if (mp.Success)
                {
                    nombrePc = mp.Groups["pc"].Value.Trim();
                    estado = Estado.EsperandoAccion;
                }
                continue;
            }

            if (estado == Estado.EsperandoAccion)
            {
                estado = Estado.EsperandoCabecera;
                var ma = LineaAccion().Match(linea);
                if (ma.Success && fecha is not null &&
                    DateTime.TryParse($"{fecha} {ma.Groups["hora"].Value}", CultureInfo.GetCultureInfo("es-AR"), DateTimeStyles.None, out var momento))
                {
                    yield return new Evento(momento, @base, usuario, serie, nombrePc, ma.Groups["resto"].Value.Trim());
                }
            }
        }
    }

    private enum Estado { EsperandoCabecera, EsperandoPc, EsperandoAccion }
}
