---
name: mcp-tools-desarrollo
description: Gotchas a tener en cuenta al escribir o modificar una tool en cualquier MCP server de este repo (excepciones que no llegan al modelo, etc.). Usar ANTES de escribir código nuevo para un MCP server de AgenteAnalista, o al revisar por qué un error de una tool le llega a Claude como mensaje genérico.
---

# Gotchas al desarrollar tools de MCP en este repo

## El SDK (`ModelContextProtocol` paquete NuGet) sanitiza las excepciones .NET comunes

Confirmado probando `SqlDiagnosticoMcp` end-to-end: si una tool tira `InvalidOperationException`
(o cualquier excepción que no sea `ModelContextProtocol.McpException`), el SDK la sanitiza antes de
devolverla — el modelo recibe un mensaje genérico ("An error occurred invoking 'x'.") sin el texto
descriptivo real, aunque el código haya escrito un mensaje claro. Confirmado revisando el XML de
documentación del paquete (`ModelContextProtocol.Core.xml`): `McpException` es, a propósito, el
único tipo cuyo `.Message` se propaga tal cual al cliente MCP — para cualquier otra excepción es
comportamiento de diseño del SDK, no un bug del lado nuestro.

**Consecuencia práctica:** cualquier tool nueva (en este o futuros MCP de este repo) que tire una
excepción esperando que el mensaje llegue al modelo tiene que tirar `McpException`, no
`InvalidOperationException` u otra genérica — o envolver el cuerpo del método en un `try/catch`
que reconvierta cualquier excepción capturada en `McpException(ex.Message, ex)`.

**Patrón ya aplicado en este repo** (copiar este criterio en cualquier tool nueva):
- `SqlDiagnosticoMcp/SqlDiagnosticoTools.cs` — helper privado `Envolver(Func<string> accion)`
  (versión sync).
- `DragonfishApiMcp/DragonfishApiTools.cs`, `ZNubeEcommerceMcp/ZNubeEcommerceTools.cs` y
  `ZlApiMcp/ZlApiTools.cs` — mismo helper, versión async (`Envolver(Func<Task<string>> accion)`),
  porque esas tools son `async Task<string>`.
- `GestionBackupsMcp/GestionBackupsTools.cs` — ese archivo ya venía con un estilo distinto
  (atrapar la excepción y devolver un string descriptivo directamente, en vez de dejarla escapar y
  envolverla) — funciona igual de bien siempre que **ningún** camino dentro de una tool pública dé
  lugar a que una excepción se escape sin pasar por un `catch`. Si se sigue ese estilo en vez del
  wrapper `Envolver`, revisar que TODO el cuerpo de la tool pública esté cubierto por el try/catch,
  no solo la parte "principal" (un bug real que se corrigió acá: `RestaurarUno` llamaba a
  `Process.Start` fuera de cualquier try/catch).

**Cuidado con capas más internas que atrapan y "convierten en null" cualquier error, no solo
excepciones que escapan sin envolver.** En `ZlApiMcp/ZlApiClient.cs` había un bug más grave que el
de sanitización: `GetAsync`/`GetListAsync` atrapaban **cualquier** excepción o código HTTP de error
(401, 500, timeout, lo que sea) y devolvían `default`/lista vacía — como la tool interpreta `null`
como "no encontrado", un error real de credenciales o de red se veía idéntico a "no existe". La
corrección: solo un 404 real es "no encontrado"; cualquier otro código de error o excepción se deja
propagar (para que `Envolver` la convierta en `McpException` con el detalle real). Revisar esto en
cualquier capa de cliente HTTP nueva, no solo en la tool que llama al SDK de MCP directamente.

Los helpers de soporte (`ConexionHelper`, `ConsultaSqlValidator`, `EmpHelper`, `SwaggerCatalog`,
`ResolverPerfil`) también tiran `McpException` directamente en sus propios `throw`, no
`InvalidOperationException` — así quedan correctos por sí solos, sin depender de que el llamador
los envuelva.

## Cómo probarlo de verdad, no solo asumir que el SDK se comporta como el código sugiere

No alcanza con revisar el código — este bug se descubrió recién al probar tools reales contra el
protocolo MCP (no simulando con SQL/HTTP directo). Si se agrega o modifica una tool y hay dudas
sobre el manejo de errores, hablar el protocolo JSON-RPC directamente contra el `.dll` compilado
(un script chico que hace `initialize` → `notifications/initialized` → `tools/call`, leyendo
stdout línea por línea) es la forma más rápida de confirmar qué le llega realmente al modelo,
en vez de asumirlo por lectura de código.
