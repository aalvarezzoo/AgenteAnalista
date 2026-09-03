using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;

namespace LogsMcp;

/// <summary>Lee un archivo <c>.evtx</c> (export del Visor de eventos de Windows) directamente del
/// disco, sin depender de que el evento siga existiendo en el visor en vivo de esa PC.</summary>
[SupportedOSPlatform("windows")]
public static class EventosWindowsReader
{
    public sealed record Evento(DateTime Momento, string Nivel, string Proveedor, int Id, string Mensaje);

    public static List<Evento> Leer(string rutaEvtx, DateTime? desde, DateTime? hasta, string? nivel, int limite)
    {
        var query = new EventLogQuery(rutaEvtx, PathType.FilePath);
        using var reader = new EventLogReader(query);

        var eventos = new List<Evento>();
        EventRecord? rec;
        while ((rec = reader.ReadEvent()) is not null)
        {
            using (rec)
            {
                var momento = rec.TimeCreated;
                if (momento is null) continue;
                if (desde is not null && momento < desde) continue;
                if (hasta is not null && momento > hasta) continue;

                var nivelTexto = rec.LevelDisplayName ?? rec.Level?.ToString() ?? "";
                if (nivel is not null && nivelTexto.IndexOf(nivel, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string mensaje;
                try { mensaje = rec.FormatDescription() ?? "[sin descripción]"; }
                catch { mensaje = "[no se pudo formatear el mensaje — falta el proveedor/manifiesto en esta máquina]"; }

                eventos.Add(new Evento(momento.Value, nivelTexto, rec.ProviderName ?? "", rec.Id, mensaje));
                if (eventos.Count >= limite) break;
            }
        }
        return eventos;
    }
}
