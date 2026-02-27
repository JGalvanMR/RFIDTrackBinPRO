using Android.App;
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
using Com.Unitech.Lib.Transport.Types;
using Com.Unitech.Lib.Types;
using Com.Unitech.Lib.Uhf;
using Com.Unitech.Lib.Uhf.Event;
using Com.Unitech.Lib.Uhf.Params;
using Com.Unitech.Lib.Uhf.Types;
using Com.Unitech.Lib.Util.Diagnotics;
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

namespace RFIDTrackBin.fragment
{
    [IntentFilter(new[] { NfcAdapter.ActionNdefDiscovered, NfcAdapter.ActionTagDiscovered, Intent.CategoryDefault })]
    public class InventarioFragment : BaseFragment, IReaderEventListener, IRfidUhfEventListener, MainReceiver.IEventLitener, View.IOnTouchListener
    {
        static string TAG = typeof(InventarioFragment).Name;

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

        #region Views
        private Button btnGuardarInventario;
        TextView connectedState;
        TextView areaLectura;
        TextView totalCajasLeidas;
        TextView txtTotalAcumulado;
        Spinner sprAreas;
        #endregion

        #region SoundPool
        private SoundPool soundPool;
        private int beepSoundId;
        #endregion

        MainReceiver mReceiver;
        Bundle tempKeyCode = null;

        GridView gvObject;
        private List<TagLeido> tagsLeidos = new List<TagLeido>();
        private myGVitemAdapter adapter;

        DataSet ds = new DataSet();
        public static DataTable areas = new DataTable("areas");

        int totalCajasLeidasINT = 0;
        int totalAcumuladoINT = 0;

        // FIX #7: Eliminado campo "SqlConnection thisConnection" que no se cerraba

        NfcAdapter _nfcAdapter;

        IMenu _menu;
        int IdConseInv;

        ProgressBar progressBar;
        RelativeLayout loadingOverlay;

        #region PERSISTENCIA DE INVENTARIO
        private const string PREFS_NAME = "InventarioPrefs";
        private const string KEY_ID_CONSE_INV = "IdConseInv";
        private const string KEY_AREA = "Area";
        private const string KEY_FECHA_INICIO = "FechaInicio";
        private const string KEY_USUARIO = "UsuarioInventario";
        private bool _isProcessingMenuAction = false;
        #endregion

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            return inflater.Inflate(Resource.Layout.InventarioFragment, container, false);
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

            FindViewById(view);

            // FIX #4: Carga de áreas en background
            await LoadAreasAsync(view);

            SetButtonClick();
            InitializeSoundPool();

            HasOptionsMenu = true;

            mReceiver = new MainReceiver(this);
            IntentFilter filter = new IntentFilter();
            filter.AddAction(MainReceiver.rfidGunPressed);
            filter.AddAction(MainReceiver.rfidGunReleased);
            _activity.RegisterReceiver(mReceiver, filter);

            adapter = new myGVitemAdapter(_activity, tagsLeidos);
            gvObject.Adapter = adapter;

            sprAreas.ItemSelected += sprAreas_ItemSelected;
            btnGuardarInventario.Enabled = false;
            _nfcAdapter = NfcAdapter.GetDefaultAdapter(_activity);

            _activity.EnableNavigationItems(Resource.Id.navigation_entradas, Resource.Id.navigation_salidas);

            progressBar = view.FindViewById<ProgressBar>(Resource.Id.progressBarGuardar);
            loadingOverlay = view.FindViewById<RelativeLayout>(Resource.Id.loadingOverlay);

            VerificarInventarioPendiente();
        }

        #region GESTIÓN DE INVENTARIO PERSISTENTE

        private void VerificarInventarioPendiente()
        {
            var prefs = Activity.GetSharedPreferences(PREFS_NAME, FileCreationMode.Private);
            int idPendiente = prefs.GetInt(KEY_ID_CONSE_INV, 0);
            string areaPendiente = prefs.GetString(KEY_AREA, null);
            string fechaInicio = prefs.GetString(KEY_FECHA_INICIO, null);
            string usuarioInv = prefs.GetString(KEY_USUARIO, null);

            if (idPendiente > 0)
            {
                string mensaje = $"Tiene un inventario sin finalizar:\n\n" +
                                 $"ID: {idPendiente}\nÁrea: {areaPendiente ?? "N/A"}\n" +
                                 $"Iniciado: {fechaInicio ?? "N/A"}\nUsuario: {usuarioInv ?? "N/A"}";

                new AlertDialog.Builder(Activity)
                    .SetTitle("Inventario Pendiente")
                    .SetMessage(mensaje)
                    .SetPositiveButton("Continuar", (s, e) =>
                        RestaurarInventarioPendienteAsync(idPendiente, areaPendiente))
                    .SetNegativeButton("Cerrar Ahora", async (s, e) =>
                    {
                        await CerrarInventarioHuerfanoAsync(idPendiente);
                        LimpiarPreferencias();
                    })
                    .SetCancelable(false)
                    .Show();
            }
        }

        // FIX #4: Restauración en background — ya no bloquea UI thread
        private async void RestaurarInventarioPendienteAsync(int idInventario, string area)
        {
            try
            {
                bool sigueAbierto = await Task.Run(() =>
                {
                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    conn.Open();
                    const string query = "SELECT InvStatus FROM Tb_RFID_Inventario WHERE IdConseInv = @Id AND HoraCierre IS NULL";
                    using SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@Id", idInventario);
                    return cmd.ExecuteScalar() != null;
                });

                if (!sigueAbierto)
                {
                    Toast.MakeText(Activity, "El inventario ya fue cerrado anteriormente", ToastLength.Long).Show();
                    LimpiarPreferencias();
                    return;
                }

                IdConseInv = idInventario;

                if (!string.IsNullOrEmpty(area) && sprAreas != null)
                {
                    for (int i = 0; i < sprAreas.Count; i++)
                    {
                        if (sprAreas.GetItemAtPosition(i)?.ToString() == area)
                        {
                            sprAreas.SetSelection(i);
                            break;
                        }
                    }
                }

                _menu?.FindItem(Resource.Id.inicio_inventario)?.SetEnabled(false);
                _menu?.FindItem(Resource.Id.final_inventario)?.SetEnabled(true);
                btnGuardarInventario.Enabled = true;
                if (sprAreas != null) sprAreas.Enabled = false;

                _activity?.DisableNavigationItems(
                    Resource.Id.navigation_entradas,
                    Resource.Id.navigation_salidas,
                    Resource.Id.navigation_inventario);

                Toast.MakeText(Activity, $"Inventario #{idInventario} restaurado", ToastLength.Short).Show();
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al restaurar inventario: {ex.Message}");
                Toast.MakeText(Activity, "Error al recuperar inventario pendiente", ToastLength.Long).Show();
                await CerrarInventarioHuerfanoAsync(idInventario);
                LimpiarPreferencias();
            }
        }

        private void GuardarInventarioEnPreferencias(int idInventario, string area)
        {
            try
            {
                var prefs = Activity.GetSharedPreferences(PREFS_NAME, FileCreationMode.Private);
                var editor = prefs.Edit();
                editor.PutInt(KEY_ID_CONSE_INV, idInventario);
                editor.PutString(KEY_AREA, area);
                editor.PutString(KEY_FECHA_INICIO, DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                editor.PutString(KEY_USUARIO, ((MainActivity)Activity)?.usuario ?? "Desconocido");
                editor.Commit();
                Log.Debug(TAG, $"Inventario guardado en prefs: ID={idInventario}");
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al guardar preferencias: {ex.Message}");
            }
        }

        private void LimpiarPreferencias()
        {
            try
            {
                var prefs = Activity.GetSharedPreferences(PREFS_NAME, FileCreationMode.Private);
                var editor = prefs.Edit();
                editor.Remove(KEY_ID_CONSE_INV);
                editor.Remove(KEY_AREA);
                editor.Remove(KEY_FECHA_INICIO);
                editor.Remove(KEY_USUARIO);
                editor.Commit();
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al limpiar preferencias: {ex.Message}");
            }
        }

        // FIX #4: Versión async — no bloquea UI thread
        private async Task CerrarInventarioHuerfanoAsync(int idInventario)
        {
            try
            {
                int filas = await Task.Run(() =>
                {
                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    conn.Open();
                    const string query = @"UPDATE Tb_RFID_Inventario
                                           SET HoraCierre = GETDATE()
                                           WHERE IdConseInv = @Id AND HoraCierre IS NULL";
                    using SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@Id", idInventario);
                    return cmd.ExecuteNonQuery();
                });

                if (filas > 0)
                    Toast.MakeText(Activity, $"Inventario #{idInventario} cerrado automáticamente", ToastLength.Short).Show();
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al cerrar inventario huérfano: {ex.Message}");
                Toast.MakeText(Activity, "No se pudo cerrar el inventario pendiente. Contacte soporte.", ToastLength.Long).Show();
            }
        }
        #endregion

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

        #region NFC
        public override void OnNfcTagScanned(string tagId)
        {
            base.OnNfcTagScanned(tagId);
            MainActivity.ShowToast("Tag escaneado: " + tagId);

            // FIX #8: Búsqueda O(1) usando HashSet
            if (_activity.CatalogoEPCSet?.Contains(tagId) == true)
                MainActivity.ShowToast("Tag reconocido en catálogo");
            else
                MainActivity.ShowToast("Tag NO encontrado en catálogo");
        }
        #endregion

        #region MenuInflater
        public override void OnCreateOptionsMenu(IMenu menu, MenuInflater inflater)
        {
            inflater.Inflate(Resource.Menu.menu_inventario, menu);
            _menu = menu;
            menu.FindItem(Resource.Id.inicio_inventario).SetEnabled(false);
            menu.FindItem(Resource.Id.final_inventario).SetEnabled(false);
            base.OnCreateOptionsMenu(menu, inflater);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            if (_isProcessingMenuAction) return true;

            try
            {
                _isProcessingMenuAction = true;

                return item.ItemId switch
                {
                    Resource.Id.inicio_inventario => HandleInicioInventario(),
                    Resource.Id.final_inventario => HandleFinalInventario(),
                    _ => base.OnOptionsItemSelected(item)
                };
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error crítico en menú: {ex}");
                Toast.MakeText(Activity, "Error inesperado. Intente nuevamente.", ToastLength.Long).Show();
                return true;
            }
            finally
            {
                new Android.OS.Handler(Looper.MainLooper)
                    .PostDelayed(() => _isProcessingMenuAction = false, 500);
            }
        }

        private bool HandleInicioInventario()
        {
            if (sprAreas?.SelectedItemPosition <= 0)
            {
                Toast.MakeText(Activity, "Seleccione un área válida primero", ToastLength.Short).Show();
                return true;
            }

            if (areas?.Rows == null || areas.Rows.Count == 0)
            {
                Toast.MakeText(Activity, "No hay áreas disponibles", ToastLength.Short).Show();
                return true;
            }

            int indice = sprAreas.SelectedItemPosition - 1;
            if (indice < 0 || indice >= areas.Rows.Count)
            {
                Toast.MakeText(Activity, "Error: Área inválida", ToastLength.Short).Show();
                return true;
            }

            string idArea = areas.Rows[indice]["IdArea"]?.ToString();
            string nombreArea = sprAreas.SelectedItem?.ToString();
            var mainActivity = Activity as MainActivity;

            if (string.IsNullOrEmpty(idArea) || mainActivity == null)
            {
                Toast.MakeText(Activity, "Error de datos del área", ToastLength.Short).Show();
                return true;
            }

            if (IdConseInv > 0)
            {
                Toast.MakeText(Activity, $"Ya hay un inventario activo (ID: {IdConseInv})", ToastLength.Long).Show();
                return true;
            }

            // FIX #4: Insertar en background
            _ = IniciarInventarioAsync(nombreArea, mainActivity, idArea);
            return true;
        }

        private async Task IniciarInventarioAsync(string nombreArea, MainActivity mainActivity, string idArea)
        {
            int nuevoId = await InsertarInventarioAsync(nombreArea, mainActivity.usuario,
                "A", mainActivity.idUnidadNegocio, idArea);

            if (nuevoId <= 0)
            {
                Toast.MakeText(Activity, "Error al iniciar inventario en base de datos", ToastLength.Long).Show();
                return;
            }

            GuardarInventarioEnPreferencias(nuevoId, nombreArea);

            IdConseInv = nuevoId;
            if (sprAreas != null) sprAreas.Enabled = false;
            btnGuardarInventario.Enabled = true;
            _menu?.FindItem(Resource.Id.inicio_inventario)?.SetEnabled(false);
            _menu?.FindItem(Resource.Id.final_inventario)?.SetEnabled(true);

            _activity?.DisableNavigationItems(
                Resource.Id.navigation_entradas,
                Resource.Id.navigation_salidas,
                Resource.Id.navigation_inventario);

            Toast.MakeText(Activity, $"Inventario #{nuevoId} iniciado", ToastLength.Short).Show();
        }

        private bool HandleFinalInventario()
        {
            if (IdConseInv <= 0)
            {
                var prefs = Activity.GetSharedPreferences(PREFS_NAME, FileCreationMode.Private);
                IdConseInv = prefs.GetInt(KEY_ID_CONSE_INV, 0);
            }

            if (IdConseInv <= 0)
            {
                Toast.MakeText(Activity, "No hay inventario activo para finalizar", ToastLength.Short).Show();
                return true;
            }

            int idActual = IdConseInv;
            int totalActual = totalAcumuladoINT;

            new AlertDialog.Builder(Activity)
                .SetTitle("Finalizar Inventario")
                .SetMessage($"¿Confirmar cierre del inventario #{idActual}?\n\nTotal acumulado: {totalActual} cajas")
                .SetPositiveButton("Sí, Finalizar", (EventHandler<DialogClickEventArgs>)((s, e) =>
                {
                    _ = FinalizarInventarioAsync();
                }))
                .SetNegativeButton("Cancelar", (EventHandler<DialogClickEventArgs>)null)
                .SetCancelable(false)
                .Show();

            return true;
        }

        private async Task FinalizarInventarioAsync()
        {
            Activity.RunOnUiThread(() =>
            {
                loadingOverlay.Visibility = ViewStates.Visible;
                btnGuardarInventario.Enabled = false;
            });

            try
            {
                // FIX #4: ActualizarHoraCierre y UpdateFechaUltimoMovimiento son llamados
                // desde Task.Run — nunca bloquean el UI thread
                await Task.Run(() =>
                {
                    ActualizarHoraCierre(IdConseInv);
                    UpdateFechaUltimoMovimiento(IdConseInv);
                });

                LimpiarPreferencias();

                Activity.RunOnUiThread(() =>
                {
                    int idCerrado = IdConseInv;
                    IdConseInv = 0;
                    totalAcumuladoINT = 0;

                    sprAreas?.SetSelection(0);
                    if (sprAreas != null) sprAreas.Enabled = true;

                    txtTotalAcumulado.Text = "0";
                    btnGuardarInventario.Enabled = false;

                    _menu?.FindItem(Resource.Id.inicio_inventario)?.SetEnabled(false);
                    _menu?.FindItem(Resource.Id.final_inventario)?.SetEnabled(false);

                    _activity?.EnableNavigationItems(
                        Resource.Id.navigation_entradas,
                        Resource.Id.navigation_salidas,
                        Resource.Id.navigation_inventario);

                    ClearGridView();
                    loadingOverlay.Visibility = ViewStates.Gone;
                    Toast.MakeText(Activity, "Inventario finalizado correctamente", ToastLength.Short).Show();
                    Log.Info(TAG, $"Inventario finalizado: ID={idCerrado}");
                });
            }
            catch (Exception ex)
            {
                Activity.RunOnUiThread(() =>
                {
                    loadingOverlay.Visibility = ViewStates.Gone;
                    btnGuardarInventario.Enabled = true;
                    MainActivity.ShowDialog("Error al Cerrar",
                        $"No se pudo finalizar el inventario:\n{ex.Message}\n\n" +
                        $"El inventario #{IdConseInv} sigue activo. Intente nuevamente.");
                });
            }
        }
        #endregion

        #region INICIAR INVENTARIO
        // FIX #4: Versión async — INSERT no bloquea UI thread
        public async Task<int> InsertarInventarioAsync(
            string invArea, string usuario, string invStatus,
            string idUnidadNegocio, string idArea)
        {
            if (string.IsNullOrWhiteSpace(invArea) || string.IsNullOrWhiteSpace(usuario))
            {
                Log.Error(TAG, "Parámetros inválidos en InsertarInventario");
                return -1;
            }

            const string query = @"
                INSERT INTO [dbo].[Tb_RFID_Inventario]
                    (InvArea, InvFecha, Usuario, InvStatus, IdUnidadNegocio, IdArea)
                VALUES
                    (@InvArea, GETDATE(), @Usuario, @InvStatus, @IdUnidadNegocio, @IdArea);
                SELECT SCOPE_IDENTITY();";

            try
            {
                return await Task.Run(() =>
                {
                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    using SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@InvArea", invArea);
                    cmd.Parameters.AddWithValue("@Usuario", usuario);
                    cmd.Parameters.AddWithValue("@InvStatus", invStatus ?? "A");
                    cmd.Parameters.AddWithValue("@IdUnidadNegocio",
                        string.IsNullOrEmpty(idUnidadNegocio) ? DBNull.Value : (object)idUnidadNegocio);
                    cmd.Parameters.AddWithValue("@IdArea",
                        string.IsNullOrEmpty(idArea) ? DBNull.Value : (object)idArea);
                    conn.Open();
                    object result = cmd.ExecuteScalar();

                    if (result != null && int.TryParse(result.ToString(), out int id))
                    {
                        Log.Info(TAG, $"Inventario insertado en BD: ID={id}");
                        return id;
                    }
                    return -1;
                });
            }
            catch (SqlException sqlEx)
            {
                Log.Error(TAG, $"Error SQL al insertar: {sqlEx.Message}");
                Activity.RunOnUiThread(() =>
                    MainActivity.ShowDialog("Error de Base de Datos",
                        $"No se pudo iniciar el inventario:\n{sqlEx.Message}"));
                return -1;
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al insertar inventario: {ex}");
                return -1;
            }
        }

        // Shim de compatibilidad (llamado desde Task.Run en FinalizarInventarioAsync — hilo de fondo, OK)
        private int InsertarInventario(string invArea, string usuario, string invStatus,
            string idUnidadNegocio, string idArea)
            => InsertarInventarioAsync(invArea, usuario, invStatus, idUnidadNegocio, idArea).GetAwaiter().GetResult();
        #endregion

        #region FINALIZAR INVENTARIO
        // Llamados SOLO desde Task.Run (hilo de fondo) en FinalizarInventarioAsync — correcto
        public void ActualizarHoraCierre(decimal idConseInv)
        {
            if (idConseInv <= 0)
                throw new ArgumentException("ID de inventario inválido", nameof(idConseInv));

            const string query = @"
                UPDATE Tb_RFID_Inventario
                SET HoraCierre = GETDATE()
                WHERE IdConseInv = @IdConseInv AND HoraCierre IS NULL";

            using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
            using SqlCommand cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@IdConseInv", idConseInv);
            conn.Open();
            int rows = cmd.ExecuteNonQuery();

            if (rows == 0)
                throw new InvalidOperationException(
                    $"El inventario {idConseInv} ya estaba cerrado o no existe");

            Log.Info(TAG, $"Hora de cierre actualizada para inventario {idConseInv}");
        }

        public void UpdateFechaUltimoMovimiento(int idConseInv)
        {
            const string query = @"
                UPDATE c
                SET c.FechaUltimoMovimiento = d.FechaCaptura
                FROM Tb_RFID_Catalogo c
                INNER JOIN Tb_RFID_DetInv d ON c.IdClaveInt = d.IdClaveInt
                WHERE d.IdConseInv = @IdConseInv";

            try
            {
                using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                conn.Open();
                using SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@IdConseInv", idConseInv);
                int rows = cmd.ExecuteNonQuery();
                MainActivity.ShowToast($"{rows} filas actualizadas en el catálogo");
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
            ((AndroidX.AppCompat.App.AppCompatActivity)Activity).SupportActionBar.Title = "INVENTARIO";

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

            // FIX #5: Eliminado Thread.Sleep que bloqueaba el UI thread
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
                if (gvObject != null) { gvObject.Adapter = null; gvObject.Dispose(); gvObject = null; }
                adapter?.Dispose();
                adapter = null;
                tagsLeidos?.Clear();
                tagsLeidos = null;
                soundPool?.Release();
                soundPool = null;
            }
            catch { }

            base.OnDestroy();
        }
        #endregion

        #region RFID EVENTOS
        public void OnNotificationState(NotificationState state, Java.Lang.Object @params) { }

        // FIX #1: _isInventoryInProgress se resetea al Stop
        public void OnReaderActionChanged(BaseReader reader, ResultCode retCode, ActionState state, Java.Lang.Object @params)
        {
            if (state == ActionState.Stop)
            {
                lock (_inventoryLock) { _isInventoryInProgress = false; }
                UpdateText(IDType.Inventory, GetString(Resource.String.inventory));
            }
            else if (state == ActionState.Inventory6c)
            {
                UpdateText(IDType.Inventory, GetString(Resource.String.stop));
            }
        }

        public void OnReaderBatteryState(BaseReader reader, int batteryState, Java.Lang.Object @params) { }

        public void OnReaderKeyChanged(BaseReader reader, KeyType type, KeyState state, Java.Lang.Object @params)
        {
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
                // *** VALIDACIÓN DE NEGOCIO: No permitir lecturas hasta que el usuario
                //     seleccione un área Y haya dado "Inicio al inventario" ***
                var areaSeleccionada = sprAreas?.SelectedItem?.ToString();

                if (string.IsNullOrEmpty(areaSeleccionada) || !btnGuardarInventario.Enabled)
                {
                    MainActivity.ShowDialog("AVISO", "Debe de dar Inicio a la captura del inventario y Seleccionar un Area!");
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
                        _activity.baseReader.SetDisplayTags(new DisplayTags(ReadOnceState.Off, BeepAndVibrateState.On));
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
                if (_activity.baseReader.Action == ActionState.Inventory6c)
                {
                    try { _activity.baseReader.RfidUhf.Stop(); }
                    catch (Exception ex) { Log.Error(TAG, $"Error deteniendo: {ex.Message}"); }
                }
                else if (!btnGuardarInventario.Enabled)
                {
                    // Mostrar aviso si sueltan el gatillo sin haber iniciado
                    MainActivity.ShowDialog("AVISO", "Debe de dar Inicio a la captura del inventario y Seleccionar un Area!");
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

        public void OnRfidUhfAccessResult(BaseUHF uhf, ResultCode code, ActionState action, string epc, string data, Java.Lang.Object @params)
        {
            UpdateText(IDType.AccessResult, code == ResultCode.NoError ? "Success" : code.ToString());
            UpdateText(IDType.Data, StringUtil.IsNullOrEmpty(data) ? "" : data);
            accessTagResult = (code == ResultCode.NoError);
        }

        public void OnRfidUhfReadTag(BaseUHF uhf, string tag, Java.Lang.Object @params)
        {
            if (_isDisposed || !IsAdded || _activity == null || tagsLeidos == null)
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
                if (_isDisposed || tagsLeidos == null) return;
                if (tagsLeidos.Any(t => t.EPC == tag)) return;

                // FIX #8: validaEPC usa HashSet O(1)
                if (validaEPC(tag))
                {
                    PlayBeepSound();
                    tagsLeidos.Add(new TagLeido { EPC = tag, RSSI = rssi, FechaLectura = DateTime.Now });
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

        private void FindViewById(View view)
        {
            btnGuardarInventario = view.FindViewById<Button>(Resource.Id.btnGuardarInventario);
            connectedState = view.FindViewById<TextView>(Resource.Id.txtConnectedStateInventario);
            areaLectura = view.FindViewById<TextView>(Resource.Id.txtAreaLecturaInventario);
            totalCajasLeidas = view.FindViewById<TextView>(Resource.Id.txtNumTotalCajas);
            txtTotalAcumulado = view.FindViewById<TextView>(Resource.Id.txtNumTotalAcumulado);
            sprAreas = view.FindViewById<Spinner>(Resource.Id.sprAreas);
            gvObject = view.FindViewById<GridView>(Resource.Id.gvleidoInventario);
        }

        #region SPINNER ÁREAS
        // FIX #4: Carga en background
        // FIX SQL INJECTION: La query usaba concatenación directa de usuario. Ahora usa parámetro.
        private async Task LoadAreasAsync(View view)
        {
            try
            {
                string usuario = ((MainActivity)Activity).usuario;

                DataTable dt = await Task.Run(() =>
                {
                    const string sql = @"
                        SELECT * FROM Tb_RFID_Areas
                        WHERE UnidadNegocio = (
                            SELECT UBICACION FROM Tb_RFID_Usuarios
                            WHERE usuario = @usuario
                        )";

                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    using SqlCommand cmd = new SqlCommand(sql, conn);
                    // ✅ Parámetro en lugar de concatenación — elimina SQL Injection
                    cmd.Parameters.AddWithValue("@usuario", usuario);
                    using SqlDataAdapter da = new SqlDataAdapter(cmd);
                    var table = new DataTable("areas");
                    da.Fill(table);
                    return table;
                });

                areas = dt;

                string[] items = new string[dt.Rows.Count + 1];
                items[0] = "Seleccione un Area";
                for (int i = 0; i < dt.Rows.Count; i++)
                    items[i + 1] = dt.Rows[i]["NombreArea"].ToString();

                Spinner spinner = view.FindViewById<Spinner>(Resource.Id.sprAreas);
                var adp = new ArrayAdapter<string>(_activity, Android.Resource.Layout.SimpleSpinnerItem, items);
                spinner.Adapter = adp;
                spinner.Enabled = true;
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, "Error al cargar áreas: " + ex.Message, ToastLength.Long).Show();
                Log.Error(TAG, $"LoadAreasAsync: {ex.Message}");
            }
        }

        private void sprAreas_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            if (e.Position > 0)
                _menu?.FindItem(Resource.Id.inicio_inventario)?.SetEnabled(true);
        }
        #endregion

        #region BOTÓN GUARDAR
        private void SetButtonClick()
        {
            btnGuardarInventario.Click += async (s, e) =>
            {
                if (!TryAssertReader())
                {
                    Log.Warn(TAG, "No se pudo validar el lector.");
                    return;
                }

                if (tagsLeidos == null || tagsLeidos.Count == 0)
                {
                    MainActivity.ShowToast("No hay datos para guardar.");
                    return;
                }

                loadingOverlay.Visibility = ViewStates.Visible;
                btnGuardarInventario.Enabled = false;

                int registrosInsertados = 0;

                try
                {
                    // FIX #4: INSERT en background
                    List<TagLeido> snapshot = tagsLeidos.ToList();

                    registrosInsertados = await Task.Run(() =>
                    {
                        int insertados = 0;
                        const string query = @"
                            INSERT INTO Tb_RFID_DetInv (IdConseInv, IdClaveInt, FechaCaptura)
                            SELECT @IdConseInv, IdClaveInt, @FechaCaptura
                            FROM Tb_RFID_Catalogo
                            WHERE IdClaveTag = @IdClaveTag";

                        using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                        conn.Open();
                        using SqlCommand cmd = new SqlCommand(query, conn);
                        cmd.Parameters.Add(new SqlParameter("@IdClaveTag", SqlDbType.VarChar));
                        cmd.Parameters.Add(new SqlParameter("@FechaCaptura", SqlDbType.DateTime));
                        cmd.Parameters.Add(new SqlParameter("@IdConseInv", SqlDbType.Decimal)).Value = IdConseInv;

                        foreach (var tag in snapshot)
                        {
                            cmd.Parameters["@IdClaveTag"].Value = tag.EPC;
                            cmd.Parameters["@FechaCaptura"].Value = tag.FechaLectura;
                            insertados += cmd.ExecuteNonQuery();
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
                    btnGuardarInventario.Enabled = true;
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
                    _activity.baseReader.RfidUhf.Session = Session.S0;
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

        private async Task ConnectTask()
        {
            try
            {
                if (_activity == null) return;

                if (_activity.baseReader == null || !_activity.IsReaderConnected)
                    await _activity.InitializeReader();

                if (_activity.baseReader != null && _activity.IsReaderConnected && _activity.baseReader.RfidUhf != null)
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
            SelectMask6cParam param = new SelectMask6cParam(true, Mask6cTarget.Sl, Mask6cAction.Ab,
                BankType.Epc, 0, maskEpc, maskEpc.Length * NIBLE_SIZE);
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
                try { _activity.baseReader.RfidUhf.SetSelectMask6cEnabled(i, false); }
                catch (ReaderException e) { throw e; }
            }
        }

        public void UpdateText(IDType id, string data)
            => Utilities.UpdateUIText(FragmentType.Inventario, (int)id, data);

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
                ? android12keymappingPath : keymappingPath;

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
                Bundle result = KeymappingCtrl.GetInstance(ctx).AddKeyMappings(
                    keyName, keyCode, wakeup,
                    MainReceiver.rfidGunPressed, getParams(tempKeyCode.GetBundle("broadcastDownParams")),
                    MainReceiver.rfidGunReleased, getParams(tempKeyCode.GetBundle("broadcastUpParams")),
                    getParams(tempKeyCode.GetBundle("startActivityParams")));

                if (result.GetInt("errorCode") == 0) Log.Debug(TAG, "Set Gun Key Code success");
                else Log.Error(TAG, "Set Gun Key Code failed: " + result.GetString("errorMsg"));
            });
        }

        private void restoreGunKeyCode()
        {
            if (tempKeyCode == null) return;
            Task.Run(() =>
            {
                Bundle result = KeymappingCtrl.GetInstance(
                    MainActivity.getInstance().ApplicationContext).ImportKeyMappings(getKeymappingPath());
                if (result.GetInt("errorCode") == 0) Log.Debug(TAG, "restoreGunKeyCode success");
                else Log.Error(TAG, "restoreGunKeyCode failed: " + result.GetString("errorMsg"));
                tempKeyCode = null;
            });
        }

        public void ClearGridView()
        {
            _activity.RunOnUiThread(() =>
            {
                tagsLeidos.Clear();
                adapter.NotifyDataSetChanged();
                totalCajasLeidasINT = 0;
                if (totalCajasLeidas != null)
                    totalCajasLeidas.Text = "0";
            });
        }

        public bool OnTouch(View v, MotionEvent e)
        {
            if (sprAreas != null && v.Id == sprAreas.Id && e.Action == MotionEventActions.Down)
            {
                Toast.MakeText(_activity, "Debe de dar Inicio a la captura del inventario!", ToastLength.Short).Show();
                return true;
            }
            return false;
        }

        #region VALIDAR TAG VS CATÁLOGO
        // FIX #8: Búsqueda O(1) usando HashSet
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
