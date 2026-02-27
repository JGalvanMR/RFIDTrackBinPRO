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
using static Android.App.DownloadManager;


namespace RFIDTrackBin
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true, Exported = true)]
    public class LoginActivity : AppCompatActivity
    {
        public static string cadenaConexionLogin = "Persist Security Info=False;user id=sa; password=Gabira1;Initial Catalog = GAB_Irapuato; server=tcp:189.206.160.206,2352; MultipleActiveResultSets=true; Connect Timeout = 0";
        SqlConnection thisConnection = new SqlConnection(cadenaConexionLogin);
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
        SqlDataAdapter da;
        DataSet ds = new DataSet();
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
            // Habilitar soporte para Windows-1252
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            SetContentView(Resource.Layout.activity_login);

            sprUsuarios = FindViewById<Spinner>(Resource.Id.sprAreas);
            txtContrasena = FindViewById<EditText>(Resource.Id.txtContrasena);
            btnLogin = FindViewById<Button>(Resource.Id.btnLogin);

            txtContrasena.SetFilters(new IInputFilter[] { new InputFilterAllCaps() });

            CheckAndRequestPermissions();

            validaWiFi();
            validaConexionRed();

            imei = getDeviceID();
            validaservidores();

            versionApp = FindViewById<TextView>(Resource.Id.versionApp);

            validateAppUpdate();

            #region NFC SETUP
            // NFC Setup
            nfcAdapter = NfcAdapter.GetDefaultAdapter(this);

            if (nfcAdapter == null)
            {
                Toast.MakeText(this, "NFC no disponible en este dispositivo", ToastLength.Long).Show();
            }
            else
            {
                pendingIntent = PendingIntent.GetActivity(this, 0,
                    new Intent(this, typeof(LoginActivity)).AddFlags(ActivityFlags.SingleTop), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable);

                IntentFilter ndefDetected = new IntentFilter(NfcAdapter.ActionTechDiscovered);
                intentFiltersArray = new IntentFilter[] { ndefDetected };

                techListsArray = new string[][] {
        new string[] { Java.Lang.Class.FromType(typeof(NfcA)).Name }
    };
            }
            #endregion

            #region Llenado Spinner 2

            query = "SELECT usuario, password FROM Tb_RFID_Usuarios WHERE idestatus = 1 and RFIDTrackBin = 1 ORDER BY usuario";
            da = new SqlDataAdapter(query, thisConnection);
            da.Fill(ds, "responsables");
            responsables = ds.Tables["responsables"];
            thisConnection.Close();

            Spinner spinner2 = FindViewById<Spinner>(Resource.Id.sprUsuarios);
            System.Collections.ArrayList listaFrutas2 = new System.Collections.ArrayList();

            strFrutas = new System.String[responsables.Rows.Count + 1];
            strFrutas[0] = "Seleccione un Responsable";
            for (int i = 1; i <= responsables.Rows.Count; i++)
            {
                int x = i - 1;
                strFrutas[i] = responsables.Rows[x]["usuario"].ToString();
            }


            Collections.AddAll(listaFrutas2, strFrutas);
            comboAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, strFrutas);
            spinner2.Adapter = comboAdapter;
            spinner2.ItemSelected += new EventHandler<AdapterView.ItemSelectedEventArgs>(sprUsuarios_ItemSelected2);
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
                        if ((responsables.Rows[i]["usuario"].ToString() == responsablesplit) && (responsables.Rows[i]["password"].ToString() == txtContrasena.Text.ToString().Trim()))
                        {
                            responsable = responsables.Rows[i]["usuario"].ToString();
                        }
                    }
                }
                else
                {
                    Toast.MakeText(this, "Por favor, Seleccione un responsable", ToastLength.Long).Show();
                    return;
                }


                if (responsable == "admin" && txtContrasena.Text == "1234" || getTb_RFID_Login(responsable, txtContrasena.Text))
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

                    // Ir a MainActivity
                    var intent = new Intent(this, typeof(MainActivity));
                    intent.PutExtra("usuario", responsable); // Envías el valor
                    intent.PutExtra("ubicacion", ubicacion); // INDICA UNIDAD DE NEGOCIOany
                    intent.PutExtra("idUnidadNegocio", idUnidadNegocio); // INDICA UNIDAD DE NEGOCIO
                    intent.PutExtra("BajaCajones", BajaCajones); // INDICA SI EL USUARIO TIENE PERMISO DE BAJA DE CAJONES
                    StartActivity(intent);
                    //Finish(); // Opcional, para que no puedan volver con el botón atrás
                }
                else
                {
                    Toast.MakeText(this, "Credenciales incorrectas", ToastLength.Short).Show();
                }
            };
        }

        #region METODOS PARA VALIDACIONES (ACTUALIZAR VERSION
        private bool validaWiFi()
        {
            #region ValidaWiFi
            WifiManager wifi = (WifiManager)Android.App.Application.Context.GetSystemService(Android.Content.Context.WifiService);
            if (wifi.IsWifiEnabled == false)
            {
                RFIDTrackBin.GuardaLocal GuardaError = new RFIDTrackBin.GuardaLocal();
                GuardaError.creartxt("Wifi Deshabilitada");
                Android.App.AlertDialog.Builder alertDialog = new Android.App.AlertDialog.Builder(this);
                alertDialog.SetTitle(Html.FromHtml("<font color='#FCEC70' size = 10>Error en el Adaptador WIFI</font>"));
                alertDialog.SetIcon(Resource.Drawable.warning);
                alertDialog.SetMessage(Html.FromHtml("<font color='#E0F1FA' size = 10>El Dispositivo no tiene la Wifi Activada, favor de activarlo</font>"));
                alertDialog.SetCancelable(false);
                alertDialog.SetNeutralButton("Ok", delegate
                {
                    alertDialog.Dispose();
                    Finish();

                });
                alertDialog.Show();
                return false;
            }
            else
            {
                return true;
            }
            #endregion
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
                    Android.App.AlertDialog.Builder alertDialog = new Android.App.AlertDialog.Builder(this);
                    alertDialog.SetTitle(Html.FromHtml("<font color='#FCEC70' size = 10>Error en la Conexion a Internet</font>"));
                    alertDialog.SetIcon(Resource.Drawable.warning);
                    alertDialog.SetMessage(Html.FromHtml("<font color='#E0F1FA' size = 10>El Dispositivo no Esta conectado a ninguna Red, favor de verificarlo</font>"));
                    alertDialog.SetCancelable(false);
                    alertDialog.SetNeutralButton("Ok", delegate
                    {
                        alertDialog.Dispose();
                        Finish();

                    });
                    alertDialog.Show();
                    return false;
                }

                return true;
            }
            else
            {
                return true;
            }
        }
        public bool validaservidores()
        {
            bool online = true;
            string[] sitios = new string[1];
            sitios[0] = "http://189.206.160.206:81/EmbarquesApk/";
            //sitios[0] = "http://192.168.123.4:81/EmbarquesApk/";
            //sitios[1] = "http://192.168.123.6";

            for (int i = 0; i < sitios.Length; i++)
            {
                RFIDTrackBin.GuardaLocal ValidarServidor = new RFIDTrackBin.GuardaLocal();
                bool onlinex = ValidarServidor.HayConexion(sitios[i]);

                if (onlinex == false)
                {
                    ValidarServidor.creartxt("Error al Conectar a " + sitios[i]);
                }
            }
            return online;
        }
        private string getDeviceID()
        {

            Android.Telephony.TelephonyManager telephonyManager;
            telephonyManager = (Android.Telephony.TelephonyManager)GetSystemService(TelephonyService);
            //string deviceid=telephonyManager.DeviceId;
            string deviceid = CrossDeviceInfo.Current.Id;
            return deviceid;
        }
        private void validateAppUpdate()
        {
            //Inicio de Validacion de Actualizacion *******************************************************************
            try
            {
                getData();
            }
            catch
            {

            }
            versionApp.Text = "RFID Track Bin - Versión: " + currentVersionName;
            if (isNewVersionAvailable())
            {
                //Crea mensaje con datos de versión.
                string msj = "Nueva Version: " + isNewVersionAvailable();
                msj += "\nActual Version: " + currentVersionName + "(" + currentVersionCode + ")";
                msj += "\nUltima Version: " + latestVersionName + "(" + latestVersionCode + ")";
                msj += "\nDesea Actualizar?";
                //Crea ventana de alerta.
                Android.App.AlertDialog.Builder alertDialog = new Android.App.AlertDialog.Builder(this);
                alertDialog.SetTitle(Html.FromHtml("<font color='#DF0101' size = 10>Actualizacion Disponible"));
                alertDialog.SetIcon(Resource.Drawable.update);
                alertDialog.SetMessage(Html.FromHtml("<font color='#000000' size = 10>" + msj + "</font>"));
                alertDialog.SetPositiveButton(Html.FromHtml("<font face = 'Comic Sans MS, arial' color='#DF0101' size = '10'>OK</font>"), SaveAction);
                //alertDialog.SetNegativeButton(Html.FromHtml("<font face = 'Comic Sans MS, arial' color='#DF0101' size = '10'>No</font>"), CancelaAction);
                alertDialog.SetCancelable(false);
                alertDialog.Create();
                alertDialog.Show();
                //Muestra la ventana esperando respuesta.
            }
        }
        private void getData()
        {
            try
            {
                context = this;
                // Datos locales
                System.Console.WriteLine("AutoUpdater", "GetData");
                Android.Content.PM.PackageInfo pckginfo = context.PackageManager.GetPackageInfo(context.PackageName, 0);

                currentVersionCode = pckginfo.VersionCode;
                currentVersionName = pckginfo.VersionName;

                // Datos remotos
                string data = downloadHttp(new URL(INFO_FILE));
                JSONObject json = new JSONObject(data.ToString());
                latestVersionCode = json.GetInt("versionCode");
                latestVersionName = json.OptString("versionName");
                downloadURL = json.GetString("downloadURL");
                System.Console.WriteLine("AutoUpdate", "Datos obtenidos con éxito");
            }
            catch (JSONException e)
            {
                System.Console.WriteLine("AutoUpdate", "Ha habido un error con el JSON", e);
            }
            catch (Android.Content.PM.PackageManager.NameNotFoundException e)
            {
                System.Console.WriteLine("AutoUpdate", "Ha habido un error con el packete :S", e);
            }
            catch (System.IO.IOException e)
            {
                System.Console.WriteLine("AutoUpdate", "Ha habido un error con la descarga", e);
            }
        }
        public bool isNewVersionAvailable()
        {
            return latestVersionCode > currentVersionCode;
        }
        private void SaveAction(object sender, DialogClickEventArgs e)
        {
            downloadApp();
        }
        private void CancelaAction(object sender, DialogClickEventArgs e)
        {
            Finish();
        }
        private string DownloadApp()
        {
            var progressDialog = ProgressDialog.Show(this, "Espere Por Favor...", "Descargando Actualizacion", true);
            new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {//LOAD METHOD TO GET ACCOUNT INFO
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

                        #region Actualizar APK OLD
                        /*RunOnUiThread(() => Toast.MakeText(this, "Aplicacion Actualizada.", ToastLength.Long).Show()); //HIDE PROGRESS DIALOG 
                        RunOnUiThread(() => progressDialog.Hide());
                        Intent intentx = new Intent(Intent.ActionView);
                        intentx.SetDataAndType(Android.Net.Uri.FromFile(new Java.IO.File(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath + "/RFIDTrackBin/com.mrlucky.rfidtrackbin.apk")), "application/vnd.android.package-archive");
                        intentx.SetFlags(ActivityFlags.NewTask);
                        StartActivity(intentx);
                        Finish();*/
                        #endregion
                    };

                    var folder = Android.OS.Environment.ExternalStorageDirectory.AbsolutePath + "/PreSplitCamionetas";
                    //webClient.DownloadFileAsync(new System.Uri("http://192.168.123.4:81/EmbarquesApk/RFIDTrackBin/com.mrlucky.rfidtrackbin.apk"), folder + "/com.mrlucky.rfidtrackbin.apk");
                    webClient.DownloadFileAsync(new System.Uri("http://189.206.160.206:81/EmbarquesApk/RFIDTrackBin/com.mrlucky.rfidtrackbin.apk"), folder + "/com.mrlucky.rfidtrackbin.apk");
                }
                catch (System.IO.IOException e)
                {
                    RunOnUiThread(() => progressDialog.Hide());
                    RunOnUiThread(() => Toast.MakeText(this, e.ToString(), ToastLength.Long).Show()); //HIDE PROGRESS DIALOG 
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
                    // Usar ContextCompat para obtener el directorio público de la aplicación
                    var pathToNewFolder = System.IO.Path.Combine(Android.App.Application.Context.GetExternalFilesDir(null).AbsolutePath, "RFIDTrackBin");
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
                        Android.Net.Uri downloadUri = AndroidX.Core.Content.FileProvider.GetUriForFile(this, this.ApplicationContext.PackageName + ".fileprovider", toInstall);

                        Intent intentx = new Intent(Intent.ActionView);
                        intentx.SetDataAndType(downloadUri, "application/vnd.android.package-archive");
                        intentx.SetFlags(ActivityFlags.NewTask);
                        intentx.AddFlags(ActivityFlags.GrantReadUriPermission);

                        StartActivity(intentx);
                    };

                    if (INFO_FILE == "http://192.168.123.4:81/EmbarquesApk/RFIDTrackBin/version.txt")
                    {
                        webClient.DownloadFileAsync(new System.Uri("http://192.168.123.4:81/EmbarquesApk/RFIDTrackBin/com.mrlucky.rfidtrackbin.apk"), archivo);
                    }
                    else
                    {
                        webClient.DownloadFileAsync(new System.Uri("http://189.206.160.206:81/EmbarquesApk/RFIDTrackBin/com.mrlucky.rfidtrackbin.apk"), archivo);
                    }
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
            // Codigo de coneccion, Irrelevante al tema.

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
            {
                stringBuilder.Append(line + "\n");
            }
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
            {
                ActivityCompat.RequestPermissions(this, missingPermissions.ToArray(), REQUEST_PERMISSION_CODE);
            }
            else
            {
                CheckBluetooth();
            }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
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
            {
                bluetoothAdapter.Enable();
            }
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

                // Validación del UID
                ValidarLoginPorNFC(uid);
            }
        }

        public override void OnBackPressed()
        {
            new AndroidX.AppCompat.App.AlertDialog.Builder(this)
                .SetTitle("Salir de la aplicación")
                .SetMessage("¿Deseas cerrar la aplicación?")
                .SetCancelable(true)
                .SetPositiveButton("Sí", (sender, args) =>
                {
                    SalirDeLaAplicacion();
                })
                .SetNegativeButton("Cancelar", (sender, args) =>
                {
                    // No hacemos nada, solo cerramos el diálogo
                })
                .Show();
        }

        private void SalirDeLaAplicacion()
        {
            // Si estás usando MainActivity como contenedor principal, forzamos cierre total
            FinishAffinity(); // Cierra todas las actividades abiertas
            Java.Lang.JavaSystem.Exit(0); // Finaliza el proceso de la app
        }

        private void ValidarLoginPorNFC(string uid)
        {
            // Aquí colocas tu lógica real. Puedes consultar una base de datos o usar una lista permitida.
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
                    string query = "SELECT * FROM Tb_RFID_Usuarios WHERE idEstatus = 1 AND usuario = '" + usuario + "' AND password = '" + password + "' AND RFIDTrackBin = 1";

                    using (SqlDataAdapter da = new SqlDataAdapter(query, thisConnection))
                    {
                        // Limpiar dataset y validar la tabla antes de asignarla
                        dsLogin.Clear();
                        da.Fill(dsLogin, "Tb_RFID_Usuarios");

                        if (dsLogin.Tables.Contains("Tb_RFID_Usuarios") && dsLogin.Tables["Tb_RFID_Usuarios"].Rows.Count > 0)
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
            catch (SqlException sqlEx)
            {
                // Error específico de SQL
                //Toast.MakeText(this, "Error SQL al cargar catálogo: " + sqlEx.Message, ToastLength.Long).Show();
                return result;
            }
            catch (Exception ex)
            {
                // Otros errores generales
                //Toast.MakeText(this, "Error general al cargar catálogo: " + ex.Message, ToastLength.Long).Show();
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