---
name: investigar-bug-de-codigo
description: Metodología general para cuando un incidente huele a defecto de la aplicación Dragonfish (no a configuración o dato puntual del cliente) — cómo verificar la hipótesis contra código y datos reales antes de darla por confirmada, y cómo reproducirla de forma aislada. Usar cuando el síntoma de un incidente sugiere que "esto no debería poder pasar" según las reglas de negocio, más allá de lo que ya haya escrito SAL.
---

# Investigar un posible bug de aplicación

Documento vivo — se sigue afinando incidente a incidente, mismo criterio que
`analizar-orden-ecommerce`. No es la metodología para "esto es un dato mal cargado" o "falta un
parámetro" — es para cuando el síntoma sugiere que el código mismo tiene un defecto.

## Señales de que puede ser un bug de aplicación, no solo config/datos

- El comportamiento es inconsistente entre clientes/bases sin una razón de configuración obvia.
- El síntoma "no debería poder pasar" según las reglas de negocio que ya conocemos (ej. un dato
  de un cliente apareciendo en el registro de otro).
- SAL ya investigó bastante y encontró un patrón raro pero no una causa definitiva.

## Paso 1 — No confiar ciegamente en lo que ya escribió el incidente

Aunque SAL haya dejado una investigación detallada, **verificar cada afirmación contra datos
reales antes de aceptarla** — no asumir que su lectura del problema es exacta. Ejemplo real: un
incidente decía "el campo X aparece vacío en la tabla" — al consultar la tabla real, el campo
nunca estaba vacío ahí; lo que pasaba es que una consulta de búsqueda lo pisaba con un valor vacío
solo en su propio resultado. Cruzar siempre contra la tabla real (`describir_tabla`,
`consultar_sql`) antes de repetir una conclusión que no se verificó.

## Paso 2 — Restaurar solo lo necesario

Mismas reglas ya conocidas de `GestionBackupsMcp`: nunca tocar otros `.zip` de la misma carpeta sin
que se pidan, confirmar explícitamente antes de restaurar algo que pise infraestructura propia
(ej. el `ZOOLOGICMASTER` local), buscar primero si el backup ya está en `C:\<numeroDeIncidente>`.

## Paso 3 — Código fuente de Dragonfish solo con autorización explícita

Recordar la regla dura: solo lectura, y solo cuando la persona lo pide o lo sugiere. Al investigar,
priorizar precisión sobre completitud — si algo no se puede confirmar con evidencia de código
(por ejemplo, por qué una excepción se atraja siempre a un mensaje genérico, o qué proceso puntual
escribió un dato), decirlo explícitamente en vez de completar con una teoría sin evidencia.

## Paso 4 — Verificar la hipótesis de código contra datos reales

Una lectura de código da una hipótesis, no una confirmación. Antes de darla por buena:
- Buscar en la base real (`buscar_en_esquema`, `consultar_sql`) si existen las condiciones que la
  hipótesis predice (ej.: ¿hay realmente una fila que matchee la condición sospechosa?).
- Si es posible, conseguir la consulta real ejecutada (ej. con Express Profiler/SQL Profiler) y
  compararla línea por línea contra lo que dice el código — no asumir que el código fuente sin
  placeholders resueltos es exactamente lo que corrió.

## Paso 5 — Reproducir en una base propia/de prueba antes de dar el bug por confirmado

**Nunca depender de los datos del cliente para la reproducción final.** Armar el caso mínimo en
una base propia (ej. `DEMO`), aislado:
- Si el bug depende de un dato puntual (ej. un campo que normalmente no está vacío), simularlo por
  SQL directo en la base de prueba en vez de buscarlo en el cliente.
- Si depende de una acción de UI real (ej. cómo se carga un comprobante), reproducir esa acción tal
  cual en la base de prueba, no simularla con un INSERT si se puede hacer por la pantalla real.
- Confirmar también el caso negativo (sin la condición sospechosa, el problema no aparece) — no
  alcanza con reproducir el positivo.
- **Controlar por SQL antes Y después de cada acción de UI, no solo al final.** No hacer varios
  pasos de la reproducción seguidos y recién mirar la tabla al final — consultar el estado real
  antes de cada cambio y de nuevo después de cada uno. Esto fue lo que permitió detectar a tiempo,
  en la práctica, que una acción de la UI había vaciado una tabla completa sin que se pidiera — si
  solo se hubiera mirado al final, esa pérdida de datos hubiera pasado desapercibida y podría
  haberse confundido con parte del comportamiento del bug.

Esta reproducción aislada es la validación más fuerte antes de escribir el reporte — más confiable
que la lectura de código sola.

## Paso 6 — Redactar el hallazgo

Ver skill `redactar-reporte-de-bug` para el formato.

## Nota

Es normal que la hipótesis se corrija más de una vez en el camino (pasó en la práctica: una lectura
inicial de "el dato está vacío en la tabla" resultó ser un artefacto de la consulta, y una lectura
inicial de "el parámetro no se usa en la query" resultó ser un caso de dos métodos con el nombre
cruzado). El objetivo no es acertar a la primera, es seguir verificando contra evidencia real en
cada paso en vez de asumir.
