# Mapa de código fuente de Dragonfish

Índice de navegación sobre `C:\IADragon2028` (o donde esté ese checkout en cada máquina — ver
"Código fuente de Dragonfish" en `CLAUDE.md`), separado del conocimiento de negocio/SQL para que
ese archivo no se vuelva inmanejable. Se arma incidente a incidente: cada vez que una investigación
encuentra dónde vive algo real, se agrega acá — el objetivo es no tener que re-descubrirlo la
próxima vez.

**Aplica la misma regla dura de siempre: solo lectura.** Este archivo es un índice de rutas, no
autoriza a tocar nada del código que referencia.

**Antes de confiar en una entrada:** son rutas confirmadas en el momento en que se escribieron —
pueden cambiar entre versiones de Dragonfish. Si algo no cuadra (archivo no existe, método
renombrado), no asumir que la entrada miente: confirmar contra el código actual y corregir acá.

**Cómo mantenerlo:** antes de grepear a ciegas sobre un tema nuevo, revisar si ya hay una entrada
relacionada acá. Al cerrar una investigación de código que encontró algo reusable, agregar una
entrada nueva (ver paso 3 de la skill `investigar-bug-de-codigo`).

---

## Convenciones de nombres

Útil para orientarse en un archivo nuevo sin haberlo visto antes:

- **`Din_<Entidad>_REST.prg`** — entidad generada específicamente para el endpoint REST de esa
  entidad (ej. `Din_EntidadMovimientodestock_REST.prg`). Generado, no es donde vive la regla de
  negocio central.
- **`ent_<entidad>.PRG` / `Ent_<Entidad>.PRG`** — la entidad de negocio central (no generada,
  vive en `Organic.BusinessLogic`), comparte lógica entre UI y API salvo que algo puntual la
  desvíe.
- **`Kontroler<Algo>` / `Tra_Kontroler<Algo>`** — controlador de una **pantalla** (validaciones de
  formulario VFP), no de la entidad — si algo se valida ÚNICAMENTE acá, la API (que no pasa por
  ninguna pantalla) no hereda esa validación puntual. **Ojo:** esto no significa que la entidad no
  tenga NINGUNA lógica de negocio relacionada — confirmado en la práctica (ver "Movimiento de
  stock" más abajo) que una primera lectura del Kontroler llevó a asumir que era "el único lugar
  que valida algo", cuando en realidad la entidad sí tenía su propia lógica (de transferencia, en
  ese caso) en una clase base distinta que no se había revisado todavía. Nunca dar por cerrada una
  búsqueda de "dónde se valida X" solo por no encontrarlo en la entidad hija — subir por toda la
  cadena de herencia antes de concluir que algo "solo se valida en la pantalla".
- **`ColorYTalle` vs `Feline`** — dos productos con árboles de código separados
  (`Organic.Dragonfish` y `Organic.Feline`), pero **no asumir que son espejos independientes**: una
  clase de ColorYTalle puede heredar directamente de una clase base que vive físicamente en el
  árbol de Feline (confirmado con `Ent_MovimientoDeStock` — ver "Movimiento de stock" más abajo,
  donde la cadena real de herencia cruza de un árbol al otro). Antes de asumir que un concepto tiene
  dos implementaciones paralelas e independientes, subir la cadena de herencia (`define class X as
  Y of Y.prg`) del archivo del lado que se está mirando — puede resolver a un archivo del OTRO
  árbol.
- **`&&` en código VFP es delimitador de comentario, NO "AND" lógico** (el AND real es
  `AND`/`.AND.`). Leer `if A && B` como "if A and B" lleva a una conclusión equivocada sobre qué
  rama se ejecuta — pasó en la práctica (ver "Restore de backups" más abajo).

---

## Restore de backups y ADN Implant

Investigado a fondo el 2026-09-02 (ver CLAUDE.md, sección `GestionBackupsMcp`, "Qué hace
exactamente el restore..."). Cadena completa, en orden de ejecución real:

1. **`Components.RecoveryManager/ZooLogicSA.RecoveryManager.Core/Managers/RestoreBase.cs`** —
   orquesta todo (`EjecutarLogicaRestore`): `ObtenerInformacionBackup` → `VerificarVersiones` →
   `ValidarBDConectada` (llama a `AdnImplantManager.ValidarBaseDatosDesconectada` — chequea un
   estado de salud/semáforo, **no** si el archivo físico existe) → `Restaurar` → 
   `AdecuarBaseDatosUsandoAdnImplant` → `ControlarSalud`.
2. **`.../Lanzadores/Estrategias/RestoreFromBackup.cs`** — `Restaurar()` llama a
   `SqlDmoWrapper.RestoreDatabase(...)` para cualquier base que no sea `master`.
3. **`.../ZooLogicSA.SqlDmoWrapper/SqlDmoWrapper.cs`** — `PerformRestoreDatabase` arma el
   `RESTORE DATABASE ... WITH MOVE ..., REPLACE` real vía SMO. El destino de los archivos sale de
   `Server.Information.MasterDBPath`/`MasterDBLogPath` (la carpeta default de la instancia SQL),
   **nunca** de `Emp.crutamdf` — ese campo no participa en este camino.
4. **`.../ZooLogicSA.SqlDmoWrapper/Managers/SqlDmoServer.cs`** — `MasterDBPath`/`MasterDBLogPath`
   (leen `Server.Information`), `KillAllProcesses`/`SetSingleUser`/`SetOnline` (todos usan
   `FindDatabase` → `Databases.Contains` — no-op seguro si la base todavía no existe físicamente).
5. **`.../ZooLogicSA.RecoveryManager.Core/Managers/AdnImplantManager.cs`** —
   `AdecuarBaseDeDatos` (compara collation contra `ZOOLOGICMASTER`, corre
   `EjecutarAdnImplant`/`EjecutarAdnImplantConCorreccionCollation` — reconcilia ESQUEMA, no crea el
   archivo), `ConfigurarOnlineBD` (marca online en la tabla de semáforo, mensaje literal "Base de
   datos restaurada desde Zoo Logic Backup").
6. **`ADNImplant.AdnImplant/ZooLogicSA.AdnImplant.Sql/Helpers/HelperControlDeConexionServidor.cs`**
   — `VerificarConexion` → `Servidor.EstaActivo` (`Common.Core/ZooLogicSA.Core.BasesDeDatos/Servidor.cs`)
   — chequeo de conectividad genérico contra `master`, nada específico de la base restaurada.

**Alta manual de una base inexistente (pantalla, no consola):**
`Components.RecoveryManager/ZooLogicSA.RecoveryManager.UI/Controls/RestoreRemoteContent.cs`,
método `CrearBD` (el cartel "¿desea darla de alta?"). La consulta/columnas de `Emp` las resuelve
`Common.Core/ZooLogicSA.Core.BasesDeDatos/ProveedorBD.cs`.

---

## Conexión SQL manual y `dataconfig.ini` (pantalla clásica VFP)

Investigado el 2026-09-02 a partir de un error real de login tras restaurar un `ZOOLOGICMASTER`
ajeno (ver CLAUDE.md). Todo esto es **local, basado en archivo** — no depende de datos dentro de
la base restaurada:

- **`Organic.Core/Organic.BusinessLogic/CENTRALSS/Nucleo/Data/managerconexionasql.prg`** —
  `ObtenerIdConexion` es la función exacta que arma el cartel "Ocurrió un error al intentar acceder
  a los datos... ¿Desea intentar acceder a los datos nuevamente?". `EsServidorNoVerificado`
  compara el `CodigoDeServidor` calculado contra el guardado en el ini.
- **`Common.Core/ZooLogicSA.Core.DatosAplicacion/DataConfigIni.cs`** — `SeguridadIntegrada` lee
  `[SQL] SeguridadIntegrada` del ini y se mapea 1:1 a `SqlConnectionStringBuilder.IntegratedSecurity`
  en `GenerarCadenaConexion`. Si es `false` y no hay `UsuarioDeInstalacion`/`PasswordUsuarioDeInstalacion`
  cargados, `Usuario`/`Clave` quedan `""` → `Login failed for user ''`.
- **`.../Nucleo/_Base/crearDataConfigini.PRG`** — `SincronizarCodigoDeServidor` consulta
  `[Organizacion].[Discover]` para resolver el `CodigoDeServidor` y lo escribe en el ini si el
  servidor resuelve como local.
- **`.../Nucleo/_Base/aplicacionbase.prg`** — `aArchivosIni[1]` = `Aplicacion.INI`, `aArchivosIni[2]`
  = `DataConfig.INI` (mismo archivo físico que se edita a mano, no dos archivos distintos).

---

## Resolución de "puesto actual" y parámetros por puesto

Ya documentado en CLAUDE.md (sección de convenciones de esquema) — acá el detalle de código:

- **`Components.Buscador/ZooLogicSA.Buscador.ColorYTalle.Generados/Din_Busqueda5AD.cs`**,
  método `ObtenerConsultaPorBaseDeDatos()` — resuelve el puesto actual con
  `Environment.MachineName` directo, **ignorando** la caché de sesión y el modo usuario/equipo que
  sí respeta el mecanismo "normal" (`ParametroPuestoSqlServer.ObtenerIdPuesto()` — clase
  mencionada en el código, ubicación exacta todavía sin confirmar, buscar por nombre de clase la
  próxima vez). Este mismo archivo es la causa raíz del bug real de "cheques de terceros
  repetidos en consulta de deuda" (ver `reportes-bug/cheques-terceros-repetidos-consulta-deuda.md`):
  la subquery `cli` fuerza `CLCOD=''` para clientes centralizados, y a diferencia del join de
  `CtaCte` (que tiene un fallback por `GlobalId`), el join de `CHEQUE` no lo tiene.
- **`Organic.Dragonfish/Organic.Generated/Generados/Din_Parametros.prg`** (generado por producto —
  existe un equivalente por cada árbol: `Organic.Feline`, `Organic.ZL`, etc.) — los parámetros
  "por puesto" tienen `.Default = .T.`, o sea que la fila en `PARAMETROS.PUESTO` recién se crea la
  primera vez que se LEE el parámetro, no al instalar — una base recién creada no la tiene todavía.

---

## Movimiento de stock, transferencias y buzones

Investigado el 2026-09-02 para el bug 15939 ("API marca ENVIADO sin buzón asociado"). Primera
pasada (por nombre/keyword) llevó a una conclusión incompleta — quedó corregida acá después de
subir la cadena de herencia completa (ver nota en "Convenciones de nombres" sobre `Kontroler` y
ColorYTalle/Feline). **La API POST 15939 sigue sin reproducirse** en la prueba real hecha; el
motivo de por qué no reprodujo sigue sin resolver (no es por un flag de entorno de desarrollo —
confirmado explícitamente que este equipo siempre trabaja contra el producto final, no contra un
build de desarrollo).

**Entidades/pantalla (nivel superficial — acá NO vive la decisión real):**
- **`Organic.Dragonfish/Organic.Generated/Generados/Din_EntidadMovimientodestock_REST.prg`** —
  mapeador DTO↔entidad genérico (`ServicioRestOperacionesEntidad` of
  `Organic.Core/Organic.BusinessLogic/CENTRALSS/Nucleo/API/ServicioRestOperacionesEntidad.prg`).
  Solo copia campos del JSON a la entidad (`SetearEntidadConDatosModelo`) y al final llama
  `loEntidad.Grabar()` — no tiene ninguna lógica de `ESTTRANS`/buzón.
- **`Organic.Dragonfish/Organic.BusinessLogic/CENTRALSS/ColorYTalle/Ventas/entcolorytalle_movimientodestock.PRG`**
  (`EntColorYTalle_MovimientoDeStock`) — hereda de **`Organic.Feline/Organic.BusinessLogic/CENTRALSS/Felino/Ventas/Ent_MovimientoDeStock.PRG`**
  (cruza de árbol — ver nota de convenciones). Sin lógica propia de transferencia, la hereda de ahí.
- **`Organic.Dragonfish/Organic.BusinessLogic/CENTRALSS/ColorYTalle/_Base/tra_kontrolertransferenciamovimientodestock.prg`**
  (`Tra_KontrolerTransferenciaMovimientoDeStock`) — controlador de la PANTALLA de transferencia
  manual (usa `ObtenerControl("BOXESDATO")`). Valida que el buzón elegido A MANO coincida con el
  origen/destino ANTES de guardar — bloquea el guardado si no coincide. Es una validación previa
  de UI, **no** es el mecanismo que decide `ESTTRANS` después de grabar (ver más abajo) — son dos
  cosas distintas que conviven.

**La cadena real que decide si se transfiere (y por lo tanto marcaría `ESTTRANS`), encontrada
subiendo la herencia desde `Ent_MovimientoDeStock`:**

1. **`Organic.Feline/Organic.BusinessLogic/CENTRALSS/Felino/Ventas/Ent_MovimientoDeStock.PRG`** —
   `AccionesAutomatizadas(tcMetodo)`: cuando `tcMetodo == "DESPUESDEGRABAR"`, solo dispara el paso
   siguiente si `this.lEntidadInstanciadaPorFormulario` **o** `this.lGeneraDesdePicking` **o**
   `this.verificarContexto("R")` — condición todavía sin confirmar si la cumple una entidad creada
   desde la API (no desde un formulario). También define `CrearItemTransferencia()`: arma un
   `ItemFiltroTransferencia` y, si `OrigenDestino_Pk` no está vacío, llama a
   `this.oEntidadBuzon.CompletarItemTransferencia(...)` para resolver `cBuzon`/`cBaseDeDatos`; si
   ninguno resuelve, intenta `IntentarSeteoDeBaseDeDatosComoDestino`.
2. **`Organic.Feline/Organic.BusinessLogic/CENTRALSS/Felino/_base/EmpaquetarComprobanteDespuesDeGrabar.PRG`**
   — `DebeEmpaquetarElComprobante(tlElComprobanteEstaHabilitado)`: devuelve directamente `.f.`
   (nunca empaqueta) si `_screen.zoo.lDesarrollo` o `_screen.zoo.EsBuildAutomatico` son verdaderos y
   no está forzado por `goServicios.Registry.nucleo.Transferencias.ForzarElUsoDelAaoEnTiempoDeDesarrollo`
   — **no aplica en este equipo** (siempre se trabaja contra producto final, nunca contra un build
   de desarrollo), así que en la práctica acá devuelve directamente el parámetro
   `goServicios.Parametros.Felino.Transferencias.MovimientoDeStock.EmpaquetarComprobanteDespuesDeGrabar`.
   Si pasa, `Empaquetar()` llama a `toEntidad.CrearItemTransferencia()` y, solo si `cBuzon` o
   `cBaseDeDatos` no vinieron vacíos, llama a
   `goServicios.Transferencias.EnviarTransferenciaSegunItemFiltro(...)` — ahí (todavía sin abrir)
   es donde debería vivir el seteo real de `ESTTRANS`, no en el Kontroler de pantalla.

**Pendiente de confirmar** (no se llegó a verificar en esta investigación):
- Si una entidad creada vía API queda con `lEntidadInstanciadaPorFormulario = .t.` o no — de eso
  depende si el paso 1 siquiera dispara el empaquetado para un POST.
- Qué hace exactamente `goServicios.Transferencias.EnviarTransferenciaSegunItemFiltro` con
  `ESTTRANS` — todavía no se abrió ese archivo.
- Por qué la prueba real (POST vs. manual, mismo `OrigenDestino_Pk`) no mostró diferencia — con
  esta cadena más completa, sigue siendo una pregunta abierta, no resuelta por un flag de entorno.

**Tablas**: `ZooLogic.MSTOCK`/`DETMSTOCK` (base de negocio — cabecera/detalle del movimiento,
`ESTTRANS`/`ORIGDEST`/`DIRMOV` en `MSTOCK`); `PUESTO.AGRUPABUZON` (definición de buzón, en
`DRAGONFISH_ZOOLOGICMASTER`); `PUESTO.AGRUPAG`/`AGRUPAGB` (agrupamientos que incluyen buzones —
vínculo exacto buzón↔origen/destino todavía sin terminar de confirmar).
