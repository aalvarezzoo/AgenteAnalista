---
name: configurar-perfil-dragonfish-api
description: Paso a paso para dar de alta o reconfigurar un perfil de DragonfishApiMcp (instancia SQL, IdCliente, Token) cuando alguien pide "usar la API de Dragonfish" y no hay ningún perfil que funcione. Incluye el gotcha de rebuild+reload antes de probar.
---

# DragonfishApiMcp — cómo configurar un perfil

Esto es de **uso interno de MasterHelp**, no para clientes — la API que se usa es la instalación
propia de quien esté trabajando (hoy, la de AALVAREZ en local), no la de un cliente real. Una
instalación de Dragonfish sirve para cualquier base de esa instancia — es única por instalación,
no por base de datos.

## Paso 0 — Probar antes de asumir que un perfil funciona

Cuando alguien pida usar la API, **por default asumir el perfil `TEST`** (no preguntar cuál) —
pero **nunca asumir que funciona solo porque `listar_perfiles` lo devuelve en la lista**. Probarlo
primero con una llamada real y liviana (ej. `consultar` sobre `Articulo` con `limit:1`). Si falla
(401, timeout, conexión rechazada), tratarlo como si no existiera — explicar qué pasó y pasar al
Paso 1, en vez de dejar que el error crudo de la API confunda.

Si el perfil existe pero está vacío (`BaseUrl`/`IdCliente`/`Authorization` en blanco), es lo mismo
que "no existe" — nunca se terminó de configurar. Seguir con los pasos de acá.

## Paso 1 — Si no hay perfil que funcione: elegir la instancia SQL de una lista, no pedir que la tipeen

Se necesita saber en qué instancia de SQL Server vive `DRAGONFISH_ZOOLOGICMASTER` para poder
buscar host/puerto/base de datos (ver "Bases de datos SQL Server" en CLAUDE.md). Ese nombre varía
por máquina — no hay que pedirle a la persona que lo escriba a mano (dato "de su entorno", fácil
de errar). En cambio, listar las instancias SQL instaladas en esa máquina y que elija de ahí:

```powershell
Get-Service -Name "MSSQL*" | Where-Object Status -eq Running
```

El nombre de instancia sale del nombre del servicio: `MSSQL$SQLEXPRESS2022` → instancia
`.\SQLEXPRESS2022`; `MSSQLSERVER` (sin `$`) → instancia por defecto `.`. Presentar las opciones
encontradas para que la persona elija una (ahí sí tiene sentido preguntar con opciones, a
diferencia del Paso 2).

## Paso 2 — Pedir IdCliente y Token (nada de credenciales para firmar nada)

`agregar-perfil` **no reimplementa la firma del token** — hubo una versión anterior que sí
(replicaba `getJWT.exe`, la herramienta previa al botón "Obtener Token" de Dragonfish) y se sacó
por frágil: cualquier detalle de la firma que no coincidiera byte a byte con lo que Dragonfish
esperaba internamente daba 401 sin pista real del motivo (se perdió una sesión entera cazando esos
bugs). Ahora se usa el token tal cual lo entrega Dragonfish — cero riesgo de desincronización con
su algoritmo.

Pedirle a la persona que en la pantalla "Cliente REST API" de Dragonfish (la misma donde ya creó o
está por crear el cliente) apriete **"Obtener Token"** y pegue acá el `IdCliente` y el token
resultante. Bloque libre, sin herramienta de preguntas con opciones (son datos libres/secretos):

```
IdCliente:  [                                    ]
             → el "Código" de la pantalla Cliente REST API de Dragonfish

Token:      [                                    ]
             → el que da el botón "Obtener Token" de esa misma pantalla
```

No preguntar el nombre de `Perfil` — el equipo usa una sola API/servicio de prueba, así que por
default es `TEST`. Solo se usa otro nombre si la persona menciona explícitamente que está
configurando un cliente distinto (ej. "creé otro cliente para probar X, ¿cómo lo uso?") — ahí sí
correspondería un perfil nuevo con otro nombre. `agregar-perfil` sobrescribe por nombre, no
duplica.

## Paso 3 — Ejecutar y confirmar

```
dotnet McpServers/DragonfishApiMcp/bin/Debug/net10.0/DragonfishApiMcp.dll agregar-perfil <instanciaSQL> <perfil> <idCliente> <token>
```

Corrido desde la raíz del repo. **Nunca mostrar** el token en la respuesta — confirmar solo
perfil, `IdCliente` y `BaseUrl` encontrado.

El "Servicio REST API" y el "Cliente REST API" en Dragonfish se siguen creando a mano en la
pantalla de Dragonfish — eso no se automatiza. Este flujo solo evita transcribir host/puerto/base
de datos a mano (los busca por SQL a partir del `IdCliente`).

## Paso 4 — Rebuild + reiniciar el MCP antes de probar (si no, el 401 engaña)

`agregar-perfil` escribe el perfil nuevo en `appsettings.secrets.enc` **relativo al directorio
desde donde se corre el comando** (la raíz del repo, si se sigue esta guía). Pero el servidor MCP
`dragonfish-api` que ya está corriendo en la sesión lee esa misma ruta relativa a
`AppContext.BaseDirectory` (la carpeta del `.dll`,
`McpServers/DragonfishApiMcp/bin/Debug/net10.0/`) — **son dos archivos físicos distintos**. Ese
bin-copy solo se sincroniza con el de la raíz en build (vía el `Content Include` del csproj), no
al correr `agregar-perfil`.

Consecuencia: si se prueba el perfil solo recargando VS Code (sin rebuild antes), el MCP sigue
leyendo la copia vieja del bin y devuelve 401 — un 401 que **no dice nada sobre si el perfil nuevo
está bien o mal**, porque ni siquiera lo está usando todavía.

Pasos completos después de correr `agregar-perfil` (alta o reconfiguración de un perfil
existente):

1. **Chequear que no haya un proceso `DragonfishApiMcp.dll` huérfano** de un reload anterior (cada
   reload de VS Code parece lanzar uno nuevo sin matar el anterior):
   ```powershell
   Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*DragonfishApiMcp.dll*" -and $_.Name -eq "dotnet.exe" } | Select-Object ProcessId
   ```
   Si aparece más de uno (o incluso uno solo), matarlo(s) con `Stop-Process -Id <pid> -Force` — el
   rebuild del paso siguiente falla con "archivo bloqueado" (MSB3021/MSB3026) si el proceso sigue
   vivo, porque tiene su propia copia del `.dll` cargada en memoria. Este mismo problema (proceso
   huérfano bloqueando el rebuild) aplica a **cualquier** MCP de este repo, no solo a
   DragonfishApiMcp — mismo síntoma, mismo fix.
2. **Rebuildear** para que MSBuild vuelva a copiar el `appsettings.secrets.enc` de la raíz al bin
   de salida:
   ```
   dotnet build McpServers/DragonfishApiMcp
   ```
3. **Recargar la ventana de VS Code** (`Developer: Reload Window`, o cerrar y volver a abrir) —
   recién ahí el MCP arranca leyendo el archivo correcto.

Si después de estos tres pasos sigue en 401, ahí sí vale investigar credenciales/token — antes de
eso, no es diagnóstico.
