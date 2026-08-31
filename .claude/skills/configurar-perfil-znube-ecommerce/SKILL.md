---
name: configurar-perfil-znube-ecommerce
description: Cómo dar de alta el storeId de Mercado Libre de un cliente nuevo para poder usar ZNubeEcommerceMcp. Usar cuando se necesite trazabilidad de una orden de venta de ecommerce (Mercado Libre) de un cliente que todavía no tiene perfil configurado.
---

# ZNubeEcommerceMcp — dar de alta el storeId de un cliente

Un perfil acá es un **cliente real** (no un ambiente de prueba interno) y guarda únicamente su
`storeId` de Mercado Libre — nunca un token.

## Paso 1 — Conseguir el storeId

Es el valor de `ZooLogic.ECOM.IDVINC` en la base de ese cliente (`DRAGONFISH_<nombre>`). Si ya
hay acceso SQL vivo a esa base por otro motivo del incidente, se puede confirmar con:

```sql
SELECT codigo, idvinc, tipoecom, cuenta, cuentacom, ultorder FROM ZooLogic.ECOM
```

Pero **no vale la pena restaurar un backup solo para conseguir este dato** — es más eficiente
pedírselo directamente a la persona (SAL, el REDZOO, o quien tenga el acceso al zNube del
cliente) y guardarlo una vez.

## Paso 2 — Dar de alta el perfil

```
dotnet McpServers/ZNubeEcommerceMcp/bin/Debug/net10.0/ZNubeEcommerceMcp.dll agregar-perfil <perfil> <storeId>
```

Corrido desde la raíz del repo, ej.:
```
dotnet McpServers/ZNubeEcommerceMcp/bin/Debug/net10.0/ZNubeEcommerceMcp.dll agregar-perfil DELFINES 123456789
```

Usar como nombre de perfil el nombre del cliente (mismo criterio que se usa para nombrar carpetas
de backups en el Espacio Compartido — ej. `DELFINES`, `TIGRES`).

## Paso 3 — Rebuild + reload (mismo gotcha que los demás MCP)

`agregar-perfil` escribe en el `appsettings.secrets.enc` de la raíz, pero el MCP que ya está
corriendo lee la copia en su propio `bin/`:

1. Chequear que no haya un proceso `ZNubeEcommerceMcp.dll` huérfano:
   ```powershell
   Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*ZNubeEcommerceMcp.dll*" -and $_.Name -eq "dotnet.exe" } | Select-Object ProcessId
   ```
   Si aparece alguno, matarlo con `Stop-Process -Id <pid> -Force`.
2. Rebuildear:
   ```
   dotnet build McpServers/ZNubeEcommerceMcp
   ```
3. Recargar la ventana de VS Code (`Developer: Reload Window`).

## El token nunca se da de alta acá

El token de `zNube-token` no tiene comando de alta porque no se persiste — se pide como parámetro
directamente en cada llamada a `obtener_orden`/`buscar_ordenes`/etc., fresco cada vez (lo tiene
MDA, rota cada cierto tiempo).
