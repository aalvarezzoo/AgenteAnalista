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

1. `listar_perfiles` — ¿ya hay storeId guardado para este cliente? Si no, conseguirlo (ver skill
   `configurar-perfil-znube-ecommerce`) — no vale la pena restaurar un backup solo para leerlo de
   `ZooLogic.ECOM`.
2. Pedir el token de zNube del momento (lo tiene MDA, rota — nunca asumir uno de un incidente
   anterior).
3. `obtener_orden` (o `historial_orden` si interesa ver la secuencia completa de estados, no solo
   el final) — confirma si la orden **existe del lado de zNube**, con qué datos (cliente, monto,
   items) y en qué estado quedó.

Esto ya responde buena parte de la pregunta antes de gastar el esfuerzo de restaurar nada: si la
orden ni siquiera está en zNube, el problema es anterior (ML→zNube, fuera del alcance de este
equipo); si está pero con datos raros, hay algo para comparar contra Dragonfish.

## Paso 3 — Recién ahí, cruzar con Dragonfish si hace falta

Si el Paso 2 no alcanza para explicar el síntoma, conseguir acceso SQL a la base del cliente
(restaurar backup vía `GestionBackupsMcp` si no hay ya acceso) y usar `SqlDiagnosticoMcp`:

- `ZooLogic.OPECOM` — ¿existe la operación? ¿qué cliente Dragonfish quedó asociado? ¿coincide con
  lo que muestra zNube?
- `ZooLogic.ECOM` (`ultorder`, `ultclaim`) — hasta qué ID de orden/reclamo llegó a procesar
  Dragonfish. Si el número de la orden del incidente es mayor a `ultorder`, la descarga se cortó
  en algún punto anterior — buscar por qué ahí, no en la orden puntual.

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
