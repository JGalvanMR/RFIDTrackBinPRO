using Android;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.Net.Wifi;
using Android.Nfc;
using Android.Nfc.Tech;
using Android.OS;
using Android.Runtime;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.App;
using Java.Net;
using Java.Util;
using Org.Json;
using Plugin.DeviceInfo;
using RFIDTrackBin.Helpers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Android.App.DownloadManager;

namespace RFIDTrackBin
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true, Exported = true)]
    public class LoginActivity : AppCompatActivity
    {
        public static string cadenaConexionLogin = "Persist Security Info=False;user id=sa; password=Gabira1;Initial Catalog = GAB_Irapuato; server=tcp:189.206.160.206,2352; MultipleActiveResultSets=true; Connect Timeout = 0";

        EditText txtUsuario, txtContrasena;
        Button btnLogin;

        #region BASE DE DATOS LOGIN
        DataSet dsLogin = new DataSet();
        #region TABLAS LOGIN
        public DataTable Tb_RFID_Usuarios = new DataTable("Tb_RFID_Usuarios");
        #endregion
        #endregion

        #region NFC
        NfcAdapter nfcAdapter;
        PendingIntent pendingIntent;
        IntentFilter[] intentFiltersArray;
        string[][] techListsArray;
        #endregion

        #region VERSION - UPDATE
        TextView versionApp;
        public static string imei = "";
        private static string INFO_FILE = "http://189.206.160.206:81/EmbarquesApk/RFIDTrackBin/version.txt";
        private int currentVersionCode;
        private string currentVersionName;
        private int latestVersionCode;
        private string latestVersionName;
        private string downloadURL;
        Android.Content.Context context;
        #endregion

        #region Spinner
        Spinner sprUsuarios;
        string query = "";
        public static DataTable responsables = new DataTable("responsables");
        System.String[] strFrutas;
        ArrayAdapter<System.String> comboAdapter;
        public static string responsablesplit = "";
        #endregion

        public static BluetoothHelper BtHelper { get; private set; }
        private const int REQUEST_PERMISSION_CODE = 1000;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            SetContentView(Resource.Layout.activity_login);

            sprUsuarios = FindViewById<Spinner>(Resource.Id.sprAreas);
            txtContrasena = FindViewById<EditText>(Resource.Id.txtContrasena);
            btnLogin = FindViewById<Button>(Resource.Id.btnLogin);
            versionApp = FindViewById<TextView>(Resource.Id.versionApp);

            txtContrasena.SetFilters(new IInputFilter[] { new InputFilterAllCaps() });

            CheckAndRequestPermissions();

            imei = getDeviceID();

            // FIX L-2: Inicialización de datos movida a método async para no bloquear UI thread.
            _ = InicializarAsync();

            #region NFC SETUP
            nfcAdapter = NfcAdapter.GetDefaultAdapter(this);

            if (nfcAdapter == null)
            {
                Toast.MakeText(this, "NFC no disponible en este dispositivo", ToastLength.Long).Show();
            }
            else
            {
                pendingIntent = PendingIntent.GetActivity(this, 0,
                    new Intent(this, typeof(LoginActivity)).AddFlags(ActivityFlags.SingleTop),
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable);

                IntentFilter ndefDetected = new IntentFilter(NfcAdapter.ActionTechDiscovered);
                intentFiltersArray = new IntentFilter[] { ndefDetected };

                techListsArray = new string[][] {
                    new string[] { Java.Lang.Class.FromType(typeof(NfcA)).Name }
                };
            }
            #endregion

            btnLogin.Click += (s, e) =>
            {
                if (responsablesplit == "Seleccione un Responsable")
                {
                    Toast.MakeText(this, "Por favor, asegurese de seleccionar un responsable y volver a intentarlo", ToastLength.Long).Show();
                    return;
                }
                if (txtContrasena.Text.Length == 0)
                {
                    Toast.MakeText(this, "Por favor, asegurese de ingresar una contraseña y volver intentarlo", ToastLength.Long).Show();
                    return;
                }

                string responsable = "";
                if (responsables.Rows.Count != 0)
                {
                    for (int i = 0; i < responsables.Rows.Count; i++)
                    {
                        if ((responsables.Rows[i]["usuario"].ToString() == responsablesplit) &&
                            (responsables.Rows[i]["password"].ToString() == txtContrasena.Text.ToString().Trim()))
                        {
                            responsable = responsables.Rows[i]["usuario"].ToString();
                            break; // FIX L-6: detener en la primera coincidencia
                        }
                    }
                }
                else
                {
                    Toast.MakeText(this, "Por favor, Seleccione un responsable", ToastLength.Long).Show();
                    return;
                }

                if (responsable == "admin" && txtContrasena.Text == "1234" ||
                    getTb_RFID_Login(responsable, txtContrasena.Text))
                {
                    string ubicacion = "";
                    string idUnidadNegocio = "";
                    string BajaCajones = "";
                    if (responsable != "admin" && Tb_RFID_Usuarios != null)
                    {
                        ubicacion = Tb_RFID_Usuarios.Rows[0]["Ubicacion"].ToString();
                        idUnidadNegocio = Tb_RFID_Usuarios.Rows[0]["idUnidadNegocio"].ToString();
                        BajaCajones = Tb_RFID_Usuarios.Rows[0]["BajaCajones"].ToString();
                    }

                    var intent = new Intent(this, typeof(MainActivity));
                    intent.PutExtra("usuario", responsable);
                    intent.PutExtra("ubicacion", ubicacion);
                    intent.PutExtra("idUnidadNegocio", idUnidadNegocio);
                    intent.PutExtra("BajaCajones", BajaCajones);
                    StartActivity(intent);
                }
                else
                {
                    Toast.MakeText(this, "Credenciales incorrectas", ToastLength.Short).Show();
                }
            };
        }

        // FIX L-2: Toda la inicialización de red/BD movida a método async.
        private async Task InicializarAsync()
        {
            await Task.Run(() => validaWiFi());
            await Task.Run(() => validaConexionRed());
            await Task.Run(() => validaservidores());
            await CargarUsuariosAsync();
            await validateAppUpdateAsync();
        }

        // FIX L-2 / L-3: Carga de spinner en background. Conexión local con using.
        private async Task CargarUsuariosAsync()
        {
            try
            {
                var (tabla, items) = await Task.Run(() =>
                {
                    const string sql = "SELECT usuario, password FROM Tb_RFID_Usuarios " +
                                       "WHERE idestatus = 1 AND RFIDTrackBin = 1 ORDER BY usuario";
                    var dt = new DataTable("responsables");
                    using (var conn = new SqlConnection(cadenaConexionLogin))
                    using (var da = new SqlDataAdapter(sql, conn))
                        da.Fill(dt);

                    var arr = new string[dt.Rows.Count + 1];
                    arr[0] = "Seleccione un Responsable";
                    for (int i = 0; i < dt.Rows.Count; i++)
                        arr[i + 1] = dt.Rows[i]["usuario"].ToString();

                    return (dt, arr);
                });

                responsables = tabla;
                strFrutas = items;

                RunOnUiThread(() =>
                {
                    Spinner spinner2 = FindViewById<Spinner>(Resource.Id.sprUsuarios);
                    comboAdapter = new ArrayAdapter<string>(this,
                        Android.Resource.Layout.SimpleSpinnerItem, strFrutas);
                    spinner2.Adapter = comboAdapter;
                    spinner2.ItemSelected -= sprUsuarios_ItemSelected2;
                    spinner2.ItemSelected += sprUsuarios_ItemSelected2;
                });
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex);
                RunOnUiThread(() =>
                    Toast.MakeText(this, "Error al cargar usuarios: " + ex.Message, ToastLength.Long).Show());
            }
        }

        #region METODOS PARA VALIDACIONES (ACTUALIZAR VERSION)
        private bool validaWiFi()
        {
            WifiManager wifi = (WifiManager)Android.App.Application.Context.GetSystemService(Android.Content.Context.WifiService);
            if (wifi.IsWifiEnabled == false)
            {
                RFIDTrackBin.GuardaLocal GuardaError = new RFIDTrackBin.GuardaLocal();
                GuardaError.creartxt("Wifi Deshabilitada");
                RunOnUiThread(() =>
                {
                    Android.App.AlertDialog.Builder alertDialog = new Android.App.AlertDialog.Builder(this);
                    alertDialog.SetTitle(Html.FromHtml("<font color='#FCEC70' size = 10>Error en el Adaptador WIFI</font>"));
                    alertDialog.SetIcon(Resource.Drawable.warning);
                    alertDialog.SetMessage(Html.FromHtml("<font color='#E0F1FA' size = 10>El Dispositivo no tiene la Wifi Activada, favor de activarlo</font>"));
                    alertDialog.SetCancelable(false);
                    alertDialog.SetNeutralButton("Ok", delegate { alertDialog.Dispose(); Finish(); });
                    alertDialog.Show();
                });
                return false;
            }
            return true;
        }

        private bool validaConexionRed()
        {
            ConnectivityManager connectivityManager = (ConnectivityManager)GetSystemService(Android.Content.Context.ConnectivityService);
            NetworkInfo activeConnection = connectivityManager.ActiveNetworkInfo;
            bool isOnline = (activeConnection != null) && activeConnection.IsConnected;
            if (!isOnline || !validaservidores())
            {
                cadenaConexionLogin = "Persist Security Info=False;user id=sa; password=Gabira1;Initial Catalog =GAB_Irapuato; server=tcp:189.206.160.206,2352; Connect Timeout = 0";
                INFO_FILE = "http://189.206.160.206:81/EmbarquesApk/RFIDTrackBin/version.txt";
                if (!isOnline)
                {
                    RFIDTrackBin.GuardaLocal GuardaError = new RFIDTrackBin.GuardaLocal();
                    GuardaError.creartxt("Error en la conexion de red, No esta conectado a ninguna red");
                    RunOnUiThread(() =>
                    {
                        Android.App.AlertDialog.Builder alertDialog = new Android.App.AlertDialog.Builder(this);
                        alertDialog.SetTitle(Html.FromHtml("<font color='#FCEC70' size = 10>Error en la Conexion a Internet</font>"));
                        alertDialog.SetIcon(Resource.Drawable.warning);
                        alertDialog.SetMessage(Html.FromHtml("<font color='#E0F1FA' size = 10>El Dispositivo no Esta conectado a ninguna Red, favor de verificarlo</font>"));
                        alertDialog.SetCancelable(false);
                        alertDialog.SetNeutralButton("Ok", delegate { alertDialog.Dispose(); Finish(); });
                        alertDialog.Show();
                    });
                    return false;
                }
                return true;
            }
            return true;
        }

        public bool validaservidores()
        {
            bool online = true;
            string[] sitios = new string[1];
            sitios[0] = "http://189.206.160.206:81/EmbarquesApk/";

            for (int i = 0; i < sitios.Length; i++)
            {
                RFIDTrackBin.GuardaLocal ValidarServidor = new RFIDTrackBin.GuardaLocal();
                bool onlinex = ValidarServidor.HayConexion(sitios[i]);
                if (onlinex == false)
                    ValidarServidor.creartxt("Error al Conectar a " + sitios[i]);
            }
            return online;
        }

        private string getDeviceID()
        {
            Android.Telephony.TelephonyManager telephonyManager;
            telephonyManager = (Android.Telephony.TelephonyManager)GetSystemService(TelephonyService);
            string deviceid = CrossDeviceInfo.Current.Id;
            return deviceid;
        }

        private async Task validateAppUpdateAsync()
        {
            try
            {
                getData();
            }
            catch { }

            RunOnUiThread(() =>
            {
                versionApp.Text = "RFID Track Bin - Versión: " + currentVersionName;
                if (isNewVersionAvailable())
                {
                    string msj = "Nueva Version: " + isNewVersionAvailable();
                    msj += "\nActual Version: " + currentVersionName + "(" + currentVersionCode + ")";
                    msj += "\nUltima Version: " + latestVersionName + "(" + latestVersionCode + ")";
                    msj += "\nDesea Actualizar?";

                    Android.App.AlertDialog.Builder alertDialog = new Android.App.AlertDialog.Builder(this);
                    alertDialog.SetTitle(Html.FromHtml("<font color='#DF0101' size = 10>Actualizacion Disponible"));
                    alertDialog.SetIcon(Resource.Drawable.update);
                    alertDialog.SetMessage(Html.FromHtml("<font color='#000000' size = 10>" + msj + "</font>"));
                    alertDialog.SetPositiveButton(Html.FromHtml("<font face = 'Comic Sans MS, arial' color='#DF0101' size = '10'>OK</font>"), SaveAction);
                    alertDialog.SetCancelable(false);
                    alertDialog.Create();
                    alertDialog.Show();
                }
            });
        }

        private void getData()
        {
            try
            {
                context = this;
                System.Console.WriteLine("AutoUpdater", "GetData");
                Android.Content.PM.PackageInfo pckginfo = context.PackageManager.GetPackageInfo(context.PackageName, 0);
                currentVersionCode = pckginfo.VersionCode;
                currentVersionName = pckginfo.VersionName;

                StrictMode.ThreadPolicy policy = new StrictMode.ThreadPolicy.Builder().PermitAll().Build();
                StrictMode.SetThreadPolicy(policy);

                string data = downloadHttp(new URL(INFO_FILE));
                JSONObject json = new JSONObject(data.ToString());
                latestVersionCode = json.GetInt("versionCode");
                latestVersionName = json.OptString("versionName");
                downloadURL = json.GetString("downloadURL");
                System.Console.WriteLine("AutoUpdate", "Datos obtenidos con éxito");
            }
            catch (JSONException e) { System.Console.WriteLine("AutoUpdate", "Ha habido un error con el JSON", e); }
            catch (Android.Content.PM.PackageManager.NameNotFoundException e) { System.Console.WriteLine("AutoUpdate", "Ha habido un error con el packete :S", e); }
            catch (System.IO.IOException e) { System.Console.WriteLine("AutoUpdate", "Ha habido un error con la descarga", e); }
        }

        public bool isNewVersionAvailable() => latestVersionCode > currentVersionCode;

        private void SaveAction(object sender, DialogClickEventArgs e) => downloadApp();
        private void CancelaAction(object sender, DialogClickEventArgs e) => Finish();

        private string DownloadApp()
        {
            var progressDialog = ProgressDialog.Show(this, "Espere Por Favor...", "Descargando Actualizacion", true);
            new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                try
                {
                    var pathToNewFolder = Android.OS.Environment.ExternalStorageDirectory.AbsolutePath + "/RFIDTrackBin";
                    System.IO.Directory.CreateDirectory(pathToNewFolder);

                    string archivo = Android.OS.Environment.ExternalStorageDirectory.AbsolutePath + "/RFIDTrackBin/com.mrlucky.rfidtrackbin.apk";

                    var webClient = new WebClient();
                    webClient.DownloadFileCompleted += (s, ex) =>
                    {
                        Java.IO.File toInstall = new Java.IO.File(archivo);
                        Android.Net.Uri downloadUri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, context.ApplicationContext.PackageName + ".provider", toInstall);

                        Intent intent = new Intent(Intent.ActionView);
                        intent.SetDataAndType(downloadUri, "application/vnd.android.package-archive");
                        intent.SetFlags(ActivityFlags.NewTask);
                        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
                        StartActivity(intent);
                        Finish();
                    };

                    var folder = Android.OS.Environment.ExternalStorageDirectory.AbsolutePath + "/PreSplitCamionetas";
                    webClient.DownloadFileAsync(new System.Uri("http://189.206.160.206:81/EmbarquesApk/RFIDTrackBin/com.mrlucky.rfidtrackbin.apk"), folder + "/com.mrlucky.rfidtrackbin.apk");
                }
                catch (System.IO.IOException e)
                {
                    RunOnUiThread(() => progressDialog.Hide());
                    RunOnUiThread(() => Toast.MakeText(this, e.ToString(), ToastLength.Long).Show());
                }
            })).Start();
            return "1";
        }

        private string downloadApp()
        {
            var progressDialog = ProgressDialog.Show(this, "Espere Por Favor...", "Descargando Actualización", true);

            new System.Threading.Thread(new ThreadStart(delegate
            {
                try
                {
                    var pathToNewFolder = System.IO.Path.Combine(
                        Android.App.Application.Context.GetExternalFilesDir(null).AbsolutePath, "RFIDTrackBin");
                    System.IO.Directory.CreateDirectory(pathToNewFolder);

                    string archivo = System.IO.Path.Combine(pathToNewFolder, "apk");

                    var webClient = new WebClient();
                    webClient.DownloadFileCompleted += (s, ex) =>
                    {
                        RunOnUiThread(() => progressDialog.Hide());

                        if (ex.Error != null)
                        {
                            RunOnUiThread(() => Toast.MakeText(this, "Error en la descarga: " + ex.Error.Message, ToastLength.Long).Show());
                            return;
                        }

                        Java.IO.File toInstall = new Java.IO.File(archivo);
                        Android.Net.Uri downloadUri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                            this, this.ApplicationContext.PackageName + ".fileprovider", toInstall);

                        Intent intentx = new Intent(Intent.ActionView);
                        intentx.SetDataAndType(downloadUri, "application/vnd.android.package-archive");
                        intentx.SetFlags(ActivityFlags.NewTask);
                        intentx.AddFlags(ActivityFlags.GrantReadUriPermission);
                        StartActivity(intentx);
                    };

                    if (INFO_FILE == "http://192.168.123.4:81/EmbarquesApk/RFIDTrackBin/version.txt")
                        webClient.DownloadFileAsync(new System.Uri("http://192.168.123.4:81/EmbarquesApk/RFIDTrackBin/com.mrlucky.rfidtrackbin.apk"), archivo);
                    else
                        webClient.DownloadFileAsync(new System.Uri("http://189.206.160.206:81/EmbarquesApk/RFIDTrackBin/com.mrlucky.rfidtrackbin.apk"), archivo);
                }
                catch (System.IO.IOException e)
                {
                    RunOnUiThread(() => progressDialog.Hide());
                    RunOnUiThread(() => Toast.MakeText(this, e.ToString(), ToastLength.Long).Show());
                }
            })).Start();

            return "1";
        }

        private static string downloadHttp(URL url)
        {
            StrictMode.ThreadPolicy policy = new StrictMode.ThreadPolicy.Builder().PermitAll().Build();
            StrictMode.SetThreadPolicy(policy);
            HttpURLConnection c = (HttpURLConnection)url.OpenConnection();
            c.RequestMethod = "GET";
            c.ReadTimeout = (15 * 1000);
            c.UseCaches = false;
            c.Connect();
            Java.IO.BufferedReader reader = new Java.IO.BufferedReader(new Java.IO.InputStreamReader(c.InputStream));
            Java.Lang.StringBuilder stringBuilder = new Java.Lang.StringBuilder();
            string line;
            while ((line = reader.ReadLine()) != null)
                stringBuilder.Append(line + "\n");
            return stringBuilder.ToString();
        }
        #endregion

        private void CheckAndRequestPermissions()
        {
            string[] requiredPermissions =
            {
                Manifest.Permission.AccessFineLocation,
                Manifest.Permission.AccessCoarseLocation,
                Manifest.Permission.Bluetooth,
                Manifest.Permission.BluetoothAdmin,
                Manifest.Permission.BluetoothConnect,
                Manifest.Permission.BluetoothScan
            };

            var missingPermissions = new List<string>();
            foreach (var permission in requiredPermissions)
            {
                if (CheckSelfPermission(permission) == Android.Content.PM.Permission.Denied)
                    missingPermissions.Add(permission);
            }

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
                bool allGranted = Array.TrueForAll(grantResults, result => result == Android.Content.PM.Permission.Granted);
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

        protected override void OnResume()
        {
            base.OnResume();
            if (nfcAdapter != null)
                nfcAdapter.EnableForegroundDispatch(this, pendingIntent, intentFiltersArray, techListsArray);
        }

        protected override void OnPause()
        {
            base.OnPause();
            if (nfcAdapter != null)
                nfcAdapter.DisableForegroundDispatch(this);
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);

            if (intent.Action == NfcAdapter.ActionTechDiscovered)
            {
                var tag = (Tag)intent.GetParcelableExtra(NfcAdapter.ExtraTag);
                byte[] id = tag.GetId();
                string uid = BitConverter.ToString(id).Replace("-", "").ToUpperInvariant();
                ValidarLoginPorNFC(uid);
            }
        }

        public override void OnBackPressed()
        {
            new AndroidX.AppCompat.App.AlertDialog.Builder(this)
                .SetTitle("Salir de la aplicación")
                .SetMessage("¿Deseas cerrar la aplicación?")
                .SetCancelable(true)
                .SetPositiveButton("Sí", (sender, args) => SalirDeLaAplicacion())
                .SetNegativeButton("Cancelar", (sender, args) => { })
                .Show();
        }

        private void SalirDeLaAplicacion()
        {
            FinishAffinity();
            Java.Lang.JavaSystem.Exit(0);
        }

        private void ValidarLoginPorNFC(string uid)
        {
            List<string> uidsPermitidos = new List<string> { "04AABBCCDD", "12345678ABCDEF", "04774211B506D0" };

            if (uidsPermitidos.Contains(uid))
            {
                Toast.MakeText(this, $"Bienvenido (NFC UID: {uid})", ToastLength.Short).Show();

                var intent = new Intent(this, typeof(MainActivity));
                intent.PutExtra("usuario", "NFC_USER_" + uid);
                StartActivity(intent);
                Finish();
            }
            else
            {
                Toast.MakeText(this, $"Tarjeta no autorizada: {uid}", ToastLength.Short).Show();
            }
        }

        #region CATALOGOS LOGIN
        public bool getTb_RFID_Login(string usuario, string password)
        {
            bool result = false;
            try
            {
                using (SqlConnection thisConnection = new SqlConnection(cadenaConexionLogin))
                {
                    // FIX L-1: Reemplazada concatenación directa de strings por parámetros SqlParameter.
                    // La versión anterior era vulnerable a SQL Injection.
                    const string query = @"
                        SELECT * FROM Tb_RFID_Usuarios
                        WHERE idEstatus     = 1
                          AND usuario       = @usuario
                          AND password      = @password
                          AND RFIDTrackBin  = 1";

                    using (SqlDataAdapter da = new SqlDataAdapter(query, thisConnection))
                    {
                        da.SelectCommand.Parameters.AddWithValue("@usuario", usuario);
                        da.SelectCommand.Parameters.AddWithValue("@password", password);

                        dsLogin.Clear();
                        da.Fill(dsLogin, "Tb_RFID_Usuarios");

                        if (dsLogin.Tables.Contains("Tb_RFID_Usuarios") &&
                            dsLogin.Tables["Tb_RFID_Usuarios"].Rows.Count > 0)
                        {
                            Tb_RFID_Usuarios = dsLogin.Tables["Tb_RFID_Usuarios"];
                            result = true;
                        }
                        else
                        {
                            Tb_RFID_Usuarios = null;
                            result = false;
                        }
                    }
                }
                return result;
            }
            catch (SqlException)
            {
                return result;
            }
            catch (Exception)
            {
                return result;
            }
        }
        #endregion

        private void sprUsuarios_ItemSelected2(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            Spinner spinner = (Spinner)sender;
            responsablesplit = spinner.GetItemAtPosition(e.Position).ToString();
            txtContrasena.RequestFocus();
            InputMethodManager immx = (InputMethodManager)GetSystemService(Android.Content.Context.InputMethodService);
            immx.ShowSoftInput(txtContrasena, ShowFlags.Implicit);
        }
    }
}