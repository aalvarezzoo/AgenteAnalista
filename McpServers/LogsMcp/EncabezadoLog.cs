using System.Text.RegularExpressions;

namespace LogsMcp;

/// <summary>Cabecera + línea de PC compartidas por los logs de Dragonfish que arman un bloque
/// "fecha, Base/Usuario/Serie" seguido de una línea de "Nombre de la PC" antes de cada evento —
/// mismo formato en operaciones.log, ZOOSESSION.log y log.err. Extraído para no duplicar el mismo
/// regex frágil en cada parser.</summary>
public static partial class EncabezadoLog
{
    [GeneratedRegex(@"^(?<fecha>\d{1,2}/\d{1,2}/\d{4}),\s*Base:\s*(?<base>[^,]*),\s*Usuario:\s*(?<usuario>[^,]*),\s*Aplicaci[oó]n:\s*(?<app>[^,]*),\s*Versi[oó]n:\s*(?<version>[^,]*),\s*Serie:\s*(?<serie>.*)$")]
    public static partial Regex Cabecera();

    [GeneratedRegex(@"Nombre de la PC:\s*(?<pc>[^,]*),")]
    public static partial Regex LineaPc();
}
