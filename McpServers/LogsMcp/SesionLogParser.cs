using System.Globalization;
using System.Text.RegularExpressions;

namespace LogsMcp;

/// <summary>Parsea el formato "de sesión" compartido por <c>operaciones.log</c> y
/// <c>ZOOSESSION.log</c>: cabecera (fecha/Base/Usuario/Serie) + línea de PC, seguidas de UNA O MÁS
/// líneas "hora,mensaje" — confirmado en la práctica que ZOOSESSION.log puede tener varias líneas
/// de mensaje bajo un mismo encabezado (ej. varios pasos de una importación), mientras que
/// operaciones.log típicamente repite el encabezado por cada acción — el parser soporta ambos
/// casos sin asumir cuál es. Función pura, testeable sin archivos reales.</summary>
public static partial class SesionLogParser
{
    [GeneratedRegex(@"^\s*(?<hora>\d{1,2}:\d{2}:\d{2}),\s*(?<resto>.*)$")]
    private static partial Regex LineaAccion();

    public sealed record Evento(DateTime Momento, string Base, string Usuario, string Serie, string NombrePc, string Mensaje);

    public static IEnumerable<Evento> Parsear(IEnumerable<string> lineas)
    {
        string? fecha = null;
        string @base = "", usuario = "", serie = "", nombrePc = "";
        var esperandoPc = false;

        foreach (var linea in lineas)
        {
            var mc = EncabezadoLog.Cabecera().Match(linea);
            if (mc.Success)
            {
                fecha = mc.Groups["fecha"].Value;
                @base = mc.Groups["base"].Value.Trim();
                usuario = mc.Groups["usuario"].Value.Trim();
                serie = mc.Groups["serie"].Value.Trim();
                esperandoPc = true;
                continue;
            }

            if (esperandoPc)
            {
                var mp = EncabezadoLog.LineaPc().Match(linea);
                if (mp.Success)
                {
                    nombrePc = mp.Groups["pc"].Value.Trim();
                    esperandoPc = false;
                }
                continue;
            }

            if (fecha is null) continue; // todavía no vimos ningún encabezado — línea rara al principio del archivo

            var ma = LineaAccion().Match(linea);
            if (ma.Success && DateTime.TryParse($"{fecha} {ma.Groups["hora"].Value}", CultureInfo.GetCultureInfo("es-AR"), DateTimeStyles.None, out var momento))
                yield return new Evento(momento, @base, usuario, serie, nombrePc, ma.Groups["resto"].Value.Trim());
            // si no matchea (línea en blanco, etc.) se ignora — seguimos esperando más líneas de
            // mensaje bajo el mismo encabezado, o el próximo encabezado.
        }
    }
}
