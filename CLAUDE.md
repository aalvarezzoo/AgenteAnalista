# AgenteAnalista

Repo de las "habilidades" técnicas (MCP servers) y el conocimiento de dominio que un agente de
Claude Code usa para analizar y resolver incidentes de MasterHelp de punta a punta — separado de
[PanelMasterHelp](https://github.com/Jiniguezzoo/PanelMasterHelp) (el portal Blazor de gestión de
incidentes), que es un producto distinto con su propio ciclo de vida. Este repo no tiene UI propia;
lo que expone son MCP servers que Claude Code carga al abrir la carpeta.

## Equipo

**MasterHelp** es un equipo de IyD dentro de ZooLogic que recibe incidentes escalados desde la Mesa
de Ayuda SAL. Integrantes: JINIGUEZ, AALVAREZ, DPIERCAMILLI.

Los incidentes llegan asignados a "MASTERHELP" en el sistema ZL. El equipo los analiza, resuelve y
cierra.

## Flujo de trabajo habitual

1. Los incidentes llegan con tarea asignada a MASTERHELP en ZL.
2. El equipo decide quién toma cada uno.
3. Se analiza y resuelve. Puede implicar queries SQL en la base del cliente, acceso remoto vía
   AnyDesk, coordinación con técnicos de SAL.
4. Al resolver: se cierra la tarea de MASTERHELP y se abre una nueva para el técnico de SAL con las
   instrucciones.

## SSRS — consulta directa de incidentes

El reporte de incidentes se puede consultar directamente sin el portal usando credenciales Windows
(NTLM):

```powershell
$url = "http://reportes03/ReportServer?/IyD/Iyd%20incidentes&rs:Command=Render&rs:Format=XML&rs:ClearSession=True&DuenoTarea=MASTERHELP&Estado=Pendientes"
$client = New-Object System.Net.WebClient
$client.UseDefaultCredentials = $true
$xml = $client.DownloadString($url)
```

**Namespace XML del reporte:** `Iyd_x0020_incidentes`
**Elemento por incidente:** `Details4` → `Details6` (tareas)

Para cerrados agregar: `&Estado=Cerrados&FechaTareaCierreDesde=MM/DD/YYYY&FechaTareaCierreHasta=MM/DD/YYYY`

**Ojo con reportes grandes (rango de Cerrados amplio):**
- `System.Net.WebClient.DownloadString` puede tardar más de un minuto en descargar — no asumir que
  colgó; usar `System.Net.Http.HttpClient` con `Timeout = TimeSpan.FromMinutes(5)` si el `WebClient`
  da timeout.
- Para recorrer el XML, **nunca** anidar `GetElementsByTagName` (un `foreach` de `Details4` con un
  `GetElementsByTagName("Details6")` adentro) — cada llamada re-escanea el subárbol entero y con
  cientos de incidentes se vuelve extremadamente lento (varios minutos, puede parecer colgado).
  Usar `SelectNodes("//*[local-name()='Details6'][@Tarea_Asignada_A1='MASTERHELP']")` una sola vez y
  subir al padre con `.ParentNode` para llegar al `Details4` correspondiente — milisegundos en vez de
  minutos.
- La cadena de ancestros desde un `Details6` que matchea no es siempre la misma cantidad de saltos:
  en el reporte de "Cerrados" hay un nivel intermedio extra (`Details6 → Details6_Collection →
  Tablix21 → Details4`) que no aparece igual en "Pendientes". Si `GetAttribute` en el padre devuelve
  vacío, imprimir la cadena completa de `.Name` subiendo por `.ParentNode` para confirmar cuántos
  saltos hacen falta antes de asumir un bug en el attribute.

## Bases de datos SQL Server

Las bases de clientes siempre tienen el prefijo `DRAGONFISH_` seguido del nombre de la base.

**Regla:** si alguien escribe el nombre de la base con el prefijo ya incluido (`DRAGONFISH_ALGO`),
usarlo tal cual. Si escribe solo el nombre corto (`ALGO` o `I AM`), agregar el prefijo:
`DRAGONFISH_ALGO` / `[DRAGONFISH_I AM]`. Nunca agregar el prefijo dos veces.

Los nombres con espacios van entre corchetes: `[DRAGONFISH_I AM]`.

### Esquema

Las tablas de clientes viven en el esquema `ZooLogic`. Ruta completa para UPDATEs:
```
[DRAGONFISH_NOMBRE].[ZooLogic].[TABLA]
```

Config de infraestructura (no de negocio de un cliente puntual) vive aparte, en
`DRAGONFISH_ZOOLOGICMASTER`, esquema `[ORGANIZACION]` — ahí están por ejemplo `SERVREST`/`SECRETREST`
(config de Servicio/Cliente REST API).

### Convenciones generales del esquema de Dragonfish

Útil para orientarse en cualquier tabla nueva, no solo las ya documentadas acá:

- **Bloque de auditoría, se repite en casi toda tabla:** `FALTAFW`/`FMODIFW` (fecha alta/modificación),
  `HALTAFW`/`HMODIFW` (hora), `UALTAFW`/`UMODIFW` (usuario), `BDALTAFW`/`BDMODIFW` (base de datos),
  `SALTAFW`/`VALTAFW` (serie/versión de Dragonfish), `FECEXPO`/`FECIMPO`/`HORAEXPO`/`HORAIMPO`/`ESTTRANS`
  (transferencia entre bases), `ZADSFW` (acciones del sistema), `TIMESTAMP` (rowversion interno de SQL
  Server). Ninguno de estos se modifica manualmente — es trazabilidad interna. Cuando una fila nueva
  se inserta fuera del flujo normal de Dragonfish (ej. un alta hecha por script), estos campos quedan
  vacíos/en su valor de sentinela — es una decisión consciente, no un olvido (ver sección
  `GestionBackupsMcp` más abajo, caso `dar_alta_base_para_restore`).
- **Los `Codigo` internos son GUIDs/hex largos generados por el servidor** (ej.
  `1F685F0F218B7C1409A1B26910648529330281`) — nunca inventarlos ni construirlos a mano, ni al armar un
  body para la API ni al escribir SQL.
- **El nombre de tabla suele ser una abreviatura de la entidad de negocio**, no siempre obvia:
  `ART`=Artículo, `XVAL`=valores (medios de pago), `MSTOCK`/`DETMSTOCK`=Movimiento de stock
  (cabecera/detalle), `COMPROBANTEV`=comprobantes de venta, `SERVREST`/`SECRETREST`=Servicio/Cliente
  REST API, `REMCOMPRA`=Remito de compra, `EMP`=registro de bases de datos conocidas por la instalación
  (ver `GestionBackupsMcp`).
- **Los nombres de campo del JSON de la API REST no corresponden 1:1 a los nombres de columna SQL** —
  son convenciones de nombrado completamente distintas (ej. el campo de API `ValoresDetalle[].Tipo` no
  es lo mismo que ninguna columna obvia de `XVAL`). Nunca asumir la correspondencia por parecido de
  nombre — siempre cruzar contra un registro real (por API con `consultar`, o por SQL si hace falta más
  detalle) antes de usarlo en un alta. Esta misma regla aplica al armar un INSERT a mano contra
  cualquier tabla de Dragonfish: cruzar contra una fila real creada por el propio sistema antes de
  asumir qué columnas hacen falta y qué valores llevan (ver el caso de `EMP` más abajo, donde asumir
  mal produjo dos campos incorrectos hasta comparar contra una fila real).

## Tabla COMPROBANTEV — consideraciones al hacer cambios

Antes de ejecutar cualquier UPDATE sobre `COMPROBANTEV`, analizar todos los campos de la tabla
relevantes para la operación y evaluar si el cambio puede generar inconsistencias en otros campos
relacionados. No limitarse al campo pedido — pensar si hay campos dependientes que quedarían
desincronizados.

### Filtros obligatorios al operar sobre comprobantes

Siempre filtrar por `FACTTIPO` además de `FLETRA` y `FPTOVEN`. El mismo número de comprobante puede
existir varias veces con distintos tipos. Sin el filtro de `FACTTIPO` se pueden afectar registros que
no corresponden.

### Valores conocidos de FACTTIPO

| FACTTIPO | Tipo de comprobante |
|----------|---------------------|
| 1        | Factura (versiones anteriores de Dragonfish) |
| 27       | Factura Electrónica (versión 15.x+) |
| 28       | Nota de Crédito Electrónica (versión 15.x+) |

> Esta tabla se va completando a medida que se identifican nuevos valores en distintas bases y
> versiones.

### Campos de fecha en COMPROBANTEV

| Campo     | Qué es | ¿Modificar? |
|-----------|--------|-------------|
| `FFCH`    | Fecha del comprobante — la que lee AFIP para el CAE | Sí, cuando se pide cambiar fecha |
| `FALTAFW` | Fecha de alta en el sistema (auditoría) | No — es trazabilidad interna |
| `FMODIFW` | Fecha de última modificación (auditoría) | No — es trazabilidad interna |
| `HALTAFW` | Hora de alta (auditoría) | No |
| `HMODIFW` | Hora de modificación (auditoría) | No |
| `FPAGO`   | Fecha de pago | Solo si corresponde al pedido |

### Regla AFIP — ventana de 5 días para el CAE

Para comprobantes electrónicos (FACTTIPO 27/28), AFIP solo otorga CAE dentro de los 5 días calendario
**contados desde la fecha del comprobante (`FFCH`)** — no desde el momento en que se solicita el CAE.

Ejemplo: si `FFCH` = 26/08, se puede seguir pidiendo CAE hasta el 31/08 (5º día). Al llegar el 01/09
(6º día) AFIP ya rechaza la solicitud.

Al cambiar `FFCH` en un comprobante electrónico, verificar que la nueva fecha permita seguir estando
dentro de esta ventana si todavía no se emitió el CAE.

**CAI ≠ CAE — no confundir:**
- **CAI** (Código de Autorización de Impresión): para facturas de papel/impresas.
- **CAE** (Código de Autorización Electrónica): para comprobantes electrónicos (FACTTIPO 27/28).

## Código fuente de Dragonfish

El código fuente de Dragonfish está en `C:\IADragon2028` — es un proyecto/repo aparte, distinto de
este. Sirve para entender el funcionamiento interno de algo de Dragonfish que no esté documentado y
no se pueda inferir con confianza solo probando (ej. flags de CLI de `ZooBkp.exe`, qué hace
exactamente el alta de una base nueva, cómo se resuelve el esquema de una tabla en runtime, etc.) —
en vez de ir a ciegas por prueba y error.

**Importante:** revisarlo solo cuando la persona lo pida o lo sugiera explícitamente — no ir por
iniciativa propia sin que se pida.

---

# MCP servers (`McpServers/`) — primera vez que se levantan en una máquina nueva

El repo trae tres servidores MCP: `ZlApiMcp`, `DragonfishApiMcp` y `GestionBackupsMcp`. Los dos
últimos están registrados en `.mcp.json`, que Claude Code levanta solo al abrir el proyecto.
`DragonfishApiMcp` necesita estos pasos la primera vez en una máquina que nunca los corrió — si no,
falla al arrancar (a propósito, con un error claro en vez de fallar en silencio):

1. **Generar tu propia clave de cifrado** (no se comparte entre integrantes del equipo):
   ```
   dotnet McpServers/DragonfishApiMcp/bin/Debug/net10.0/DragonfishApiMcp.dll generate-key
   ```
   Guardá el hex de 64 caracteres que imprime. (La primera vez hace falta compilar el proyecto antes
   — `dotnet build McpServers/DragonfishApiMcp` — para tener el `.dll`.)

2. **Setearla como variable de entorno persistente** (una sola vez, PowerShell como administrador si
   hace falta):
   ```powershell
   [System.Environment]::SetEnvironmentVariable('AGENTEANALISTA_SECRET_KEY', '<tu-clave>', 'User')
   ```
   Reabrir la terminal/VS Code después de esto para que la vea.

3. **Crear `appsettings.secrets.json`** en la raíz del repo (nunca se commitea, está en
   `.gitignore`) con tus propias credenciales. Ejemplo mínimo:
   ```json
   {
     "ZlApi": { "BaseUrl": "", "IdCliente": "", "Authorization": "", "BaseDeDatos": "" },
     "DragonfishApi": { "Perfiles": {} }
   }
   ```
   Completar `DragonfishApi.Perfiles` con tus propios perfiles si vas a probar contra alguna
   instalación de Dragonfish (ver `McpServers/DragonfishApiMcp/DragonfishApiConfig.cs` para la forma
   exacta).

4. **Cifrarlo:**
   ```
   dotnet McpServers/DragonfishApiMcp/bin/Debug/net10.0/DragonfishApiMcp.dll encrypt
   ```
   Esto genera `appsettings.secrets.enc` en la raíz del repo (tampoco se commitea) — un solo archivo,
   compartido por todos los MCPs que necesiten secretos (cada uno lo copia a su propio `bin/` al
   compilar). Podés borrar el `.json` plano después — el comando `decrypt` lo reconstruye si hace
   falta editarlo más adelante.

5. **Compilar los MCP servers al menos una vez** — `.mcp.json` apunta al `.dll` ya compilado, no a
   `dotnet run`, así que si no se buildea antes, Claude Code no encuentra el ejecutable:
   ```
   dotnet build McpServers/DragonfishApiMcp
   dotnet build McpServers/GestionBackupsMcp
   ```

6. **Reiniciar VS Code** (o recargar la ventana) para que Claude Code levante los servidores del
   `.mcp.json`.

Cada uno gestiona su propia clave y su propio `appsettings.secrets.json` — no hay una clave ni un
archivo de secretos único para todo el equipo.

**Si en el paso 5 (o antes) tira este error:**
```
error MSB3030: No se pudo copiar el archivo "appsettings.secrets.enc" porque no se encontró.
```
Significa que se saltearon el paso 3/4 — hay que crear y cifrar el `appsettings.secrets.json` antes
de compilar.

## DragonfishApiMcp — cómo configurar un perfil cuando alguien pide "usar la API de Dragonfish"

Esto es de **uso interno de MasterHelp**, no para clientes — la API que se usa es la instalación
propia de quien esté trabajando (hoy, la de AALVAREZ en local), no la de un cliente real. Una
instalación de Dragonfish sirve para cualquier base de esa instancia — es única por instalación, no
por base de datos.

### Paso 1 — Probar antes de asumir que un perfil funciona

Cuando alguien pida usar la API, **por default asumir el perfil `TEST`** (no preguntar cuál) — pero
**nunca asumir que funciona solo porque `listar_perfiles` lo devuelve en la lista**. Probarlo primero
con una llamada real y liviana (ej. `consultar` sobre `Articulo` con `limit:1`). Si falla (401,
timeout, conexión rechazada), tratarlo como si no existiera — explicar qué pasó y pasar al Paso 2, en
vez de dejar que el error crudo de la API confunda.

### Paso 2 — Si no hay perfil que funcione: elegir la instancia SQL de una lista, no pedir que la tipeen

Se necesita saber en qué instancia de SQL Server vive `DRAGONFISH_ZOOLOGICMASTER` para poder buscar
host/puerto/base de datos (ver "Bases de datos SQL Server" más arriba). Ese nombre varía por
máquina — no hay que pedirle a la persona que lo escriba a mano (dato "de su entorno", fácil de
errar). En cambio, listar las instancias SQL instaladas en esa máquina y que elija de ahí:

```powershell
Get-Service -Name "MSSQL*" | Where-Object Status -eq Running
```

El nombre de instancia sale del nombre del servicio: `MSSQL$SQLEXPRESS2022` → instancia
`.\SQLEXPRESS2022`; `MSSQLSERVER` (sin `$`) → instancia por defecto `.`. Presentar las opciones
encontradas para que la persona elija una (ahí sí tiene sentido preguntar con opciones, a diferencia
del Paso 3).

### Paso 3 — Pedir IdCliente y Token (nada de credenciales para firmar nada)

`agregar-perfil` **no reimplementa la firma del token** — hubo una versión anterior que sí (replicaba
`getJWT.exe`, la herramienta previa al botón "Obtener Token" de Dragonfish) y se sacó por frágil:
cualquier detalle de la firma que no coincidiera byte a byte con lo que Dragonfish esperaba
internamente daba 401 sin pista real del motivo (se perdió una sesión entera cazando esos bugs). Ahora
se usa el token tal cual lo entrega Dragonfish — cero riesgo de desincronización con su algoritmo.

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
correspondería un perfil nuevo con otro nombre. `agregar-perfil` sobrescribe por nombre, no duplica.

### Paso 4 — Ejecutar y confirmar

```
dotnet McpServers/DragonfishApiMcp/bin/Debug/net10.0/DragonfishApiMcp.dll agregar-perfil <instanciaSQL> <perfil> <idCliente> <token>
```

Corrido desde la raíz del repo. **Nunca mostrar** el token en la respuesta — confirmar solo perfil,
`IdCliente` y `BaseUrl` encontrado.

El "Servicio REST API" y el "Cliente REST API" en Dragonfish se siguen creando a mano en la pantalla
de Dragonfish — eso no se automatiza. Este flujo solo evita transcribir host/puerto/base de datos a
mano (los busca por SQL a partir del `IdCliente`).

### Paso 5 — Rebuild + reiniciar el MCP antes de probar (si no, el 401 engaña)

`agregar-perfil` escribe el perfil nuevo en `appsettings.secrets.enc` **relativo al directorio desde
donde se corre el comando** (la raíz del repo, si se sigue esta guía). Pero el servidor MCP
`dragonfish-api` que ya está corriendo en la sesión lee esa misma ruta relativa a
`AppContext.BaseDirectory` (la carpeta del `.dll`, `McpServers/DragonfishApiMcp/bin/Debug/net10.0/`)
— **son dos archivos físicos distintos**. Ese bin-copy solo se sincroniza con el de la raíz en build
(vía el `Content Include` del csproj), no al correr `agregar-perfil`.

Consecuencia: si se prueba el perfil solo recargando VS Code (sin rebuild antes), el MCP sigue
leyendo la copia vieja del bin y devuelve 401 — un 401 que **no dice nada sobre si el perfil nuevo
está bien o mal**, porque ni siquiera lo está usando todavía.

Pasos completos después de correr `agregar-perfil` (alta o reconfiguración de un perfil existente):

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
2. **Rebuildear** para que MSBuild vuelva a copiar el `appsettings.secrets.enc` de la raíz al bin de
   salida:
   ```
   dotnet build McpServers/DragonfishApiMcp
   ```
3. **Recargar la ventana de VS Code** (`Developer: Reload Window`, o cerrar y volver a abrir) — recién
   ahí el MCP arranca leyendo el archivo correcto.

Si después de estos tres pasos sigue en 401, ahí sí vale investigar credenciales/token — antes de
eso, no es diagnóstico.

---

## GestionBackupsMcp — restauración silenciosa de backups de Dragonfish

Servidor MCP registrado como `gestion-backups`. Wrapea `ZooBkp.exe` (la herramienta nativa de
Dragonfish para backup/restore) para poder restaurar backups de clientes sin abrir ninguna ventana ni
requerir intervención manual salvo cuando corresponde.

### Resolución automática de entorno — no pide nada a mano

- **Carpeta de instalación de Dragonfish**: se lee de `HKLM\SOFTWARE\Zoo Logic\<producto>\InstallDir`
  del registro de Windows — no se asume `C:\Dragonfish`, porque la instalación puede estar en
  cualquier disco/carpeta (ej. Archivos de Programa). El nombre exacto del producto varía según la
  edición de Dragonfish instalada (Color y Talle, Comercios, etc.), así que se busca cualquier
  subclave de `Zoo Logic` que contenga "Dragonfish" en el nombre, en vez de un nombre fijo. Se prueba
  la vista de registro de 64 y 32 bits. Ojo: existe una subclave separada `ZL` con su propio
  `InstallDir` (otro producto de ZooLogic, no confundir).
- **Instancia SQL Server**: se lee de `dataconfig.ini` (sección `[SQL]`, clave `Servidor`), que vive
  al lado de `ZooBkp.exe` — es el mismo archivo que usa Dragonfish para saber contra qué instancia
  conectarse.
- Ambos overrideables con variables de entorno (`ZOOBKP_EXE_PATH`, `ZOOBKP_LOG_PATH`,
  `ZOOBKP_SQL_INSTANCE`) por si en alguna máquina la resolución automática no aplica.

### `restaurar_backup(carpeta, nombreBase)`

Busca en la carpeta dada el único `.zip` cuyo nombre de base coincida con `nombreBase` (formato:
`<fecha>-<hora>-<frecuencia>-<NombreBase>-<version>.zip`, versión con 3 partes numéricas, ej.
`16.0004.14964`) y restaura solo ese — **nunca toca otros `.zip` que pueda haber en la misma carpeta**,
aunque los haya. Esto es una regla explícita, no un detalle de implementación: en una prueba real, una
primera versión del tool restauró sin que se pidiera un backup de `DRAGONFISH_ZOOLOGICMASTER` que
estaba en la misma carpeta que el backup pedido, pisando datos de infraestructura que rompieron la
autenticación de `DragonfishApiMcp`. Nunca actuar sobre archivos/bases más allá de lo explícitamente
nombrado — mismo principio que rige `crear` en `DragonfishApiMcp` (nunca inventar/completar de más).

Antes de restaurar, chequea si la base ya está registrada en la tabla `Emp` de
`DRAGONFISH_ZOOLOGICMASTER` (ver más abajo). Si no lo está, **no restaura nada** — devuelve un mensaje
pidiendo confirmar y usar `dar_alta_base_para_restore` primero. Esto reemplaza una detección más débil
basada solo en el log de `ZooBkp.exe`: el exit code y la frase "con éxito" sueltos no alcanzan (pasos
intermedios del log pueden contenerla aunque el proceso termine mal); la frase específica que indica
éxito real es `"finalizado con éxito"` (no `"finalizado con errores"`/`"retorno erróneo"`), y la
restauración real recién se confirma si el log contiene `"Invocando al componente SQLDmoWrapper"` —
sin eso, ZooBkp puede reportar éxito general sin haber tocado nada (pasaba con bases no registradas,
antes de agregar el chequeo de `Emp`).

### `dar_alta_base_para_restore(nombreBase)`

Registra una base nueva/no conocida en `Emp` para que `restaurar_backup` pueda restaurar sobre ella.
**Usar solo tras confirmar explícitamente con la persona que corresponde crear esta base** — nunca
automático dentro de `restaurar_backup`.

Por qué hace falta: la pantalla de restauración de Dragonfish, cuando la base no existe, pregunta
"¿desea darla de alta?" (MessageBox OK/Cancelar, default Cancelar) y si se confirma, registra la base
en `Emp` antes de restaurar encima. **Esa lógica vive únicamente en la UI de Windows Forms de
Dragonfish** (`RestoreRemoteContent.cs`, método `CrearBD`) — confirmado revisando el código fuente
real (ver "Código fuente de Dragonfish" más arriba) que no hay ningún flag de `ZooBkp.exe -c`
(consola/silencioso) que la dispare; en consola, si la base no está en `Emp`, el restore se saltea en
silencio sin excepción (mismo resultado que un no-op disfrazado de éxito).

**Cómo se determina "existe o no" (ambos caminos, UI y consola):** la tabla `Emp` dentro de
`DRAGONFISH_ZOOLOGICMASTER` — el esquema no está hardcodeado, se resuelve en runtime buscando qué
esquema tiene una tabla llamada `emp` (mismo patrón que usa Dragonfish internamente).

**Diseño del alta — clonar y pisar, no adivinar el esquema completo:** la tabla `Emp` real tiene 34
columnas, no solo las 5 que se ven en una consulta básica (`empcod, epath, RutaBack, crutamdf,
replica`). En vez de armar un INSERT con una lista de columnas inventada (mismo riesgo que asumir
nombres de campo de la API sin cruzar contra un registro real), `dar_alta_base_para_restore` clona una
fila real existente de `Emp` (con `replica=0`, para no heredar valores pensados para una réplica) como
plantilla, y solo pisa las columnas que se confirmaron comparando contra una base creada de verdad por
Dragonfish (`RECOLETA`, 2026-08-29):

- `empcod` = código de la base (sin prefijo, mayúsculas)
- `epath` = `DRAGONFISH_<código>`
- `descrip` = igual al código (confirmado: en una creación real, `Descripcion` = `Codigo`)
- `RutaBack` = `""` (confirmado: siempre vacío en una creación real)
- `crutamdf` = `"[Ruta predeterminada del servidor SQL]"` — **no** `""`. Este fue un error real: la
  limpieza a vacío en el código de Dragonfish (`ent_basededatos.PRG`, `AntesDeGrabar`) compara
  `RutaCompleta` (no `RutaMDF`) contra el default, y `RutaCompleta` solo se usa con motor nativo —
  nunca con SQL Server, así que ese campo jamás se limpia en una instalación sobre SQL Server.
- `replica` = `0`

Los campos de auditoría (`FALTAFW`/`HALTAFW`/`UALTAFW`/`BDALTAFW`/`SALTAFW`/`VALTAFW` y sus "MODI")
quedan **sin tocar a propósito** (vacíos/con el valor de la plantilla) — en una creación real quedan
con fecha/hora/usuario/versión reales, pero no hay forma honesta de completar "usuario"/"base de alta"
sin estar logueado en Organic, y son trazabilidad interna que no se toca a mano. Decisión explícita:
si en el futuro esto causa algún problema (algún reporte o pantalla que dependa de esos campos para
una base recién restaurada), revisar acá primero — es un riesgo conocido y aceptado, no un olvido.

### Restauración real, no un caso hipotético

Este flujo completo (chequeo → alta → restore) se probó de punta a punta contra un incidente real
(Noxion/NCENTRO, backup en carpeta con otro backup de `ZOOLOGICMASTER` al lado) y funcionó: detectó
que faltaba, dio de alta con confirmación, restauró solo la base pedida, y en una restauración
posterior de la misma base detectó que ya existía y restauró directo sin volver a preguntar.
