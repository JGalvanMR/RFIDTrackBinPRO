# 📦 RFIDTrackBin — Sistema de Rastreo de Bins por RFID

---

## 🧭 Propósito

RFIDTrackBin es una aplicación Android nativa (Xamarin/C#) diseñada para gestionar el ciclo de vida físico de contenedores (bins/cajones) dentro de instalaciones de almacenamiento en frío. Utiliza tecnología RFID UHF y NFC para registrar movimientos de inventario, entradas y salidas de producto, garantizando trazabilidad por proveedor, rancho y tabla de origen. Opera sobre una conexión directa a SQL Server sin capa de servicio intermedia.

---

## ⚙️ Responsabilidades

- Autenticar operadores mediante credenciales en base de datos SQL Server, con soporte alternativo por tarjeta NFC.
- Detectar y leer etiquetas RFID UHF mediante hardware Unitech PA768 / HT730 integrado al dispositivo.
- Registrar movimientos de tres tipos: **Inventario (I)**, **Entradas (E)** y **Salidas (S)** en tablas maestras de SQL Server.
- Validar cada etiqueta escaneada contra un catálogo centralizado (`Tb_RFID_Catalogo`) antes de aceptarla.
- Resolver y crear automáticamente relaciones proveedor→rancho→tabla cuando se procesan fletes externos.
- Persistir el estado de sesiones activas (inventario, entradas, salidas) en `SharedPreferences` para sobrevivir rotaciones de pantalla o retorno desde Home.
- Gestionar actualizaciones automáticas de la APK comparando versiones contra un servidor HTTP.
- Registrar errores y eventos en un archivo de log local thread-safe.
- Proveer una interfaz de baja manual de etiquetas RFID mediante botón flotante (FAB) con drag-and-drop.
- Mostrar en pantalla el estado de conexión del lector RFID en tiempo real.

---

## 🔄 Flujo de Funcionamiento

### 1. Arranque y Autenticación (`LoginActivity`)
1. La actividad solicita permisos de ubicación y Bluetooth al sistema operativo.
2. Se inicia `InicializarAsync()` en background: verifica WiFi, conectividad de red, disponibilidad del servidor HTTP y carga la lista de usuarios desde `Tb_RFID_Usuarios`.
3. El operador selecciona su nombre en un `Spinner` e introduce su contraseña (solo mayúsculas por filtro `InputFilterAllCaps`).
4. El sistema valida las credenciales localmente en `DataTable responsables` y luego confirma contra la base de datos mediante `getTb_RFID_Login()` con parámetros SQL.
5. Si las credenciales son válidas, navega a `MainActivity` pasando `usuario`, `ubicacion`, `idUnidadNegocio` y `BajaCajones` como extras del `Intent`.
6. Alternativamente, si se detecta una tarjeta NFC (`ActionTechDiscovered`), el UID se compara contra una lista hardcodeada de UIDs permitidos y se inicia sesión directamente.

### 2. Inicialización del Lector RFID (`MainActivity`)
1. `MainActivity` registra manejadores globales de excepciones no capturadas.
2. Lanza `InitializeReader()` en `Task.Run`, que instancia `RG768Reader` y espera hasta 5 segundos (50 × 100 ms) a que el estado sea `Connected`.
3. Al conectar, configura el mapeo de la tecla gatillo física según el modelo del dispositivo (`PA768` → código 294; `HT730` → código 298).
4. Inicia `MonitorReaderStatus()` en background, que verifica el estado del lector cada 10 segundos.
5. Carga el catálogo completo de etiquetas (`Tb_RFID_Catalogo`) en un `HashSet<string>` para validación O(1).

### 3. Operación de Inventario (`InventarioFragment`)
1. Carga las áreas disponibles desde `Tb_RFID_Areas` filtradas por la ubicación del usuario.
2. El operador selecciona un área y pulsa **Inicio Inventario** en el menú de opciones.
3. Se inserta un registro en `Tb_RFID_Inventario` y se persiste el ID resultante en `SharedPreferences`.
4. El operador presiona el gatillo físico: se invoca `Inventory6c()` del SDK de Unitech.
5. Cada etiqueta recibida en `OnRfidUhfReadTag` es validada contra `CatalogoEPCSet`; si es válida y no duplicada (via `_epcLeidosSet`), se agrega a la lista visible.
6. El operador pulsa **Guardar**: los tags se insertan en `Tb_RFID_DetInv` dentro de una transacción SQL, evitando duplicados con `NOT EXISTS`.
7. Simultáneamente se actualiza `Tb_RFID_Catalogo` con el último movimiento (`Tipo='I'`, área, unidad de negocio).
8. Al finalizar el inventario, se registra `HoraCierre` en `Tb_RFID_Inventario` y se actualiza `FechaUltimoMovimiento` en el catálogo.

### 4. Operación de Entradas (`EntradasFragment`)
1. Carga proveedores desde `Tb_RFID_Proveedores` filtrados por `IdUnidadNegocio`.
2. La selección de proveedor dispara la carga de ranchos; la selección de rancho dispara la carga de tablas (cascada de spinners).
3. Opcionalmente se carga un flete pendiente desde una API REST externa, que resuelve y crea automáticamente el proveedor, rancho y tabla si no existen en la unidad de negocio actual.
4. El operador inicia la entrada: se inserta en `Tb_RFID_Mstr` con `TipoMov='E'` y se persiste la sesión.
5. El flujo de escaneo y guardado es equivalente al de inventario, insertando en `Tb_RFID_Det`.
6. Al finalizar, se registra `HoraCierre` en `Tb_RFID_Mstr` y se actualiza `FechaUltimoMovimiento` en el catálogo.

### 5. Operación de Salidas (`SalidasFragment`)
Flujo análogo a Entradas, con `TipoMov='S'`. Los proveedores se obtienen desde vistas (`vwProveedor`, `vwRanchos`, `vwTablas`) cruzando con recepciones de los últimos 365 días.

### 6. Baja Manual de Etiquetas (FAB)
1. Solo visible si el campo `BajaCajones` del usuario es `"True"`.
2. El FAB es arrastrable; un toque corto abre el diálogo `BajaRFID`.
3. El operador escanea etiquetas QR mediante `ScanReceiver` (broadcast `unitech.scanservice.data`).
4. Cada etiqueta es validada contra `CatalogoEPCSet` antes de agregarse.
5. Al guardar, se actualiza `IdStatus = 2` en `Tb_RFID_Catalogo` para cada EPC seleccionado.

---

## 📐 Reglas de Negocio

### 🔒 Restricciones

- **R-01**: Solo pueden iniciar sesión usuarios con `idestatus = 1` AND `RFIDTrackBin = 1` en `Tb_RFID_Usuarios`.
- **R-02**: La contraseña se fuerza a mayúsculas mediante `InputFilterAllCaps`; el sistema no acepta contraseñas en minúsculas.
- **R-03**: No se puede iniciar escaneo RFID si no se ha presionado **Inicio** de la operación (inventario/entrada/salida).
- **R-04**: No se puede iniciar una segunda operación del mismo tipo si hay una activa (guardada en `SharedPreferences`).
- **R-05**: Las etiquetas con `IdStatus = 2` han sido dadas de baja y ya no participan en operaciones normales (no aparecen en el catálogo activo).
- **R-06**: La funcionalidad de Baja de Cajones (FAB) solo está disponible para usuarios con `BajaCajones = 'True'`.
- **R-07**: Los usuarios `DESCARGUE` y `SISTEMAS` son redirigidos directamente al fragmento de Verificación al iniciar sesión.
- **R-08**: No se permiten tags duplicados dentro de la misma sesión de guardado (`NOT EXISTS` en el INSERT de detalle).

### ✅ Validaciones

- **V-01**: Toda etiqueta RFID escaneada debe existir en `Tb_RFID_Catalogo` (`IdStatus = 1`) para ser aceptada.
- **V-02**: El proveedor debe ser seleccionado antes de poder seleccionar rancho; el rancho antes de poder seleccionar tabla (validación en cascada).
- **V-03**: Debe seleccionarse un área válida (posición > 0 en el spinner) antes de habilitar el inicio de inventario.
- **V-04**: La operación no puede finalizarse si el `IdConseInv` / `IdConse` es menor o igual a 0.
- **V-05**: En la baja de cajones, cada EPC escaneado se valida contra `CatalogoEPCSet` antes de agregarse al diálogo.
- **V-06**: Las credenciales NFC válidas son las únicas presentes en la lista `uidsPermitidos` definida en `ValidarLoginPorNFC`.
- **V-07**: Al restaurar una sesión persistida, se verifica contra la base de datos que el registro sigue con estado `'A'` (activo) antes de reanudarla.
- **V-08**: El proveedor y sus derivados (rancho, tabla) deben existir en la misma `IdUnidadNegocio` del dispositivo para poder iniciar una operación.

### 🔁 Agrupaciones

- **AG-01**: Las áreas de inventario están agrupadas por `UnidadNegocio`, que corresponde a la `UBICACION` del usuario logueado.
- **AG-02**: Los proveedores disponibles para salidas se agrupan mediante UNION de tres fuentes: `tb_mstr_recepcion_mp`, `tb_mstr_recepcion_pt` y `tb_mstr_recepcion_esparrago`, limitados a los últimos 365 días.
- **AG-03**: El catálogo de EPCs se indexa en dos dimensiones: por `IdClaveTag` (EPC físico) y por `IdClaveInt` (clave interna), ambas en el mismo `HashSet`.

### ⚙️ Reglas Operativas

- **RO-01**: Al cerrar una sesión huérfana (detectada al regresar al fragmento), el sistema actualiza el estado a `'C'` (Cancelado) en lugar de eliminar el registro.
- **RO-02**: Si un proveedor, rancho o tabla del flete no existe en la unidad de negocio destino, el sistema los crea automáticamente usando datos de otra unidad como plantilla, con estrategia de fallback (buscar en unidad origen → buscar en cualquier unidad → crear por defecto con el código como nombre).
- **RO-03**: El insert de detalle usa `NOT EXISTS` para garantizar idempotencia: guardar el mismo conjunto de tags dos veces no genera duplicados.
- **RO-04**: `FechaUltimoMovimiento` en el catálogo se actualiza SOLO al finalizar la operación (cierre), no al guardar parciales.
- **RO-05**: El modo de prueba (`modoPrueba = true`) en `ActualizarCatalogoTagsAsync` hace `ROLLBACK` de los cambios al catálogo, permitiendo probar sin alterar datos. **Actualmente está activo en el flujo de inventario** (`modoPrueba: true` hardcodeado en la llamada de `BtnGuardarInventario_Click`).
- **RO-06**: La posición del FAB se puede persistir mediante `FabPreferencesHelper` usando `SharedPreferences` nombrado `"FabPreferences"`.
- **RO-07**: Al detectar una nueva versión del APK (comparando `versionCode`), se descarga y presenta al usuario para instalación mediante `FileProvider`.

---

## 🔗 Dependencias

| Tipo | Nombre / Identificador | Propósito |
|---|---|---|
| **Base de datos** | SQL Server (`GAB_Irapuato` en `189.206.160.206:2352`) | Persistencia central de todas las operaciones |
| **SDK nativo** | `libunitechRFID` (Unitech RFID SDK v1.0.41) | Comunicación con lector RFID UHF integrado |
| **Paquete NuGet** | `Newtonsoft.Json 13.0.3` | Deserialización de respuestas de API REST (fletes) |
| **Paquete NuGet** | `System.Data.SqlClient` | Acceso a SQL Server |
| **Paquete NuGet** | `Xamarin.Essentials 1.6.1` | Permisos y utilidades de plataforma |
| **Paquete NuGet** | `Xam.Plugin.DeviceInfo 4.1.1` | Obtención del ID único del dispositivo (IMEI) |
| **Paquete NuGet** | `Xamarin.AndroidX.AppCompat`, `Xamarin.Google.Android.Material` | Componentes UI Material Design |
| **API REST externa** | `Tb_RFID_UnidadNegocio.getFletesPendientes` (URL dinámica por BD) | Obtención de fletes pendientes para entradas |
| **Servidor HTTP** | `http://189.206.160.206:81/EmbarquesApk/RFIDTrackBin/version.txt` | Verificación de actualizaciones de APK |
| **Broadcast Android** | `unitech.scanservice.data` | Recepción de datos del escáner QR integrado |
| **Broadcast Android** | `com.unitech.RFID_GUN.PRESSED/RELEASED` | Detección del gatillo físico RFID |
| **FileProvider** | `${applicationId}.fileprovider` | Instalación segura de APK descargada |
| **Vistas SQL** | `vwProveedor`, `vwRanchos`, `vwTablas` | Catálogos de proveedores para el módulo de Salidas |

---

## ⚠️ Riesgos Técnicos

- **RT-01 — Credenciales hardcodeadas en código fuente**: La cadena de conexión SQL con usuario `sa`, contraseña `Gabira1` e IP pública está literalmente en `LoginActivity.cadenaConexionLogin` y `MainActivity.cadenaConexion`. Cualquier persona con acceso al APK decompilado tiene acceso total a la base de datos de producción.
- **RT-02 — Comunicación sin cifrado**: Toda la comunicación con SQL Server y el servidor HTTP ocurre en texto plano sobre la red. Un atacante en la misma red puede interceptar credenciales y datos.
- **RT-03 — UIDs NFC hardcodeados**: La validación NFC en `ValidarLoginPorNFC` compara contra una lista estática en el código (`04AABBCCDD`, `12345678ABCDEF`, `04774211B506D0`). Agregar o revocar tarjetas requiere recompilar y redistribuir el APK.
- **RT-04 — Modo prueba activo en producción**: En `InventarioFragment.BtnGuardarInventario_Click`, la llamada a `ActualizarCatalogoTagsAsync` tiene `modoPrueba = true` con el comentario `"cambiar a false en producción"`. Esto significa que el catálogo **nunca se actualiza** al guardar un inventario.
- **RT-05 — Sin retry ni circuit-breaker en conexiones SQL**: Todas las operaciones de base de datos fallan inmediatamente ante cualquier error de red. No hay reintentos ni manejo de reconexión, lo cual en un ambiente de almacén con WiFi inestable resulta en pérdida de operaciones.
- **RT-06 — Acoplamiento directo a SQL Server desde la UI**: Los fragmentos Android ejecutan queries directamente contra SQL Server. Un cambio de esquema de BD rompe el cliente sin posibilidad de actualización parcial.
- **RT-07 — `StrictMode.PermitAll()` en hilo principal**: `getData()` en `LoginActivity` llama explícitamente a `StrictMode.SetThreadPolicy(new Builder().PermitAll())`, anulando las protecciones del sistema operativo contra operaciones de red en el hilo UI.
- **RT-08 — Conexión con `Connect Timeout = 0`**: La cadena de conexión tiene `Connect Timeout = 0`, lo que significa espera indefinida si el servidor no responde. Esto puede colgar la aplicación permanentemente.
- **RT-09 — `DataSet dsLogin` compartido entre sesiones**: El `DataSet dsLogin` en `LoginActivity` es un campo de instancia que se limpia con `.Clear()` pero nunca se descarta entre intentos de login, acumulando referencias.
- **RT-10 — Código de método `ObtenerNombrePorClave` en `SalidasFragment` con SQL dinámico**: El método construye consultas SQL usando interpolación de nombres de columna y tabla (`$"SELECT TOP 1 {campoNombre} FROM {vista} WHERE {campoClave} = @clave"`), lo cual permite inyección de SQL a nivel de estructura si los parámetros de nombre son controlables externamente.

---

## 🧪 Casos Edge

- **CE-01**: El operador presiona el gatillo RFID justo cuando el lector está en proceso de reconexión (`_isInitializingReader = true`). El sistema mostrará "Lector no disponible" pero no intentará reconectar automáticamente.
- **CE-02**: Un flete tiene un proveedor cuyo `Prov_Clave` existe en la unidad destino pero con `Activo = 0`. El sistema lo encontrará con `EncontrarPosicionEnDataTable` pero la query de carga solo trae registros con `Activo = 1`, generando una posición -1 y un error de restauración.
- **CE-03**: El usuario guarda tags, luego la aplicación se cierra forzosamente antes de que `GuardarSesionSalida()` escriba en `SharedPreferences`. La sesión se pierde sin posibilidad de recuperación.
- **CE-04**: El catálogo está vacío (`CatalogoEPCSet.Count == 0`). La validación de EPCs dispara una recarga del catálogo y retorna `false`, rechazando todas las etiquetas hasta que la recarga termine. No hay mecanismo de espera ni notificación al operador.
- **CE-05**: En `VerificacionFragment`, si `_catalogoLoadTask` tarda más de 2 segundos, `ValidaEPCAsync` devuelve `true` por defecto (fallback permisivo), aceptando etiquetas que podrían no estar en el catálogo.
- **CE-06**: Se escanean más de ~1,000 tags en una sola sesión de inventario. El `tagsLeidos` List crece sin límite y el `NotifyDataSetChanged()` repetido sobre el `GridView` puede generar jank severo en la UI.
- **CE-07**: El servidor de actualizaciones no responde. `getData()` lanza una excepción que es silenciada por el `catch` vacío en `validateAppUpdateAsync`, y la versión mostrada en pantalla será la local correctamente.
- **CE-08**: Dos operadores en dos dispositivos inician simultáneamente una operación con el mismo proveedor/rancho/tabla inexistente. Ambos intentarán crear los registros; la protección `WHERE NOT EXISTS` en los INSERTs previene duplicados, pero puede haber condición de carrera en SQL Server con aislamiento READ COMMITTED por defecto.

---

## 🧱 Suposiciones Detectadas

- **S-01**: Se asume que el dispositivo siempre tiene conectividad WiFi estable hacia el SQL Server durante toda la operación. No hay modo offline.
- **S-02**: Se asume que `idUnidadNegocio` recibido desde `LoginActivity` siempre es un entero válido parseable; `int.Parse(_activity.idUnidadNegocio)` no tiene manejo de `FormatException`.
- **S-03**: Se asume que el servidor SQL tiene el esquema exacto esperado (nombres de tablas, columnas, vistas). No hay migración ni versionado de esquema.
- **S-04**: Se asume que solo existe una instancia de `MainActivity` activa; la referencia estática `instance` no contempla escenarios multi-ventana.
- **S-05**: Se asume que `Build.Device` retornará exactamente `"PA768"` o `"HT730"` en los dispositivos de producción. Cualquier variación de fabricante silencia la configuración del gatillo sin error.
- **S-06**: Se asume que `Tb_RFID_DetInv_Staging` existe en la base de datos y tiene permiso `TRUNCATE` para el usuario `sa` (usado en el método de guardado bulk de inventario, aunque actualmente no se alcanza porque `SetButtonClickV1` no está conectado al botón).
- **S-07**: Se asume que el campo `IdArea` en `Tb_RFID_Areas` siempre es parseable como `int`. No hay validación de tipo antes de `int.Parse(areas.Rows[indice]["IdArea"].ToString())`.

---

## 📈 Recomendaciones Técnicas

1. **Eliminar credenciales del código fuente de forma urgente**: Migrar la cadena de conexión a un archivo de configuración cifrado en el dispositivo (Android Keystore), o implementar una capa de API REST con autenticación por token JWT que sirva como único punto de entrada a los datos.

2. **Activar el flag de producción en inventario**: Cambiar `modoPrueba: true` a `modoPrueba: false` en `BtnGuardarInventario_Click` o eliminar el parámetro si ya no se requiere el modo de prueba.

3. **Migrar UIDs NFC a la base de datos**: Reemplazar la lista hardcodeada en `ValidarLoginPorNFC` por una consulta a `Tb_RFID_Usuarios` con un campo `NFC_UID`, permitiendo gestión sin recompilación.

4. **Implementar `Connect Timeout` finito y política de reintentos**: Cambiar a `Connect Timeout=15` en la cadena de conexión e implementar `Polly` o lógica de retry exponencial para las operaciones de red.

5. **Reemplazar el método `ObtenerNombrePorClave` en `SalidasFragment`**: Sustituir la interpolación de estructura SQL por consultas específicas con nombres de tabla y columna fijos, eliminando el vector de inyección estructural.

6. **Implementar una capa de repositorio**: Extraer toda la lógica SQL de los fragmentos a clases de repositorio independientes (`IInventarioRepository`, `IEntradasRepository`, etc.), facilitando pruebas unitarias y mantenimiento.

7. **Añadir validación de tipo antes de `int.Parse`**: En todos los puntos donde se hace `int.Parse(someString)`, añadir `int.TryParse` con manejo del caso de fallo para evitar `FormatException` en producción.

8. **Activar HTTPS en el servidor de APK y actualizaciones**: Migrar `http://189.206.160.206:81/...` a HTTPS para prevenir ataques de tipo man-in-the-middle durante la descarga de actualizaciones.

9. **Eliminar `SetButtonClickV1`**: El método está muerto (no está conectado a ningún botón) pero referencia `Tb_RFID_DetInv_Staging` que puede no existir. Debe removerse para evitar confusión.

10. **Establecer un límite máximo de tags por sesión**: Añadir una validación que avise al operador cuando el número de tags supere un umbral configurable (ej. 500), previniendo degradación de la UI y timeouts en los inserts masivos.

---

## 🧾 Resumen Ejecutivo

RFIDTrackBin es la aplicación de bodega en mano que permite al personal registrar con un escáner RFID todos los movimientos de contenedores (cajones) dentro de las instalaciones de almacenamiento en frío. Cuando un camión llega con producto, el operador escanea los cajones y el sistema los registra como **entradas**; cuando salen hacia distribución, se registran como **salidas**; y periódicamente el personal realiza conteos físicos llamados **inventarios**. El sistema también permite dar de baja contenedores físicamente dañados o fuera de servicio.

La aplicación funciona conectada directamente a la base de datos central de la empresa a través de la red WiFi de la planta. Cada movimiento queda vinculado al proveedor, rancho y tabla agrícola de origen del producto, lo que permite trazabilidad completa desde el campo hasta el embarque.

**Situación actual**: La aplicación cumple con su función operativa principal, pero presenta **riesgos de seguridad críticos** que deben atenderse antes de cualquier expansión: las credenciales de la base de datos están visibles en el código instalado en los dispositivos, lo que representa una exposición directa a los datos de producción de la empresa. Adicionalmente, existe un **error funcional confirmado** en el módulo de inventario donde los cambios al catálogo de etiquetas no se guardan realmente (modo de prueba activo), lo que significa que el sistema de inventario no refleja los movimientos en el catálogo central como se espera.
