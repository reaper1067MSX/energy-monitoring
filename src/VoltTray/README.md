# VoltTray — Host de Escritorio y Modos de Ejecución

> Aplicación nativa y ultra-ligera para la bandeja del sistema (System Tray) de Windows 11 y 10. Monitorea en tiempo real el UPS **Forza FX-2200 LCD** y su banco de baterías **2× Forza FUB-1290** (24V 9.0Ah AGM).

---

## 1. Perfiles de Ejecución en JetBrains Rider y Visual Studio

Al abrir `src/VoltTray.sln` o `VoltTray.sln` en JetBrains Rider, verás los perfiles de ejecución configurados en la barra superior:

| Perfil de Ejecución | Argumentos CLI | Descripción |
|---|---|---|
| **`VoltTray (Hardware USB)`** | *(ninguno)* | **Modo Producción normal.** Conecta directamente al puerto USB HID del UPS (`VID_0665&PID_5161`) y sondea cada 1500 ms. |
| **`VoltTray (Simulación UI)`** | `--simulate` | **Modo Desarrollo / Demo.** Genera telemetría realista con micro-jitter para probar y diseñar la UI sin necesitar el UPS conectado físicamente al PC. |
| **`VoltTray (Dry-Run Seguro)`** | `--dry-run` | Conecta al hardware real pero **simula** las acciones de apagado crítico de Windows (seguridad total para pruebas de corte). |
| **`VoltTray (Simulación + Dry-Run)`** | `--simulate --dry-run` | Combina telemetría simulada con protección de apagado simulada. |

---

## 2. Parámetros de Línea de Comandos (CLI)

Puedes ejecutar el proyecto desde la terminal o PowerShell con cualquiera de los siguientes argumentos:

```powershell
# Ejecución normal en hardware real
dotnet run --project src/VoltTray

# Modo simulación interactivo (sin hardware conectado)
dotnet run --project src/VoltTray -- --simulate

# Modo simulación con intervalo de sondeo personalizado (ej: 1000 ms)
dotnet run --project src/VoltTray -- --simulate --interval 1000

# Conectar al UPS real con protección de apagado simulada
dotnet run --project src/VoltTray -- --dry-run
```

### Tabla de Parámetros

| Parámetro | Alias | Valores | Valor por Defecto | Descripción |
|---|---|---|---|---|
| `--simulate` | `--sim`, `--mock` | *(flag)* | Desactivado | Activa el generador en memoria de tramas Voltronic Protocol 'T' con micro-jitter físico. |
| `--dry-run` | | *(flag)* | Desactivado | Simula el apagado de seguridad en caso de descarga o corte crítico sin apagar el sistema operativo. |
| `--interval` | | Entero en ms ($\ge 250$) | `1500` | Frecuencia de actualización del ciclo de sondeo USB. |

---

## 3. Controles y Navegación en el Sistema

* **Ícono Dinámico en la Bandeja (System Tray):**
  - Muestra un glifo geométrico de 16–32 px según el estado operacional:
    - 🟢 **Verde (`LineNormal`):** UPS operando en red eléctrica normal.
    - 🟡 **Ámbar (`OnBattery`):** Operando en batería con porcentaje o minutos restantes.
    - 🔴 **Rojo (`BatteryLow`):** Batería baja / inminente apagado coordinado.
    - 🔵 **Azul Claro (`ChargingAvr`):** AVR Boost / Buck activo (regulación de tensión).
    - ⚫ **Gris (`Paused` / `CommunicationsLost`):** Sensor pausado o cable USB desconectado.
* **Tarjeta Popover Split-Cockpit Matrix (50/50):**
  - Clic izquierdo sobre el ícono de bandeja para abrir la tarjeta de **372 px** exactos.
  - Muestra las 16 métricas divididas simétricamente: Red/Inversor a la izquierda y Banco de Baterías 24V (2× FUB-1290) a la derecha, con consumo activo del PC al centro.
* **Botones de Acción Rápida (44 px):**
  - **Prueba de Batería:** Envía comando `T\r` para autodiagnóstico de 10s.
  - **Alarma Activa:** Conmuta el pitido del UPS mediante `Q\r`.
* **Diálogo de Salida:**
  - Al cerrar desde el menú, ofrece 3 alternativas: `Pausar 15m` (libera el puerto USB para otras herramientas), `Cerrar` (apaga la aplicación) o `Cancelar`.

---

## 4. Mutex de Instancia Única

VoltTray utiliza el mutex del sistema `Global\VoltTray_SingleInstance_Mutex`. Si intentas lanzar una segunda instancia mientras una ya está activa, la nueva instancia terminará limpiamente con código `0` en menos de 1 segundo para evitar íconos duplicados en la bandeja del sistema.
