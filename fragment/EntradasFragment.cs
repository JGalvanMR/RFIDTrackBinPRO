using Android.App;
using Android.Content;
using Android.Media;
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
using Newtonsoft.Json;
using RFIDTrackBin.enums;
using RFIDTrackBin.Modal;
using RFIDTrackBin.Model;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Exception = System.Exception;
using Math = Java.Lang.Math;
using StringBuilder = System.Text.StringBuilder;

namespace RFIDTrackBin.fragment
{
    public class EntradasFragment : BaseFragment, IReaderEventListener, IRfidUhfEventListener, MainReceiver.IEventLitener
    {
        static string TAG = typeof(EntradasFragment).Name;

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
        private Button btnGuardar;
        TextView connectedState;
        TextView totalCajasLeidas;
        TextView txtTotalAcumulado;
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

        DataSet ds = new DataSet();

        // FIX: DataTables locales — NO static para evitar contaminación entre fragmentos
        public DataTable vwProveedor = new DataTable("vwProveedor");
        public DataTable vwRanchos = new DataTable("vwRanchos");
        public DataTable vwTablas = new DataTable("vwTablas");

        int totalCajasLeidasINT = 0;
        int totalAcumuladoINT = 0;

        // FIX #7: Eliminado campo "SqlConnection thisConnection" que no se cerraba.
        // FIX: Eliminado campo "MySqlConnection mySqlConn" — credenciales hardcodeadas
        //      y NUNCA se usaba (la conexión MySQL se obtiene a través de GetURL / HttpClient).

        string IdClaveTag;
        View vwEntradas;

        string prov_nombre;
        string rch_nombre;
        string tbl_nombre;

        string prov_clave;
        string rch_clave;
        string tbl_clave;

        IMenu _menu;
        int IdConse;
        string tipoMovimiento = "E";

        ProgressBar progressBar;
        RelativeLayout loadingOverlay;

        #region VARIABLES PARA FLETES PENDIENTES
        private GridView gvFP;
        List<FleteItem> fpList = new List<FleteItem>();
        private myGVitemAdapterFP fpAdapter;
        private Android.App.AlertDialog fletesPendientesDialog;
        #endregion

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            return inflater.Inflate(Resource.Layout.EntradasFragment, container, false);
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

            // FIX #4: Carga en background
            await LoadProveedorAsync(view, Convert.ToInt32(_activity.idUnidadNegocio));

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

            _activity.EnableNavigationItems(Resource.Id.navigation_inventario, Resource.Id.navigation_salidas);

            progressBar = view.FindViewById<ProgressBar>(Resource.Id.progressBarGuardar);
            loadingOverlay = view.FindViewById<RelativeLayout>(Resource.Id.loadingOverlay);
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
            inflater.Inflate(Resource.Menu.menu_entradas, menu);
            _menu = menu;
            menu.FindItem(Resource.Id.fletes_pendientes_entradas).SetEnabled(true);
            menu.FindItem(Resource.Id.inicio_entradas).SetEnabled(false);
            menu.FindItem(Resource.Id.final_entradas).SetEnabled(false);
            base.OnCreateOptionsMenu(menu, inflater);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.inicio_entradas:
                    // FIX #4: InsertarEntrada ahora async — no bloquea UI
                    _ = InsertarEntradaAsync(tipoMovimiento,
                        ((MainActivity)Activity).usuario,
                        prov_clave, rch_clave, tbl_clave, "A");

                    _menu?.FindItem(Resource.Id.inicio_entradas)?.SetEnabled(false);
                    _menu?.FindItem(Resource.Id.final_entradas)?.SetEnabled(true);
                    btnGuardar.Enabled = true;
                    _activity.DisableNavigationItems(
                        Resource.Id.navigation_inventario,
                        Resource.Id.navigation_salidas,
                        Resource.Id.navigation_entradas);
                    return true;

                case Resource.Id.final_entradas:
                    // FIX #4: Operaciones de cierre encadenadas async
                    _ = FinalizarEntradaAsync(IdConse);
                    return true;

                case Resource.Id.fletes_pendientes_entradas:
                    MostrarDialogoFletesPendientes();
                    return true;

                default:
                    return base.OnOptionsItemSelected(item);
            }
        }

        // Helper para encadenar las operaciones de cierre async
        private async Task FinalizarEntradaAsync(int idConse)
        {
            await ActualizarHoraCierreAsync(idConse);
            await UpdateFechaUltimoMovimientoAsync(idConse);

            sprProveedor.SetSelection(0);
            sprProveedor.Enabled = true;
            sprRancho.SetSelection(0);
            sprTabla.SetSelection(0);
            btnGuardar.Enabled = false;

            _activity.EnableNavigationItems(
                Resource.Id.navigation_inventario,
                Resource.Id.navigation_salidas,
                Resource.Id.navigation_entradas);

            _menu?.FindItem(Resource.Id.inicio_entradas)?.SetEnabled(false);
            _menu?.FindItem(Resource.Id.final_entradas)?.SetEnabled(false);
            ClearGridView();
            totalAcumuladoINT = 0;
            txtTotalAcumulado.Text = "0";
        }
        #endregion

        #region CARGAR FLETES PENDIENTES
        private async void MostrarDialogoFletesPendientes()
        {
            LayoutInflater inflater = LayoutInflater.From(_activity);
            View dialogView = inflater.Inflate(Resource.Layout.FletesPendientes, null);

            gvFP = dialogView.FindViewById<GridView>(Resource.Id.gvFletesPendientes);

            if (gvFP == null)
            {
                Toast.MakeText(_activity, "No se encontró el GridView", ToastLength.Long).Show();
                return;
            }

            if (fpAdapter == null)
                fpAdapter = new myGVitemAdapterFP(_activity, fpList);

            gvFP.Adapter = fpAdapter;
            gvFP.ItemClick -= GvFletesPendientes_ItemClick;
            gvFP.ItemClick += GvFletesPendientes_ItemClick;

            await CargarFletesPendientes();

            var builder = new Android.App.AlertDialog.Builder(_activity, Resource.Style.AppTheme_CustomAlertDialog);
            builder.SetView(dialogView);
            builder.SetTitle("Fletes Pendientes");
            builder.SetCancelable(false);
            builder.SetPositiveButton("Cerrar", (s, e) =>
            {
                fpList.Clear();
                fpAdapter.NotifyDataSetChanged();
            });
            builder.Show();
        }

        private async Task CargarFletesPendientes()
        {
            fpList.Clear();

            try
            {
                // FIX #4: GetURL es async — no bloquea
                string url = await GetURL(Convert.ToInt32(_activity.idUnidadNegocio));

                if (string.IsNullOrEmpty(url))
                {
                    Toast.MakeText(Context, "URL no configurada", ToastLength.Long).Show();
                    return;
                }

                using HttpClient client = new HttpClient();
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Toast.MakeText(Context, "Error al consultar servidor", ToastLength.Long).Show();
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var fletes = JsonConvert.DeserializeObject<List<Flete>>(json);

                if (fletes == null || fletes.Count == 0) return;

                foreach (var f in fletes)
                {
                    fpList.Add(new FleteItem
                    {
                        IdFlete = f.id_flete,
                        Orde = f.orde,
                        IdProveedor = f.id_proveedor,
                        IdRancho = f.id_ranchoDET,
                        IdTabla = f.id_tablaDET
                    });
                }

                fpAdapter?.NotifyDataSetChanged();
            }
            catch (Exception ex)
            {
                Toast.MakeText(Context, ex.Message, ToastLength.Long).Show();
            }
        }

        private void GvFletesPendientes_ItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            try
            {
                var flete = fpList[e.Position];
                prov_clave = flete.IdProveedor;
                rch_clave = flete.IdRancho;
                tbl_clave = flete.IdTabla;
                ActualizarSpinnersDesdeFlete(flete);

                Toast.MakeText(_activity,
                    $"Proveedor: {prov_clave}\nRancho: {rch_clave}\nTabla: {tbl_clave}",
                    ToastLength.Short).Show();
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, ex.Message, ToastLength.Long).Show();
            }
        }

        private async void ActualizarSpinnersDesdeFlete(FleteItem flete)
        {
            try
            {
                if (vwEntradas == null) return;

                Spinner spinnerProv = vwEntradas.FindViewById<Spinner>(Resource.Id.sprProveedor);
                Spinner spinnerRancho = vwEntradas.FindViewById<Spinner>(Resource.Id.sprRancho);
                Spinner spinnerTabla = vwEntradas.FindViewById<Spinner>(Resource.Id.sprTabla);


                LoadProveedorAsync(vwEntradas, Convert.ToInt32(_activity.idUnidadNegocio)).Wait();
                SeleccionarSpinner(spinnerProv, vwProveedor, "IdProveedor", flete.IdProveedor, "NombreProveedor");
                await Task.Delay(200);
                LoadRanchosAsync(Convert.ToInt32(flete.IdProveedor)).Wait();
                SeleccionarSpinner(spinnerRancho, vwRanchos, "IdRancho", flete.IdRancho, "NombreRancho");
                await Task.Delay(200);
                LoadTablasAsync(Convert.ToInt32(flete.IdRancho)).Wait();
                SeleccionarSpinner(spinnerTabla, vwTablas, "IdTabla", flete.IdTabla, "NombreTabla");
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, ex.Message, ToastLength.Long).Show();
            }
        }

        private void SeleccionarSpinner(Spinner spinner, DataTable tabla, string campoClave, string valorClave, string campoNombre)
        {
            if (tabla == null || spinner?.Adapter == null) return;

            var row = tabla.Select($"{campoClave} = '{valorClave}'");
            if (row.Length == 0) return;

            string nombre = row[0][campoNombre].ToString().Trim();
            int index = ((ArrayAdapter)spinner.Adapter).GetPosition(nombre);
            if (index >= 0) spinner.SetSelection(index);
        }

        // FIX #4: GetURL es async — operación de red no bloquea el UI thread
        public async Task<string> GetURL(int IdUnidadNegocio)
        {
            return await Task.Run(() =>
            {
                using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                const string query = "SELECT getFletesPendientes FROM Tb_RFID_UnidadNegocio WHERE IdUnidadNegocio = @Id";
                using SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Id", IdUnidadNegocio);
                conn.Open();
                return cmd.ExecuteScalar()?.ToString();
            });
        }
        #endregion

        #region INICIAR ENTRADA
        // FIX #4: Versión async — INSERT no bloquea el UI thread
        public async Task<int> InsertarEntradaAsync(
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
                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    using SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@TipoMov", tipoMovimiento);
                    cmd.Parameters.AddWithValue("@Usuario", usuario ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Prov_Clave", entProveedor ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Ran_Clave", entRancho ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Tab_Clave", entTabla ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Mstr_Status", entStatus);
                    conn.Open();
                    object result = cmd.ExecuteScalar();
                    return (result != null && int.TryParse(result.ToString(), out int id)) ? id : -1;
                });

                IdConse = newId;
                Toast.MakeText(Activity, "Inicio De Entrada...", ToastLength.Short).Show();
                sprProveedor.Enabled = false;
                sprRancho.Enabled = false;
                sprTabla.Enabled = false;
            }
            catch (Exception ex)
            {
                MainActivity.ShowDialog("Error al iniciar entrada en Base de Datos:", ex.Message);
            }

            return IdConse;
        }
        #endregion

        #region FINALIZAR ENTRADA
        // FIX #4: Versión async
        public async Task ActualizarHoraCierreAsync(int idConse)
        {
            const string query = @"
                UPDATE Tb_RFID_Mstr
                SET HoraCierre = GETDATE()
                WHERE IdConse = @IdConse";

            try
            {
                int rows = await Task.Run(() =>
                {
                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    using SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@IdConse", idConse);
                    conn.Open();
                    return cmd.ExecuteNonQuery();
                });

                if (rows > 0)
                    Toast.MakeText(Activity, "Fin de Entrada...", ToastLength.Long).Show();
                else
                    MainActivity.ShowToast("No se encontró la entrada para actualizar.");
            }
            catch (Exception ex)
            {
                MainActivity.ShowDialog("Error al actualizar HoraCierre:", ex.Message);
            }
        }

        // FIX #4: Versión async
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
                int rows = await Task.Run(() =>
                {
                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    using SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@IdConseInv", idConseInv);
                    conn.Open();
                    return cmd.ExecuteNonQuery();
                });

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
            ((AndroidX.AppCompat.App.AppCompatActivity)Activity).SupportActionBar.Title = "ENTRADA";
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

        // FIX #1: _isInventoryInProgress se resetea cuando el inventario termina
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
                bool proveedorSeleccionado = !sprProveedor.Enabled && sprProveedor.SelectedItemPosition > 0;
                bool ranchoSeleccionado = !sprRancho.Enabled && sprRancho.SelectedItemPosition > 0;
                bool tablaSeleccionada = !sprTabla.Enabled && sprTabla.SelectedItemPosition > 0;

                if (!proveedorSeleccionado || !ranchoSeleccionado || !tablaSeleccionada || !btnGuardar.Enabled)
                {
                    MainActivity.ShowDialog("AVISO", "Debe de dar Inicio a la captura de la entrada y Seleccionar Proveedor, Rancho y Tabla!");
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

                // FIX #8: validaEPC ahora usa HashSet O(1)
                if (validaEPC(tag))
                {
                    PlayBeepSound();
                    tagEPCList.Add(new TagLeido { EPC = tag, RSSI = rssi, FechaLectura = DateTime.Now });
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

        #region SPINNERS — todos async (FIX #4)
        private async Task LoadProveedorAsync(View view, int idUnidadNegocio)
        {
            try
            {
                vwEntradas = view;

                DataTable dt = await Task.Run(() =>
                {
                    const string sql = @"
                        SELECT IdProveedor, NombreProveedor
                        FROM Tb_RFID_Proveedores
                        WHERE Activo = 1
                          AND IdUnidadNegocio = @idUnidadNegocio
                        ORDER BY NombreProveedor";

                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    using SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@idUnidadNegocio", idUnidadNegocio);
                    using SqlDataAdapter da = new SqlDataAdapter(cmd);
                    var table = new DataTable("vwProveedor");
                    da.Fill(table);
                    return table;
                });

                vwProveedor = dt;

                string[] lista = new string[dt.Rows.Count + 1];
                lista[0] = "Seleccione un Proveedor";
                for (int i = 0; i < dt.Rows.Count; i++)
                    lista[i + 1] = dt.Rows[i]["NombreProveedor"].ToString().Trim();

                Spinner spinner = view.FindViewById<Spinner>(Resource.Id.sprProveedor);
                var adp = new ArrayAdapter<string>(_activity, Android.Resource.Layout.SimpleSpinnerItem, lista);
                adp.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
                spinner.ItemSelected -= sprProveedor_ItemSelected;
                spinner.Adapter = adp;
                spinner.ItemSelected += sprProveedor_ItemSelected;
                spinner.Enabled = true;
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, "Error al cargar proveedores: " + ex.Message, ToastLength.Long).Show();
            }
        }

        private void sprProveedor_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            if (e.Position == 0) return;

            int idProveedor = Convert.ToInt32(vwProveedor.Rows[e.Position - 1]["IdProveedor"]);

            // FIX #4: carga en background
            _ = LoadRanchosAsync(idProveedor);
        }

        private async Task LoadRanchosAsync(int idProveedor)
        {
            try
            {
                DataTable dt = await Task.Run(() =>
                {
                    const string sql = @"
                        SELECT IdRancho, NombreRancho
                        FROM Tb_RFID_Ranchos
                        WHERE IdProveedor = @IdProveedor AND Activo = 1
                        ORDER BY NombreRancho";

                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    using SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@IdProveedor", idProveedor);
                    using SqlDataAdapter da = new SqlDataAdapter(cmd);
                    var table = new DataTable("vwRanchos");
                    da.Fill(table);
                    return table;
                });

                vwRanchos = dt;

                string[] lista = new string[dt.Rows.Count + 1];
                lista[0] = "Seleccione un Rancho";
                for (int i = 0; i < dt.Rows.Count; i++)
                    lista[i + 1] = dt.Rows[i]["NombreRancho"].ToString().Trim();

                Spinner spinner = vwEntradas.FindViewById<Spinner>(Resource.Id.sprRancho);
                var adp = new ArrayAdapter<string>(_activity, Android.Resource.Layout.SimpleSpinnerItem, lista);
                adp.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
                spinner.ItemSelected -= sprRancho_ItemSelected;
                spinner.Adapter = adp;
                spinner.ItemSelected += sprRancho_ItemSelected;
                spinner.Enabled = true;
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, ex.Message, ToastLength.Long).Show();
            }
        }

        private void sprRancho_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            if (e.Position == 0) return;

            int idRancho = Convert.ToInt32(vwRanchos.Rows[e.Position - 1]["IdRancho"]);
            _ = LoadTablasAsync(idRancho);
        }

        private async Task LoadTablasAsync(int idRancho)
        {
            try
            {
                DataTable dt = await Task.Run(() =>
                {
                    const string sql = @"
                        SELECT IdTabla, NombreTabla
                        FROM Tb_RFID_Tablas
                        WHERE IdRancho = @IdRancho AND Activo = 1
                        ORDER BY NombreTabla";

                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    using SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@IdRancho", idRancho);
                    using SqlDataAdapter da = new SqlDataAdapter(cmd);
                    var table = new DataTable("vwTablas");
                    da.Fill(table);
                    return table;
                });

                vwTablas = dt;

                string[] lista = new string[dt.Rows.Count + 1];
                lista[0] = "Seleccione una Tabla";
                for (int i = 0; i < dt.Rows.Count; i++)
                    lista[i + 1] = dt.Rows[i]["NombreTabla"].ToString().Trim();

                Spinner spinner = vwEntradas.FindViewById<Spinner>(Resource.Id.sprTabla);
                var adp = new ArrayAdapter<string>(_activity, Android.Resource.Layout.SimpleSpinnerItem, lista);
                adp.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
                spinner.ItemSelected -= sprTabla_ItemSelected;
                spinner.Adapter = adp;
                spinner.ItemSelected += sprTabla_ItemSelected;
                spinner.Enabled = true;
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, ex.Message, ToastLength.Long).Show();
            }
        }

        private void sprTabla_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            if (e.Position == 0) return;

            var selectedItem = ((Spinner)sender).GetItemAtPosition(e.Position)?.ToString();
            if (string.IsNullOrEmpty(selectedItem)) return;

            tbl_nombre = selectedItem;
            tbl_clave = getTbl_Clave(tbl_nombre);
            _menu?.FindItem(Resource.Id.inicio_entradas)?.SetEnabled(true);
        }

        private string getTbl_Clave(string nombre)
        {
            var rows = vwTablas.Select($"NombreTabla = '{nombre.Replace("'", "''")}'");
            return rows.Length > 0 ? rows[0]["IdTabla"].ToString().Trim() : "";
        }
        #endregion

        #region BOTÓN GUARDAR
        private void SetButtonClick()
        {
            btnGuardar.Click += async (s, e) =>
            {
                // FIX: Eliminada la llamada a ConnectTask en cada guardado.
                //      Solo verificar si el lector está disponible.
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
                    // FIX #4: INSERT en background thread
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

                        using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                        conn.Open();
                        using SqlCommand cmd = new SqlCommand(query, conn);
                        cmd.Parameters.Add(new SqlParameter("@IdClaveTag", SqlDbType.VarChar));
                        cmd.Parameters.Add(new SqlParameter("@IdConseInv", SqlDbType.Decimal)).Value = IdConse;

                        foreach (var tag in tagEPCList)
                        {
                            cmd.Parameters["@IdClaveTag"].Value = tag.EPC;
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

        #region CONFIGURACION RFID
        public void InitSetting()
        {
            try
            {
                if (_activity.baseReader?.RfidUhf != null)
                {
                    _activity.baseReader.RfidUhf.ModuleProfile = 3;
                    _activity.baseReader.RfidUhf.Power = 30;
                    _activity.baseReader.RfidUhf.InventoryTime = 150;
                    _activity.baseReader.RfidUhf.IdleTime = 0;
                    _activity.baseReader.RfidUhf.Target = Target.A;
                    _activity.baseReader.RfidUhf.Session = Session.S3;
                    _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.DynamicQ;
                    _activity.baseReader.RfidUhf.ToggleTarget = true;
                    _activity.baseReader.RfidUhf.ContinuousMode = true;
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
            => Utilities.UpdateUIText(FragmentType.Entradas, (int)id, data);

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
                tagEPCList.Clear();
                adapter.NotifyDataSetChanged();
                totalCajasLeidasINT = 0;
                if (totalCajasLeidas != null)
                    totalCajasLeidas.Text = "0";
            });
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
