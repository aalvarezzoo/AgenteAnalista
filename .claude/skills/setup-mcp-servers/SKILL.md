---
name: setup-mcp-servers
description: Primeros pasos para levantar los MCP servers de AgenteAnalista en una máquina que nunca los corrió — generar clave de cifrado, crear y cifrar appsettings.secrets.json, compilar. Usar cuando un MCP falla al arrancar en una máquina nueva, o cuando alguien pregunta cómo configurar este repo por primera vez.
---

# Primeros pasos de los MCP servers en una máquina nueva

El repo trae cuatro servidores MCP: `ZlApiMcp`, `DragonfishApiMcp`, `GestionBackupsMcp` y
`SqlDiagnosticoMcp`. Los últimos tres están registrados en `.mcp.json`, que Claude Code levanta
solo al abrir el proyecto. `DragonfishApiMcp` y `SqlDiagnosticoMcp` necesitan estos pasos la
primera vez en una máquina que nunca los corrió — si no, fallan al arrancar (a propósito, con un
error claro en vez de fallar en silencio).

1. **Generar tu propia clave de cifrado** (no se comparte entre integrantes del equipo):
   ```
   dotnet McpServers/DragonfishApiMcp/bin/Debug/net10.0/DragonfishApiMcp.dll generate-key
   ```
   Guardá el hex de 64 caracteres que imprime. (La primera vez hace falta compilar el proyecto
   antes — `dotnet build McpServers/DragonfishApiMcp` — para tener el `.dll`.)

2. **Setearla como variable de entorno persistente** (una sola vez, PowerShell como administrador
   si hace falta):
   ```powershell
   [System.Environment]::SetEnvironmentVariable('AGENTEANALISTA_SECRET_KEY', '<tu-clave>', 'User')
   ```
   Reabrir la terminal/VS Code después de esto para que la vea.

3. **Crear `appsettings.secrets.json`** en la raíz del repo (nunca se commitea, está en
   `.gitignore`) con tus propias credenciales. Ejemplo mínimo:
   ```json
   {
     "ZlApi": { "BaseUrl": "", "IdCliente": "", "Authorization": "", "BaseDeDatos": "" },
     "DragonfishApi": { "Perfiles": {} },
     "SqlDiagnostico": { "Perfiles": {} }
   }
   ```
   Para completar `DragonfishApi.Perfiles` o `SqlDiagnostico.Perfiles` ver los skills
   `configurar-perfil-dragonfish-api` y `configurar-perfil-sql-diagnostico` respectivamente —
   ninguno de los dos hace falta completarlo a mano acá, cada uno tiene su propio comando
   `agregar-perfil`.

4. **Cifrarlo:**
   ```
   dotnet McpServers/DragonfishApiMcp/bin/Debug/net10.0/DragonfishApiMcp.dll encrypt
   ```
   Esto genera `appsettings.secrets.enc` en la raíz del repo (tampoco se commitea) — un solo
   archivo, compartido por todos los MCPs que necesiten secretos (cada uno lo copia a su propio
   `bin/` al compilar). Podés borrar el `.json` plano después — el comando `decrypt` lo reconstruye
   si hace falta editarlo más adelante.

5. **Compilar los MCP servers al menos una vez** — `.mcp.json` apunta al `.dll` ya compilado, no a
   `dotnet run`, así que si no se buildea antes, Claude Code no encuentra el ejecutable:
   ```
   dotnet build McpServers/DragonfishApiMcp
   dotnet build McpServers/GestionBackupsMcp
   dotnet build McpServers/SqlDiagnosticoMcp
   ```

6. **Reiniciar VS Code** (o recargar la ventana) para que Claude Code levante los servidores del
   `.mcp.json`.

Cada uno gestiona su propia clave y su propio `appsettings.secrets.json` — no hay una clave ni un
archivo de secretos único para todo el equipo.

**Si en el paso 5 (o antes) tira este error:**
```
error MSB3030: No se pudo copiar el archivo "appsettings.secrets.enc" porque no se encontró.
```
Significa que se saltearon el paso 3/4 — hay que crear y cifrar el `appsettings.secrets.json`
antes de compilar.
