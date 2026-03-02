using System.Linq;
using System.Data;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Media;
using Android.Nfc;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Com.Unitech.Api.Keymap;
using Com.Unitech.Lib.Diagnositics;
using Com.Unitech.Lib.Reader;
using Com.Unitech.Lib.Reader.Event;
using Com.Unitech.Lib.Reader.Params;
using Com.Unitech.Lib.Reader.Types;
using Com.Unitech.Lib.Rgx;
using Com.Unitech.Lib.Transport.Types;
using Com.Unitech.Lib.Types;
using Com.Unitech.Lib.Uhf;
using Com.Unitech.Lib.Uhf.Event;
using Com.Unitech.Lib.Uhf.Params;
using Com.Unitech.Lib.Uhf.Types;
using Com.Unitech.Lib.Util.Diagnotics;
using Java.Lang;
using RFIDTrackBin.enums;
using RFIDTrackBin.Modal;
using RFIDTrackBin.Model;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Exception = System.Exception;
using Math = Java.Lang.Math;
using StringBuilder = System.Text.StringBuilder;

namespace RFIDTrackBin.fragment
{
    public class SalidasFragment : BaseFragment, IReaderEventListener, IRfidUhfEventListener, MainReceiver.IEventLitener
    {
        static string TAG = typeof(SalidasFragment).Name;

        static string keymappingPath = "/storage/emulated/0/Android/data/com.unitech.unitechrfidsample";
        static string android12keymappingPath = "/storage/emulated/0/Unitech/unitechrfidsample/";
        static string systemUssTriggerScan = "unitech.scanservice.software_scankey";
        static string ExtraScan = "scan";

        public int MAX_MASK = 2;
        private int NIBLE_SIZE = 4;

        bool accessTagResult;

        private bool _isFindTag = false;
        private bool _isDisposed = false;
        private readonly object _inventoryLock = new object();
        private bool _isInventoryInProgress = false;
        private DateTime _lastTriggerTime = DateTime.MinValue;

        #region Button
        private Button btnGuardar;
        #endregion

        #region TextView
        TextView connectedState;
        // FIX M2: Eliminado "TextView areaLectura" — declarado pero nunca inicializado
        //         en FindViews ni utilizado en ninguna parte del fragmento.
        //         Dejar campos de UI sin inicializar genera NullReferenceException latente.
        TextView totalCajasLeidas;
        TextView txtTotalAcumulado;
        #endregion

        #region Spinner
        Spinner sprProveedor;
        Spinner sprRancho;
        Spinner sprTabla;
        #endregion

        #region SoundPool
        private SoundPool soundPool;
        private int beepSoundId;
        #endregion

        MainReceiver mReceiver;
        Bundle tempKeyCode = null;

        GridView gvObject;
        private List<TagLeido> tagEPCList = new List<TagLeido>();
        private myGVitemAdapter adapter;

        // FIX B1: Eliminado "DataSet ds" — nunca se usaba. Instancia innecesaria en memoria.

        // FIX A1: Eliminado modificador "static" de las tres DataTables.
        //         Con "static", todas las instancias del fragmento compartían el mismo estado,
        //         causando que datos de una sesión anterior contaminaran la siguiente
        //         al navegar y volver al fragmento. Además, los DataTable estáticos nunca
        //         son recolectados por el GC mientras la clase exista → memory leak.
        public DataTable vwProveedor = new DataTable("vwProveedor");
        public DataTable vwRanchos = new DataTable("vwRanchos");
        public DataTable vwTablas = new DataTable("vwTablas");

        int totalCajasLeidasINT = 0;
        int totalAcumuladoINT = 0;

        string IdClaveTag;
        View vwSalidas;

        string prov_nombre;
        string rch_nombre;
        string tbl_nombre;

        string prov_clave;
        string rch_clave;
        string tbl_clave;

        IMenu _menu;
        int IdConse;
        string tipoMovimiento = "S";

        ProgressBar progressBar;
        RelativeLayout loadingOverlay;

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            return inflater.Inflate(Resource.Layout.SalidasFragment, container, false);
        }

        public override async void OnViewCreated(View view, Bundle savedInstanceState)
        {
            base.OnViewCreated(view, savedInstanceState);

            bool ok = await MainActivity.BtHelper.EnsureBluetoothAsync();
            if (!ok)
            {
                Toast.MakeText(Activity, "Bluetooth es obligatorio para el inventario.", ToastLength.Short).Show();
                return;
            }

            FindViews(view);

            await LoadProveedorAsync(view);

            SetButtonClick();
            InitializeSoundPool();

            HasOptionsMenu = true;

            mReceiver = new MainReceiver(this);
            IntentFilter filter = new IntentFilter();
            filter.AddAction(MainReceiver.rfidGunPressed);
            filter.AddAction(MainReceiver.rfidGunReleased);
            _activity.RegisterReceiver(mReceiver, filter);

            adapter = new myGVitemAdapter(_activity, tagEPCList);
            gvObject.Adapter = adapter;

            sprProveedor.Enabled = true;
            sprRancho.Enabled = false;
            sprTabla.Enabled = false;
            btnGuardar.Enabled = false;

            _activity.EnableNavigationItems(
                Resource.Id.navigation_entradas,
                Resource.Id.navigation_inventario);

            progressBar = view.FindViewById<ProgressBar>(Resource.Id.progressBarGuardar);
            loadingOverlay = view.FindViewById<RelativeLayout>(Resource.Id.loadingOverlay);

            // ISSUE 2: Intentar restaurar sesión activa previa (si el usuario vino de Home)
            await RestaurarSesionSalidaAsync();
        }

        public void InitializeSoundPool()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
                var audioAttributes = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.AssistanceSonification)
                    .SetContentType(AudioContentType.Sonification)
                    .Build();

                soundPool = new SoundPool.Builder()
                    .SetMaxStreams(5)
                    .SetAudioAttributes(audioAttributes)
                    .Build();
            }
            else
            {
                soundPool = new SoundPool(5, Stream.Music, 0);
            }

            beepSoundId = soundPool.Load(_activity, Resource.Drawable.beep, 1);
        }

        #region MenuInflater
        public override void OnCreateOptionsMenu(IMenu menu, MenuInflater inflater)
        {
            inflater.Inflate(Resource.Menu.menu_salidas, menu);
            _menu = menu;
            menu.FindItem(Resource.Id.fletes_pendientes_salidas).SetEnabled(true);
            menu.FindItem(Resource.Id.inicio_salidas).SetEnabled(false);
            menu.FindItem(Resource.Id.final_salidas).SetEnabled(false);
            base.OnCreateOptionsMenu(menu, inflater);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.inicio_salidas:
                    _ = InsertarSalidaAsync(
                        tipoMovimiento,
                        ((MainActivity)Activity).usuario,
                        prov_clave, rch_clave, tbl_clave, "A");

                    _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                    _menu?.FindItem(Resource.Id.final_salidas)?.SetEnabled(true);
                    btnGuardar.Enabled = true;
                    _activity.DisableNavigationItems(
                        Resource.Id.navigation_entradas,
                        Resource.Id.navigation_inventario,
                        Resource.Id.navigation_salidas);
                    return true;

                case Resource.Id.final_salidas:
                    _ = FinalizarSalidaAsync(IdConse);
                    return true;

                default:
                    return base.OnOptionsItemSelected(item);
            }
        }

        private async Task FinalizarSalidaAsync(int idConse)
        {
            await ActualizarHoraCierreAsync(idConse);
            await UpdateFechaUltimoMovimientoAsync(idConse);

            // ISSUE 2: Borrar la sesión persistida al cerrarla limpiamente
            LimpiarSesionSalida();

            sprProveedor.SetSelection(0);
            sprProveedor.Enabled = true;
            sprRancho.SetSelection(0);
            sprTabla.SetSelection(0);
            btnGuardar.Enabled = false;

            _activity.EnableNavigationItems(
                Resource.Id.navigation_entradas,
                Resource.Id.navigation_inventario,
                Resource.Id.navigation_salidas);

            _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
            _menu?.FindItem(Resource.Id.final_salidas)?.SetEnabled(false);
            ClearGridView();
            totalAcumuladoINT = 0;
            txtTotalAcumulado.Text = "0";
        }
        #endregion

        #region INICIAR SALIDA
        public async Task<int> InsertarSalidaAsync(
            string tipoMovimiento, string usuario,
            string entProveedor, string entRancho, string entTabla, string entStatus)
        {
            IdConse = -1;

            const string query = @"
                INSERT INTO [dbo].[Tb_RFID_Mstr]
                    (TipoMov, FechaMov, Usuario, Prov_Clave, Ran_Clave, Tab_Clave, Mstr_Status)
                VALUES
                    (@TipoMov, GETDATE(), @Usuario, @Prov_Clave, @Ran_Clave, @Tab_Clave, @Mstr_Status);
                SELECT SCOPE_IDENTITY();";

            try
            {
                int newId = await Task.Run(() =>
                {
                    using (SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion))
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@TipoMov", tipoMovimiento);
                        cmd.Parameters.AddWithValue("@Usuario", usuario ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Prov_Clave", entProveedor ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Ran_Clave", entRancho ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Tab_Clave", entTabla ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Mstr_Status", entStatus);
                        conn.Open();
                        object result = cmd.ExecuteScalar();
                        return (result != null && int.TryParse(result.ToString(), out int id)) ? id : -1;
                    }
                });

                IdConse = newId;

                // ISSUE 2: Persistir la sesión activa para sobrevivir Home → regreso
                GuardarSesionSalida();
                Log.Debug(TAG, $"Salida insertada en BD: ID={IdConse}");

                Toast.MakeText(Activity, "Inicio De Salida...", ToastLength.Short).Show();
                sprProveedor.Enabled = false;
                sprRancho.Enabled = false;
                sprTabla.Enabled = false;
            }
            catch (Exception ex)
            {
                MainActivity.ShowDialog("Error al iniciar salida en Base de Datos:", ex.Message);
            }

            return IdConse;
        }
        #endregion

        // ─── ISSUE 2: PERSISTENCIA DE SESIÓN DE SALIDA ───────────────────────────
        // Mismo patrón que EntradasFragment. Archivo "rfid_salidas_prefs" independiente.

        private const string PREFS_SALIDAS = "rfid_salidas_prefs";
        private const string PREF_S_ID = "IdConseSalida";
        private const string PREF_S_PROV = "ProvClaveSalida";
        private const string PREF_S_RANCHO = "RchClaveSalida";
        private const string PREF_S_TABLA = "TblClaveSalida";
        private const string PREF_S_ACTIVA = "SalidaActiva";

        private void GuardarSesionSalida()
        {
            if (IdConse <= 0) return;
            var editor = _activity.GetSharedPreferences(PREFS_SALIDAS, FileCreationMode.Private).Edit();
            editor.PutInt(PREF_S_ID, IdConse);
            editor.PutString(PREF_S_PROV, prov_clave ?? "");
            editor.PutString(PREF_S_RANCHO, rch_clave ?? "");
            editor.PutString(PREF_S_TABLA, tbl_clave ?? "");
            editor.PutBoolean(PREF_S_ACTIVA, true);
            editor.Apply();
            Log.Debug(TAG, $"Salida guardada en prefs: ID={IdConse}");
        }

        private void LimpiarSesionSalida()
        {
            _activity.GetSharedPreferences(PREFS_SALIDAS, FileCreationMode.Private).Edit().Clear().Apply();
            Log.Debug(TAG, "Sesión de salida limpiada en prefs");
        }

        /// <summary>
        /// Llamado al final de OnViewCreated, después de cargar spinners.
        /// Restaura el estado completo de la sesión si el usuario regresa desde Home.
        /// </summary>
        private async Task RestaurarSesionSalidaAsync()
        {
            var prefs = _activity.GetSharedPreferences(PREFS_SALIDAS, FileCreationMode.Private);
            if (!prefs.GetBoolean(PREF_S_ACTIVA, false)) return;

            int savedId = prefs.GetInt(PREF_S_ID, -1);
            string savedProv = prefs.GetString(PREF_S_PROV, "");
            string savedRancho = prefs.GetString(PREF_S_RANCHO, "");
            string savedTabla = prefs.GetString(PREF_S_TABLA, "");

            if (savedId <= 0) { LimpiarSesionSalida(); return; }

            // Verificar que el registro sigue abierto en la BD
            bool sigueAbierta = await Task.Run(() =>
            {
                try
                {
                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    const string sql = "SELECT COUNT(1) FROM Tb_RFID_Mstr WHERE IdConse=@id AND Mstr_Status='A'";
                    using SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@id", savedId);
                    conn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
                catch { return false; }
            });

            if (!sigueAbierta) { LimpiarSesionSalida(); return; }

            // Restaurar estado en memoria
            IdConse = savedId;
            prov_clave = savedProv;
            rch_clave = savedRancho;
            tbl_clave = savedTabla;

            // Recargar spinners con los valores guardados
            // SalidasFragment carga proveedor con una query diferente (prov_clave en lugar de IdProveedor),
            // por lo que usamos el mismo patrón que funciona en el fragment: selección directa
            await RestaurarSpinnersSalidaAsync(savedProv, savedRancho, savedTabla);

            // Restaurar estado de UI: sesión en progreso
            sprProveedor.Enabled = false;
            sprRancho.Enabled = false;
            sprTabla.Enabled = false;
            btnGuardar.Enabled = true;

            _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
            _menu?.FindItem(Resource.Id.final_salidas)?.SetEnabled(true);

            _activity.DisableNavigationItems(
                Resource.Id.navigation_entradas,
                Resource.Id.navigation_inventario,
                Resource.Id.navigation_salidas);

            Log.Debug(TAG, $"Sesión de salida restaurada desde prefs: ID={IdConse}");
            Toast.MakeText(_activity, $"Salida #{IdConse} en curso (restaurada)", ToastLength.Short).Show();
        }

        /// <summary>
        /// Restaura la selección de los spinners de Salidas.
        /// Usa los métodos y nombres de columna específicos de SalidasFragment:
        /// prov_clave/prov_nombre, rch_clave/rch_nombre, tbl_clave/tbl_nombre.
        /// </summary>
        private async Task RestaurarSpinnersSalidaAsync(string prov, string rancho, string tabla)
        {
            try
            {
                // Paso 1: seleccionar proveedor (ya cargado en LoadProveedorAsync)
                SeleccionarSpinnerPorValorDirecto(sprProveedor, vwProveedor, "prov_clave", prov, "prov_nombre");

                // Paso 2: cargar ranchos del proveedor y seleccionar
                await LoadRanchosPorProveedorAsync(prov);
                SeleccionarSpinnerPorValorDirecto(sprRancho, vwRanchos, "rch_clave", rancho, "rch_nombre");

                // Paso 3: cargar tablas del rancho y seleccionar
                await LoadTablasPorRanchoAsync(rancho, prov);
                SeleccionarSpinnerPorValorDirecto(sprTabla, vwTablas, "tbl_clave", tabla, "tbl_nombre");
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al restaurar spinners de salida: {ex.Message}");
            }
        }

        /// <summary>
        /// Selecciona un item del Spinner buscando el valor en la DataTable.
        ///
        /// HISTORIAL: versiones previas usaban DataTable.Select() con lógica de comillas
        /// basada en int.TryParse y luego en DataType de columna. Ambos enfoques fallaban
        /// con valores alfanuméricos ("R21") en columnas INT (Cannot find column [R21]).
        ///
        /// SOLUCIÓN DEFINITIVA: usar LINQ — inmune al tipo de columna, compara como string.
        /// </summary>
        private void SeleccionarSpinnerPorValorDirecto(Spinner spinner, DataTable tabla,
            string campoClave, string valorClave, string campoNombre)
        {
            if (tabla == null || spinner?.Adapter == null || string.IsNullOrEmpty(valorClave)) return;
            try
            {
                if (tabla.Columns[campoClave] == null) return;

                string valorBuscar = valorClave.Trim();
                DataRow[] rows = tabla.AsEnumerable()
                    .Where(r => {
                        if (r[campoClave] == null || r[campoClave] == DBNull.Value) return false;
                        string cellStr = r[campoClave].ToString().Trim();
                        if (cellStr == valorBuscar) return true;
                        // Normalización numérica: "03" == "3", "38" == "38"
                        if (int.TryParse(valorBuscar, out int vInt) && int.TryParse(cellStr, out int cInt))
                            return vInt == cInt;
                        return false;
                    })
                    .ToArray();

                if (rows.Length == 0) return;

                string nombre = rows[0][campoNombre]?.ToString()?.Trim() ?? "";
                for (int i = 0; i < spinner.Adapter.Count; i++)
                {
                    if (string.Equals(spinner.Adapter.GetItem(i)?.ToString()?.Trim(),
                                      nombre, StringComparison.OrdinalIgnoreCase))
                    {
                        spinner.SetSelection(i);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"SeleccionarSpinnerPorValorDirecto: {ex.Message}");
            }
        }

        #region FINALIZAR SALIDA
        public async Task ActualizarHoraCierreAsync(int idConse)
        {
            const string query = @"
                UPDATE Tb_RFID_Mstr
                SET HoraCierre = GETDATE()
                WHERE IdConse = @IdConse";

            try
            {
                int rowsAffected = await Task.Run(() =>
                {
                    using (SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion))
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@IdConse", idConse);
                        conn.Open();
                        return cmd.ExecuteNonQuery();
                    }
                });

                if (rowsAffected > 0)
                    Toast.MakeText(Activity, "Fin de Salida...", ToastLength.Long).Show();
                else
                    MainActivity.ShowToast("No se encontró la salida para actualizar.");
            }
            catch (Exception ex)
            {
                MainActivity.ShowDialog("Error al actualizar HoraCierre:", ex.Message);
            }
        }

        public async Task UpdateFechaUltimoMovimientoAsync(int idConseInv)
        {
            const string query = @"
                UPDATE c
                SET c.FechaUltimoMovimiento = d.FechaCaptura
                FROM Tb_RFID_Catalogo c
                INNER JOIN Tb_RFID_Det d ON c.IdClaveInt = d.IdClaveInt
                WHERE d.IdConseInv = @IdConseInv";

            try
            {
                int rowsAffected = await Task.Run(() =>
                {
                    using (SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion))
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@IdConseInv", idConseInv);
                        conn.Open();
                        return cmd.ExecuteNonQuery();
                    }
                });

                MainActivity.ShowToast($"{rowsAffected} filas actualizadas en el catálogo");
            }
            catch (SqlException sqlEx)
            {
                MainActivity.ShowToast($"Error SQL al actualizar: {sqlEx.Message}");
            }
            catch (Exception ex)
            {
                MainActivity.ShowToast($"Error al actualizar: {ex.Message}");
            }
        }
        #endregion

        #region CICLO DE VIDA
        public override void OnResume()
        {
            base.OnResume();
            ((AndroidX.AppCompat.App.AppCompatActivity)Activity).SupportActionBar.Title = "SALIDAS";
            _activity.currentRfidFragment = this;

            if (_activity?.baseReader != null &&
                _activity.baseReader.State == ConnectState.Connected &&
                _activity.baseReader.RfidUhf != null)
            {
                try
                {
                    _activity.baseReader.AddListener(this);
                    _activity.baseReader.RfidUhf.AddListener(this);
                    InitSetting();
                    UpdateText(IDType.ConnectState, "Connected");
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Error al agregar listeners: {ex.Message}");
                }
            }
            else
            {
                UpdateText(IDType.ConnectState, "Esperando lector...");
            }
        }

        public override void OnPause()
        {
            Log.Debug(TAG, "OnPause - Deteniendo inventario");

            try
            {
                if (_activity?.baseReader?.Action == ActionState.Inventory6c)
                    _activity.baseReader.RfidUhf?.Stop();
            }
            catch { }

            if (_activity?.currentRfidFragment == this)
                _activity.currentRfidFragment = null;

            try
            {
                _activity?.baseReader?.RemoveListener(this);
                _activity?.baseReader?.RfidUhf?.RemoveListener(this);
            }
            catch { }

            try { _activity?.UnregisterReceiver(mReceiver); } catch { }

            base.OnPause();
            Log.Debug(TAG, "OnPause completado");
        }

        public override void OnDestroy()
        {
            _isDisposed = true;

            try
            {
                if (_activity?.baseReader?.Action == ActionState.Inventory6c)
                    _activity.baseReader.RfidUhf?.Stop();
            }
            catch { }

            try
            {
                _activity?.baseReader?.RemoveListener(this);
                _activity?.baseReader?.RfidUhf?.RemoveListener(this);
            }
            catch { }

            try { restoreGunKeyCode(); } catch { }

            try
            {
                if (gvObject != null)
                {
                    gvObject.Adapter = null;
                    gvObject.Dispose();
                    gvObject = null;
                }

                if (adapter != null)
                {
                    adapter.Dispose();
                    adapter = null;
                }

                tagEPCList?.Clear();
                tagEPCList = null;

                soundPool?.Release();
                soundPool = null;
            }
            catch { }

            base.OnDestroy();
        }
        #endregion

        #region RFID EVENTOS
        public void OnNotificationState(NotificationState state, Java.Lang.Object @params) { }

        public void OnReaderActionChanged(BaseReader reader, ResultCode retCode, ActionState state, Java.Lang.Object @params)
        {
            try
            {
                if (state == ActionState.Inventory6c)
                {
                    if (_isFindTag)
                        UpdateText(IDType.Find, GetString(Resource.String.stop));
                    else
                        UpdateText(IDType.Inventory, GetString(Resource.String.stop));
                }
                else if (state == ActionState.Stop)
                {
                    lock (_inventoryLock)
                    {
                        _isInventoryInProgress = false;
                    }
                    UpdateText(IDType.Inventory, GetString(Resource.String.inventory));
                    UpdateText(IDType.Find, GetString(Resource.String.find));
                }
            }
            catch (Exception e)
            {
                Log.Error(TAG, e.Message);
            }
        }

        public void OnReaderBatteryState(BaseReader reader, int batteryState, Java.Lang.Object @params) { }

        public void OnReaderKeyChanged(BaseReader reader, KeyType type, KeyState state, Java.Lang.Object @params)
        {
            // Debounce
            var now = DateTime.Now;
            if ((now - _lastTriggerTime).TotalMilliseconds < 200) return;
            _lastTriggerTime = now;

            if (type != KeyType.Trigger) return;

            if (_activity?.baseReader?.RfidUhf == null)
            {
                MainActivity.ShowToast("Lector no disponible");
                return;
            }

            if (state == KeyState.KeyDown)
            {
                bool proveedorSeleccionado = !sprProveedor.Enabled && sprProveedor.SelectedItemPosition > 0;
                bool ranchoSeleccionado = !sprRancho.Enabled && sprRancho.SelectedItemPosition > 0;
                bool tablaSeleccionada = !sprTabla.Enabled && sprTabla.SelectedItemPosition > 0;

                if (!proveedorSeleccionado || !ranchoSeleccionado || !tablaSeleccionada || !btnGuardar.Enabled)
                {
                    MainActivity.ShowDialog("AVISO",
                        "Debe de dar Inicio a la captura de la salida y Seleccionar Proveedor, Rancho y Tabla!");
                    return;
                }

                if (_activity.baseReader.Action != ActionState.Stop) return;

                lock (_inventoryLock)
                {
                    if (_isInventoryInProgress)
                    {
                        Log.Warn(TAG, "Inventario ya en progreso, ignorando trigger");
                        return;
                    }

                    try
                    {
                        _isInventoryInProgress = true;
                        InitSetting();
                        _activity.baseReader.RfidUhf.Inventory6c();
                        _isFindTag = false;
                        _activity.baseReader.SetDisplayTags(
                            new DisplayTags(ReadOnceState.Off, BeepAndVibrateState.On));
                        Log.Debug(TAG, "Inventario iniciado correctamente");
                    }
                    catch (Exception ex)
                    {
                        _isInventoryInProgress = false;
                        Log.Error(TAG, $"Error iniciando inventario: {ex.Message}");
                        MainActivity.ShowToast($"Error RFID: {ex.Message}");
                    }
                }
            }
            else if (state == KeyState.KeyUp)
            {
                // FIX A4: Eliminado el bloque "else" que mostraba un segundo diálogo de aviso
                //         cuando las condiciones no estaban cumplidas. Ese diálogo ya se
                //         mostró en KeyDown, por lo que aparecía dos veces consecutivas
                //         (una al presionar y otra al soltar el gatillo), confundiendo al operario.
                //         EntradasFragment no tenía este bloque, confirmando que era un error.
                if (_activity.baseReader.Action == ActionState.Inventory6c)
                {
                    try
                    {
                        _activity.baseReader.RfidUhf.Stop();
                        Log.Debug(TAG, "Inventario detenido por usuario");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(TAG, $"Error deteniendo inventario: {ex.Message}");
                    }
                }
            }
        }

        public void OnReaderStateChanged(BaseReader reader, ConnectState state, Java.Lang.Object @params)
        {
            UpdateText(IDType.ConnectState, state.ToString());

            if (_activity?.baseReader?.RfidUhf != null)
                _activity.baseReader.RfidUhf.AddListener(this);

            setUseGunKeyCode();
        }

        public void OnReaderTemperatureState(BaseReader reader, double temperatureState, Java.Lang.Object @params) { }

        public void OnRfidUhfAccessResult(BaseUHF uhf, ResultCode code, ActionState action,
            string epc, string data, Java.Lang.Object @params)
        {
            UpdateText(IDType.AccessResult, code == ResultCode.NoError ? "Success" : code.ToString());
            UpdateText(IDType.Data, StringUtil.IsNullOrEmpty(data) ? "" : data);
            accessTagResult = (code == ResultCode.NoError);
        }

        public void OnRfidUhfReadTag(BaseUHF uhf, string tag, Java.Lang.Object @params)
        {
            if (_isDisposed || !IsAdded || _activity == null || tagEPCList == null)
            {
                Log.Warn(TAG, "OnRfidUhfReadTag: Fragmento no disponible, ignorando tag");
                return;
            }

            if (StringUtil.IsNullOrEmpty(tag)) return;

            float rssi = 0;
            string tid = "";
            if (@params != null)
            {
                TagExtParam param = (TagExtParam)@params;
                rssi = param.Rssi;
                tid = param.TID;
            }

            if (!_isFindTag)
            {
                UpdateText(IDType.TagEPC, tag);
                UpdateText(IDType.TagTID, tid);
            }

            _activity.RunOnUiThread(() =>
            {
                if (_isDisposed || tagEPCList == null) return;

                if (tagEPCList.Any(t => t.EPC == tag)) return;

                if (validaEPC(tag))
                {
                    PlayBeepSound();
                    tagEPCList.Add(new TagLeido
                    {
                        EPC = tag,
                        RSSI = rssi,
                        FechaLectura = DateTime.Now
                    });

                    adapter?.NotifyDataSetChanged();
                    totalCajasLeidasINT++;

                    if (totalCajasLeidas != null)
                        totalCajasLeidas.Text = totalCajasLeidasINT.ToString();
                }
            });

            UpdateText(IDType.TagRSSI, rssi.ToString());
        }
        #endregion

        private void PlayBeepSound()
        {
            if (beepSoundId != 0)
                soundPool.Play(beepSoundId, 1.0f, 1.0f, 0, 0, 1.0f);
        }

        private void FindViews(View view)
        {
            btnGuardar = view.FindViewById<Button>(Resource.Id.btnGuardar);
            connectedState = view.FindViewById<TextView>(Resource.Id.txtConnectedState);
            totalCajasLeidas = view.FindViewById<TextView>(Resource.Id.txtNumTotalCajas);
            txtTotalAcumulado = view.FindViewById<TextView>(Resource.Id.txtNumTotalAcumulado);
            sprProveedor = view.FindViewById<Spinner>(Resource.Id.sprProveedor);
            sprRancho = view.FindViewById<Spinner>(Resource.Id.sprRancho);
            sprTabla = view.FindViewById<Spinner>(Resource.Id.sprTabla);
            gvObject = view.FindViewById<GridView>(Resource.Id.gvleido);
        }

        #region SPINNERS

        #region Spinner Proveedor
        private async Task LoadProveedorAsync(View view)
        {
            try
            {
                vwSalidas = view;

                const string sql = @"
                    SELECT LTRIM(RTRIM(prov_clave)) AS prov_clave,
                           LTRIM(RTRIM(prov_nombre)) AS prov_nombre
                    FROM vwProveedor
                    WHERE prov_clave IN (
                        SELECT DISTINCT prov_clave FROM tb_mstr_recepcion_mp
                        WHERE rmp_fecha >= CAST(DATEADD(DAY,-365,GETDATE()) AS DATE)
                          AND rmp_fecha  < DATEADD(DAY,1,CAST(GETDATE() AS DATE))
                    )
                    UNION
                    SELECT LTRIM(RTRIM(prov_clave)), LTRIM(RTRIM(prov_nombre))
                    FROM vwProveedor
                    WHERE prov_clave IN (
                        SELECT DISTINCT prov_clave FROM tb_mstr_recepcion_pt
                        WHERE rpt_fecha >= CAST(DATEADD(DAY,-365,GETDATE()) AS DATE)
                          AND rpt_fecha  < DATEADD(DAY,1,CAST(GETDATE() AS DATE))
                    )
                    UNION
                    SELECT LTRIM(RTRIM(prov_clave)), LTRIM(RTRIM(prov_nombre))
                    FROM vwProveedor
                    WHERE prov_clave IN (
                        SELECT DISTINCT prov_clave FROM tb_mstr_recepcion_esparrago
                        WHERE rmp_fecha >= CAST(DATEADD(DAY,-365,GETDATE()) AS DATE)
                          AND rmp_fecha  < DATEADD(DAY,1,CAST(GETDATE() AS DATE))
                    )
                    ORDER BY prov_nombre ASC";

                DataTable dt = await Task.Run(() =>
                {
                    using var conn = new SqlConnection(MainActivity.cadenaConexion);
                    using var da = new SqlDataAdapter(sql, conn);
                    var table = new DataTable();
                    da.Fill(table);
                    return table;
                });

                vwProveedor = dt;

                string[] items = new string[dt.Rows.Count + 1];
                items[0] = "Seleccione un Proveedor";
                for (int i = 0; i < dt.Rows.Count; i++)
                    items[i + 1] = dt.Rows[i]["prov_nombre"].ToString().Trim();

                var adapter2 = new ArrayAdapter<string>(
                    _activity, Android.Resource.Layout.SimpleSpinnerItem, items);

                Spinner spinner = view.FindViewById<Spinner>(Resource.Id.sprProveedor);
                spinner.ItemSelected -= sprProveedor_ItemSelected;
                spinner.Adapter = adapter2;
                spinner.ItemSelected += sprProveedor_ItemSelected;
                spinner.Enabled = true;
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, "Error al cargar los datos del spinner.", ToastLength.Long).Show();
                Log.Error(TAG, $"LoadProveedorAsync: {ex.Message}");
            }
        }

        private void sprProveedor_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            try
            {
                Spinner spinner = (Spinner)sender;
                var selectedItem = spinner.GetItemAtPosition(e.Position)?.ToString();

                if (!string.IsNullOrEmpty(selectedItem) && e.Position > 0)
                {
                    Spinner spinnerRancho = vwSalidas.FindViewById<Spinner>(Resource.Id.sprRancho);
                    Spinner spinnerTabla = vwSalidas.FindViewById<Spinner>(Resource.Id.sprTabla);
                    spinnerRancho.SetSelection(0);
                    spinnerTabla.SetSelection(0);
                    rch_clave = "";
                    tbl_clave = "";

                    _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                    MainActivity.ShowDialog("Proveedor Seleccionado:", selectedItem.Trim());

                    prov_nombre = selectedItem;
                    prov_clave = getProv_Clave(prov_nombre);

                    _ = LoadRanchosPorProveedorAsync(prov_clave);
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("Error Spinner", "Error en selección de proveedor: " + ex.Message);
            }
        }

        private string getProv_Clave(string nombre)
        {
            if (vwProveedor?.Rows.Count > 0)
            {
                DataRow[] rows = vwProveedor.Select($"prov_nombre = '{nombre.Replace("'", "''")}'");
                if (rows.Length > 0)
                    return rows[0]["prov_clave"].ToString().Trim();
            }
            return "";
        }
        #endregion

        #region Spinner Rancho
        private async Task LoadRanchosPorProveedorAsync(string provClave)
        {
            try
            {
                const string sql = @"
                    SELECT LTRIM(RTRIM(rch_clave)) AS rch_clave,
                           LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(
                               rch_nombre, CHAR(160),''), CHAR(9),''), CHAR(13),''))) AS rch_nombre
                    FROM vwRanchos
                    WHERE prov_clave = @prov_clave
                    ORDER BY rch_nombre ASC";

                DataTable dt = await Task.Run(() =>
                {
                    using var conn = new SqlConnection(MainActivity.cadenaConexion);
                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@prov_clave", provClave);
                    using var da = new SqlDataAdapter(cmd);
                    var table = new DataTable();
                    da.Fill(table);
                    return table;
                });

                vwRanchos = dt;

                string[] items = new string[dt.Rows.Count + 1];
                items[0] = "Seleccione un Rancho";
                for (int i = 0; i < dt.Rows.Count; i++)
                    items[i + 1] = dt.Rows[i]["rch_nombre"].ToString().Trim();

                var adapterR = new ArrayAdapter<string>(
                    _activity, Android.Resource.Layout.SimpleSpinnerItem, items);

                Spinner spinner = vwSalidas.FindViewById<Spinner>(Resource.Id.sprRancho);
                spinner.ItemSelected -= sprRancho_ItemSelected;
                spinner.Adapter = adapterR;
                spinner.ItemSelected += sprRancho_ItemSelected;
                spinner.Enabled = true;
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, "Error al cargar ranchos: " + ex.Message, ToastLength.Long).Show();
                Log.Error(TAG, $"LoadRanchosPorProveedorAsync: {ex.Message}");
            }
        }

        private void sprRancho_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            try
            {
                Spinner spinner = (Spinner)sender;
                var selectedItem = spinner.GetItemAtPosition(e.Position)?.ToString();

                if (!string.IsNullOrEmpty(selectedItem) && e.Position > 0)
                {
                    Spinner spinnerTabla = vwSalidas.FindViewById<Spinner>(Resource.Id.sprTabla);
                    spinnerTabla.SetSelection(0);
                    tbl_clave = "";

                    _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                    MainActivity.ShowDialog("Rancho Seleccionado:", selectedItem.Trim());

                    rch_nombre = selectedItem;
                    rch_clave = getRch_Clave(rch_nombre);

                    _ = LoadTablasPorRanchoAsync(rch_clave, prov_clave);
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("Error Spinner", "Error en selección de rancho: " + ex.Message);
            }
        }

        private string getRch_Clave(string nombre)
        {
            DataRow[] rows = vwRanchos.Select($"rch_nombre = '{nombre.Replace("'", "''")}'");
            return rows.Length > 0 ? rows[0].ItemArray[0].ToString().Trim() : "";
        }
        #endregion

        #region Spinner Tablas
        private async Task LoadTablasPorRanchoAsync(string ranchoClave, string provClave)
        {
            try
            {
                const string sql = @"
                    SELECT LTRIM(RTRIM(tbl_clave)) AS tbl_clave,
                           LTRIM(RTRIM(tbl_nombre)) AS tbl_nombre
                    FROM vwTablas
                    WHERE rch_clave  = @rch_clave
                      AND prov_clave = @prov_clave
                    ORDER BY tbl_nombre ASC";

                DataTable dt = await Task.Run(() =>
                {
                    using var conn = new SqlConnection(MainActivity.cadenaConexion);
                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@rch_clave", ranchoClave);
                    cmd.Parameters.AddWithValue("@prov_clave", provClave);
                    using var da = new SqlDataAdapter(cmd);
                    var table = new DataTable();
                    da.Fill(table);
                    return table;
                });

                vwTablas = dt;

                string[] items = new string[dt.Rows.Count + 1];
                items[0] = "Seleccione una Tabla";
                for (int i = 0; i < dt.Rows.Count; i++)
                    items[i + 1] = dt.Rows[i]["tbl_nombre"].ToString().Trim();

                var adapterT = new ArrayAdapter<string>(
                    _activity, Android.Resource.Layout.SimpleSpinnerItem, items);

                Spinner spinner = vwSalidas.FindViewById<Spinner>(Resource.Id.sprTabla);
                spinner.ItemSelected -= sprTabla_ItemSelected;
                spinner.Adapter = adapterT;
                spinner.ItemSelected += sprTabla_ItemSelected;
                spinner.Enabled = true;
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, "Error al cargar tablas: " + ex.Message, ToastLength.Long).Show();
                Log.Error(TAG, $"LoadTablasPorRanchoAsync: {ex.Message}");
            }
        }

        private void sprTabla_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            try
            {
                Spinner spinner = (Spinner)sender;
                var selectedItem = spinner.GetItemAtPosition(e.Position)?.ToString();

                if (!string.IsNullOrEmpty(selectedItem) && e.Position > 0)
                {
                    MainActivity.ShowDialog("Tabla Seleccionada:", selectedItem.Trim());
                    tbl_nombre = selectedItem;
                    tbl_clave = getTbl_Clave(tbl_nombre);
                    _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(true);
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("Error Spinner", "Error en selección de tabla: " + ex.Message);
            }
        }

        private string getTbl_Clave(string nombre)
        {
            DataRow[] rows = vwTablas.Select($"tbl_nombre = '{nombre.Replace("'", "''")}'");
            return rows.Length > 0 ? rows[0].ItemArray[0].ToString().Trim() : "";
        }
        #endregion
        #endregion

        #region BOTÓN GUARDAR
        private void SetButtonClick()
        {
            btnGuardar.Click += async (s, e) =>
            {
                if (!TryAssertReader())
                {
                    Log.Warn(TAG, "No se pudo validar el lector.");
                    return;
                }

                if (tagEPCList == null || tagEPCList.Count == 0)
                {
                    MainActivity.ShowToast("No hay datos para guardar.");
                    return;
                }

                loadingOverlay.Visibility = ViewStates.Visible;
                btnGuardar.Enabled = false;

                int registrosInsertados = 0;

                try
                {
                    registrosInsertados = await Task.Run(() =>
                    {
                        int insertados = 0;
                        const string query = @"
                            INSERT INTO Tb_RFID_Det (IdConseInv, IdClaveInt, FechaCaptura)
                            SELECT @IdConseInv, IdClaveInt, GETDATE()
                            FROM Tb_RFID_Catalogo
                            WHERE IdClaveTag = @IdClaveTag
                            AND NOT EXISTS (
                                SELECT 1 FROM Tb_RFID_Det
                                WHERE IdClaveInt = Tb_RFID_Catalogo.IdClaveInt
                                  AND IdConseInv = @IdConseInv
                            )";

                        using (SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion))
                        {
                            conn.Open();
                            using (SqlCommand cmd = new SqlCommand(query, conn))
                            {
                                cmd.Parameters.Add(new SqlParameter("@IdClaveTag", SqlDbType.VarChar));
                                cmd.Parameters.Add(new SqlParameter("@IdConseInv", SqlDbType.Decimal)).Value = IdConse;

                                foreach (var tag in tagEPCList)
                                {
                                    cmd.Parameters["@IdClaveTag"].Value = tag.EPC;
                                    insertados += cmd.ExecuteNonQuery();
                                }
                            }
                        }
                        return insertados;
                    });

                    totalAcumuladoINT += registrosInsertados;
                    txtTotalAcumulado.Text = totalAcumuladoINT.ToString();

                    MainActivity.ShowDialog("INFORMACIÓN ALMACENADA",
                        $"Se han guardado {registrosInsertados} registros exitosamente.");
                    ClearGridView();
                }
                catch (Exception ex)
                {
                    MainActivity.ShowToast("Error al guardar: " + ex.Message);
                }
                finally
                {
                    loadingOverlay.Visibility = ViewStates.Gone;
                    btnGuardar.Enabled = true;
                }
            };
        }
        #endregion

        public override void ReceiveHandler(Bundle bundle)
        {
            UpdateUIType updateUIType = (UpdateUIType)bundle.GetInt(ExtraName.Type);
            if (updateUIType == UpdateUIType.Text)
            {
                string data = bundle.GetString(ExtraName.Text);
                IDType idType = (IDType)bundle.GetInt(ExtraName.TargetID);
                if (idType == IDType.ConnectState && connectedState != null)
                    connectedState.Text = data;
            }
        }

        private bool TryAssertReader() => _activity.TryAssertReader();

        private void DoStop()
        {
            _isFindTag = false;
            _activity.baseReader.RfidUhf.Stop();
        }

        #region CONFIGURACION RFID
        public void InitSetting()
        {
            try
            {
                if (_activity.baseReader?.RfidUhf != null)
                {
                    _activity.baseReader.RfidUhf.ModuleProfile = 0;
                    _activity.baseReader.RfidUhf.Power = 30;
                    _activity.baseReader.RfidUhf.InventoryTime = 150;
                    _activity.baseReader.RfidUhf.IdleTime = 0;
                    _activity.baseReader.RfidUhf.Target = Target.A;
                    _activity.baseReader.RfidUhf.Session = Session.S3;
                    _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.DynamicQ;
                    _activity.baseReader.RfidUhf.ToggleTarget = true;
                    _activity.baseReader.RfidUhf.ContinuousMode = true;
                    Log.Debug(TAG, "Configuración del lector aplicada correctamente");
                }
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "Error en InitSetting: " + e.Message);
            }
        }
        #endregion

        public async Task ConnectTask()
        {
            try
            {
                if (_activity == null) return;

                if (_activity.baseReader == null || !_activity.IsReaderConnected)
                {
                    Log.Info(TAG, "Inicializando lector...");
                    await _activity.InitializeReader();
                }

                if (_activity.baseReader != null &&
                    _activity.IsReaderConnected &&
                    _activity.baseReader.RfidUhf != null)
                {
                    _activity.baseReader.AddListener(this);
                    _activity.baseReader.RfidUhf.AddListener(this);
                    InitSetting();
                    UpdateText(IDType.ConnectState, "Connected");
                }
                else
                {
                    UpdateText(IDType.ConnectState, "Disconnected");
                }
            }
            catch (Exception e)
            {
                Log.Error(TAG, $"Error: {e.Message}");
                UpdateText(IDType.ConnectState, "Error");
            }
        }

        public bool SetSelectMask(string maskEpc)
        {
            SelectMask6cParam param = new SelectMask6cParam(
                true, Mask6cTarget.Sl, Mask6cAction.Ab, BankType.Epc,
                0, maskEpc, maskEpc.Length * NIBLE_SIZE);
            try
            {
                for (int i = 0; i < MAX_MASK; i++)
                    _activity.baseReader.RfidUhf.SetSelectMask6cEnabled(i, false);
                _activity.baseReader.RfidUhf.SetSelectMask6c(0, param);
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "setSelectMask failed: " + e.Code.Message);
                MainActivity.ShowToast("setSelectMask failed");
                return false;
            }
            return true;
        }

        public void ClearSelectMask()
        {
            for (int i = 0; i < MAX_MASK; i++)
            {
                try
                {
                    _activity.baseReader.RfidUhf.SetSelectMask6cEnabled(i, false);
                }
                catch (ReaderException)
                {
                    // FIX M3: "throw;" en lugar de "throw e;" para preservar
                    //         el stack trace original de la excepción.
                    throw;
                }
            }
        }

        public void UpdateText(IDType id, string data)
            => Utilities.UpdateUIText(FragmentType.Salidas, (int)id, data);

        public void OnCustomActionReceived(Context context, Intent intent)
        {
            if (_activity.currentRfidFragment != this) return;

            string action = intent.Action;
            if (action.Equals(MainReceiver.rfidGunPressed))
            {
                if (_activity.baseReader != null)
                    OnReaderKeyChanged(null, KeyType.Trigger, KeyState.KeyDown, null);
            }
            else if (action.Equals(MainReceiver.rfidGunReleased))
            {
                if (_activity.baseReader != null)
                    OnReaderKeyChanged(null, KeyType.Trigger, KeyState.KeyUp, null);
            }
        }

        private void sendUssScan(bool enable)
        {
            Intent intent = new Intent();
            intent.SetAction(systemUssTriggerScan);
            intent.PutExtra(ExtraScan, enable);
            MainActivity.getInstance().SendBroadcast(intent);
        }

        private string getKeymappingPath()
            => Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat
                ? android12keymappingPath
                : keymappingPath;

        private Bundle[] getParams(Bundle bundle)
        {
            if (bundle == null) return null;
            Bundle[] paramArray = new Bundle[bundle.KeySet().Count];
            int i = 0;
            foreach (string key in bundle.KeySet())
            {
                Bundle tmp = new Bundle();
                tmp.PutString("Key", key);
                tmp.PutString("Value", bundle.GetString(key));
                paramArray[i++] = tmp;
            }
            return paramArray;
        }

        private void setUseGunKeyCode()
        {
            if (tempKeyCode != null) return;

            Task.Run(() =>
            {
                string keyName = "", keyCode = "";
                switch (Build.Device)
                {
                    case "HT730": keyName = "TRIGGER_GUN"; keyCode = "298"; break;
                    case "PA768": keyName = "SCAN_GUN"; keyCode = "294"; break;
                    default: Log.Debug(TAG, "Skip to set gun key code"); return;
                }

                sendUssScan(false);
                var ctx = MainActivity.getInstance().ApplicationContext;

                KeymappingCtrl.GetInstance(ctx).ExportKeyMappings(getKeymappingPath());
                KeymappingCtrl.GetInstance(ctx).EnableKeyMapping(true);

                tempKeyCode = KeymappingCtrl.GetInstance(ctx).GetKeyMapping(keyName);

                bool wakeup = tempKeyCode.GetBoolean("wakeUp");
                Bundle[] bDown = getParams(tempKeyCode.GetBundle("broadcastDownParams"));
                Bundle[] bUp = getParams(tempKeyCode.GetBundle("broadcastUpParams"));
                Bundle[] bStart = getParams(tempKeyCode.GetBundle("startActivityParams"));

                Bundle result = KeymappingCtrl.GetInstance(ctx).AddKeyMappings(
                    keyName, keyCode, wakeup,
                    MainReceiver.rfidGunPressed, bDown,
                    MainReceiver.rfidGunReleased, bUp,
                    bStart);

                if (result.GetInt("errorCode") == 0)
                    Log.Debug(TAG, "Set Gun Key Code success");
                else
                    Log.Error(TAG, "Set Gun Key Code failed: " + result.GetString("errorMsg"));
            });
        }

        private void restoreGunKeyCode()
        {
            if (tempKeyCode == null) return;

            Task.Run(() =>
            {
                Bundle result = KeymappingCtrl.GetInstance(
                    MainActivity.getInstance().ApplicationContext).ImportKeyMappings(getKeymappingPath());

                if (result.GetInt("errorCode") == 0)
                    Log.Debug(TAG, "restoreGunKeyCode success");
                else
                    Log.Error(TAG, "restoreGunKeyCode failed: " + result.GetString("errorMsg"));

                tempKeyCode = null;
            });
        }

        public void ClearGridView()
        {
            _activity.RunOnUiThread(() =>
            {
                tagEPCList.Clear();
                adapter.NotifyDataSetChanged();
                totalCajasLeidasINT = 0;
                if (totalCajasLeidas != null)
                    totalCajasLeidas.Text = "0";
            });
        }

        #region VALIDAR LECTURA DE TAG VS CATALOGO
        private bool validaEPC(string EPC)
        {
            if (_activity.CatalogoEPCSet == null || _activity.CatalogoEPCSet.Count == 0)
            {
                _activity.getTb_RFID_Catalogo();
                return false;
            }
            return _activity.CatalogoEPCSet.Contains(EPC.Trim());
        }
        #endregion
    }
}
