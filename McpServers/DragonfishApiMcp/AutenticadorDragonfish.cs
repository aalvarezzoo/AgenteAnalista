using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;

namespace DragonfishApiMcp;

/// <summary>
/// La API de Dragonfish no acepta los headers IdCliente/Authorization en cualquier llamada por sí
/// solos — primero hay que llamar a POST /Autenticar con {"IdCliente", "JWToken"} en el body.
/// Confirmado en la práctica: sin este paso, cualquier otra llamada (incluso con credenciales
/// válidas y no vencidas) devuelve 401 "Cliente no autenticado". Una vez autenticado, el servidor
/// acepta el mismo Authorization en llamadas posteriores de cualquier conexión/proceso — no hace
/// falta repetir /Autenticar en cada request, solo la primera vez (o de nuevo si el servicio de
/// Dragonfish se reinició y perdió esa sesión).
/// </summary>
public sealed class AutenticadorDragonfish(HttpClient http)
{
    private readonly ConcurrentDictionary<string, Task> _autenticado = new();

    public Task AsegurarAutenticadoAsync(DragonfishPerfil p) =>
        _autenticado.GetOrAdd(Clave(p), _ => AutenticarAsync(p));

    /// <summary>Descarta la autenticación cacheada para este perfil, para forzar un /Autenticar
    /// nuevo en el próximo intento (ej. tras un 401 inesperado en una llamada posterior).</summary>
    public void Invalidar(DragonfishPerfil p) => _autenticado.TryRemove(Clave(p), out _);

    private static string Clave(DragonfishPerfil p) => $"{p.BaseUrl}|{p.IdCliente}";

    private async Task AutenticarAsync(DragonfishPerfil p)
    {
        var url = $"{p.BaseUrl.TrimEnd('/')}/Autenticar";
        var body = JsonSerializer.Serialize(new { IdCliente = p.IdCliente, JWToken = p.Authorization });

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        using var resp = await http.SendAsync(req);

        if (!resp.IsSuccessStatusCode)
            throw new McpException(
                $"No se pudo autenticar contra Dragonfish (perfil con IdCliente '{p.IdCliente}'): HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}. "
                + "Revisar que el Authorization guardado sea el token vigente de 'Obtener Token' en la pantalla Cliente REST API.");
    }
}
