using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace ZlApiMcp;

[McpServerToolType]
public sealed class ZlApiTools(ZlApiClient client)
{
    [McpServerTool(Name = "get_tarea")]
    [Description("Obtiene una tarea de ZL por número (entidad Tareas, path /Tareas/).")]
    public async Task<string> GetTarea(
        [Description("Número de la tarea en ZL")] int numero)
    {
        var tarea = await client.GetTareaAsync(numero);
        return tarea is null
            ? $"Tarea {numero} no encontrada."
            : JsonSerializer.Serialize(tarea);
    }

    [McpServerTool(Name = "get_incidente")]
    [Description("Obtiene un incidente de ZL por número (entidad mdaincmda, path /mdaincmda/).")]
    public async Task<string> GetIncidente(
        [Description("Número del incidente en ZL")] int numero)
    {
        var incidente = await client.GetIncidenteAsync(numero);
        return incidente is null
            ? $"Incidente {numero} no encontrado."
            : JsonSerializer.Serialize(incidente);
    }

    [McpServerTool(Name = "buscar_tareas_pendientes")]
    [Description("Busca tareas de ZL sin cierre (numCIERRE=0) asignadas a alguno de los owners dados.")]
    public async Task<string> BuscarTareasPendientes(
        [Description("Usuarios owner a buscar, ej: [\"AALVAREZ\", \"JINIGUEZ\", \"DPIERCAMILLI\"]")] string[] owners)
    {
        var tareas = await client.BuscarTareasPendientesAsync(owners);
        return JsonSerializer.Serialize(tareas);
    }
}
