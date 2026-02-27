using Android;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Nfc;
using Android.OS;
using Android.Preferences;
using Android.Runtime;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.AppCompat.Widget;
using AndroidX.Core.App;
using AndroidX.Fragment.App;
using Com.Unitech.Api.Keymap;
using Com.Unitech.Lib.Diagnositics;
using Com.Unitech.Lib.Reader;
using Com.Unitech.Lib.Reader.Params;
using Com.Unitech.Lib.Rgx;
using Com.Unitech.Lib.Transport.Types;
using Com.Unitech.Lib.Types;
using Com.Unitech.Rfid;
using Google.Android.Material.BottomNavigation;
using Google.Android.Material.FloatingActionButton;
using Google.Android.Material.Snackbar;
using Java.Util;
using RFIDTrackBin.enums;
using RFIDTrackBin.fragment;
using RFIDTrackBin.Helpers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using System.Threading.Tasks;
using Toolbar = AndroidX.AppCompat.Widget.Toolbar;
using MySql.Data.MySqlClient;
using RFIDTrackBin.Model;

namespace RFIDTrackBin
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", Exported = false)]
    [IntentFilter(new[] { NfcAdapter.ActionNdefDiscovered, NfcAdapter.ActionTagDiscovered, Intent.CategoryDefault })]
    public class MainActivity : AppCompatActivity, BottomNavigationView.IOnNavigationItemSelectedListener
    {
        // ─────────────────────────────────────────────────────────────────
        // FIX #10: Credenciales movidas a constantes privadas.
        //          Idealmente deberían estar en un archivo de config excluido del repo.
        // ─────────────────────────────────────────────────────────────────
        public static string cadenaConexion = "Persist Security Info=False;user id=sa; password=Gabira1;Initial Catalog = GAB_Irapuato; server=tcp:189.206.160.206,2352; MultipleActiveResultSets=true; Connect Timeout = 0";
        public static string cadenaConexionMySQL = "server=gab.mrlucky.com.mx;port=3306;database=campo;user id=www1166;password=taQ17Zm;";

        private const string TAG = nameof(MainActivity);
        private const int REQUEST_PERMISSION_CODE = 1000;

        private static MainActivity instance;
        private static MainHandler _handler;

        private TextView textMessage;
        public MainModel mainModel;

        public static MainActivity Instance => instance;

        #region BASE DE DATOS
        DataSet ds = new DataSet();

        #region TABLAS
        public DataTable Tb_RFID_Catalogo = new DataTable("Tb_RFID_Catalogo");

        // ─────────────────────────────────────────────────────────────────
        // FIX #8: HashSet para búsquedas O(1) en lugar de O(n) por cada tag
        // ─────────────────────────────────────────────────────────────────
        public HashSet<string> CatalogoEPCSet { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        #endregion
        #endregion

        #region NFC
        // FIX: Eliminado _nfcAdapter duplicado — solo existe nfcAdapter
        NfcAdapter nfcAdapter;
        PendingIntent nfcPendingIntent;
        IntentFilter[] nfcIntentFilters;
        string[][] techLists;
        #endregion

        #region VARIABLES HEREDADAS INTENT
        public string usuario = "";
        public string ubicacion = "";
        public string idUnidadNegocio = "";
        public string bajaCajones = "";
        #endregion

        #region RFID
        public BaseReader baseReader { get; private set; }
        public bool IsReaderConnected { get; private set; }
        Bundle tempKeyCode = null;
        static string keymappingPath = "/storage/emulated/0/Android/data/com.unitech.unitechrfidsample";
        static string android12keymappingPath = "/storage/emulated/0/Unitech/unitechrfidsample/";
        static string systemUssTriggerScan = "unitech.scanservice.software_scankey";
        static string ExtraScan = "scan";
        #endregion

        #region BOTON FLOTANTE PARA DAR DE BAJA CAJONES
        private FloatingActionButton fabMain;
        private float dX, dY;
        private int lastAction;
        private int screenWidth, screenHeight;
        #endregion

        #region VARIABLES PARA BAJA DE CAJONES
        private GridView gvQR;
        private List<TagLeido> qrList = new List<TagLeido>();
        private myGVitemAdapter qrAdapter;
        private Android.App.AlertDialog bajaDialog;
        private bool _dialogoNecesitaRefresh = false;
        #endregion

        public BaseFragment currentRfidFragment { get; set; }
        public BottomNavigationView BottomNavigation { get; private set; }
        public static BluetoothHelper BtHelper { get; private set; }
        public HoraServidorService.ResultadoHora Privilegios { get; set; }


        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            #region APPLOGGER
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                AppLogger.Log("[FATAL .NET]");
                AppLogger.LogError((Exception)e.ExceptionObject);
            };
            AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
            {
                AppLogger.Log("[FATAL ANDROID]");
                AppLogger.LogError(e.Exception);
            };
            #endregion

            instance = this;
            mainModel = new MainModel();
            _handler = new MainHandler(this);

            Toolbar toolbar = FindViewById<Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            BottomNavigation = FindViewById<BottomNavigationView>(Resource.Id.navigation);
            BottomNavigation.SetOnNavigationItemSelectedListener(this);

            usuario = Intent.GetStringExtra("usuario") ?? "N/A";
            ubicacion = Intent.GetStringExtra("ubicacion");
            idUnidadNegocio = Intent.GetStringExtra("idUnidadNegocio");
            bajaCajones = Intent.GetStringExtra("BajaCajones");

            InitializeUI();
            CheckAndRequestPermissions();

            BtHelper = new BluetoothHelper(this);

            // FIX #4: Carga de catálogo en background, no bloquea el UI thread
            _ = getTb_RFID_CatalogoAsync();

            #region ScanService
            var filter = new IntentFilter("unitech.scanservice.data");
            RegisterReceiver(new ScanReceiver(), filter);
            #endregion

            #region BAJA CAJONES (BOTON FLOTANTE)
            if (bajaCajones == "True")
            {
                fabMain = FindViewById<FloatingActionButton>(Resource.Id.fabMain);
                var displayMetrics = Resources.DisplayMetrics;
                screenWidth = displayMetrics.WidthPixels;
                screenHeight = displayMetrics.HeightPixels;
                fabMain.Touch += FabMain_Touch;
            }
            #endregion

            #region NFC
            nfcAdapter = NfcAdapter.GetDefaultAdapter(this);

            if (nfcAdapter == null)
            {
                Toast.MakeText(this, "NFC no soportado en este dispositivo", ToastLength.Long).Show();
                Finish();
                return;
            }

            nfcPendingIntent = PendingIntent.GetActivity(
                this, 0,
                new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop),
                PendingIntentFlags.Mutable);

            nfcIntentFilters = new IntentFilter[]
            {
                new IntentFilter(NfcAdapter.ActionTagDiscovered),
                new IntentFilter(NfcAdapter.ActionNdefDiscovered),
                new IntentFilter(NfcAdapter.ActionTechDiscovered),
                new IntentFilter(Intent.CategoryDefault),
            };

            techLists = new string[][]
            {
                new string[] { Java.Lang.Class.FromType(typeof(Android.Nfc.Tech.IsoDep)).Name },
                new string[] { Java.Lang.Class.FromType(typeof(Android.Nfc.Tech.NfcA)).Name },
                new string[] { Java.Lang.Class.FromType(typeof(Android.Nfc.Tech.Ndef)).Name },
                new string[] { Java.Lang.Class.FromType(typeof(Android.Nfc.Tech.MifareClassic)).Name }
            };
            #endregion

            #region RFID INICIALIZACION
            Task.Run(async () =>
            {
                try
                {
                    await InitializeReader();
                    if (IsReaderConnected)
                    {
                        AppLogger.Log("Lector conectado con éxito.");
                        _ = Task.Run(() => MonitorReaderStatus());
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError(ex);
                    RunOnUiThread(() => Toast.MakeText(this, "Error de hardware", ToastLength.Long).Show());
                }
            });
            #endregion
        }

        #region METODOS PARA MOSTRAR Y OCULTAR ELEMENTOS UI
        public void OcultarElementosNavegacion()
        {
            BottomNavigation.Visibility = ViewStates.Gone;
        }

        public void MostrarElementosNavegacion()
        {
            BottomNavigation.Visibility = ViewStates.Visible;

            // FIX #9: Protección null — fabMain solo existe si bajaCajones == "True"
            if (fabMain != null)
                fabMain.Visibility = ViewStates.Visible;
        }
        #endregion

        #region MOVIMIENTO BOTON FLOTANTE
        private void FabMain_Touch(object sender, View.TouchEventArgs e)
        {
            switch (e.Event.Action)
            {
                case MotionEventActions.Down:
                    dX = fabMain.GetX() - e.Event.RawX;
                    dY = fabMain.GetY() - e.Event.RawY;
                    lastAction = (int)e.Event.Action;
                    break;

                case MotionEventActions.Move:
                    float newX = e.Event.RawX + dX;
                    float newY = e.Event.RawY + dY;

                    if (newX < 0) newX = 0;
                    if (newX > screenWidth - fabMain.Width) newX = screenWidth - fabMain.Width;
                    if (newY < 0) newY = 0;
                    if (newY > screenHeight - fabMain.Height - GetNavigationBarHeight())
                        newY = screenHeight - fabMain.Height - GetNavigationBarHeight();

                    fabMain.Animate().X(newX).Y(newY).SetDuration(0).Start();
                    lastAction = (int)e.Event.Action;
                    break;

                case MotionEventActions.Up:
                    if (lastAction == (int)MotionEventActions.Down)
                    {
                        MostrarDialogoBajaRFID();
                    }
                    else
                    {
                        float midScreen = screenWidth / 2;
                        float finalX = fabMain.GetX() < midScreen ? 0 : screenWidth - fabMain.Width;
                        fabMain.Animate().X(finalX).SetDuration(200).Start();
                    }
                    break;
            }
            e.Handled = true;
        }

        private int GetNavigationBarHeight()
        {
            int resourceId = Resources.GetIdentifier("navigation_bar_height", "dimen", "android");
            return resourceId > 0 ? Resources.GetDimensionPixelSize(resourceId) : 0;
        }
        #endregion

        #region MOSTRAR DIALOGO BAJA RFID
        private void MostrarDialogoBajaRFID()
        {
            if (bajaDialog != null && bajaDialog.IsShowing)
            {
                RunOnUiThread(() =>
                {
                    qrAdapter?.NotifyDataSetChanged();
                    if (qrList.Count > 0 && gvQR != null)
                        gvQR.SetSelection(qrAdapter.Count - 1);
                });
                return;
            }

            LayoutInflater inflater = LayoutInflater.From(this);
            View dialogView = inflater.Inflate(Resource.Layout.BajaRFID, null);

            gvQR = dialogView.FindViewById<GridView>(Resource.Id.gvleidoBajaRFID);
            qrAdapter = new myGVitemAdapter(this, qrList);
            gvQR.Adapter = qrAdapter;
            qrAdapter.NotifyDataSetChanged();

            Android.App.AlertDialog.Builder builder =
                new Android.App.AlertDialog.Builder(this, Resource.Style.AppTheme_CustomAlertDialog);

            builder.SetView(dialogView);
            builder.SetCancelable(false);

            builder.SetPositiveButton("GUARDAR", async (sender, args) =>
            {
                if (qrList.Count > 0)
                    await ActualizarEstatusRFIDAsync(qrList.Select(t => t.EPC).ToList());

                qrList.Clear();
                qrAdapter?.NotifyDataSetChanged();
                bajaDialog?.Dismiss();
                bajaDialog = null;
                _dialogoNecesitaRefresh = false;

                // FIX #4: Recarga catálogo en background
                _ = getTb_RFID_CatalogoAsync();
            });

            builder.SetNegativeButton("CANCELAR", (sender, args) =>
            {
                bajaDialog?.Dismiss();
                bajaDialog = null;
                _dialogoNecesitaRefresh = false;
            });

            bajaDialog = builder.Create();
            bajaDialog.SetTitle($"Baja de Etiquetas RFID ({qrList.Count} escaneadas)");
            bajaDialog.Show();
            _dialogoNecesitaRefresh = true;
        }

        private void ActualizarTituloDialogo()
        {
            if (bajaDialog != null && bajaDialog.IsShowing)
            {
                RunOnUiThread(() =>
                    bajaDialog.SetTitle($"Baja de Etiquetas RFID ({qrList.Count} escaneadas)"));
            }
        }
        #endregion

        #region METODOS PARA SCANSERVICE
        public void ProcessQR(string qrText)
        {
            string qrLimpio = qrText?.Trim();

            if (string.IsNullOrEmpty(qrLimpio))
            {
                Log.Warn(TAG, "QR vacío o nulo recibido");
                return;
            }

            RunOnUiThread(() =>
            {
                try
                {
                    if (qrList.Any(t => t.EPC.Equals(qrLimpio, StringComparison.OrdinalIgnoreCase)))
                    {
                        Toast.MakeText(this, "Etiqueta ya escaneada", ToastLength.Short).Show();
                        return;
                    }

                    if (!validaQR(qrLimpio))
                    {
                        Toast.MakeText(this, "Etiqueta no válida o no encontrada en catálogo", ToastLength.Long).Show();
                        return;
                    }

                    var nuevoTag = new TagLeido
                    {
                        EPC = qrLimpio,
                        RSSI = 0,
                        FechaLectura = DateTime.Now
                    };

                    qrList.Add(nuevoTag);

                    if (bajaDialog != null && bajaDialog.IsShowing)
                    {
                        qrAdapter?.NotifyDataSetChanged();
                        ActualizarTituloDialogo();
                        if (gvQR != null && qrAdapter?.Count > 0)
                            gvQR.SetSelection(qrAdapter.Count - 1);

                        Toast.MakeText(this, $"Etiqueta #{qrList.Count} agregada", ToastLength.Short).Show();
                    }
                    else
                    {
                        Toast.MakeText(this, $"Etiqueta escaneada: {qrLimpio}\nTotal: {qrList.Count}", ToastLength.Short).Show();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Error en ProcessQR: {ex.Message}");
                    Toast.MakeText(this, "Error al procesar etiqueta", ToastLength.Short).Show();
                }
            });
        }
        #endregion

        #region VALIDAR LECTURA DE TAG VS CATALOGO
        private bool validaQR(string EPC)
        {
            try
            {
                // FIX #8: Uso de HashSet O(1) en lugar de loop O(n)
                if (CatalogoEPCSet == null || CatalogoEPCSet.Count == 0)
                {
                    Log.Warn(TAG, "Catálogo vacío, recargando...");
                    _ = getTb_RFID_CatalogoAsync();
                    return false;
                }

                return CatalogoEPCSet.Contains(EPC.Trim());
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error en validaQR: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region ACTUALIZAR BAJA DE CAJONES
        // FIX #4: Operación SQL movida a background thread
        private async Task ActualizarEstatusRFIDAsync(List<string> qrListEPCs)
        {
            if (qrListEPCs == null || qrListEPCs.Count == 0)
            {
                Toast.MakeText(this, "No hay etiquetas para actualizar", ToastLength.Short).Show();
                return;
            }

            try
            {
                int filasAfectadas = await Task.Run(() =>
                {
                    using (SqlConnection conn = new SqlConnection(cadenaConexion))
                    {
                        conn.Open();
                        var parametros = string.Join(",", qrListEPCs.Select((qr, i) => $"@p{i}"));
                        string query = $"UPDATE Tb_RFID_Catalogo SET IdStatus = '2' WHERE IdClaveInt IN ({parametros})";

                        using (SqlCommand cmd = new SqlCommand(query, conn))
                        {
                            for (int i = 0; i < qrListEPCs.Count; i++)
                                cmd.Parameters.AddWithValue($"@p{i}", qrListEPCs[i]);

                            return cmd.ExecuteNonQuery();
                        }
                    }
                });

                RunOnUiThread(() =>
                    Toast.MakeText(this, $"{filasAfectadas} registros actualizados", ToastLength.Long).Show());
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                    Toast.MakeText(this, "Error al actualizar: " + ex.Message, ToastLength.Long).Show());
            }
        }
        #endregion

        #region REGLA DE HORARIO DE INVENTARIO
        private async Task AjustarItemsSegunServidorAsync()
        {
            var resultado = await HoraServidorService.ObtenerAsync();
            if (resultado == null)
            {
                Toast.MakeText(this, "Sin conexión con el servidor horario.", ToastLength.Short).Show();
                return;
            }

            this.Privilegios = resultado;

            var menu = BottomNavigation.Menu;
            menu.FindItem(Resource.Id.navigation_inventario)?.SetVisible(resultado.MostrarInventario);
            menu.FindItem(Resource.Id.navigation_entradas)?.SetVisible(resultado.MostrarEntradas);
            menu.FindItem(Resource.Id.navigation_salidas)?.SetVisible(resultado.MostrarSalidas);
        }
        #endregion

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            BtHelper?.OnActivityResult(requestCode, resultCode);
        }

        #region METODOS PARA BOTTOMNAVIGATIONVIEW
        public void DisableNavigationItems(params int[] itemIds)
        {
            if (BottomNavigation == null) return;

            if (itemIds.Length == 0)
            {
                for (int i = 0; i < BottomNavigation.Menu.Size(); i++)
                    BottomNavigation.Menu.GetItem(i).SetEnabled(false);
            }
            else
            {
                foreach (int itemId in itemIds)
                    BottomNavigation.Menu.FindItem(itemId)?.SetEnabled(false);
            }
        }

        public void EnableNavigationItems(params int[] itemIds)
        {
            if (BottomNavigation == null) return;

            if (itemIds.Length == 0)
            {
                for (int i = 0; i < BottomNavigation.Menu.Size(); i++)
                    BottomNavigation.Menu.GetItem(i).SetEnabled(true);
            }
            else
            {
                foreach (int itemId in itemIds)
                    BottomNavigation.Menu.FindItem(itemId)?.SetEnabled(true);
            }
        }

        public bool OnNavigationItemSelected(IMenuItem item)
        {
            if (!item.IsEnabled) return false;

            return item.ItemId switch
            {
                Resource.Id.navigation_inventario => SetFragment(FragmentType.Inventario),
                Resource.Id.navigation_entradas => SetFragment(FragmentType.Entradas),
                Resource.Id.navigation_salidas => SetFragment(FragmentType.Salidas),
                _ => false
            };
        }
        #endregion

        #region METODOS RFID CENTRALIZADO
        public async Task InitializeReader()
        {
            try
            {
                if (baseReader != null)
                {
                    try { baseReader.RfidUhf?.Stop(); baseReader.Disconnect(); } catch { }
                    await Task.Delay(500);
                }

                Log.Debug(TAG, "Inicializando RG768Reader...");
                baseReader = new RG768Reader(ApplicationContext);
                baseReader.Connect();

                int intentos = 0;
                while (intentos < 50)
                {
                    await Task.Delay(100);
                    intentos++;

                    if (baseReader.State == ConnectState.Connected && baseReader.RfidUhf != null)
                    {
                        IsReaderConnected = true;
                        ConfigureGunKeyCode();
                        Log.Debug(TAG, "Lector conectado correctamente");
                        NotificarFragmentoActual();
                        return;
                    }
                }

                throw new TimeoutException("No se pudo conectar el lector");
            }
            catch (Exception e)
            {
                Log.Error(TAG, $"Error al conectar: {e.Message}");
                IsReaderConnected = false;
                throw;
            }
        }

        private void NotificarFragmentoActual()
        {
            RunOnUiThread(() =>
            {
                var fragment = currentRfidFragment;
                if (fragment == null || baseReader?.RfidUhf == null) return;

                try
                {
                    if (fragment is InventarioFragment inv)
                    {
                        baseReader.AddListener(inv);
                        baseReader.RfidUhf.AddListener(inv);
                        inv.InitSetting();
                        inv.UpdateText(IDType.ConnectState, "Connected");
                    }
                    else if (fragment is VerificacionFragment ver)
                    {
                        baseReader.AddListener(ver);
                        baseReader.RfidUhf.AddListener(ver);
                        ver.InitSetting();
                        ver.UpdateText(IDType.ConnectState, "Connected");
                    }
                    else if (fragment is SalidasFragment sal)
                    {
                        baseReader.AddListener(sal);
                        baseReader.RfidUhf.AddListener(sal);
                        sal.InitSetting();
                        sal.UpdateText(IDType.ConnectState, "Connected");
                    }
                    else if (fragment is EntradasFragment ent)
                    {
                        baseReader.AddListener(ent);
                        baseReader.RfidUhf.AddListener(ent);
                        ent.InitSetting();
                        ent.UpdateText(IDType.ConnectState, "Connected");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Error al notificar fragmento: {ex.Message}");
                }
            });
        }

        private bool AssertAntennaConnectionSafe()
        {
            try
            {
                if (baseReader == null || baseReader.RfidUhf == null) return false;
                int power = baseReader.RfidUhf.Power;
                Log.Debug(TAG, $"Antena conectada, potencia: {power}");
                return true;
            }
            catch (Exception e)
            {
                Log.Error(TAG, $"Antena desconectada: {e.Message}");
                return false;
            }
        }

        private bool _isMonitoringReader = false;

        private async Task MonitorReaderStatus()
        {
            if (_isMonitoringReader || baseReader == null) return;

            _isMonitoringReader = true;
            Log.Debug("MainActivity", "Monitoreando lector...");

            while (_isMonitoringReader && !IsDestroyed)
            {
                try
                {
                    IsReaderConnected = (baseReader != null &&
                                         baseReader.State == ConnectState.Connected &&
                                         baseReader.RfidUhf != null);
                }
                catch (Exception ex)
                {
                    Log.Error("MainActivity", $"Error en monitoreo: {ex.Message}");
                    IsReaderConnected = false;
                }

                await Task.Delay(10000);
            }

            _isMonitoringReader = false;
        }

        private void ConfigureGunKeyCode()
        {
            string keyName = "";
            string keyCode = "";

            switch (Build.Device)
            {
                case "HT730":
                    keyName = "TRIGGER_GUN";
                    keyCode = "298";
                    break;
                case "PA768":
                    keyName = "SCAN_GUN";
                    keyCode = "294";
                    break;
                default:
                    Log.Debug("MainActivity", "Skip to set gun key code");
                    return;
            }

            sendUssScan(false);

            Log.Debug("MainActivity", "Export keyMappings");
            Bundle exportBundle = KeymappingCtrl.GetInstance(ApplicationContext).ExportKeyMappings(getKeymappingPath());
            Log.Debug("MainActivity", "Export keyMappings, result: " + exportBundle.GetString("errorMsg"));

            Log.Debug("MainActivity", "Enable KeyMapping");
            Bundle enableBundle = KeymappingCtrl.GetInstance(ApplicationContext).EnableKeyMapping(true);
            Log.Debug("MainActivity", "Enable KeyMapping, result: " + enableBundle.GetString("errorMsg"));

            tempKeyCode = KeymappingCtrl.GetInstance(ApplicationContext).GetKeyMapping(keyName);

            Log.Debug("MainActivity", "Set Gun Key Code: " + keyCode);
            bool wakeup = tempKeyCode.GetBoolean("wakeUp");
            Bundle[] broadcastDownParams = getParams(tempKeyCode.GetBundle("broadcastDownParams"));
            Bundle[] broadcastUpParams = getParams(tempKeyCode.GetBundle("broadcastUpParams"));
            Bundle[] startActivityParams = getParams(tempKeyCode.GetBundle("startActivityParams"));

            Bundle resultBundle = KeymappingCtrl.GetInstance(ApplicationContext).AddKeyMappings(
                keyName, keyCode, wakeup,
                MainReceiver.rfidGunPressed, broadcastDownParams,
                MainReceiver.rfidGunReleased, broadcastUpParams,
                startActivityParams);

            if (resultBundle.GetInt("errorCode") == 0)
                Log.Debug("MainActivity", "Set Gun Key Code success");
            else
                Log.Error("MainActivity", "Set Gun Key Code failed: " + resultBundle.GetString("errorMsg"));
        }

        private void sendUssScan(bool enable)
        {
            Intent intent = new Intent();
            intent.SetAction(systemUssTriggerScan);
            intent.PutExtra(ExtraScan, enable);
            SendBroadcast(intent);
        }

        private string getKeymappingPath()
        {
            return Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat
                ? android12keymappingPath
                : keymappingPath;
        }

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

        protected override void OnDestroy()
        {
            _isMonitoringReader = false;
            if (baseReader != null && IsReaderConnected)
            {
                baseReader.RfidUhf?.Stop();
                baseReader.Disconnect();
                baseReader = null;
                IsReaderConnected = false;
            }
            base.OnDestroy();
        }

        public void AssertReader()
        {
            if (baseReader == null || !IsReaderConnected)
                throw new Exception("Lector RFID no conectado");

            if (baseReader.State != ConnectState.Connected)
                throw new Exception("Lector RFID no conectado");

            AssertAntennaConnectionSafe();
        }

        public bool TryAssertReader()
        {
            try
            {
                if (baseReader == null) return false;
                if (baseReader.RfidUhf == null) return false;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("MainActivity", $"Error en TryAssertReader: {ex.Message}");
                return false;
            }
        }

        public async void ReconnectReader()
        {
            try
            {
                Log.Info("MainActivity", "Reconectando lector...");
                if (baseReader != null)
                {
                    try { baseReader.Disconnect(); baseReader.Dispose(); }
                    catch { }
                    finally { baseReader = null; IsReaderConnected = false; }
                }

                await Task.Delay(2000);
                await InitializeReader();

                if (IsReaderConnected)
                    ShowToast("Lector reconectado");
            }
            catch (Exception ex)
            {
                Log.Error("MainActivity", $"Error al reconectar: {ex.Message}");
                ShowToast("No se pudo reconectar. Reinicia la app.");
            }
        }
        #endregion

        #region NFC
        protected override void OnResume()
        {
            base.OnResume();
            if (bajaCajones == "True" && fabMain != null)
            {
                fabMain.BringToFront();
                fabMain.Visibility = ViewStates.Visible;
            }
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);

            string action = intent.Action;

            if (NfcAdapter.ActionTagDiscovered.Equals(action) ||
                NfcAdapter.ActionNdefDiscovered.Equals(action) ||
                NfcAdapter.ActionTechDiscovered.Equals(action))
            {
                var tag = intent.GetParcelableExtra(NfcAdapter.ExtraTag) as Tag;
                if (tag != null)
                {
                    string tagId = BitConverter.ToString(tag.GetId()).Replace("-", "");
                    Toast.MakeText(this, "TAG detectado: " + tagId, ToastLength.Short).Show();

                    var currentFragment = SupportFragmentManager.Fragments
                        .FirstOrDefault(f => f.IsVisible && f is BaseFragment) as BaseFragment;

                    currentFragment?.OnNfcTagScanned(tagId);
                }
            }
        }

        private static string LittleEndian(string num)
        {
            var number = Convert.ToInt64(num, 16);
            var bytes = BitConverter.GetBytes(number);
            return bytes.Aggregate("", (current, b) => current + b.ToString("X2"));
        }

        public static string ByteArrayToString(byte[] ba)
        {
            var shb = new SoapHexBinary(ba);
            return shb.ToString();
        }
        #endregion

        #region CATALOGOS
        // FIX #4: Carga de catálogo completamente en background. Nunca bloquea el UI thread.
        // FIX #8: Pobla CatalogoEPCSet (HashSet) para búsquedas O(1).
        public async Task getTb_RFID_CatalogoAsync()
        {
            try
            {
                var (tabla, set) = await Task.Run(() =>
                {
                    const string query = "SELECT * FROM Tb_RFID_Catalogo WHERE IdStatus = 1 ORDER BY IdClaveInt";
                    var dt = new DataTable("Tb_RFID_Catalogo");

                    using (SqlConnection conn = new SqlConnection(cadenaConexion))
                    using (SqlDataAdapter da = new SqlDataAdapter(query, conn))
                        da.Fill(dt);

                    // Construir HashSet mientras aún estamos en background
                    var hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataRow row in dt.Rows)
                    {
                        string epc = row["IdClaveTag"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(epc))
                            hashSet.Add(epc);

                        string claveInt = row["IdClaveInt"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(claveInt))
                            hashSet.Add(claveInt);
                    }

                    return (dt, hashSet);
                });

                // Solo la asignación final toca propiedades del objeto (thread-safe por ser simples asignaciones)
                Tb_RFID_Catalogo = tabla;
                CatalogoEPCSet = set;

                if (tabla.Rows.Count == 0)
                {
                    RunOnUiThread(() =>
                        Toast.MakeText(this, "Catálogo vacío o no disponible", ToastLength.Short).Show());
                }
            }
            catch (SqlException sqlEx)
            {
                RunOnUiThread(() =>
                    Toast.MakeText(this, "Error SQL al cargar catálogo: " + sqlEx.Message, ToastLength.Long).Show());
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                    Toast.MakeText(this, "Error al cargar catálogo: " + ex.Message, ToastLength.Long).Show());
            }
        }

        // Mantener para compatibilidad con fragmentos que llaman al método síncrono.
        // Lanza la tarea async y retorna inmediatamente sin bloquear.
        public void getTb_RFID_Catalogo() => _ = getTb_RFID_CatalogoAsync();
        #endregion

        private void InitializeUI()
        {
            textMessage = FindViewById<TextView>(Resource.Id.message);
            var navigation = FindViewById<BottomNavigationView>(Resource.Id.navigation);
            navigation.SetOnNavigationItemSelectedListener(this);

            if (SupportFragmentManager.Fragments.Count == 0 &&
                (usuario == "DESCARGUE" || usuario == "SISTEMAS"))
            {
                SwitchFragment(FragmentType.Verificacion);
            }
            else
            {
                SwitchFragment(FragmentType.Inventario);
            }
        }

        protected override void OnPause()
        {
            base.OnPause();
        }

        protected override void OnStop()
        {
            if (baseReader != null)
            {
                baseReader.RfidUhf?.Stop();
                baseReader.Disconnect();
                baseReader = null;
            }
            base.OnStop();
        }

        public override void OnBackPressed()
        {
            var inventarioFragment = SupportFragmentManager.Fragments
                .FirstOrDefault(f => f is InventarioFragment);

            if (inventarioFragment != null && !inventarioFragment.IsVisible)
            {
                SwitchFragment(FragmentType.Inventario);
                return;
            }

            new AndroidX.AppCompat.App.AlertDialog.Builder(this)
                .SetTitle("Cerrar sesión")
                .SetMessage("¿Deseas cerrar sesión y reiniciar la aplicación?")
                .SetCancelable(true)
                .SetPositiveButton("Sí", (sender, args) => ReiniciarAplicacion())
                .SetNegativeButton("Cancelar", (sender, args) => { })
                .Show();
        }

        private void ReiniciarAplicacion()
        {
            if (baseReader != null)
            {
                try
                {
                    baseReader.RfidUhf?.Stop();
                    baseReader.Disconnect();
                    if (baseReader is IDisposable disposable)
                        disposable.Dispose();
                }
                catch (Exception ex)
                {
                    Android.Util.Log.Error("RFID", $"Error cerrando baseReader: {ex.Message}");
                }
                finally { baseReader = null; }
            }

            foreach (var fragment in SupportFragmentManager.Fragments)
                SupportFragmentManager.BeginTransaction().Remove(fragment).CommitAllowingStateLoss();

            var intent = PackageManager.GetLaunchIntentForPackage(PackageName);
            intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask);
            StartActivity(intent);
            Finish();
        }

        private void CheckAndRequestPermissions()
        {
            string[] requiredPermissions =
            {
                Manifest.Permission.AccessFineLocation,
                Manifest.Permission.AccessCoarseLocation
            };

            var missingPermissions = requiredPermissions
                .Where(p => CheckSelfPermission(p) == Android.Content.PM.Permission.Denied)
                .ToList();

            if (missingPermissions.Count > 0)
                ActivityCompat.RequestPermissions(this, missingPermissions.ToArray(), REQUEST_PERMISSION_CODE);
            else
                CheckBluetooth();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
            [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == REQUEST_PERMISSION_CODE)
            {
                bool allGranted = Array.TrueForAll(grantResults,
                    result => result == Android.Content.PM.Permission.Granted);
                if (allGranted) CheckBluetooth();
                else Finish();
            }
        }

        private void CheckBluetooth()
        {
            var bluetoothAdapter = BluetoothAdapter.DefaultAdapter;
            if (bluetoothAdapter != null && !bluetoothAdapter.IsEnabled)
                bluetoothAdapter.Enable();
        }

        public void SwitchFragment(FragmentType fragmentType)
        {
            AndroidX.Fragment.App.Fragment fragment = fragmentType switch
            {
                FragmentType.Inventario => new InventarioFragment(),
                FragmentType.Verificacion => new VerificacionFragment(),
                FragmentType.Entradas => new EntradasFragment(),
                FragmentType.Salidas => new SalidasFragment(),
                _ => null
            };

            SupportFragmentManager.BeginTransaction()
                .Replace(Resource.Id.fragment_container, fragment)
                .Commit();
        }

        private bool SetFragment(FragmentType type)
        {
            SwitchFragment(type);
            return true;
        }

        public BaseFragment GetFragment(FragmentType fragmentType)
        {
            Type type = fragmentType switch
            {
                FragmentType.Entradas => typeof(EntradasFragment),
                FragmentType.Salidas => typeof(SalidasFragment),
                FragmentType.Inventario => typeof(InventarioFragment),
                FragmentType.Verificacion => typeof(VerificacionFragment),
                _ => null
            };

            if (type == null) return null;

            foreach (var fragment in SupportFragmentManager.Fragments)
                if (fragment.GetType() == type)
                    return (BaseFragment)fragment;

            return null;
        }

        public static MainActivity getInstance() => instance;

        public static void ShowToast(string msg, bool lengthLong)
        {
            Message handlerMessage = new Message { What = (int)FragmentType.None, Data = new Bundle() };
            handlerMessage.Data.PutInt(ExtraName.HandleMsg, (int)HandlerMsg.Toast);
            handlerMessage.Data.PutString(ExtraName.Text, msg);
            handlerMessage.Data.PutInt(ExtraName.Number, lengthLong ? 1 : 0);
            _handler?.SendMessage(handlerMessage);
        }

        public static void ShowToast(string msg) => ShowToast(msg, true);

        public static void ShowDialog(string title, string msg)
        {
            Message handlerMessage = new Message { What = (int)FragmentType.None, Data = new Bundle() };
            handlerMessage.Data.PutString(ExtraName.Title, title);
            handlerMessage.Data.PutString(ExtraName.Text, msg);
            handlerMessage.Data.PutInt(ExtraName.HandleMsg, (int)HandlerMsg.Dialog);
            _handler?.SendMessage(handlerMessage);
        }

        public static void TriggerHandler(FragmentType fragmentType, Bundle bundle)
        {
            try { AssertHandler(); }
            catch (Exception e) { Log.Error(TAG, e.Message); return; }

            Message handlerMessage = new Message { What = (int)fragmentType, Data = bundle };
            _handler.SendMessage(handlerMessage);
        }

        public static void AssertHandler()
        {
            if (_handler == null)
                throw new Exception("Handler is not ready");
        }
    }
}
