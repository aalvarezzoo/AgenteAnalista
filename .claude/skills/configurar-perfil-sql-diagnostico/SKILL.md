---
name: configurar-perfil-sql-diagnostico
description: Paso a paso para crear el login SQL de solo lectura y dar de alta un perfil de SqlDiagnosticoMcp (instancia + credenciales). Usar cuando alguien pide diagnosticar/consultar una base Dragonfish por SQL y no hay ningún perfil configurado o funcionando, o cuando haga falta habilitar una base adicional para un login existente.
---

# SqlDiagnosticoMcp — configurar un perfil

Un perfil es una instancia de SQL Server + las credenciales de un login SQL dedicado de solo
lectura. Se guarda cifrado en `appsettings.secrets.enc` (compartido por todos los MCPs del
repo — ver skill `setup-mcp-servers` para generar la clave la primera vez en una máquina nueva).

## Paso 1 — Crear el login SQL de solo lectura

**Esta es la protección real** — no la validación de texto del propio MCP. El login debe usar
**SQL Authentication** (nunca Integrated Security con la cuenta Windows del analista, nunca `sa`)
y tener **únicamente el rol `db_datareader` + el permiso `VIEW DEFINITION`** en las bases que se
vayan a consultar:

```sql
CREATE LOGIN mh_sql_readonly WITH PASSWORD = '<password-fuerte>', CHECK_POLICY = ON;

USE DRAGONFISH_DEMO;
CREATE USER mh_sql_readonly FOR LOGIN mh_sql_readonly;
ALTER ROLE db_datareader ADD MEMBER mh_sql_readonly;
GRANT VIEW DEFINITION TO mh_sql_readonly;
-- Repetir el bloque USE/CREATE USER/ALTER ROLE/GRANT por cada base adicional a habilitar.
```

**El `GRANT VIEW DEFINITION` no es opcional** — sin él, `obtener_definicion_objeto` devuelve
`NULL` para *cualquier* vista/SP/función, aunque no esté cifrada — `db_datareader` da permiso para
leer datos, pero no para ver la definición de los objetos. Confirmado en la práctica: las 12 vistas
de `DRAGONFISH_DEMO` devolvían "no tiene definición SQL accesible" (mensaje que sugiere que podría
estar cifrada) cuando en realidad ninguna lo estaba — solo faltaba este permiso. Si se creó un
perfil antes de que este paso existiera, agregar el `GRANT` a mano en cada base ya habilitada.

Si el login ya existe y solo hace falta habilitar una base nueva, alcanza con repetir el bloque
`USE`/`CREATE USER`/`ALTER ROLE` para esa base — no hace falta tocar el `CREATE LOGIN`.

## Paso 2 — Dar de alta el perfil

```
dotnet McpServers/SqlDiagnosticoMcp/bin/Debug/net10.0/SqlDiagnosticoMcp.dll agregar-perfil <perfil> <instancia> <usuario> <password>
```

Corrido desde la raíz del repo, ej.:
```
dotnet McpServers/SqlDiagnosticoMcp/bin/Debug/net10.0/SqlDiagnosticoMcp.dll agregar-perfil TEST .\SQLEXPRESS2022 mh_sql_readonly <password>
```

**Nunca mostrar el password en la respuesta** — confirmar solo perfil, instancia y usuario.

Por default asumir el perfil `TEST` (mismo criterio que `DragonfishApiMcp`) salvo que la persona
mencione explícitamente que es para otra instancia/cliente.

## Paso 3 — Rebuild + reload (mismo gotcha que DragonfishApiMcp)

`agregar-perfil` escribe en el `appsettings.secrets.enc` de la raíz, pero el MCP que ya está
corriendo lee la copia en su propio `bin/` — son dos archivos físicos distintos, y el bin-copy
solo se sincroniza en build. Pasos:

1. **Chequear que no haya un proceso `SqlDiagnosticoMcp.dll` huérfano**:
   ```powershell
   Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*SqlDiagnosticoMcp.dll*" -and $_.Name -eq "dotnet.exe" } | Select-Object ProcessId
   ```
   Si aparece alguno, matarlo con `Stop-Process -Id <pid> -Force` — si no, el rebuild falla con
   "archivo bloqueado" (MSB3021/MSB3026).
2. **Rebuildear** para que se copie el `.enc` actualizado al bin de salida:
   ```
   dotnet build McpServers/SqlDiagnosticoMcp
   ```
3. **Recargar la ventana de VS Code** (`Developer: Reload Window`) — recién ahí el MCP arranca
   leyendo el archivo correcto.

Si se prueba el perfil sin este paso, el error de conexión no dice nada real sobre si el perfil
está bien o mal (puede estar leyendo la copia vieja, sin el perfil nuevo).
