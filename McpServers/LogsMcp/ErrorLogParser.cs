using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LogsMcp;

/// <summary>Parsea el formato de <c>log.err</c>: mismo bloque de cabecera (fecha/Base/Usuario/
/// Serie + línea de PC) que <see cref="SesionLogParser"/>, pero después de "hora," viene un bloque
/// de detalle en VARIAS líneas (Programa/Procedimiento/Nº Error/Message/Stack) en vez de un
/// mensaje de una sola línea. Se junta todo como "Detalle" crudo hasta el próximo encabezado, sin
/// intentar descomponer cada campo — confirmado en la práctica que el formato interno de ese
/// bloque varía (a veces viene indentado y recortado para el mismo error), así que separarlo campo
/// por campo sería frágil.</summary>
public static partial class ErrorLogParser
{
    [GeneratedRegex(@"^\s*(?<hora>\d{1,2}:\d{2}:\d{2}),\s*(?<resto>.*)$")]
    private static partial Regex LineaHora();

    public sealed record Evento(DateTime Momento, string Base, string Usuario, string Serie, string NombrePc, string Detalle);

    public static List<Evento> Parsear(IEnumerable<string> lineas)
    {
        var eventos = new List<Evento>();
        string? fecha = null;
        string @base = "", usuario = "", serie = "", nombrePc = "";
        var esperandoPc = false;

        DateTime? momento = null;
        var detalle = new StringBuilder();

        void Flush()
        {
            if (momento is not null)
                eventos.Add(new Evento(momento.Value, @base, usuario, serie, nombrePc, detalle.ToString().Trim()));
            detalle.Clear();
            momento = null;
        }

        foreach (var linea in lineas)
        {
            var mc = EncabezadoLog.Cabecera().Match(linea);
            if (mc.Success)
            {
                Flush();
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

            if (fecha is null) continue;

            if (momento is null)
            {
                var mh = LineaHora().Match(linea);
                if (mh.Success && DateTime.TryParse($"{fecha} {mh.Groups["hora"].Value}", CultureInfo.GetCultureInfo("es-AR"), DateTimeStyles.None, out var nuevoMomento))
                {
                    momento = nuevoMomento;
                    if (!string.IsNullOrWhiteSpace(mh.Groups["resto"].Value))
                        detalle.AppendLine(mh.Groups["resto"].Value.Trim());
                }
                continue;
            }

            detalle.AppendLine(linea);
        }
        Flush();
        return eventos;
    }
}
