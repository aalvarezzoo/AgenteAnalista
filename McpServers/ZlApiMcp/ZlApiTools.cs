using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ZlApiMcp;

[McpServerToolType]
public sealed class ZlApiTools(ZlApiClient client)
{
    /// <summary>Ver el mismo helper en los demás MCP de este repo (skill mcp-tools-desarrollo) —
    /// el SDK de MCP sanitiza cualquier excepción que no sea McpException a un mensaje genérico
    /// antes de devolvérsela al modelo.</summary>
    private static async Task<string> Envolver(Func<Task<string>> accion)
    {
        try
        {
            return await accion();
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "get_tarea")]
    [Description("Obtiene una tarea de ZL por número (entidad Tareas, path /Tareas/).")]
    public Task<string> GetTarea(
        [Description("Número de la tarea en ZL")] int numero) => Envolver(async () =>
    {
        var tarea = await client.GetTareaAsync(numero);
        return tarea is null
            ? $"Tarea {numero} no encontrada."
            : JsonSerializer.Serialize(tarea);
    });

    [McpServerTool(Name = "get_incidente")]
    [Description("Obtiene un incidente de ZL por número (entidad mdaincmda, path /mdaincmda/).")]
    public Task<string> GetIncidente(
        [Description("Número del incidente en ZL")] int numero) => Envolver(async () =>
    {
        var incidente = await client.GetIncidenteAsync(numero);
        return incidente is null
            ? $"Incidente {numero} no encontrado."
            : JsonSerializer.Serialize(incidente);
    });

    [McpServerTool(Name = "buscar_tareas_pendientes")]
    [Description("Busca tareas de ZL sin cierre (numCIERRE=0) asignadas a alguno de los owners dados.")]
    public Task<string> BuscarTareasPendientes(
        [Description("Usuarios owner a buscar, ej: [\"AALVAREZ\", \"JINIGUEZ\", \"DPIERCAMILLI\"]")] string[] owners) => Envolver(async () =>
    {
        var tareas = await client.BuscarTareasPendientesAsync(owners);
        return JsonSerializer.Serialize(tareas);
    });
}
