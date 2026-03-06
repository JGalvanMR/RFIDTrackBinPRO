using Android;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Nfc;
using Android.OS;
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
using Com.Unitech.Lib.Reader;
using Com.Unitech.Lib.Reader.Params;
using Com.Unitech.Lib.Rgx;
using Com.Unitech.Lib.Transport.Types;
using Com.Unitech.Lib.Types;
using Com.Unitech.Rfid;
using Google.Android.Material.BottomNavigation;
using Google.Android.Material.FloatingActionButton;
using Java.Util;
using RFIDTrackBin.enums;
using RFIDTrackBin.fragment;
using RFIDTrackBin.Helpers;
using RFIDTrackBin.Model;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using System.Threading.Tasks;
using Toolbar = AndroidX.AppCompat.Widget.Toolbar;

namespace RFIDTrackBin
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", Exported = false)]
    [IntentFilter(new[] {
        NfcAdapter.ActionNdefDiscovered,
        NfcAdapter.ActionTagDiscovered,
        Intent.CategoryDefault })]
    public class MainActivity : AppCompatActivity,
        BottomNavigationView.IOnNavigationItemSelectedListener
    {
        public static string cadenaConexion =
            "Persist Security Info=False;user id=sa; password=Gabira1;" +
            "Initial Catalog = GAB_Irapuato; server=tcp:189.206.160.206,2352;" +
            " MultipleActiveResultSets=true; Connect Timeout = 0";

        private const string TAG = nameof(MainActivity);
        private const int REQUEST_PERMISSION_CODE = 1000;

        private static MainActivity instance;
        private static MainHandler _handler;

        private TextView textMessage;
        public MainModel mainModel;

        public static MainActivity Instance => instance;

        #region BASE DE DATOS
        // FIX M-3: volatile garantiza visibilidad entre hilos sin lock para la asignación
        // de referencia. Task.Run escribe, UI thread lee — sin volatile puede haber
        // lecturas de referencias obsoletas o parcialmente construidas.
        private volatile DataTable _tb_RFID_Catalogo = new DataTable("Tb_RFID_Catalogo");
        public DataTable Tb_RFID_Catalogo
        {
            get => _tb_RFID_Catalogo;
            private set => _tb_RFID_Catalogo = value;
        }

        private volatile HashSet<string> _catalogoEPCSet =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CatalogoEPCSet => _catalogoEPCSet;
        #endregion

        #region NFC
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

        #region BOTON FLOTANTE BAJA CAJONES
        private FloatingActionButton fabMain;
        private float dX, dY;
        private int lastAction;
        private int screenWidth, screenHeight;
        #endregion

        #region VARIABLES BAJA CAJONES
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

        private bool _isMonitoringReader = false;
        private bool _isInitializingReader = false;

        public static bool UseManualScan { get; set; } = false;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            #region GLOBAL EXCEPTION HANDLERS
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
            // FIX M-2: Listener registrado UNA sola vez aquí.
            // La versión anterior también lo registraba en InitializeUI(), causando
            // un segundo SetOnNavigationItemSelectedListener sobre la misma vista.
            BottomNavigation.SetOnNavigationItemSelectedListener(this);

            usuario = Intent.GetStringExtra("usuario") ?? "N/A";
            ubicacion = Intent.GetStringExtra("ubicacion");
            idUnidadNegocio = Intent.GetStringExtra("idUnidadNegocio");
            bajaCajones = Intent.GetStringExtra("BajaCajones");

            InitializeUI();
            CheckAndRequestPermissions();

            BtHelper = new BluetoothHelper(this);

            _ = getTb_RFID_CatalogoAsync();

            #region BAJA CAJONES (FAB)
            if (bajaCajones == "True")
            {
                fabMain = FindViewById<FloatingActionButton>(Resource.Id.fabMain);
                var dm = Resources.DisplayMetrics;
                screenWidth = dm.WidthPixels;
                screenHeight = dm.HeightPixels;
                fabMain.Touch += FabMain_Touch;
            }
            #endregion

            #region NFC SETUP
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

            // FIX M-4: Eliminado new IntentFilter(Intent.CategoryDefault).
            // Intent.CategoryDefault es una categoría, no una acción. Usarlo como
            // argumento de IntentFilter(string action) es semánticamente incorrecto
            // aunque no provoca crash.
            nfcIntentFilters = new IntentFilter[]
            {
                new IntentFilter(NfcAdapter.ActionTagDiscovered),
                new IntentFilter(NfcAdapter.ActionNdefDiscovered),
                new IntentFilter(NfcAdapter.ActionTechDiscovered),
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
                    RunOnUiThread(() =>
                        Toast.MakeText(this, "Error de hardware", ToastLength.Long).Show());
                }
            });
            #endregion
            UseManualScan = false; // Activamos el botón manual
        }

        #region INICIALIZACION UI
        // FIX M-2: Eliminada la segunda llamada a SetOnNavigationItemSelectedListener
        // que existía en esta función. Ahora solo cambia el fragmento inicial.
        private void InitializeUI()
        {
            textMessage = FindViewById<TextView>(Resource.Id.message);

            if (SupportFragmentManager.Fragments.Count == 0 &&
                (usuario == "DESCARGUE" || usuario == "SISTEMAS"))
                SwitchFragment(FragmentType.Verificacion);
            else
                SwitchFragment(FragmentType.Inventario);
        }
        #endregion

        #region NAVEGACION
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

            if (fragment == null) return;

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
        #endregion

        #region RFID READER
        public async Task InitializeReader()
        {
            if (_isInitializingReader) return;
            _isInitializingReader = true;

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

                    if (baseReader.State == ConnectState.Connected &&
                        baseReader.RfidUhf != null)
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
            finally
            {
                _isInitializingReader = false;
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
                        // FIX I-2 / V-2: RemoveListener antes de AddListener para evitar
                        // acumulación de referencias que causa procesamiento duplicado de tags.
                        baseReader.RemoveListener(inv);
                        baseReader.RfidUhf.RemoveListener(inv);
                        baseReader.AddListener(inv);
                        baseReader.RfidUhf.AddListener(inv);
                        inv.InitSetting();
                        inv.UpdateText(IDType.ConnectState, "Connected");
                    }
                    else if (fragment is VerificacionFragment ver)
                    {
                        baseReader.RemoveListener(ver);
                        baseReader.RfidUhf.RemoveListener(ver);
                        baseReader.AddListener(ver);
                        baseReader.RfidUhf.AddListener(ver);
                        ver.InitSetting();
                        ver.UpdateText(IDType.ConnectState, "Connected");
                    }
                    else if (fragment is SalidasFragment sal)
                    {
                        baseReader.RemoveListener(sal);
                        baseReader.RfidUhf.RemoveListener(sal);
                        baseReader.AddListener(sal);
                        baseReader.RfidUhf.AddListener(sal);
                        sal.InitSetting();
                        sal.UpdateText(IDType.ConnectState, "Connected");
                    }
                    else if (fragment is EntradasFragment ent)
                    {
                        baseReader.RemoveListener(ent);
                        baseReader.RfidUhf.RemoveListener(ent);
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

        private async Task MonitorReaderStatus()
        {
            if (_isMonitoringReader || baseReader == null) return;
            _isMonitoringReader = true;

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
            string keyName = "", keyCode = "";

            switch (Build.Device)
            {
                case "HT730": keyName = "TRIGGER_GUN"; keyCode = "298"; break;
                case "PA768": keyName = "SCAN_GUN"; keyCode = "294"; break;
                default:
                    Log.Debug("MainActivity", "Skip to set gun key code");
                    return;
            }

            sendUssScan(false);

            var ctx = ApplicationContext;
            KeymappingCtrl.GetInstance(ctx).ExportKeyMappings(getKeymappingPath());
            KeymappingCtrl.GetInstance(ctx).EnableKeyMapping(true);
            tempKeyCode = KeymappingCtrl.GetInstance(ctx).GetKeyMapping(keyName);

            bool wakeup = tempKeyCode.GetBoolean("wakeUp");
            Bundle result = KeymappingCtrl.GetInstance(ctx).AddKeyMappings(
                keyName, keyCode, wakeup,
                MainReceiver.rfidGunPressed, getParams(tempKeyCode.GetBundle("broadcastDownParams")),
                MainReceiver.rfidGunReleased, getParams(tempKeyCode.GetBundle("broadcastUpParams")),
                getParams(tempKeyCode.GetBundle("startActivityParams")));

            if (result.GetInt("errorCode") == 0)
                Log.Debug("MainActivity", "Set Gun Key Code success");
            else
                Log.Error("MainActivity", "Set Gun Key Code failed: " + result.GetString("errorMsg"));
        }

        private void sendUssScan(bool enable)
        {
            Intent intent = new Intent();
            intent.SetAction(systemUssTriggerScan);
            intent.PutExtra(ExtraScan, enable);
            SendBroadcast(intent);
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

                if (IsReaderConnected) ShowToast("Lector reconectado");
            }
            catch (Exception ex)
            {
                Log.Error("MainActivity", $"Error al reconectar: {ex.Message}");
                ShowToast("No se pudo reconectar. Reinicia la app.");
            }
        }
        #endregion

        #region CATALOGOS
        public async Task getTb_RFID_CatalogoAsync()
        {
            try
            {
                var (tabla, set) = await Task.Run(() =>
                {
                    const string query =
                        "SELECT * FROM Tb_RFID_Catalogo WHERE IdStatus = 1 ORDER BY IdClaveInt";
                    var dt = new DataTable("Tb_RFID_Catalogo");

                    using (var conn = new SqlConnection(cadenaConexion))
                    using (var da = new SqlDataAdapter(query, conn))
                        da.Fill(dt);

                    var hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataRow row in dt.Rows)
                    {
                        string epc = row["IdClaveTag"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(epc)) hashSet.Add(epc);

                        string claveInt = row["IdClaveInt"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(claveInt)) hashSet.Add(claveInt);
                    }

                    return (dt, hashSet);
                });

                // FIX M-3: Asignación atómica de referencias volatile.
                // La UI siempre verá el objeto completo o el anterior, nunca un estado intermedio.
                _tb_RFID_Catalogo = tabla;
                _catalogoEPCSet = set;

                if (tabla.Rows.Count == 0)
                    RunOnUiThread(() =>
                        Toast.MakeText(this, "Catálogo vacío o no disponible", ToastLength.Short).Show());
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

        // Wrapper síncrono para compatibilidad con código que no puede usar await.
        public void getTb_RFID_Catalogo() => _ = getTb_RFID_CatalogoAsync();
        #endregion

        #region SCANNER QR (BAJA CAJONES)
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
                        Toast.MakeText(this, "Etiqueta no válida o no encontrada en catálogo",
                            ToastLength.Long).Show();
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
                        Toast.MakeText(this, $"Etiqueta escaneada: {qrLimpio}\nTotal: {qrList.Count}",
                            ToastLength.Short).Show();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Error en ProcessQR: {ex.Message}");
                    Toast.MakeText(this, "Error al procesar etiqueta", ToastLength.Short).Show();
                }
            });
        }

        private bool validaQR(string EPC)
        {
            if (_catalogoEPCSet == null || _catalogoEPCSet.Count == 0)
            {
                _ = getTb_RFID_CatalogoAsync();
                return false;
            }
            return _catalogoEPCSet.Contains(EPC.Trim());
        }
        #endregion

        #region DIALOGO BAJA RFID
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

            var builder = new Android.App.AlertDialog.Builder(
                this, Resource.Style.AppTheme_CustomAlertDialog);
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
                _ = getTb_RFID_CatalogoAsync();
            });

            builder.SetNegativeButton("CANCELAR", (sender, args) =>
            {
                // FIX M-5: Limpiar qrList al cancelar para evitar que tags de esta sesión
                // aparezcan en la siguiente apertura del diálogo.
                qrList.Clear();
                qrAdapter?.NotifyDataSetChanged();
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
                RunOnUiThread(() =>
                    bajaDialog.SetTitle($"Baja de Etiquetas RFID ({qrList.Count} escaneadas)"));
        }

        private async Task ActualizarEstatusRFIDAsync(List<string> epcs)
        {
            if (epcs == null || epcs.Count == 0) return;

            try
            {
                int filasAfectadas = await Task.Run(() =>
                {
                    using (var conn = new SqlConnection(cadenaConexion))
                    {
                        conn.Open();
                        var parametros = string.Join(",", epcs.Select((epc, i) => $"@p{i}"));
                        string query = $"UPDATE Tb_RFID_Catalogo SET IdStatus = '2' " +
                                       $"WHERE IdClaveInt IN ({parametros})";

                        using (var cmd = new SqlCommand(query, conn))
                        {
                            for (int i = 0; i < epcs.Count; i++)
                                cmd.Parameters.AddWithValue($"@p{i}", epcs[i]);
                            return cmd.ExecuteNonQuery();
                        }
                    }
                });

                RunOnUiThread(() =>
                    Toast.MakeText(this, $"{filasAfectadas} registros actualizados",
                        ToastLength.Long).Show());
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                    Toast.MakeText(this, "Error al actualizar: " + ex.Message,
                        ToastLength.Long).Show());
            }
        }
        #endregion

        #region FAB DRAG
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
                        MostrarDialogoBajaRFID();
                    else
                    {
                        float midScreen = screenWidth / 2f;
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

        #region LIFECYCLE
        protected override void OnResume()
        {
            base.OnResume();

            if (bajaCajones == "True" && fabMain != null)
            {
                fabMain.BringToFront();
                fabMain.Visibility = ViewStates.Visible;
            }

            if (baseReader == null && !_isMonitoringReader && !_isInitializingReader)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await InitializeReader();
                        if (IsReaderConnected)
                            _ = Task.Run(() => MonitorReaderStatus());
                    }
                    catch (Exception ex) { AppLogger.LogError(ex); }
                });
            }
        }

        protected override void OnPause()
        {
            base.OnPause();
        }

        protected override void OnStop()
        {
            _isMonitoringReader = false;
            IsReaderConnected = false;

            if (baseReader != null)
            {
                try
                {
                    baseReader.RfidUhf?.Stop();
                    baseReader.Disconnect();
                }
                catch { }
                finally { baseReader = null; }
            }

            base.OnStop();
        }

        protected override void OnDestroy()
        {
            _isMonitoringReader = false;

            instance = null;

            // FIX M-1: Nular _handler para romper la referencia circular estática.
            // Sin esta línea, _handler retiene una referencia fuerte a la activity destruida,
            // con todos sus Views y Fragments, impidiendo que el GC la recolecte.
            _handler = null;

            if (baseReader != null)
            {
                try
                {
                    baseReader.RfidUhf?.Stop();
                    baseReader.Disconnect();
                }
                catch { }
                finally
                {
                    baseReader = null;
                    IsReaderConnected = false;
                }
            }

            base.OnDestroy();
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);

            string action = intent.Action;

            if (NfcAdapter.ActionTagDiscovered.Equals(action) ||
                NfcAdapter.ActionNdefDiscovered.Equals(action) ||
                NfcAdapter.ActionTechDiscovered.Equals(action))
            {
                var tag = intent.GetParcelableExtra(NfcAdapter.ExtraTag) as Android.Nfc.Tag;
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
                    if (baseReader is IDisposable d) d.Dispose();
                }
                catch { }
                finally { baseReader = null; }
            }

            foreach (var f in SupportFragmentManager.Fragments)
                SupportFragmentManager.BeginTransaction().Remove(f).CommitAllowingStateLoss();

            var intent = PackageManager.GetLaunchIntentForPackage(PackageName);
            intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask);
            StartActivity(intent);
            Finish();
        }
        #endregion

        #region PERMISOS / BLUETOOTH
        private void CheckAndRequestPermissions()
        {
            string[] requiredPermissions =
            {
                Manifest.Permission.AccessFineLocation,
                Manifest.Permission.AccessCoarseLocation
            };

            var missing = requiredPermissions
                .Where(p => CheckSelfPermission(p) == Android.Content.PM.Permission.Denied)
                .ToList();

            if (missing.Count > 0)
                ActivityCompat.RequestPermissions(this, missing.ToArray(), REQUEST_PERMISSION_CODE);
            else
                CheckBluetooth();
        }

        public override void OnRequestPermissionsResult(
            int requestCode, string[] permissions,
            [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == REQUEST_PERMISSION_CODE)
            {
                bool allGranted = Array.TrueForAll(
                    grantResults, r => r == Android.Content.PM.Permission.Granted);
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

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            BtHelper?.OnActivityResult(requestCode, resultCode);
        }
        #endregion

        #region HELPERS UI / NAVIGATION VISIBILITY
        public void OcultarElementosNavegacion()
        {
            BottomNavigation.Visibility = ViewStates.Gone;
        }

        public void MostrarElementosNavegacion()
        {
            BottomNavigation.Visibility = ViewStates.Visible;
            if (fabMain != null) fabMain.Visibility = ViewStates.Visible;
        }
        #endregion

        #region STATIC HELPERS
        public static MainActivity getInstance() => instance;

        public static void ShowToast(string msg, bool lengthLong)
        {
            Message handlerMessage = new Message
            {
                What = (int)FragmentType.None,
                Data = new Bundle()
            };
            handlerMessage.Data.PutInt(ExtraName.HandleMsg, (int)HandlerMsg.Toast);
            handlerMessage.Data.PutString(ExtraName.Text, msg);
            handlerMessage.Data.PutInt(ExtraName.Number, lengthLong ? 1 : 0);
            _handler?.SendMessage(handlerMessage);
        }

        public static void ShowToast(string msg) => ShowToast(msg, true);

        public static void ShowDialog(string title, string msg)
        {
            Message handlerMessage = new Message
            {
                What = (int)FragmentType.None,
                Data = new Bundle()
            };
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

        public static string ByteArrayToString(byte[] ba)
        {
            var shb = new SoapHexBinary(ba);
            return shb.ToString();
        }
        #endregion
    }
}
