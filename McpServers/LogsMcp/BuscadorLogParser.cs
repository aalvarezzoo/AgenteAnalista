using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LogsMcp;

/// <summary>Parsea el formato de <c>OperacionesDelBuscador.log</c>: entradas que arrancan con
/// "fecha hora - mensaje" seguidas de N líneas de detalle/stack trace, separadas por líneas de
/// guiones. Función pura, testeable sin archivos reales (mismo criterio que
/// <see cref="OperacionesLogParser"/>).</summary>
public static partial class BuscadorLogParser
{
    [GeneratedRegex(@"^(?<fecha>\d{1,2}/\d{1,2}/\d{4})\s+(?<hora>\d{1,2}:\d{2}:\d{2})\s*-\s*(?<resto>.*)$")]
    private static partial Regex Encabezado();

    public sealed record Evento(DateTime Momento, string Mensaje, string Detalle);

    public static List<Evento> Parsear(IEnumerable<string> lineas)
    {
        var eventos = new List<Evento>();
        DateTime? momento = null;
        var mensaje = "";
        var detalle = new StringBuilder();

        void Flush()
        {
            if (momento is not null)
                eventos.Add(new Evento(momento.Value, mensaje, detalle.ToString().Trim()));
        }

        foreach (var linea in lineas)
        {
            if (linea.Length > 0 && linea.All(c => c == '-'))
                continue; // separador, no aporta

            var m = Encabezado().Match(linea);
            if (m.Success &&
                DateTime.TryParse($"{m.Groups["fecha"].Value} {m.Groups["hora"].Value}", CultureInfo.GetCultureInfo("es-AR"), DateTimeStyles.None, out var nuevoMomento))
            {
                Flush();
                momento = nuevoMomento;
                mensaje = m.Groups["resto"].Value.Trim();
                detalle.Clear();
            }
            else if (momento is not null)
            {
                detalle.AppendLine(linea);
            }
        }
        Flush();
        return eventos;
    }
}
