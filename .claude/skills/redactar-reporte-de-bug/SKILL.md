---
name: redactar-reporte-de-bug
description: Formato exacto para redactar un bug de Dragonfish para asignar/cargar, una vez que un análisis (con o sin ayuda de este agente) confirmó la causa raíz en el código fuente. Usar al cerrar la investigación de un incidente que resultó ser un bug real de la aplicación, antes de entregarlo para que un programador lo tome.
---

# Formato del reporte de bug

Cuatro secciones, en este orden. No mezclar el análisis técnico con la descripción del síntoma —
van separados a propósito.

## 1. Título

Una sola línea, el síntoma tal cual lo diría quien lo sufre — en lenguaje llano, sin nombres de
tablas ni de archivos. Ejemplo real:

> Aparecen cheques de terceros repetidos en todos los clientes en consulta de deuda

## 2. Detalle

Dos partes cortas:
- Qué se ve, en la pantalla/flujo real (qué se busca, qué aparece mal).
- Bajo qué condiciones pasa — en términos de negocio/configuración, no de código (ej. "al tener
  activo tal parámetro, tal tipo de cliente, y tal tipo de dato cargado").

## 3. Pasos para reproducirlo

**Siempre validados en una base propia/de prueba (nunca sobre datos de un cliente)** — mismo
criterio que ya seguimos en `analizar-orden-ecommerce`: preferir lo más simple y controlado antes
que depender de una base real. Cada paso tiene que ser preciso y ya haber sido probado, no
teórico:
- Qué dato hay que armar (con SQL directo si es más simple, ej. cargar un `GLOBALID` a mano para
  simular centralización).
- Por qué medio real de la UI se carga cada cosa (ej. "por medio de un comprobante de caja con
  estado 'En Cartera'" — no alcanza con decir "cargar un cheque", hay que decir cómo se cargó de
  verdad en la prueba).
- La ruta exacta de menú para cualquier configuración/parámetro (ej. "Parámetros del sistema >
  Gestión de ventas > Cuenta corriente").
- Qué se busca y qué resultado se espera ver.

## 4. Análisis con IA

Sección aparte, con ese título literal — el análisis técnico de causa raíz que arma este agente.
Estructura interna:

```
UBICACIÓN
Archivo: <ruta completa>
Método: <nombre>
Rama/parte de la consulta afectada: <cuál, con líneas aproximadas>

CAUSA RAÍZ
<explicación técnica precisa, citando el fragmento de código/SQL real que falla>

CORRECCIÓN SUGERIDA
<qué habría que cambiar, y qué hace falta confirmar antes de cambiarlo si no es obvio>
```

No dar la corrección por hecha si depende de un dato que no existe todavía (ej. "hace falta
confirmar si la tabla puede llevar tal columna antes de asumir la solución") — mejor señalar la
pregunta abierta que inventar una solución no verificada.
