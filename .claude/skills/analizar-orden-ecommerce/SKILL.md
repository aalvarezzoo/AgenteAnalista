---
name: analizar-orden-ecommerce
description: Punto de partida para analizar un incidente de una orden de venta de Mercado Libre o Tienda Nube que no bajó a Dragonfish, bajó con datos incorrectos, o quedó asociada a un cliente equivocado. Usar ANTES de empezar a investigar este tipo de incidente — da el orden de pasos a seguir, no la solución.
---

# Analizar un incidente de orden de venta de ecommerce (ML/TN)

Este documento es la "forma de pensar" acumulada para este tipo de incidente — se sigue afinando
incidente a incidente. **Si al analizar un caso real aparece un patrón nuevo, o algún paso de acá
resulta ser el orden equivocado, actualizar este archivo antes de cerrar el incidente** — es el
mismo criterio que ya se aplica en `CLAUDE.md` con `COMPROBANTEV`/`FACTTIPO`: el conocimiento se
construye caso a caso, no se asume de antemano.

## Paso 0 — Reunir lo mínimo antes de tocar nada

- Cliente / base Dragonfish (`DRAGONFISH_<nombre>`).
- Cuál es el síntoma reportado, en sus propias palabras: ¿no bajó la operación? ¿bajó con datos
  mal (cliente, precio, cantidad)? ¿quedó asociada a un cliente equivocado?
- Si hay un número de orden de ML/TN, o un número de comprobante/operación Dragonfish, o al menos
  una fecha aproximada.

Si falta el número de orden y no hay ninguna fecha ni referencia, pedirlo antes de arrancar — sin
eso no hay mucho margen para buscar.

## Paso 1 — Chequear primero si ya es un bug conocido

Una fracción grande de estos incidentes llegan a MASTERHELP con un número de bug de Dragonfish ya
asignado por SAL — el trabajo ahí es administrativo (asociar el bug en ZL), no investigación desde
cero. Revisar el texto del incidente por cualquier mención a un número de bug antes de asumir que
hay que investigar de nuevo algo ya diagnosticado.

## Paso 2 — zNube primero, SQL después (por costo)

**No restaurar un backup todavía** si no hay ya acceso SQL armado a esa base. Arrancar por
`ZNubeEcommerceMcp`:

1. `listar_perfiles` — ¿ya hay storeId guardado para este cliente? Si no, y el propio texto del
   incidente que se está analizando ya trae el storeId (a veces SAL lo deja documentado, ej. "Id
   vinculación: ..."), usarlo directamente de ahí en vez de pedirlo — solo pedirlo si no aparece
   en ningún lado.
2. Conseguir el token de zNube vigente — **si ya está en el texto del incidente que se está
   analizando, usarlo directamente tal cual** (no hace falta volver a pedirlo). Solo pedírselo a
   MDA cuando el incidente no lo trae. Lo que nunca hay que hacer es reusar un token de un
   incidente *distinto* o de una sesión anterior — rota, y no hay forma de saber si sigue vigente
   sin probarlo.
3. `obtener_orden` (o `historial_orden`) — confirma si la orden **existe del lado de zNube**
   (es decir, que ML la informó y quedó del lado ZooLogic), con qué datos (cliente, monto, items)
   y en qué estado quedó. **Ojo:** `historial_orden` (`GetOrderHistory` de zNube) no necesariamente
   trae una línea de tiempo de eventos de sincronización — probado en el incidente 1694233 y
   devolvió el mismo snapshot de la orden que traería `obtener_orden`, sin un timestamp de "cuándo
   la bajó Dragonfish". No asumir que ahí va a estar ese dato; para eso ver Paso 3.

Esta es la decisión correcta como primer paso, confirmada con un caso real: si la orden ni
siquiera está en zNube, el problema es anterior (ML→zNube, fuera del alcance de este equipo) y no
vale la pena seguir. **Si la orden SÍ existe en zNube**, recién ahí vale la pena verificar si
también existe en la base de Dragonfish (Paso 3) — y ahí es donde importa la fecha/hora exacta:
comparar cuándo llegó la orden a zNube (`CreationDate`, viene en UTC — restar 3 horas para
Argentina) contra cuándo la descargó Dragonfish, no solo confiar en lo que el cliente percibió.

## Paso 3 — Recién ahí, cruzar con Dragonfish si hace falta

Si el Paso 2 no alcanza para explicar el síntoma, conseguir acceso SQL a la base del cliente
(restaurar backup vía `GestionBackupsMcp` si no hay ya acceso) y usar `SqlDiagnosticoMcp`:

- `ZooLogic.OPECOM` — ¿existe la operación? ¿qué cliente Dragonfish quedó asociado? ¿coincide con
  lo que muestra zNube?
- `ZooLogic.ECOM` (`ultorder`) — hasta qué ID de orden llegó a procesar Dragonfish para esa cuenta
  (columna `cuenta`/`cuentacom`, no el nombre del cliente). Si el número de la orden del incidente
  es mayor a `ultorder`, la descarga se cortó en algún punto anterior — buscar por qué ahí, no en
  la orden puntual. (`ultclaim` no existe como columna real — confirmado contra una base real,
  versión 14.0010.14475; no asumir su existencia sin `describir_tabla` primero.)

**Si el síntoma es demora (no ausencia ni cliente incorrecto):** comparar `CreationDate`/
`ClosedDate` de zNube (UTC, restar 3hs) contra `FALTAFW`/`HALTAFW` de la fila en `OPECOM` — esa
diferencia real es la que hay que reportar, no solo la percepción del cliente (que en el incidente
de referencia resultó ser aproximada, no medida).

**Ojo con las diferencias horarias nocturnas — no asumir que son un problema solo porque son
grandes.** Los locales cierran a horarios variables (20hs, 21hs, o más tarde según el comercio) y
no todos dejan la PC encendida fuera de horario. Una venta a las 20:30 en un local que apaga la PC
a las 20hs va a mostrar varias horas de diferencia entre que llegó a zNube (que recibe en
cualquier horario) y que se dio de alta en Dragonfish (que necesita la PC/app encendida para
descargar) — y eso es esperable, no necesariamente evidencia de una demora sistémica peor de lo
reportado. Antes de concluir "acá hay algo peor", confirmar el horario habitual de cierre/encendido
de PC de ese local puntual.

## Paso 4 — Si el síntoma es "cliente incorrecto", chequear primero el patrón ya conocido

Antes de asumir que es un caso nuevo: ¿la operación quedó asociada a un cliente genérico o de
código más bajo (ej. `"0001"`, `"."`) en vez de al cliente real? Pasa cuando el ecommerce no manda
un dato que el cliente tiene configurado como obligatorio en "comportamientos" (ej. género, fecha
de nacimiento) y Dragonfish cae al primer cliente disponible en vez de fallar la creación. Ya se
vio como sistémico entre varios clientes (bug 15389 en el incidente de referencia) — descartar
esto primero antes de investigar algo más específico.

## Paso 5 — Documentar el patrón si no encaja en nada conocido

Si el caso no matchea ningún paso anterior, es candidato a agregarse acá (o a `CLAUDE.md` si es
conocimiento de esquema/tabla más general) para el próximo incidente parecido.

## Casos ya analizados con este skill

- **1694233 (Korek, 2026-08)** — síntoma "demora en descarga de op ML" (no ausencia, no cliente
  incorrecto). Se validó el Paso 2 de punta a punta: las dos órdenes de ejemplo del incidente
  (8658702, 8659924) se confirmaron existentes en zNube vía `historial_orden`, con `CreationDate`
  coincidiendo exactamente (convertido a hora Argentina) con la hora de venta que reportaba el
  cliente — buena señal de que el dato de zNube es confiable para corroborar/desmentir lo que
  percibe el cliente. Causa real (ya resuelta antes de este ejercicio): cola de pendientes por
  recibir muchas ventas juntas en la misma cuenta.
  - **La carpeta del backup se encontró sola** por la convención ya usada en otros incidentes
    (`C:\<numeroDeIncidente>`) — antes de pedirle la ruta a la persona, probar esa convención.
  - Al cruzar con `OPECOM`, la orden 8658702 puntual no aparecía — pero en vez de leerlo como "no
    bajó", se comparó la hora del backup (11:00) contra la hora de alta que el propio incidente
    decía (11:27) y encajaba: el backup es anterior a que la orden se procesara, no un caso real
    de ausencia. Confirmado como buen razonamiento — repetir este chequeo de timing siempre antes
    de concluir que una orden "no está".
  - Se comparó `FECHA`/`HORA` (venta) contra `FALTAFW`/`HALTAFW` (alta en Dragonfish) de órdenes
    vecinas y varias nocturnas mostraron 7-8 horas de diferencia — **pero esto no se puede leer
    como "peor de lo reportado" sin más contexto**: son órdenes de la madrugada, y si el local
    cierra y apaga la PC a la noche, ese salto horario es esperable (ver advertencia en el Paso 3),
    no necesariamente una demora sistémica. Corregido después de un comentario del usuario — quedó
    como advertencia general en el Paso 3.
