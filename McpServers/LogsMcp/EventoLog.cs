namespace LogsMcp;

/// <summary>Forma común de un evento, sin importar de qué log/formato salió — lo que permite
/// mezclar varias fuentes en una sola línea de tiempo ordenada por <see cref="Momento"/>.</summary>
public sealed record EventoLog(DateTime Momento, string Fuente, string Resumen, string? Detalle);
