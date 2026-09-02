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

### Consultar UN incidente puntual por número

**Uso mucho menos frecuente que el reporte de arriba** — el de arriba (`Iyd incidentes` por
`DuenoTarea`/`Estado`) es el que se usa en la diaria para ver qué hay pendiente. Este otro es
puntual: sirve para cuando la persona da un número de incidente concreto para analizarlo de punta a
punta (ese es justamente el objetivo de largo plazo del agente) — no para armar un listado.

```powershell
$url = "http://reportes03/ReportServer?%2FSal%2FMesa%20de%20ayuda%2FIncidentes%2FIncidente&rs:Command=Render&rs:Format=XML&rs:ClearSession=True&incid=1690552"
$client = New-Object System.Net.WebClient
$client.UseDefaultCredentials = $true
$xml = $client.DownloadString($url)
```

**Namespace XML del reporte:** `Incidente`. Sin niveles anidados por resolver (a diferencia del
reporte de arriba) — es un solo incidente, la estructura es plana:

- **Raíz `<Report>`**: atributos `incid` (ej. `"Incidente n°1690552"`), `registro`, `puesto`, `tipo`,
  `clienteDet`, `subTipo`, `rzDet`, `registro2`, y **`detalle`** — el texto completo del incidente con
  todo el historial de la investigación (puede ser varios miles de caracteres, con logs de error
  pegados tal cual).
- **`Details` (repetido) — "Interacciones"**: `Ctipcont1` (tipo de contacto: `ENT`/`AREM`/`SAL`),
  `Regpor`, `Cfecini`, `Choraini`, `Contct1`, `Nota`.
- **`Details1` (repetido) — "Tareas"**: `Numero`, `Ctitulo`, `Fechai`, `Fechaf`, **`Asignt`** (dueño
  de la tarea — acá es donde aparece `MASTERHELP`), **`Ncierre`** (`0` = todavía sin cerrar),
  `Fechac`, `Cerrot`. Da el historial de tareas de ESE incidente puntual sin tener que cruzar con el
  otro reporte.

### Consultar UN bug puntual por número

Mismo criterio que el de incidentes de arriba — uso ocasional, para cuando la persona da un número
de bug puntual (ej. para revisar qué se cargó, o releer un análisis propio ya asignado).

```powershell
$url = "http://reportes03/ReportServer?%2FIyD%2FGestion%2FBug&rs:Command=Render&rs:Format=XML&rs:ClearSession=True&Bug=15949"
$client = New-Object System.Net.WebClient
$client.UseDefaultCredentials = $true
$xml = $client.DownloadString($url)
```

**Namespace XML del reporte:** `Bug`. Estructura plana (un solo bug, sin niveles anidados que
resolver), pero con más colecciones que el de incidentes:

- **`Details` (una sola fila) — datos principales**: `nombre` (título), `UltProyNombre` (equipo
  asignado, `"Sin equipo"` si no tiene), `Etapa`, `Estado`, `Severidad`, `Ocurrencia`/`Ocurrencia2`,
  `temaNombre`, `FuncionalidadRegpor`/`FuncFechaAlta` (quién y cuándo lo cargó), y **`Textbox103`** —
  el detalle completo del bug (equivalente al `detalle` del reporte de incidentes; acá es donde
  aparece el texto armado con el formato de `redactar-reporte-de-bug`, incluida la sección
  "CORRECCIÓN SUGERIDA POR IA" si el bug se cargó a partir de un análisis de este agente).
- **`Details1` (una fila)** — `Producto1` (producto/build donde se detectó), `TieneTest`.
- **`Details2` (una fila)** — el vínculo al incidente de origen: `Origen2` (ej. `"Incidente"`),
  `NumeroOrigen2` (el número de incidente relacionado), `RegPorCli` (cliente), `Numero2`/`Fecha3` de
  la asignación. Sirve para cruzar bug↔incidente sin tener que pedirlo aparte.
- **`Details4`/`Details7`/`Details8`, `HistEpicaid2`, `HistoriaCodigo2`** — entidad relacionada, horas
  cargadas por persona, épica y vínculo a Pivotal Tracker respectivamente. Suelen venir vacíos y son
  de gestión interna del equipo de desarrollo, no aportan al análisis técnico — no hace falta leerlos
  salvo que se pida puntualmente algo de seguimiento/horas.

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
- **`CLI.GLOBALID`** (no vacío) identifica un cliente "centralizado" — pero **no implica
  necesariamente que exista un agrupamiento de bases configurado**; no asumir que siempre viene
  acompañado de esa configuración.
- **`PUESTOS` y `PARAMETROS.PUESTO`** (config de parámetros por estación de trabajo) viven en
  `DRAGONFISH_ZOOLOGICMASTER`, no en las bases de sucursal — buscarlos ahí, no en la base de
  negocio del cliente.
- **Dragonfish tiene más de un mecanismo de resolución de "puesto actual" en distintas partes del
  código** — confirmado investigando un bug real: `ParametroPuestoSqlServer.ObtenerIdPuesto()`
  resuelve con caché de sesión y respeta el modo usuario/equipo, pero algunas consultas generadas
  (ej. `Din_Busqueda5AD.cs`) resuelven el puesto con `Environment.MachineName` directo, ignorando
  ese modo y esa caché. Al investigar código relacionado a parámetros "por puesto", no asumir que
  todo el sistema resuelve el puesto de la misma forma — confirmar cuál mecanismo aplica en cada
  lugar puntual. **Consecuencia práctica a tener en cuenta:** si la instalación usa "modo por
  usuario" (no por equipo), un parámetro guardado desde la pantalla normal puede terminar escrito
  en un puesto distinto al que una consulta puntual lee — el síntoma se ve como "el parámetro no
  hace nada", aunque en realidad sí se guardó, solo que en otro lado.
- **Los parámetros "por puesto" pueden tener un valor por defecto codificado (`.Default`) que se
  auto-crea recién la primera vez que se lee el parámetro, no al instalar el sistema** — confirmado
  en `Din_Parametros.prg` (`.Default = .T.` en la definición del parámetro) y en la práctica: en una
  base recién creada, la fila en `PARAMETROS.PUESTO` no existe hasta que alguien abre la pantalla
  correspondiente por primera vez. No asumir que un valor "que ya viene así" fue sembrado por un
  script de instalación.

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

**Regla dura — SOLO LECTURA:** el código fuente de Dragonfish (hoy en `C:\IADragon2028`, pero la
regla es sobre el código en sí, no sobre esa ruta puntual — si en el futuro está en otro lado o hay
una copia/checkout en otra carpeta, aplica igual) se usa exclusivamente para *leer/entender* como
apoyo al análisis de incidentes. **Nunca modificar ni eliminar nada que sea código fuente de
Dragonfish**, sea cual sea su ubicación, salvo que la persona cambie explícitamente esta regla en
una conversación futura. Esto aplica siempre, en cualquier PC donde se use AgenteAnalista — no es
una preferencia de sesión.

**Gotcha de sintaxis al leer código VFP (parte de este código fuente es legacy VFP, no solo C#):
`&&` es delimitador de comentario, NO "AND" lógico** (el AND lógico en VFP es `AND`/`.AND.`). Leer
`if A && B` como "if A and B" lleva a una conclusión equivocada sobre qué rama de código se
ejecuta — pasó en la práctica: una rama se descartó como "código muerto" hasta que una prueba real
demostró que sí se ejecutaba, y releyendo con este criterio quedó claro por qué. Ante cualquier
condicional VFP con `&&` en el medio, tratar todo lo que sigue como comentario, no como parte de la
condición.

---

# MCP servers (`McpServers/`)

El repo trae cinco servidores MCP: `ZlApiMcp`, `DragonfishApiMcp`, `GestionBackupsMcp`,
`SqlDiagnosticoMcp` y `ZNubeEcommerceMcp`. Los últimos cuatro están registrados en `.mcp.json`, que
Claude Code levanta solo al abrir el proyecto.

**Primera vez en una máquina que nunca los corrió** (fallan al arrancar si no): ver skill
`setup-mcp-servers` — genera la clave de cifrado, arma `appsettings.secrets.json`, lo cifra y
compila.

**Antes de escribir o modificar cualquier tool de un MCP de este repo:** ver skill
`mcp-tools-desarrollo` — hay un gotcha del SDK (`ModelContextProtocol`) sobre cómo tienen que
tirarse las excepciones para que el mensaje real le llegue al modelo en vez de uno genérico.

## DragonfishApiMcp — cómo configurar un perfil cuando alguien pide "usar la API de Dragonfish"

Esto es de **uso interno de MasterHelp**, no para clientes — la API que se usa es la instalación
propia de quien esté trabajando (hoy, la de AALVAREZ en local), no la de un cliente real. Una
instalación de Dragonfish sirve para cualquier base de esa instancia — es única por instalación, no
por base de datos.

**Por default asumir el perfil `TEST`** (no preguntar cuál) — pero **nunca asumir que funciona solo
porque `listar_perfiles` lo devuelve en la lista**. Probarlo primero con una llamada real y liviana
(ej. `consultar` sobre `Articulo` con `limit:1`). Si falla (401, timeout, conexión rechazada, o el
perfil existe pero está vacío), tratarlo como si no existiera.

Si hace falta darlo de alta o reconfigurarlo (elegir instancia SQL, pedir IdCliente/Token, ejecutar
`agregar-perfil`, rebuild+reload antes de probar) ver skill `configurar-perfil-dragonfish-api`.

### `/Autenticar` — la API exige este paso antes de cualquier otra llamada

Confirmado en la práctica (2026-09-02): mandar `IdCliente`/`Authorization` en el header de
`consultar`/`crear` **no alcanza por sí solo**, aunque el token no esté vencido — la API devuelve
`401 Cliente no autenticado` hasta que se llama primero a `POST /Autenticar` con
`{"IdCliente": "...", "JWToken": "<el mismo Authorization>"}` en el body. Una vez hecho eso, el
servidor acepta el mismo `Authorization` en llamadas posteriores de cualquier conexión/proceso —
no hace falta repetirlo en cada request, solo la primera vez por perfil (o de nuevo si el servicio
de Dragonfish se reinició y perdió esa sesión).

Esto ya lo maneja solo `DragonfishApiMcp` (`AutenticadorDragonfish`, con caché por perfil y
reintento automático si una llamada posterior vuelve a dar 401) — no hace falta llamarlo a mano.
Si en el futuro un perfil da 401 con un token confirmado vigente, **no es necesariamente el token**
— antes de sospechar de las credenciales, confirmar que el propio mecanismo de autenticación no
tenga un problema nuevo (ej. la API cambió el contrato de `/Autenticar`).

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

**Antes de preguntar dónde está el backup, probar la convención ya usada en varios incidentes
reales:** la persona suele descargarlo en una carpeta local llamada como el número de incidente,
ej. `C:\1694233`. Buscar ahí primero (`ls C:\<numeroDeIncidente>`) antes de pedir la ruta.

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

### Qué hace exactamente el restore cuando la base recién se dio de alta (sin archivo físico aún)

Investigado en el código fuente real (ver "Código fuente de Dragonfish" más arriba) a pedido
explícito, porque no alcanzaba con inferirlo desde afuera — quedó la duda de qué hace el propio
restaurador/ADN Implant cuando la base está en `Emp` pero todavía no existe como archivo en SQL
Server:

- **El chequeo previo al restore no verifica el archivo físico.** `RestoreBase.EjecutarLogicaRestore`
  llama a `ValidarBaseDatosDesconectada`, que en `AdnImplantManager` delega en
  `_gestorSalud.ObtenerBasesDesconectadas(nombre)` — un estado de salud/semáforo interno, no si el
  `.mdf` existe. Una base recién dada de alta en `Emp` sin flag de "desconectada" pasa este control
  aunque el archivo todavía no exista — por eso el flujo sigue.
- **El archivo físico lo crea SQL Server mismo, no Dragonfish.** `SqlDmoWrapper.RestoreDatabase`
  (`ZooLogicSA.SqlDmoWrapper/SqlDmoWrapper.cs`) arma un `RESTORE DATABASE ... WITH MOVE ..., REPLACE`
  vía SMO. El destino de los archivos (`MOVE ... TO`) sale de `Server.Information.MasterDBPath`/
  `MasterDBLogPath` — la carpeta de datos default de la instancia SQL Server, **no** de
  `Emp.crutamdf` (que no se consulta en ningún punto de este camino — confirma y extiende lo ya
  documentado más arriba: no es que se limpia a vacío, directamente no participa en el restore).
  `SetSingleUser`/`KillAllProcesses`, llamados antes del restore, son no-ops seguros sobre una base
  que todavía no existe (buscan en `Server.Databases`; si no la encuentran, no tiran excepción).
- **La "adecuación" de ADN Implant es sobre la estructura, no sobre crear el archivo.** Después del
  restore SQL, `RestoreBase.AdecuarBaseDatosUsandoAdnImplant` → `AdnImplantManager.AdecuarBaseDeDatos`
  corre el proceso real de ADN Implant (`EjecutarAdnImplant` o
  `EjecutarAdnImplantConCorreccionCollation` según si el collation de la base restaurada coincide con
  el de `ZOOLOGICMASTER`) para reconciliar tablas/columnas/índices contra lo que espera la versión de
  esta instalación — el equivalente a una migración de esquema, no relacionado a crear el archivo
  (eso ya lo hizo el paso anterior).
- **Por último**, `ConfigurarOnlineBD` marca la base online en la tabla de semáforo interna (mensaje
  literal "Base de datos restaurada desde Zoo Logic Backup") y `ControlarSaludBD` corre la validación
  final. Un fallo en cualquiera de estos dos últimos pasos ocurre DESPUÉS de la frase "Invocando al
  componente SQLDmoWrapper" en el log de ZooBkp (esa frase es del restore SQL puro) — si algún día
  `RestoreResultado.Evaluar` necesita distinguir "restore SQL ok pero adecuación de ADN Implant
  falló" de un éxito real de punta a punta, esta es la referencia de dónde buscar esa distinción en
  el log.

---

## SqlDiagnosticoMcp — diagnóstico de SQL Server de solo lectura

Servidor MCP registrado como `sql-diagnostico`. Da herramientas chicas y específicas para explorar
el esquema de una base Dragonfish y consultar datos durante el análisis de un incidente, **sin**
exponer una única `execute_sql` genérica ni ningún camino de escritura. La idea no es que este MCP
"resuelva" el incidente — es darle a Claude los ojos para investigar (ubicar tablas relacionadas a
un término, ver columnas/FKs/índices reales, leer la definición de la vista/SP que alimenta un
reporte SSRS, consultar filas, ubicar un valor puntual, comparar el esquema entre dos bases) en vez
de tener que indicarle de antemano "hacé un SELECT de tabla X JOIN tabla Y".

**No hace falta memorizar de antemano qué guarda cada tabla/columna de Dragonfish** para que este
MCP sea útil — de hecho no conviene: el propio código fuente de Dragonfish (`C:\IADragon2028`) no
documenta el significado de negocio de sus columnas (solo metadata estructural: tipo, longitud,
PK/identity), y son ~1800+ tablas solo en el esquema de sucursal. El conocimiento de negocio real
(qué es `FACTTIPO`, qué campos de `COMPROBANTEV` no se tocan, etc.) se sigue construyendo incidente
a incidente en este mismo archivo — este MCP da las herramientas de exploración en vivo
(`buscar_en_esquema`, `describir_tabla`) para no depender de tenerlo memorizado de antemano.

### Modelo de seguridad — dos capas, la real es la primera

1. **Login SQL dedicado de solo lectura.** Cada perfil se conecta con **SQL Authentication** (nunca
   Integrated Security con la cuenta Windows del analista, nunca `sa`) usando un login que en SQL
   Server **solo tiene el rol `db_datareader`** en las bases que se vayan a consultar. Esto es lo
   que realmente impide escribir algo — no el código de este MCP.
2. **Validación en el propio MCP (`ConsultaSqlValidator`).** `consultar_sql` rechaza cualquier texto
   que no empiece con `SELECT`/`WITH`, que tenga más de un statement, o que contenga palabras como
   `INSERT/UPDATE/DELETE/DROP/ALTER/EXEC/sp_/xp_/...`. Es defensa en profundidad — da un error claro
   antes de llegar a SQL Server — pero nunca el único mecanismo de protección.

No expone backup/restore (eso ya lo hace `GestionBackupsMcp`, con sus propios privilegios) ni
ninguna tool de escritura. Si en el futuro hiciera falta, tendría que ser una tool nueva y
explícita — nunca una ampliación de `consultar_sql`.

### Configurar un perfil

Un perfil es una instancia de SQL Server + las credenciales del login de solo lectura. Para el
paso a paso completo (crear el login SQL, correr `agregar-perfil`, el gotcha de rebuild+reload
antes de probar) ver skill `configurar-perfil-sql-diagnostico`.

### Tools

| Tool | Qué hace |
|------|----------|
| `listar_perfiles` | Lista los perfiles configurados (nunca expone credenciales). |
| `listar_bases` | Bases visibles para el login del perfil, con estado (ONLINE/OFFLINE/etc). |
| `buscar_en_esquema` | Busca tablas/vistas/procedimientos/columnas por palabra clave en el nombre — punto de partida para no tener que indicar la tabla de antemano. |
| `describir_tabla` | Columnas (tipo/longitud/nullable/identity), clave primaria, FKs entrantes y salientes, e índices de una tabla. Resuelve el esquema solo si no se indica (falla claro si es ambiguo). |
| `obtener_definicion_objeto` | Código SQL real de una vista/SP/función/trigger (`OBJECT_DEFINITION`) — clave cuando un reporte SSRS llama a un SP donde vive el cálculo real. Se trunca a `limiteCaracteres` (default 8000, tope 50000); si queda truncado, la nota indica el `desde` exacto para pedir el siguiente pedazo en otra llamada (no hay forma de traer un objeto gigante completo de una sola vez — ni subiendo el límite al tope, el propio harness rechaza una respuesta de ~50-55K caracteres). Requiere que el login tenga `VIEW DEFINITION` además de `db_datareader` (ver skill `configurar-perfil-sql-diagnostico`) — sin ese permiso, cualquier objeto (esté o no cifrado) devuelve `NULL`. |
| `consultar_sql` | `SELECT`/`WITH` de solo lectura, con límite de filas y timeout configurables (defaults 50 filas / 5 segundos). Además hay un tope de ~300 celdas totales (filas×columnas) que achica el límite de filas solo en tablas anchas — un `SELECT *` en una tabla de 50+ columnas devuelve menos filas de las pedidas para no gastar de más; para traer más filas, seleccionar columnas puntuales en vez de `*`. |
| `buscar_valor` | Busca un valor exacto (CUIT, nro. de comprobante, etc.) en columnas de texto/numéricas compatibles de tablas candidatas — **requiere pasar la lista de tablas** (usar `buscar_en_esquema` primero); no hace barrido ciego de toda la base. |
| `comparar_esquemas` | Diferencias de tablas/columnas entre dos bases de la misma instancia — para el caso típico "funciona en Demo, no en la base del cliente". |

Todas las tools que apuntan a datos reciben `perfil` y `baseDeDatos` como primeros parámetros
(salvo `listar_perfiles`, que no necesita ninguno, y `listar_bases`, que solo necesita `perfil`).

---

## ZNubeEcommerceMcp — trazabilidad de órdenes de venta de ecommerce (Mercado Libre)

Servidor MCP registrado como `znube-ecommerce`. Wrapea la API "ECommerceIntegration" de zNube
(`api.znube.com.ar` — confirmado en el código fuente de Dragonfish, `App.config` de
`ZooLogicSA.Framework.zNube`), donde queda registrada cada orden de venta de un ecommerce del
cliente antes de que Dragonfish la descargue como "operación" (tabla `ZooLogic.OPECOM`). Sirve
para incidentes tipo "la venta no bajó" / "bajó con datos mal" / "cliente incorrecto" — ver qué
vio zNube de esa orden, sin depender del Portal de DevOps de zNube (al que este equipo no tiene
acceso — ver más abajo la nota sobre zNube en general).

Mismo contrato ya probado en producción en `PanelMasterHelp/Services/ZNubeService.cs` — se copió
tal cual (mismos endpoints, mismos query params), no se reinventó el request. Por ahora solo cubre
Mercado Libre (`eCommerceType=1` hardcodeado) — Tienda Nube u otras plataformas quedan para cuando
se pidan explícitamente.

**Nota de alcance:** analizar incidentes de **zNube en sí** (el portal donde el cliente configura
vinculaciones/ecommerce/cubos) para sacar nuevas habilidades no se consideró razonable por ahora
— la mayoría de esos incidentes se resuelven del lado de zCloud/Devops, sin SQL accesible para
este equipo. Este MCP es la excepción puntual: la trazabilidad de una orden de venta puntual sí es
algo que este equipo puede diagnosticar (cruzando zNube + `OPECOM`), aunque la causa raíz a veces
termine siendo de zNube.

### Modelo de credenciales — storeId persistente, token siempre a mano

Dos datos hacen falta para consultar la API: `storeId` (identifica la cuenta de Mercado Libre del
cliente — es literalmente `ZooLogic.ECOM.IDVINC` en la base de ese cliente, confirmado en el
código fuente de Dragonfish, `Cartero.cs`) y un token (`zNube-token`).

- **`storeId` se persiste por cliente** (perfil, cifrado en `appsettings.secrets.enc` — mismo
  mecanismo que los demás MCP). Es estable en el tiempo, así que una vez pedido no hace falta
  volver a pedirlo. Se pidió explícitamente NO resolverlo automáticamente por SQL contra `ECOM`
  aunque técnicamente se podría: para un triage rápido, restaurar un backup solo para leer ese
  campo no es eficiente — más barato pedirlo una vez y guardarlo.
- **El token NUNCA se persiste.** Rota cada cierto tiempo y lo tiene MDA — se pide como parámetro
  en cada llamada a cada tool, siempre fresco. Nunca asumir uno guardado de una sesión anterior.

Para dar de alta el storeId de un cliente nuevo, ver skill `configurar-perfil-znube-ecommerce`.

### Tools

| Tool | Qué hace |
|------|----------|
| `listar_perfiles` | Lista los clientes con storeId ya guardado. |
| `obtener_orden` | Una orden puntual de Mercado Libre (JSON crudo de zNube). |
| `buscar_ordenes` | Rango de órdenes desde un ID dado. |
| `historial_orden` | Secuencia completa de estados de UNA orden puntual — no solo el estado final. |
| `historial_ordenes` | Secuencia de estados de un rango de órdenes desde un ID dado. |
| `historial_reclamos` | Historial de reclamos de un rango de órdenes. |

Todas reciben `perfil` (resuelve el storeId) y `token` (siempre a pedir en el momento) como
primeros parámetros.
