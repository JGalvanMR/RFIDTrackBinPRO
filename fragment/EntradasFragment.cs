using System.Linq;
using System.Data;
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
        private Spinner sprProveedor;
        private Spinner sprRancho;
        private Spinner sprTabla;
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

        // FIX B1: Eliminado "DataSet ds" — declarado pero nunca utilizado en ninguna
        //         parte del fragmento. Instanciar DataSet tiene un coste de memoria no trivial.

        // DataTables locales — NO static para evitar contaminación entre instancias del fragmento
        public DataTable vwProveedor = new DataTable("vwProveedor");
        public DataTable vwRanchos = new DataTable("vwRanchos");
        public DataTable vwTablas = new DataTable("vwTablas");

        int totalCajasLeidasINT = 0;
        int totalAcumuladoINT = 0;

        string IdClaveTag;
        View vwEntradas;

        string prov_nombre;
        string rch_nombre;
        string tbl_nombre;

        string prov_clave;
        string rch_clave;
        string tbl_clave;
        private bool _isLoadingFlete = false;

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
        private AlertDialog _dialogoFletes;
        #endregion
        #region PERSISTENCIA DE ENTRADAS
        private const string PREFS_ENTRADAS = "rfid_entradas_prefs";
        private const string PREF_E_ID = "IdConseEntrada";
        private const string PREF_E_PROV = "ProvClaveEntrada";
        private const string PREF_E_RANCHO = "RchClaveEntrada";
        private const string PREF_E_TABLA = "TblClaveEntrada";
        private const string PREF_E_ACTIVA = "EntradaActiva";
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

            // Si hay sesión pendiente, restaurar; si no, cargar proveedores normalmente
            if (HaySesionEntradaPendiente())
            {
                MostrarDialogoEntradaPendiente();
            }
            else
            {
                await LoadProveedorAsync(vwEntradas, Convert.ToInt32(_activity.idUnidadNegocio)).ConfigureAwait(false);
            }
        }

        private bool HaySesionEntradaPendiente()
        {
            var prefs = _activity.GetSharedPreferences(PREFS_ENTRADAS, FileCreationMode.Private);
            return prefs.GetBoolean(PREF_E_ACTIVA, false);
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
                    _ = FinalizarEntradaAsync(IdConse);
                    return true;

                case Resource.Id.fletes_pendientes_entradas:
                    MostrarDialogoFletesPendientes();
                    return true;

                default:
                    return base.OnOptionsItemSelected(item);
            }
        }

        private async Task FinalizarEntradaAsync(int idConse)
        {
            await ActualizarHoraCierreAsync(idConse);
            await UpdateFechaUltimoMovimientoAsync(idConse);

            // ISSUE 2: Borrar la sesión persistida al cerrarla limpiamente
            LimpiarSesionEntrada();

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

            var builder = new AlertDialog.Builder(_activity, Resource.Style.AppTheme_CustomAlertDialog);
            builder.SetView(dialogView);
            builder.SetTitle("Fletes Pendientes");
            builder.SetCancelable(true); // Permite cerrar tocando fuera
            builder.SetPositiveButton("Cerrar", (s, e) =>
            {
                fpList.Clear();
                fpAdapter.NotifyDataSetChanged();
                // El diálogo se cierra automáticamente
            });

            _dialogoFletes = builder.Show();
            _dialogoFletes.DismissEvent += (s, e) => _dialogoFletes = null; // Limpiar referencia
        }

        private async Task CargarFletesPendientes()
        {
            fpList.Clear();

            try
            {
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

        private async void GvFletesPendientes_ItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            try
            {
                var flete = fpList[e.Position];
                await ActualizarSpinnersDesdeFletesAsync(flete);
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, ex.Message, ToastLength.Long).Show();
            }
            finally
            {
                _dialogoFletes?.Dismiss(); // ← siempre se ejecuta, con o sin excepción
            }
        }

        private async Task ActualizarSpinnersDesdeFletesAsync(FleteItem flete)
        {
            _isLoadingFlete = true;
            try
            {
                if (vwEntradas == null)
                    throw new Exception("La vista de entradas no está disponible.");

                Spinner spinnerProv = vwEntradas.FindViewById<Spinner>(Resource.Id.sprProveedor);
                Spinner spinnerRancho = vwEntradas.FindViewById<Spinner>(Resource.Id.sprRancho);
                Spinner spinnerTabla = vwEntradas.FindViewById<Spinner>(Resource.Id.sprTabla);

                // Desconectar handlers
                spinnerProv.ItemSelected -= sprProveedor_ItemSelected;
                spinnerRancho.ItemSelected -= sprRancho_ItemSelected;
                spinnerTabla.ItemSelected -= sprTabla_ItemSelected;

                prov_clave = flete.IdProveedor;
                rch_clave = flete.IdRancho;
                tbl_clave = flete.IdTabla;

                #region PROVEEDOR
                await LoadProveedorAsync(vwEntradas, Convert.ToInt32(_activity.idUnidadNegocio));
                int posProveedor = EncontrarPosicionEnSpinner(spinnerProv, vwProveedor,
                    "Prov_Clave", flete.IdProveedor, "NombreProveedor");

                if (posProveedor <= 0)
                {
                    Log.Warn(TAG, $"Proveedor {flete.IdProveedor} no encontrado. Creando...");
                    bool creado = await CrearProveedorDesdeOrigen(flete.IdProveedor, unidadOrigen: 3);
                    if (!creado)
                    {
                        string msg = $"No se pudo crear el proveedor {flete.IdProveedor}";
                        Log.Error(TAG, msg);
                        Toast.MakeText(_activity, msg, ToastLength.Long).Show();
                        throw new Exception(msg);
                    }
                    await LoadProveedorAsync(vwEntradas, Convert.ToInt32(_activity.idUnidadNegocio));
                    posProveedor = EncontrarPosicionEnSpinner(spinnerProv, vwProveedor,
                        "Prov_Clave", flete.IdProveedor, "NombreProveedor");
                }

                if (posProveedor <= 0)
                {
                    string msg = $"No se pudo encontrar ni crear el proveedor {flete.IdProveedor}";
                    Log.Error(TAG, msg);
                    Toast.MakeText(_activity, msg, ToastLength.Long).Show();
                    throw new Exception(msg);
                }

                spinnerProv.SetSelection(posProveedor);
                int idProveedor = Convert.ToInt32(vwProveedor.Rows[posProveedor - 1]["IdProveedor"]);
                #endregion

                #region RANCHO
                await LoadRanchosAsync(idProveedor);
                int posRancho = EncontrarPosicionEnSpinner(spinnerRancho, vwRanchos,
                    "Ran_Clave", flete.IdRancho, "NombreRancho");

                if (posRancho <= 0)
                {
                    Log.Warn(TAG, $"Rancho {flete.IdRancho} no encontrado. Creando...");
                    bool creado = await CrearRanchoDesdeOrigen(flete.IdProveedor, flete.IdRancho, flete.Orde, idProveedor, unidadOrigen: 3);
                    if (!creado)
                    {
                        string msg = $"No se pudo crear el rancho {flete.IdRancho}";
                        Log.Error(TAG, msg);
                        Toast.MakeText(_activity, msg, ToastLength.Long).Show();
                        throw new Exception(msg);
                    }
                    await LoadRanchosAsync(idProveedor);
                    posRancho = EncontrarPosicionEnSpinner(spinnerRancho, vwRanchos,
                        "Ran_Clave", flete.IdRancho, "NombreRancho");
                }

                if (posRancho <= 0)
                {
                    string msg = $"No se pudo encontrar ni crear el rancho {flete.IdRancho}";
                    Log.Error(TAG, msg);
                    Toast.MakeText(_activity, msg, ToastLength.Long).Show();
                    throw new Exception(msg);
                }

                spinnerRancho.SetSelection(posRancho);
                int idRancho = Convert.ToInt32(vwRanchos.Rows[posRancho - 1]["IdRancho"]);
                #endregion

                #region TABLA
                await LoadTablaAsync(idRancho);
                // Logs temporales (puedes quitarlos después)
                Log.Debug(TAG, $"Tablas cargadas: {vwTablas.Rows.Count} filas");
                foreach (DataRow row in vwTablas.Rows)
                {
                    Log.Debug(TAG, $" - Clave: '{row["Tab_Clave"]}', Nombre: '{row["NombreTabla"]}'");
                }

                int posTabla = EncontrarPosicionEnSpinner(spinnerTabla, vwTablas,
                    "Tab_Clave", flete.IdTabla, "NombreTabla");
                Log.Debug(TAG, $"Posición encontrada para '{flete.IdTabla}': {posTabla}");

                if (posTabla <= 0)
                {
                    Log.Warn(TAG, $"Tabla {flete.IdTabla} no encontrada. Creando...");
                    bool creada = await CrearTablaDesdeOrigen(flete.IdProveedor, flete.IdRancho, flete.IdTabla, idRancho, unidadOrigen: 3);
                    if (!creada)
                    {
                        string msg = $"No se pudo crear la tabla {flete.IdTabla}";
                        Log.Error(TAG, msg);
                        Toast.MakeText(_activity, msg, ToastLength.Long).Show();
                        throw new Exception(msg);
                    }

                    await Task.Delay(100); // Pequeña pausa para asegurar commit en BD
                    await LoadTablaAsync(idRancho);
                    posTabla = EncontrarPosicionEnSpinner(spinnerTabla, vwTablas,
                        "Tab_Clave", flete.IdTabla, "NombreTabla");

                    if (posTabla <= 0)
                    {
                        string msg = $"Tabla {flete.IdTabla} creada pero no encontrada después de recargar";
                        Log.Error(TAG, msg);
                        Toast.MakeText(_activity, msg, ToastLength.Long).Show();
                        throw new Exception(msg);
                    }
                }

                if (posTabla <= 0)
                {
                    string msg = $"No se pudo encontrar ni crear la tabla {flete.IdTabla}";
                    Log.Error(TAG, msg);
                    Toast.MakeText(_activity, msg, ToastLength.Long).Show();
                    throw new Exception(msg);
                }

                spinnerTabla.SetSelection(posTabla);
                tbl_id = Convert.ToInt32(vwTablas.Rows[posTabla - 1]["IdTabla"]);
                tbl_nombre = vwTablas.Rows[posTabla - 1]["NombreTabla"].ToString().Trim();
                #endregion

                // **NUEVO: Bloquear spinners y habilitar botón guardar (entrada en curso)**
                spinnerProv.Enabled = true;
                spinnerRancho.Enabled = true;
                spinnerTabla.Enabled = true;
                btnGuardar.Enabled = false;

                _menu?.FindItem(Resource.Id.inicio_entradas)?.SetEnabled(true);
                _menu?.FindItem(Resource.Id.final_entradas)?.SetEnabled(false);

                _activity.DisableNavigationItems(
                    Resource.Id.navigation_inventario,
                    Resource.Id.navigation_salidas);

                Toast.MakeText(_activity,
                    $"Flete cargado:\nProveedor: {prov_clave}\nRancho: {rch_clave}\nTabla: {tbl_clave}",
                    ToastLength.Short).Show();
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error en ActualizarSpinnersDesdeFletesAsync: {ex.Message}");
                throw; // Relanza para que el evento lo capture
            }
            // EN: ActualizarSpinnersDesdeFletesAsync → bloque finally

            finally
            {
                Spinner spinnerProv = vwEntradas?.FindViewById<Spinner>(Resource.Id.sprProveedor);
                Spinner spinnerRancho = vwEntradas?.FindViewById<Spinner>(Resource.Id.sprRancho);
                Spinner spinnerTabla = vwEntradas?.FindViewById<Spinner>(Resource.Id.sprTabla);

                if (spinnerProv != null) { spinnerProv.ItemSelected -= sprProveedor_ItemSelected; spinnerProv.ItemSelected += sprProveedor_ItemSelected; }
                if (spinnerRancho != null) { spinnerRancho.ItemSelected -= sprRancho_ItemSelected; spinnerRancho.ItemSelected += sprRancho_ItemSelected; }
                if (spinnerTabla != null) { spinnerTabla.ItemSelected -= sprTabla_ItemSelected; spinnerTabla.ItemSelected += sprTabla_ItemSelected; }

                // ✅ FIX: No poner _isLoadingFlete = false aquí directamente.
                // Android encola los ItemSelected de SetSelection() — si los dejamos disparar
                // con _isLoadingFlete = false, sprRancho_ItemSelected llama LoadTablaAsync()
                // y destruye el adapter recién seteado. PostDelayed garantiza que la guardia
                // sigue activa mientras Android drena esos eventos pendientes.
                new Android.OS.Handler(Android.OS.Looper.MainLooper).PostDelayed(
                    () => _isLoadingFlete = false, 200);
            }
        }

        /// <summary>
        /// Busca la posición de un valor en el Spinner usando la DataTable como mapa.
        ///
        /// HISTORIAL DE BUGS EN ESTE MÉTODO:
        ///   v1 — Usaba DataTable.Select("col = 'val'") siempre con comillas.
        ///        Fallaba con columnas INT: EvaluateException Cannot perform '=' on Int32 and String.
        ///   v2 — Usaba int.TryParse para decidir comillas.
        ///        Fallaba con "03": parseaba a 3, sin comillas, sobre columna String → error.
        ///   v3 — Usaba col.DataType para decidir comillas.
        ///        Fallaba con valores alfanuméricos ("R21") en columnas INT:
        ///        generaba "IdRancho = R21" → ADO.NET lo interpreta como nombre de columna
        ///        → EvaluateException: Cannot find column [R21].
        ///
        /// SOLUCIÓN DEFINITIVA: usar LINQ sobre la DataTable.
        ///   LINQ convierte todo a string para comparar — no usa el motor de expresiones
        ///   de ADO.NET, elimina todos los problemas de tipos de raíz.
        /// </summary>
        private int EncontrarPosicionEnSpinner(Spinner spinner, DataTable tabla,
            string campoClave, string valorClave, string campoNombre)
        {
            if (tabla == null || spinner?.Adapter == null || string.IsNullOrEmpty(valorClave))
                return -1;

            try
            {
                if (tabla.Columns[campoClave] == null)
                {
                    Log.Warn(TAG, $"EncontrarPosicionEnSpinner: columna '{campoClave}' no existe en {tabla.TableName}");
                    return -1;
                }

                // LINQ: compara como string, inmune al tipo de la columna (INT, VARCHAR, etc.)
                // Funciona con "03", "08", "R21", valores con leading-zeros, etc.

                string valorBuscar = valorClave.Trim();
                DataRow[] rows = tabla.AsEnumerable()
                    .Where(r =>
                    {
                        if (r[campoClave] == null || r[campoClave] == DBNull.Value) return false;
                        string cellStr = r[campoClave].ToString().Trim();
                        if (cellStr == valorBuscar) return true;
                        // Normalización numérica: "03" == "3", "38" == "38"
                        if (int.TryParse(valorBuscar, out int vInt) && int.TryParse(cellStr, out int cInt))
                            return vInt == cInt;
                        return false;
                    })
                    .ToArray();

                if (rows.Length == 0)
                {
                    Log.Warn(TAG, $"EncontrarPosicionEnSpinner: no se encontró {campoClave}='{valorClave}' en {tabla.TableName}");
                    return -1;
                }

                string nombre = rows[0][campoNombre]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(nombre)) return -1;

                // Buscar en el adapter con comparación tolerante a espacios y case-insensitive
                for (int i = 0; i < spinner.Adapter.Count; i++)
                {
                    string item = spinner.Adapter.GetItem(i)?.ToString()?.Trim() ?? "";
                    if (string.Equals(item, nombre, StringComparison.OrdinalIgnoreCase))
                        return i;
                }

                Log.Warn(TAG, $"EncontrarPosicionEnSpinner: nombre '{nombre}' no encontrado en adapter del spinner");
                return -1;
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"EncontrarPosicionEnSpinner error: {ex.Message}");
                return -1;
            }
        }

        // Mantenido para compatibilidad — ya no se usa en el flujo de flete
        private void SeleccionarSpinner(Spinner spinner, DataTable tabla, string campoClave, string valorClave, string campoNombre)
        {
            int pos = EncontrarPosicionEnSpinner(spinner, tabla, campoClave, valorClave, campoNombre);
            if (pos >= 0) spinner.SetSelection(pos);
        }

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

                // ISSUE 2: Persistir la sesión activa para sobrevivir Home → regreso
                GuardarSesionEntrada();
                Log.Debug(TAG, $"Entrada insertada en BD: ID={IdConse}");

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

        // ─── ISSUE 2: PERSISTENCIA DE SESIÓN DE ENTRADA ──────────────────────────
        // Patrón idéntico al de InventarioFragment. Guarda el estado en SharedPreferences
        // para que al regresar desde Home se restaure: IdConse, spinners y estado de menú.
        // Clave de archivo "rfid_entradas_prefs" — no colisiona con "rfid_salidas_prefs".

        #region GESTION DE ENTRADAS PERSISTENTES
        private void MostrarDialogoEntradaPendiente()
        {
            var prefs = _activity.GetSharedPreferences(PREFS_ENTRADAS, FileCreationMode.Private);
            int idPendiente = prefs.GetInt(PREF_E_ID, 0);
            string provPendiente = prefs.GetString(PREF_E_PROV, "");
            string ranchoPendiente = prefs.GetString(PREF_E_RANCHO, "");
            string tablaPendiente = prefs.GetString(PREF_E_TABLA, "");

            string mensaje = $"Tiene una entrada sin finalizar:\n\n" +
                             $"ID: {idPendiente}\n" +
                             $"Proveedor: {provPendiente}\n" +
                             $"Rancho: {ranchoPendiente}\n" +
                             $"Tabla: {tablaPendiente}";

            new AlertDialog.Builder(_activity)
                .SetTitle("Entrada Pendiente")
                .SetMessage(mensaje)
                .SetPositiveButton("Continuar", async (s, e) =>
                {
                    await RestaurarSesionEntradaAsync();
                })
                .SetNegativeButton("Cerrar Ahora", async (s, e) =>
                {
                    await CerrarEntradaHuerfanaAsync(idPendiente);
                    LimpiarSesionEntrada();
                    // Después de cerrar, cargar proveedores normalmente
                    await LoadProveedorAsync(vwEntradas, Convert.ToInt32(_activity.idUnidadNegocio));
                })
                .SetCancelable(false)
                .Show();
        }
        private void GuardarSesionEntrada()
        {
            if (IdConse <= 0) return;
            var editor = _activity.GetSharedPreferences(PREFS_ENTRADAS, FileCreationMode.Private).Edit();
            editor.PutInt(PREF_E_ID, IdConse);
            editor.PutString(PREF_E_PROV, prov_clave ?? "");
            editor.PutString(PREF_E_RANCHO, rch_clave ?? "");
            editor.PutString(PREF_E_TABLA, tbl_clave ?? "");
            editor.PutBoolean(PREF_E_ACTIVA, true);
            editor.Apply();
            Log.Debug(TAG, $"Entrada guardada en prefs: ID={IdConse}");
        }

        private void LimpiarSesionEntrada()
        {
            _activity.GetSharedPreferences(PREFS_ENTRADAS, FileCreationMode.Private).Edit().Clear().Apply();
            Log.Debug(TAG, "Sesión de entrada limpiada en prefs");
        }
        private async Task RestaurarSesionEntradaAsync()
        {
            var prefs = _activity.GetSharedPreferences(PREFS_ENTRADAS, FileCreationMode.Private);
            if (!prefs.GetBoolean(PREF_E_ACTIVA, false)) return;

            int savedId = prefs.GetInt(PREF_E_ID, -1);
            string savedProv = prefs.GetString(PREF_E_PROV, "");
            string savedRancho = prefs.GetString(PREF_E_RANCHO, "");
            string savedTabla = prefs.GetString(PREF_E_TABLA, "");

            if (savedId <= 0) { LimpiarSesionEntrada(); return; }

            // Verificar que la entrada sigue abierta en BD
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

            if (!sigueAbierta) { LimpiarSesionEntrada(); return; }

            // Restaurar usando el método unificado
            try
            {
                await ActualizarSpinnersDesdeFletesAsync(new FleteItem
                {
                    IdProveedor = savedProv,
                    IdRancho = savedRancho,
                    IdTabla = savedTabla
                });

                // Una vez restaurado, actualizar UI
                IdConse = savedId;
                prov_clave = savedProv;
                rch_clave = savedRancho;
                tbl_clave = savedTabla;

                sprProveedor.Enabled = false;
                sprRancho.Enabled = false;
                sprTabla.Enabled = false;
                btnGuardar.Enabled = true;

                _menu?.FindItem(Resource.Id.inicio_entradas)?.SetEnabled(false);
                _menu?.FindItem(Resource.Id.final_entradas)?.SetEnabled(true);

                _activity.DisableNavigationItems(
                    Resource.Id.navigation_inventario,
                    Resource.Id.navigation_salidas,
                    Resource.Id.navigation_entradas);

                Log.Debug(TAG, $"Sesión de entrada restaurada: ID={IdConse}");
                Toast.MakeText(_activity, $"Entrada #{IdConse} en curso", ToastLength.Short).Show();
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al restaurar entrada: {ex.Message}");
                Toast.MakeText(_activity, "Error al restaurar la entrada. Se cancelará la sesión.", ToastLength.Long).Show();
                LimpiarSesionEntrada();
            }
        }

        private async Task CerrarEntradaHuerfanaAsync(int idEntrada)
        {
            try
            {
                int filas = await Task.Run(() =>
                {
                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    conn.Open();
                    // Aquí debes definir qué significa "cerrar" una entrada.
                    // Puede ser actualizar un campo de estado o simplemente eliminar registros temporales.
                    // Ajusta según tu lógica de negocio.
                    const string query = @"UPDATE Tb_RFID_Mstr 
                                   SET Mstr_Status = 'C'  -- 'C' = Cancelado/Cerrado
                                   WHERE IdConse = @Id AND Mstr_Status = 'A'";
                    using SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@Id", idEntrada);
                    return cmd.ExecuteNonQuery();
                });

                if (filas > 0)
                    Toast.MakeText(_activity, $"Entrada #{idEntrada} cerrada", ToastLength.Short).Show();
                else
                    Toast.MakeText(_activity, $"La entrada #{idEntrada} ya estaba cerrada", ToastLength.Short).Show();
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al cerrar entrada huérfana: {ex.Message}");
                Toast.MakeText(_activity, "No se pudo cerrar la entrada pendiente", ToastLength.Long).Show();
            }
        }
        #endregion

        #region FINALIZAR ENTRADA
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
            vwEntradas = view;
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
        private async Task LoadProveedorAsync(View view, int idUnidadNegocio)
        {
            try
            {
                vwEntradas = view;

                DataTable dt = await Task.Run(() =>
                {
                    const string sql = @"
                        SELECT IdProveedor, Prov_Clave, NombreProveedor
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
            if (_isLoadingFlete) return;   // Ignorar durante carga automática
            if (e.Position == 0) return;
            int idProveedor = Convert.ToInt32(vwProveedor.Rows[e.Position - 1]["IdProveedor"]);
            _ = LoadRanchosAsync(idProveedor);
        }


        private async Task LoadRanchosAsync(int idProveedor)
        {
            try
            {
                DataTable dt = await Task.Run(() =>
                {
                    const string sql = @"
                SELECT IdRancho, Ran_Clave, NombreRancho
                FROM Tb_RFID_Ranchos
                WHERE IdProveedor = @idProveedor AND Activo = 1
                ORDER BY NombreRancho";

                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    using SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@idProveedor", idProveedor);
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
            if (_isLoadingFlete) return;
            if (e.Position == 0) return;
            int idRancho = Convert.ToInt32(vwRanchos.Rows[e.Position - 1]["IdRancho"]);
            _ = LoadTablaAsync(idRancho);
        }

        private async Task LoadTablaAsync(int idRancho)
        {
            try
            {
                DataTable dt = await Task.Run(() =>
                {
                    const string sql = @"
                SELECT IdTabla, Tab_Clave, NombreTabla
                FROM Tb_RFID_Tablas
                WHERE IdRancho = @idRancho AND Activo = 1
                ORDER BY NombreTabla";

                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    using SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@idRancho", idRancho);
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

        private async Task LoadTablasAsync(string provClave, string ranClave)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion))
                {
                    await conn.OpenAsync();

                    string query = @"
                SELECT t.IdTabla, t.Tab_Clave, t.NombreTabla
                FROM Tb_RFID_Tablas t
                WHERE t.IdRancho =
                (
                    SELECT r.IdRancho
                    FROM Tb_RFID_Ranchos r
                    WHERE r.Ran_Clave = @RanClave
                    AND r.IdProveedor =
                    (
                        SELECT p.IdProveedor
                        FROM Tb_RFID_Proveedores p
                        WHERE p.Prov_Clave = @ProvClave
                    )
                )
                ORDER BY t.NombreTabla";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@ProvClave", provClave);
                        cmd.Parameters.AddWithValue("@RanClave", ranClave);

                        SqlDataAdapter da = new SqlDataAdapter(cmd);

                        vwTablas = new DataTable();
                        da.Fill(vwTablas);
                    }
                }

                Spinner spinner = vwEntradas.FindViewById<Spinner>(Resource.Id.sprTabla);

                var adapter = new ArrayAdapter<string>(
                    _activity,
                    Android.Resource.Layout.SimpleSpinnerItem,
                    vwTablas.AsEnumerable()
                        .Select(r => r["NombreTabla"].ToString())
                        .ToList());

                adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);

                spinner.Adapter = adapter;
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"LoadTablasAsync: {ex.Message}");
            }
        }
        private int tbl_id;
        private void sprTabla_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            if (_isLoadingFlete) return; // Ignorar durante carga automática
            if (e.Position == 0) return;

            int idTabla = Convert.ToInt32(vwTablas.Rows[e.Position - 1]["IdTabla"]);
            string nombreTabla = vwTablas.Rows[e.Position - 1]["NombreTabla"].ToString().Trim();

            tbl_id = idTabla;
            tbl_nombre = nombreTabla;

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
                catch (ReaderException)
                {
                    // FIX M3: "throw;" en lugar de "throw e;" para preservar
                    //         el stack trace original de la excepción.
                    throw;
                }
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

        #region CREAR PROVEEDOR DESDE ORIGEN
        private async Task<bool> CrearProveedorDesdeOrigen(string provClave, int? unidadOrigen = 3)
        {
            try
            {
                using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                await conn.OpenAsync();

                // 1. Intentar en unidad origen
                if (unidadOrigen.HasValue)
                {
                    var prov = await BuscarProveedorPorClave(conn, provClave, unidadOrigen.Value);
                    if (prov != null)
                        return await InsertarProveedor(conn, prov, provClave);
                }

                // 2. Buscar en cualquier unidad
                var provAny = await BuscarProveedorPorClave(conn, provClave, null);
                if (provAny != null)
                    return await InsertarProveedor(conn, provAny, provClave);

                // 3. Crear por defecto
                return await CrearProveedorPorDefecto(conn, provClave);
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error en CrearProveedorDesdeOrigen: {ex.Message}");
                return false;
            }
        }

        private async Task<ProveedorOrigen> BuscarProveedorPorClave(SqlConnection conn, string provClave, int? unidad)
        {
            string query = @"
        SELECT NombreProveedor, RFC, Contacto, Telefono, Email, Activo, UsuarioCreacion
        FROM Tb_RFID_Proveedores
        WHERE Prov_Clave = @ProvClave";
            if (unidad.HasValue)
                query += " AND IdUnidadNegocio = @Unidad";

            using SqlCommand cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@ProvClave", provClave);
            if (unidad.HasValue)
                cmd.Parameters.AddWithValue("@Unidad", unidad.Value);

            using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ProveedorOrigen
                {
                    NombreProveedor = reader["NombreProveedor"].ToString(),
                    RFC = reader["RFC"]?.ToString(),
                    Contacto = reader["Contacto"]?.ToString(),
                    Telefono = reader["Telefono"]?.ToString(),
                    Email = reader["Email"]?.ToString(),
                    Activo = Convert.ToBoolean(reader["Activo"]),
                    UsuarioCreacion = reader["UsuarioCreacion"]?.ToString()
                };
            }
            return null;
        }

        private async Task<bool> InsertarProveedor(SqlConnection conn, ProveedorOrigen prov, string provClave)
        {
            string insert = @"
        INSERT INTO Tb_RFID_Proveedores 
            (Prov_Clave, NombreProveedor, RFC, IdUnidadNegocio, Contacto, Telefono, Email, Activo, FechaCreacion, UsuarioCreacion)
        VALUES 
            (@ProvClave, @NombreProveedor, @RFC, @IdUnidadNegocio, @Contacto, @Telefono, @Email, @Activo, GETDATE(), @UsuarioCreacion)";

            using SqlCommand cmd = new SqlCommand(insert, conn);
            cmd.Parameters.AddWithValue("@ProvClave", provClave);
            cmd.Parameters.AddWithValue("@NombreProveedor", prov.NombreProveedor);
            cmd.Parameters.AddWithValue("@RFC", prov.RFC ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@IdUnidadNegocio", Convert.ToInt32(_activity.idUnidadNegocio)); // unidad actual
            cmd.Parameters.AddWithValue("@Contacto", prov.Contacto ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Telefono", prov.Telefono ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", prov.Email ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Activo", prov.Activo);
            cmd.Parameters.AddWithValue("@UsuarioCreacion", prov.UsuarioCreacion ?? (object)DBNull.Value);

            int filas = await cmd.ExecuteNonQueryAsync();
            return filas > 0;
        }

        private async Task<bool> CrearProveedorPorDefecto(SqlConnection conn, string provClave)
        {
            string insert = @"
        INSERT INTO Tb_RFID_Proveedores 
            (Prov_Clave, NombreProveedor, IdUnidadNegocio, Activo, FechaCreacion, UsuarioCreacion)
        VALUES 
            (@ProvClave, @NombreProveedor, @IdUnidadNegocio, 1, GETDATE(), NULL)";

            using SqlCommand cmd = new SqlCommand(insert, conn);
            cmd.Parameters.AddWithValue("@ProvClave", provClave);
            cmd.Parameters.AddWithValue("@NombreProveedor", provClave); // nombre por defecto = clave
            cmd.Parameters.AddWithValue("@IdUnidadNegocio", Convert.ToInt32(_activity.idUnidadNegocio));

            int filas = await cmd.ExecuteNonQueryAsync();
            return filas > 0;
        }
        #endregion

        #region CREAR RANCHO DESDE ORIGEN
        private async Task<bool> CrearRanchoDesdeOrigen(string provClave, string ranClave, string nombreRancho, int idProveedorDestino, int? unidadOrigen = 3)
        {
            try
            {
                using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                await conn.OpenAsync();

                // 1. Intentar en unidad origen
                if (unidadOrigen.HasValue)
                {
                    var rancho = await BuscarRanchoPorClaves(conn, provClave, ranClave, unidadOrigen.Value);
                    if (rancho != null)
                        return await InsertarRancho(conn, rancho, ranClave, idProveedorDestino);
                }

                // 2. Buscar en cualquier unidad
                var ranchoAny = await BuscarRanchoPorClaves(conn, provClave, ranClave, null);
                if (ranchoAny != null)
                    return await InsertarRancho(conn, ranchoAny, ranClave, idProveedorDestino);

                // 3. Crear por defecto usando el nombre del flete
                return await CrearRanchoPorDefecto(conn, ranClave, nombreRancho, idProveedorDestino);
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error en CrearRanchoDesdeOrigen: {ex.Message}");
                return false;
            }
        }

        private async Task<RanchoOrigen> BuscarRanchoPorClaves(SqlConnection conn, string provClave, string ranClave, int? unidad)
        {
            string query = @"
        SELECT r.NombreRancho, r.Latitud, r.Longitud, r.Direccion, r.Municipio, r.Estado, r.Activo, r.UsuarioCreacion
        FROM Tb_RFID_Ranchos r
        INNER JOIN Tb_RFID_Proveedores p ON r.IdProveedor = p.IdProveedor
        WHERE p.Prov_Clave = @ProvClave AND r.Ran_Clave = @RanClave";
            if (unidad.HasValue)
                query += " AND p.IdUnidadNegocio = @Unidad";

            using SqlCommand cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@ProvClave", provClave);
            cmd.Parameters.AddWithValue("@RanClave", ranClave);
            if (unidad.HasValue)
                cmd.Parameters.AddWithValue("@Unidad", unidad.Value);

            using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new RanchoOrigen
                {
                    NombreRancho = reader["NombreRancho"].ToString(),
                    Latitud = reader["Latitud"] != DBNull.Value ? Convert.ToDouble(reader["Latitud"]) : (double?)null,
                    Longitud = reader["Longitud"] != DBNull.Value ? Convert.ToDouble(reader["Longitud"]) : (double?)null,
                    Direccion = reader["Direccion"]?.ToString(),
                    Municipio = reader["Municipio"]?.ToString(),
                    Estado = reader["Estado"]?.ToString(),
                    Activo = Convert.ToBoolean(reader["Activo"]),
                    UsuarioCreacion = reader["UsuarioCreacion"]?.ToString()
                };
            }
            return null;
        }

        private async Task<bool> InsertarRancho(SqlConnection conn, RanchoOrigen rancho, string ranClave, int idProveedorDestino)
        {
            string insert = @"
        INSERT INTO Tb_RFID_Ranchos 
            (Ran_Clave, NombreRancho, IdProveedor, Latitud, Longitud, Direccion, Municipio, Estado, Activo, IdUnidadNegocio, FechaCreacion, UsuarioCreacion)
        VALUES 
            (@RanClave, @NombreRancho, @IdProveedor, @Latitud, @Longitud, @Direccion, @Municipio, @Estado, @Activo, @IdUnidadNegocio, GETDATE(), @UsuarioCreacion)";

            using SqlCommand cmd = new SqlCommand(insert, conn);
            cmd.Parameters.AddWithValue("@RanClave", ranClave);
            cmd.Parameters.AddWithValue("@NombreRancho", rancho.NombreRancho);
            cmd.Parameters.AddWithValue("@IdProveedor", idProveedorDestino);
            cmd.Parameters.AddWithValue("@Latitud", rancho.Latitud ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Longitud", rancho.Longitud ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Direccion", rancho.Direccion ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Municipio", rancho.Municipio ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Estado", rancho.Estado ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Activo", rancho.Activo);
            cmd.Parameters.AddWithValue("@IdUnidadNegocio", Convert.ToInt32(_activity.idUnidadNegocio)); // ← NUEVO
            cmd.Parameters.AddWithValue("@UsuarioCreacion", rancho.UsuarioCreacion ?? (object)DBNull.Value);

            int filas = await cmd.ExecuteNonQueryAsync();
            return filas > 0;
        }

        private async Task<bool> CrearRanchoPorDefecto(SqlConnection conn, string ranClave, string nombreRancho, int idProveedorDestino)
        {
            string insert = @"
        INSERT INTO Tb_RFID_Ranchos 
            (Ran_Clave, NombreRancho, IdProveedor, Activo, IdUnidadNegocio, FechaCreacion, UsuarioCreacion)
        VALUES 
            (@RanClave, @NombreRancho, @IdProveedor, 1, @IdUnidadNegocio, GETDATE(), NULL)";

            using SqlCommand cmd = new SqlCommand(insert, conn);
            cmd.Parameters.AddWithValue("@RanClave", ranClave);
            cmd.Parameters.AddWithValue("@NombreRancho", nombreRancho); // Usamos el nombre del flete
            cmd.Parameters.AddWithValue("@IdProveedor", idProveedorDestino);
            cmd.Parameters.AddWithValue("@IdUnidadNegocio", Convert.ToInt32(_activity.idUnidadNegocio));

            int filas = await cmd.ExecuteNonQueryAsync();
            return filas > 0;
        }
        #endregion

        #region CREAR TABLA DESDE ORIGEN
        private async Task<bool> CrearTablaDesdeOrigen(string provClave, string ranClave, string tabClave, int idRanchoDestino, int? unidadOrigen = 3)
        {
            try
            {
                using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                await conn.OpenAsync();

                // 1. Intentar en unidad origen
                if (unidadOrigen.HasValue)
                {
                    var tabla = await BuscarTablaPorClaves(conn, provClave, ranClave, tabClave, unidadOrigen.Value);
                    if (tabla != null)
                        return await InsertarTabla(conn, tabla, tabClave, idRanchoDestino);
                }

                // 2. Buscar en cualquier unidad
                var tablaAny = await BuscarTablaPorClaves(conn, provClave, ranClave, tabClave, null);
                if (tablaAny != null)
                    return await InsertarTabla(conn, tablaAny, tabClave, idRanchoDestino);

                // 3. Crear por defecto
                return await CrearTablaPorDefecto(conn, tabClave, idRanchoDestino);
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error en CrearTablaDesdeOrigen: {ex.Message}");
                return false;
            }
        }

        private async Task<TablaOrigen> BuscarTablaPorClaves(SqlConnection conn, string provClave, string ranClave, string tabClave, int? unidad)
        {
            string query = @"
        SELECT t.NombreTabla, t.Superficie, t.Activo, t.UsuarioCreacion
        FROM Tb_RFID_Tablas t
        INNER JOIN Tb_RFID_Ranchos r ON t.IdRancho = r.IdRancho
        INNER JOIN Tb_RFID_Proveedores p ON r.IdProveedor = p.IdProveedor
        WHERE p.Prov_Clave = @ProvClave AND r.Ran_Clave = @RanClave AND t.Tab_Clave = @TabClave";
            if (unidad.HasValue)
                query += " AND p.IdUnidadNegocio = @Unidad";

            using SqlCommand cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@ProvClave", provClave);
            cmd.Parameters.AddWithValue("@RanClave", ranClave);
            cmd.Parameters.AddWithValue("@TabClave", tabClave);
            if (unidad.HasValue)
                cmd.Parameters.AddWithValue("@Unidad", unidad.Value);

            using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TablaOrigen
                {
                    NombreTabla = reader["NombreTabla"].ToString(),
                    Superficie = reader["Superficie"] != DBNull.Value ? Convert.ToDecimal(reader["Superficie"]) : 0,
                    Activo = Convert.ToBoolean(reader["Activo"]),
                    UsuarioCreacion = reader["UsuarioCreacion"]?.ToString()
                };
            }
            return null;
        }

        private async Task<bool> InsertarTabla(SqlConnection conn, TablaOrigen tabla, string tabClave, int idRanchoDestino)
        {
            string insert = @"
        INSERT INTO Tb_RFID_Tablas 
            (Tab_Clave, NombreTabla, IdRancho, Superficie, Activo, FechaCreacion, UsuarioCreacion)
        VALUES 
            (@TabClave, @NombreTabla, @IdRancho, @Superficie, @Activo, GETDATE(), @UsuarioCreacion)";

            using SqlCommand cmd = new SqlCommand(insert, conn);
            cmd.Parameters.AddWithValue("@TabClave", tabClave);
            cmd.Parameters.AddWithValue("@NombreTabla", tabla.NombreTabla);
            cmd.Parameters.AddWithValue("@IdRancho", idRanchoDestino);
            cmd.Parameters.AddWithValue("@Superficie", tabla.Superficie);
            cmd.Parameters.AddWithValue("@Activo", tabla.Activo);
            cmd.Parameters.AddWithValue("@UsuarioCreacion", tabla.UsuarioCreacion ?? (object)DBNull.Value);

            int filas = await cmd.ExecuteNonQueryAsync();
            return filas > 0;
        }

        private async Task<bool> CrearTablaPorDefecto(SqlConnection conn, string tabClave, int idRanchoDestino)
        {
            string insert = @"
        INSERT INTO Tb_RFID_Tablas 
            (Tab_Clave, NombreTabla, IdRancho, Activo, FechaCreacion, UsuarioCreacion)
        VALUES 
            (@TabClave, @NombreTabla, @IdRancho, 1, GETDATE(), NULL)";

            using SqlCommand cmd = new SqlCommand(insert, conn);
            cmd.Parameters.AddWithValue("@TabClave", tabClave);
            cmd.Parameters.AddWithValue("@NombreTabla", tabClave); // nombre por defecto = clave
            cmd.Parameters.AddWithValue("@IdRancho", idRanchoDestino);

            int filas = await cmd.ExecuteNonQueryAsync();
            return filas > 0;
        }
        #endregion
        private async Task<int?> VerificarProveedorExistenteActivo(string provClave, int idUnidadNegocio)
        {
            try
            {
                using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                await conn.OpenAsync();
                string query = "SELECT IdProveedor FROM Tb_RFID_Proveedores WHERE Prov_Clave = @ProvClave AND IdUnidadNegocio = @IdUnidadNegocio AND Activo = 1";
                using SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@ProvClave", provClave);
                cmd.Parameters.AddWithValue("@IdUnidadNegocio", idUnidadNegocio);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToInt32(result);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"VerificarProveedorExistenteActivo error: {ex.Message}");
                return null;
            }
        }

        private async Task<int?> VerificarRanchoExistenteActivo(int idProveedor, string ranClave)
        {
            try
            {
                using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                await conn.OpenAsync();
                string query = "SELECT IdRancho FROM Tb_RFID_Ranchos WHERE IdProveedor = @IdProveedor AND Ran_Clave = @RanClave AND Activo = 1";
                using SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@IdProveedor", idProveedor);
                cmd.Parameters.AddWithValue("@RanClave", ranClave);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToInt32(result);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"VerificarRanchoExistenteActivo error: {ex.Message}");
                return null;
            }
        }

        private async Task<int?> VerificarTablaExistenteIncluyendoInactivos(int idRancho, string tabClave)
        {
            try
            {
                using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                await conn.OpenAsync();
                string query = "SELECT IdTabla FROM Tb_RFID_Tablas WHERE IdRancho = @IdRancho AND Tab_Clave = @TabClave";
                using SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@IdRancho", idRancho);
                cmd.Parameters.AddWithValue("@TabClave", tabClave);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToInt32(result);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"VerificarTablaExistenteIncluyendoInactivos error: {ex.Message}");
                return null;
            }
        }
        #region CLASES AUXILIARES
        private class ProveedorOrigen
        {
            public string NombreProveedor { get; set; }
            public string RFC { get; set; }
            public string Contacto { get; set; }
            public string Telefono { get; set; }
            public string Email { get; set; }
            public bool Activo { get; set; }
            public string UsuarioCreacion { get; set; }
        }

        private class RanchoOrigen
        {
            public string NombreRancho { get; set; }
            public double? Latitud { get; set; }
            public double? Longitud { get; set; }
            public string Direccion { get; set; }
            public string Municipio { get; set; }
            public string Estado { get; set; }
            public bool Activo { get; set; }
            public string UsuarioCreacion { get; set; }
        }

        private class TablaOrigen
        {
            public string NombreTabla { get; set; }
            public decimal Superficie { get; set; }
            public bool Activo { get; set; }
            public string UsuarioCreacion { get; set; }
        }
        #endregion

    }

}
