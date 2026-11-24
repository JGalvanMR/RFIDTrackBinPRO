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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Exception = System.Exception;
using AndroidX.AppCompat.App;

namespace RFIDTrackBin.fragment
{
    public class SalidasFragment : BaseFragment, IReaderEventListener, IRfidUhfEventListener, MainReceiver.IEventLitener, IDisposable
    {
        static string TAG = typeof(SalidasFragment).Name;

        static string keymappingPath = "/storage/emulated/0/Android/data/com.unitech.unitechrfidsample";
        static string android12keymappingPath = "/storage/emulated/0/Unitech/unitechrfidsample/";
        static string systemUssTriggerScan = "unitech.scanservice.software_scankey";
        static string ExtraScan = "scan";

        private const int MAX_MASK = 2;
        private const int NIBLE_SIZE = 4;

        bool accessTagResult;

        private bool _isFindTag = false;
        private bool _isDisposed = false;
        private readonly object _inventoryLock = new object();
        private bool _isInventoryInProgress = false;
        private DateTime _lastTriggerTime = DateTime.MinValue;
        private bool _isLoadingFlete = false; // Fix C: bloquea handlers durante SetSelection programático

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
        private readonly object _soundLock = new object();
        #endregion

        MainReceiver mReceiver;
        Bundle tempKeyCode = null;

        GridView gvObject;
        private List<TagLeido> tagEPCList = new List<TagLeido>();
        private readonly ConcurrentDictionary<string, byte> _epcLeidosSet = new ConcurrentDictionary<string, byte>();
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

            // ISSUE 2: Si hay sesión pendiente mostrar diálogo; si no, flujo normal
            if (HaySesionSalidaPendiente())
                MostrarDialogoSalidaPendiente();
        }

        private bool HaySesionSalidaPendiente()
        {
            var prefs = _activity.GetSharedPreferences(PREFS_SALIDAS, FileCreationMode.Private);
            return prefs.GetBoolean(PREF_S_ACTIVA, false);
        }

        private void MostrarDialogoSalidaPendiente()
        {
            var prefs = _activity.GetSharedPreferences(PREFS_SALIDAS, FileCreationMode.Private);
            int idPendiente = prefs.GetInt(PREF_S_ID, 0);
            string provPendiente = prefs.GetString(PREF_S_PROV, "");
            string ranchoPendiente = prefs.GetString(PREF_S_RANCHO, "");
            string tablaPendiente = prefs.GetString(PREF_S_TABLA, "");

            // Resolver nombres legibles desde la BD
            string nombreProv = ObtenerNombrePorClave("prov_nombre", "vwProveedor", "prov_clave", provPendiente) ?? provPendiente;
            string nombreRancho = ObtenerNombrePorClave("rch_nombre", "vwRanchos", "rch_clave", ranchoPendiente) ?? ranchoPendiente;
            string nombreTabla = ObtenerNombrePorClave("tbl_nombre", "vwTablas", "tbl_clave", tablaPendiente) ?? tablaPendiente;

            string mensaje = $"Tiene una salida sin finalizar:\n\n" +
                             $"ID: {idPendiente}\n" +
                             $"Proveedor: {nombreProv}\n" +
                             $"Rancho: {nombreRancho}\n" +
                             $"Tabla: {nombreTabla}";

            new Android.App.AlertDialog.Builder(_activity)
                .SetTitle("Salida Pendiente")
                .SetMessage(mensaje)
                .SetPositiveButton("Continuar", async (s, e) =>
                {
                    await RestaurarSesionSalidaAsync();
                })
                .SetNegativeButton("Cerrar Ahora", async (s, e) =>
                {
                    await CerrarSalidaHuerfanaAsync(idPendiente);
                    LimpiarSesionSalida();
                })
                .SetCancelable(false)
                .Show();
        }

        /// <summary>Obtiene un nombre legible desde una vista usando su clave.</summary>
        private string ObtenerNombrePorClave(string campoNombre, string vista, string campoClave, string valorClave)
        {
            if (string.IsNullOrEmpty(valorClave)) return null;
            try
            {
                using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                conn.Open();
                string sql = $"SELECT TOP 1 {campoNombre} FROM {vista} WHERE {campoClave} = @clave";
                using SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@clave", valorClave);
                return cmd.ExecuteScalar()?.ToString();
            }
            catch { return null; }
        }

        private async Task CerrarSalidaHuerfanaAsync(int idSalida)
        {
            try
            {
                await Task.Run(() =>
                {
                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    conn.Open();
                    const string query = @"UPDATE Tb_RFID_Mstr 
                                           SET Mstr_Status = 'C'
                                           WHERE IdConse = @Id AND Mstr_Status = 'A'";
                    using SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@Id", idSalida);
                    int filas = cmd.ExecuteNonQuery();
                    Log.Debug(TAG, $"CerrarSalidaHuerfanaAsync: {filas} fila(s) actualizadas para ID={idSalida}");
                });
                Toast.MakeText(_activity, $"Salida #{idSalida} cerrada", ToastLength.Short).Show();
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"CerrarSalidaHuerfanaAsync: {ex.Message}");
                Toast.MakeText(_activity, "No se pudo cerrar la salida pendiente", ToastLength.Long).Show();
            }
        }

        #region SoundPool
        public void InitializeSoundPool()
        {
            lock (_soundLock)
            {
                if (_isDisposed) return;
                try
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
                    // ✅ CORREGIDO: Drawable -> Raw
                    beepSoundId = soundPool.Load(_activity, Resource.Raw.beep, 1);
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Error inicializando SoundPool: {ex.Message}");
                }
            }
        }

        private void PlayBeepSound()
        {
            lock (_soundLock) // ✅ Agregado
            {
                if (beepSoundId != 0 && soundPool != null && !_isDisposed)
                {
                    try { soundPool.Play(beepSoundId, 1.0f, 1.0f, 0, 0, 1.0f); }
                    catch (Exception ex) { Log.Warn(TAG, $"Error al reproducir sonido: {ex.Message}"); }
                }
            }
        }
        #endregion

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
                    // Fix A: esperar el resultado antes de actualizar UI.
                    // Si el INSERT falla, IdConse queda en -1 y la UI no se bloquea.
                    _ = IniciarSalidaAsync();
                    return true;

                case Resource.Id.final_salidas:
                    _ = FinalizarSalidaAsync(IdConse);
                    return true;

                default:
                    return base.OnOptionsItemSelected(item);
            }
        }

        /// <summary>Pide chofer y operador. Devuelve null si el usuario cancela.</summary>
        private Task<(string Chofer, string Operador)?> PedirChoferOperadorAsync()
        {
            var tcs = new TaskCompletionSource<(string, string)?>();

            Activity.RunOnUiThread(() =>
            {
                var layout = new LinearLayout(_activity) { Orientation = Android.Widget.Orientation.Vertical };
                layout.SetPadding(48, 24, 48, 0);

                var txtChofer = new EditText(_activity) { Hint = "Chofer" };
                var txtOperador = new EditText(_activity) { Hint = "Operador" };
                layout.AddView(txtChofer);
                layout.AddView(txtOperador);

                var dialog = new Android.App.AlertDialog.Builder(_activity)
                    .SetTitle("Salida de envases")
                    .SetView(layout)
                    .SetPositiveButton("Aceptar", (IDialogInterfaceOnClickListener)null)
                    .SetNegativeButton("Cancelar", (s, e) => tcs.TrySetResult(null))
                    .SetCancelable(false)
                    .Create();

                dialog.Show();

                // Sobrescribir el click para validar sin cerrar el diálogo
                dialog.GetButton((int)DialogButtonType.Positive).Click += (s, e) =>
                {
                    string chofer = txtChofer.Text?.Trim() ?? "";
                    string operador = txtOperador.Text?.Trim() ?? "";
                    if (chofer.Length == 0) { txtChofer.Error = "Requerido"; return; }
                    if (operador.Length == 0) { txtOperador.Error = "Requerido"; return; }
                    dialog.Dismiss();
                    tcs.TrySetResult((chofer, operador));
                };
            });

            return tcs.Task;
        }

        /// <summary>
        /// Replica BtnSplitPed_Click de SistemaEnvases: encabezado + detalle en
        /// TB_SALIDAS_ENVASES / TB_DETSALIDAS_ENVASES y actualización de inventarios.
        /// Cantidad = bins leídos en la salida (Tb_RFID_Det). Corre dentro de la
        /// transacción recibida. Devuelve el folio, o 0 si no había bins.
        /// </summary>
        private static int GenerarSalidaEnvases(
            SqlConnection conn, SqlTransaction tx, int idConse,
            string provClave, string provNombre,
            string rchClave, string rchNombre,
            string tblClave, string tblNombre,
            string chofer, string operador, string usuario)
        {
            string envClave = AppSettings.EnvaseSalidaClave;
            string prodClave = AppSettings.ProductoSalidaClave;
            if (string.IsNullOrWhiteSpace(envClave) || string.IsNullOrWhiteSpace(prodClave))
                throw new InvalidOperationException(
                    "No están configuradas las claves de envase/producto para la salida de envases (AppSettings).");

            if (rchNombre == "Sin Ranchos Disponibles") rchNombre = "";
            if (tblNombre == "Sin Tablas Disponibles") tblNombre = "";

            int cantidad;
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Tb_RFID_Det WHERE IdConseInv = @Id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@Id", idConse);
                cantidad = Convert.ToInt32(cmd.ExecuteScalar());
            }
            if (cantidad <= 0) return 0;

            string envNombre, prodNombre;
            using (var cmd = new SqlCommand("SELECT TOP 1 RTRIM(env_nombre) FROM tb_cat_envases WHERE env_clave = @C", conn, tx))
            {
                cmd.Parameters.AddWithValue("@C", envClave);
                envNombre = cmd.ExecuteScalar()?.ToString()
                    ?? throw new InvalidOperationException($"El envase '{envClave}' no existe en tb_cat_envases.");
            }
            using (var cmd = new SqlCommand("SELECT TOP 1 RTRIM(prod_nombre) FROM tb_cat_producto WHERE prod_clave = @C", conn, tx))
            {
                cmd.Parameters.AddWithValue("@C", prodClave);
                prodNombre = cmd.ExecuteScalar()?.ToString()
                    ?? throw new InvalidOperationException($"El producto '{prodClave}' no existe en tb_cat_producto.");
            }

            DateTime ahora;
            using (var cmd = new SqlCommand("SELECT SYSDATETIME()", conn, tx))
                ahora = Convert.ToDateTime(cmd.ExecuteScalar());
            DateTime fechaSalida = ahora.Date;

            int folio;
            using (var cmd = new SqlCommand(@"
                INSERT INTO TB_SALIDAS_ENVASES(FECHA, PROV_CLAVE, PROV_NOMBRE, RCH_CLAVE, RCH_NOMBRE,
                    TBL_CLAVE, TBL_NOMBRE, NOM_CHOFER, NOM_OPERADOR, SAL_STATUS, FECHA_GUARDADO)
                VALUES(@Fecha, @ProvClave, @ProvNombre, @RchClave, @RchNombre,
                    @TblClave, @TblNombre, @Chofer, @Operador, 'T', @FechaGuardado);
                SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tx))
            {
                cmd.Parameters.AddWithValue("@Fecha", fechaSalida);
                cmd.Parameters.AddWithValue("@ProvClave", provClave ?? "");
                cmd.Parameters.AddWithValue("@ProvNombre", provNombre ?? "");
                cmd.Parameters.AddWithValue("@RchClave", rchClave ?? "");
                cmd.Parameters.AddWithValue("@RchNombre", rchNombre ?? "");
                cmd.Parameters.AddWithValue("@TblClave", tblClave ?? "");
                cmd.Parameters.AddWithValue("@TblNombre", tblNombre ?? "");
                cmd.Parameters.AddWithValue("@Chofer", chofer);
                cmd.Parameters.AddWithValue("@Operador", operador);
                cmd.Parameters.AddWithValue("@FechaGuardado", ahora.ToString("dd/MM/yyyy HH:mm:ss"));
                folio = Convert.ToInt32(cmd.ExecuteScalar());
            }

            using (var cmd = new SqlCommand(@"
                INSERT INTO TB_DETSALIDAS_ENVASES(FOLIO, ENV_CLAVE, ENV_NOMBRE, CANTIDAD, PROD_CLAVE, PROD_NOMBRE)
                VALUES(@Folio, @EnvClave, @EnvNombre, @Cantidad, @ProdClave, @ProdNombre)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@Folio", folio);
                cmd.Parameters.AddWithValue("@EnvClave", envClave);
                cmd.Parameters.AddWithValue("@EnvNombre", envNombre);
                cmd.Parameters.AddWithValue("@Cantidad", cantidad);
                cmd.Parameters.AddWithValue("@ProdClave", prodClave);
                cmd.Parameters.AddWithValue("@ProdNombre", prodNombre);
                cmd.ExecuteNonQuery();
            }

            void Exec(string sql, params (string, object)[] ps)
            {
                using var cmd = new SqlCommand(sql, conn, tx);
                foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
                cmd.ExecuteNonQuery();
            }

            Exec(@"UPDATE TB_MSTR_ENVASES SET cant_salidas = (cant_salidas + @Cant)
                   WHERE prov_clave = @P AND rch_clave = @R AND tbl_clave = @T AND env_clave = @E",
                ("@Cant", cantidad), ("@P", provClave ?? ""), ("@R", rchClave ?? ""), ("@T", tblClave ?? ""), ("@E", envClave));
            Exec("UPDATE TB_MSTR_INV_ENVASES SET ENV_INV_CANT = (ENV_INV_CANT - @Cant) WHERE ENV_CLAVE = @E",
                ("@Cant", cantidad), ("@E", envClave));
            Exec("UPDATE TB_MSTR_INV_ENVASES_dos SET ENV_SAL_CANT = (ENV_SAL_CANT + @Cant) WHERE ENV_CLAVE = @E AND ENV_FECHA = @F",
                ("@Cant", cantidad), ("@E", envClave), ("@F", fechaSalida));
            Exec("UPDATE TB_MSTR_INV_ENVASES_sin_corte SET ENV_SAL_CANT = (ENV_SAL_CANT + @Cant) WHERE ENV_CLAVE = @E AND ENV_FECHA = @F",
                ("@Cant", cantidad), ("@E", envClave), ("@F", fechaSalida));

            Exec(@"INSERT INTO tb_registro_movimientos
                   (fecha, nom_compu, nom_usu, tipo_mov, op_clave, folio, detalle, sistema, mov_folio)
                   VALUES(@Fecha, @Maq, @Usu, 'SALIDA', '2.18', @Folio,
                          @Det, 'SISGAB', @Folio)",
                ("@Fecha", DateTime.Now), ("@Maq", Android.OS.Build.Model ?? "RFID"),
                ("@Usu", usuario ?? ""), ("@Folio", folio),
                ("@Det", $"INSERCCION DE SALIDA FECHA {fechaSalida:dd/MM/yyyy} (RFID #{idConse})"));

            return folio;
        }

        /// <summary>Muestra el folio de la salida de envases de forma destacada.</summary>
        private void MostrarFolioEnvases(int folio)
        {
            var layout = new LinearLayout(_activity) { Orientation = Android.Widget.Orientation.Vertical };
            layout.SetPadding(48, 32, 48, 16);
            layout.SetGravity(GravityFlags.CenterHorizontal);

            string titulo, leyenda, valor;
            if (AppSettings.ModoPrueba)
            {
                titulo = "Salida finalizada";
                leyenda = "Modo prueba: la salida de envases no se guardó.";
                valor = "—";
            }
            else if (folio > 0)
            {
                titulo = "Salida finalizada";
                leyenda = "Folio de salida de envases";
                valor = folio.ToString();
            }
            else
            {
                titulo = "Salida finalizada";
                leyenda = "No se generó salida de envases (sin bins leídos).";
                valor = "—";
            }

            var lbl = new TextView(_activity) { Text = leyenda, TextSize = 16f, Gravity = GravityFlags.Center };
            var num = new TextView(_activity) { Text = valor, TextSize = 44f, Gravity = GravityFlags.Center };
            num.SetTypeface(num.Typeface, Android.Graphics.TypefaceStyle.Bold);
            num.SetTextIsSelectable(true);
            layout.AddView(lbl);
            layout.AddView(num);

            new Android.App.AlertDialog.Builder(_activity)
                .SetTitle(titulo)
                .SetView(layout)
                .SetPositiveButton("Aceptar", (s, e) => { })
                .SetCancelable(false)
                .Show();
        }

        private async Task FinalizarSalidaAsync(int idConse)
        {
            var datos = await PedirChoferOperadorAsync();
            if (datos == null) return; // el usuario canceló; la salida sigue abierta
            string chofer = datos.Value.Chofer;
            string operador = datos.Value.Operador;

            // Capturar antes de que la UI limpie los campos
            string provC = prov_clave, provN = prov_nombre;
            string rchC = rch_clave, rchN = rch_nombre;
            string tblC = tbl_clave, tblN = tbl_nombre;
            string usuario = _activity.usuario;

            Activity.RunOnUiThread(() =>
            {
                loadingOverlay.Visibility = ViewStates.Visible;
                btnGuardar.Enabled = false;
            });

            int folioEnvases = 0;

            try
            {
                // ✅ Transacción atómica
                await Task.Run(() =>
                {
                    using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                    conn.Open();
                    using SqlTransaction transaction = conn.BeginTransaction();

                    try
                    {
                        // 1. Cerrar Salida
                        const string queryCierre = @"UPDATE Tb_RFID_Mstr 
                                             SET HoraCierre = GETDATE() 
                                             WHERE IdConse = @IdConse AND Mstr_Status = 'A'";
                        using (SqlCommand cmdCierre = new SqlCommand(queryCierre, conn, transaction))
                        {
                            cmdCierre.Parameters.AddWithValue("@IdConse", idConse);
                            int filasCierre = cmdCierre.ExecuteNonQuery();
                            if (filasCierre == 0)
                                throw new InvalidOperationException("La salida ya estaba cerrada o no existe.");
                        }

                        // 2. Actualizar Catálogo
                        const string queryCat = @"UPDATE c 
                                          SET c.FechaUltimoMovimiento = d.FechaCaptura 
                                          FROM Tb_RFID_Catalogo c 
                                          INNER JOIN Tb_RFID_Det d ON c.IdClaveInt = d.IdClaveInt 
                                          WHERE d.IdConseInv = @IdConseInv";
                        using (SqlCommand cmdCat = new SqlCommand(queryCat, conn, transaction))
                        {
                            cmdCat.Parameters.AddWithValue("@IdConseInv", idConse);
                            cmdCat.ExecuteNonQuery();
                        }

                        // 3. Salida de envases (SistemaEnvases) en la misma transacción.
                        //    En modo prueba se revierte solo este bloque con un savepoint.
                        if (AppSettings.ModoPrueba) transaction.Save("sp_envases");
                        folioEnvases = GenerarSalidaEnvases(conn, transaction, idConse,
                            provC, provN, rchC, rchN, tblC, tblN, chofer, operador, usuario);
                        if (AppSettings.ModoPrueba) transaction.Rollback("sp_envases");

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw; // Lanza para que el catch exterior muestre el diálogo
                    }
                });

                // Si todo fue bien, limpiar UI
                LimpiarSesionSalida();

                Activity.RunOnUiThread(() =>
                {
                    sprProveedor.SetSelection(0);
                    sprProveedor.Enabled = true;
                    sprRancho.SetSelection(0);
                    sprRancho.Enabled = true;
                    sprTabla.SetSelection(0);
                    sprTabla.Enabled = true;
                    btnGuardar.Enabled = false;

                    prov_clave = ""; rch_clave = ""; tbl_clave = "";
                    prov_nombre = ""; rch_nombre = ""; tbl_nombre = "";

                    _activity.EnableNavigationItems(
                        Resource.Id.navigation_entradas,
                        Resource.Id.navigation_inventario,
                        Resource.Id.navigation_salidas);

                    _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                    _menu?.FindItem(Resource.Id.final_salidas)?.SetEnabled(false);

                    ClearGridView();
                    totalAcumuladoINT = 0;
                    txtTotalAcumulado.Text = "0";
                    loadingOverlay.Visibility = ViewStates.Gone;

                    MostrarFolioEnvases(folioEnvases);
                });
            }
            catch (Exception ex)
            {
                Activity.RunOnUiThread(() =>
                {
                    loadingOverlay.Visibility = ViewStates.Gone;
                    btnGuardar.Enabled = true;
                    MainActivity.ShowDialog("Error al Cerrar",
                        $"No se pudo finalizar la salida:\n{ex.Message}\n\nIntente nuevamente.");
                });
            }
        }
        #endregion

        #region INICIAR SALIDA
        /// <summary>
        /// Wrapper que espera el resultado de InsertarSalidaAsync antes de actualizar la UI.
        /// Fix A: la versión anterior usaba fire-and-forget, dejando la UI en estado
        /// "en curso" aunque el INSERT hubiera fallado (IdConse = -1).
        /// </summary>
        private async Task IniciarSalidaAsync()
        {
            if (string.IsNullOrWhiteSpace(prov_clave) || string.IsNullOrWhiteSpace(rch_clave) || string.IsNullOrWhiteSpace(tbl_clave))
            {
                MainActivity.ShowDialog("Datos incompletos",
                    "No se pudo determinar la clave de proveedor, rancho o tabla. Vuelva a seleccionarlos.");
                return;
            }

            int id = await InsertarSalidaAsync(
                tipoMovimiento,
                ((MainActivity)Activity).usuario,
                prov_clave, rch_clave, tbl_clave, "A");

            if (id <= 0) return; // InsertarSalidaAsync ya mostró el diálogo de error

            _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
            _menu?.FindItem(Resource.Id.final_salidas)?.SetEnabled(true);
            btnGuardar.Enabled = true;
            _activity.DisableNavigationItems(
                Resource.Id.navigation_entradas,
                Resource.Id.navigation_inventario,
                Resource.Id.navigation_salidas);
        }

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
        /// Fix C: agrega _isLoadingFlete para bloquear handlers durante SetSelection.
        /// Fix F: valida que cada spinner se cargó correctamente antes de bloquear UI.
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

            try
            {
                _isLoadingFlete = true;

                // Desconectar handlers para evitar diálogos y queries durante SetSelection
                sprProveedor.ItemSelected -= sprProveedor_ItemSelected;
                sprRancho.ItemSelected -= sprRancho_ItemSelected;
                sprTabla.ItemSelected -= sprTabla_ItemSelected;

                // Paso 1: proveedor (ya cargado por LoadProveedorAsync en OnViewCreated)
                int posProveedor = EncontrarPosicionEnDataTable(vwProveedor, "prov_clave", savedProv);
                if (posProveedor <= 0)
                    throw new Exception($"Proveedor '{savedProv}' no encontrado al restaurar.");
                sprProveedor.SetSelection(posProveedor);

                // Paso 2: ranchos del proveedor
                await LoadRanchosPorProveedorAsync(savedProv);
                int posRancho = EncontrarPosicionEnDataTable(vwRanchos, "rch_clave", savedRancho);
                if (posRancho <= 0)
                    throw new Exception($"Rancho '{savedRancho}' no encontrado al restaurar.");
                sprRancho.SetSelection(posRancho);

                // Paso 3: tablas del rancho
                await LoadTablasPorRanchoAsync(savedRancho, savedProv);
                int posTabla = EncontrarPosicionEnDataTable(vwTablas, "tbl_clave", savedTabla);
                if (posTabla <= 0)
                    throw new Exception($"Tabla '{savedTabla}' no encontrada al restaurar.");
                sprTabla.SetSelection(posTabla);

                // Restaurar variables de estado
                IdConse = savedId;
                prov_clave = vwProveedor.Rows[posProveedor - 1]["prov_clave"].ToString().Trim();
                prov_nombre = vwProveedor.Rows[posProveedor - 1]["prov_nombre"].ToString().Trim();
                rch_clave = vwRanchos.Rows[posRancho - 1]["rch_clave"].ToString().Trim();
                rch_nombre = vwRanchos.Rows[posRancho - 1]["rch_nombre"].ToString().Trim();
                tbl_clave = vwTablas.Rows[posTabla - 1]["tbl_clave"].ToString().Trim();
                tbl_nombre = vwTablas.Rows[posTabla - 1]["tbl_nombre"].ToString().Trim();

                // UI: entrada ya en curso → bloquear spinners, habilitar guardar
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

                // Reconectar handlers
                sprProveedor.ItemSelected += sprProveedor_ItemSelected;
                sprRancho.ItemSelected += sprRancho_ItemSelected;
                sprTabla.ItemSelected += sprTabla_ItemSelected;
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al restaurar sesión de salida: {ex.Message}");
                Toast.MakeText(_activity, "Error al restaurar la salida. Se cancelará la sesión.", ToastLength.Long).Show();
                LimpiarSesionSalida();

                sprProveedor.ItemSelected += sprProveedor_ItemSelected;
                sprRancho.ItemSelected += sprRancho_ItemSelected;
                sprTabla.ItemSelected += sprTabla_ItemSelected;
            }
            finally
            {
                // Fix C: PostDelayed para que los eventos encolados por SetSelection()
                // se drenen con la guardia activa antes de liberar.
                new Android.OS.Handler(Android.OS.Looper.MainLooper).PostDelayed(
                    () => _isLoadingFlete = false, 200);
            }
        }

        /// <summary>
        /// Busca la posición (+1 por el placeholder) de un valor en un DataTable.
        /// Fix D: versión anterior dependía del adapter del Spinner para la búsqueda final,
        /// causando falsos negativos si el adapter fue reconstruido o había diferencias de espacios.
        /// Esta versión busca directamente en el DataTable, sin tocar el Spinner.
        /// </summary>
        private int EncontrarPosicionEnDataTable(DataTable tabla, string campoClave, string valorClave)
        {
            if (tabla == null || string.IsNullOrEmpty(valorClave)) return -1;

            if (tabla.Columns[campoClave] == null)
            {
                Log.Warn(TAG, $"EncontrarPosicionEnDataTable: columna '{campoClave}' no existe en {tabla.TableName}");
                return -1;
            }

            string valorBuscar = valorClave.Trim();

            for (int i = 0; i < tabla.Rows.Count; i++)
            {
                object cell = tabla.Rows[i][campoClave];
                if (cell == null || cell == DBNull.Value) continue;

                string cellStr = cell.ToString().Trim();

                if (string.Equals(cellStr, valorBuscar, StringComparison.OrdinalIgnoreCase))
                    return i + 1; // +1 por el placeholder "Seleccione..."

                // Normalización numérica: "03" == "3"
                if (int.TryParse(valorBuscar, out int vInt) && int.TryParse(cellStr, out int cInt) && vInt == cInt)
                    return i + 1;
            }

            Log.Warn(TAG, $"EncontrarPosicionEnDataTable: '{campoClave}'='{valorClave}' no encontrado en {tabla.TableName} ({tabla.Rows.Count} filas)");
            return -1;
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            if (disposing)
            {
                _isDisposed = true;
                try { if (_activity?.baseReader?.Action == ActionState.Inventory6c) _activity.baseReader.RfidUhf?.Stop(); } catch { }
                try { _activity?.baseReader?.RemoveListener(this); _activity?.baseReader?.RfidUhf?.RemoveListener(this); } catch { }
                try { restoreGunKeyCode(); } catch { }

                lock (_soundLock) { try { soundPool?.Release(); soundPool = null; } catch { } }

                try
                {
                    if (gvObject != null) { gvObject.Adapter = null; gvObject.Dispose(); gvObject = null; }
                    adapter?.Dispose(); adapter = null;
                    tagEPCList?.Clear(); tagEPCList = null;
                    _epcLeidosSet?.Clear();
                    vwProveedor?.Clear(); vwRanchos?.Clear(); vwTablas?.Clear();
                }
                catch { }
            }
        }

        public override void OnDestroy()
        {
            Dispose(true);
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

            UpdateText(IDType.TagRSSI, rssi.ToString());

            // ✅ 1. Duplicados O(1) en hilo de fondo (Thread-safe)
            if (!_epcLeidosSet.TryAdd(tag, 0)) return;

            // ✅ 2. Validación de catálogo en hilo de fondo (no bloquea UI)
            if (!validaEPC(tag))
            {
                _epcLeidosSet.TryRemove(tag, out _);
                return;
            }

            // ✅ 3. Actualizar UI solo si pasó las pruebas anteriores
            _activity.RunOnUiThread(() =>
            {
                if (_isDisposed || tagEPCList == null) return;

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
            });
        }
        #endregion

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
            if (_isLoadingFlete) return; // Fix C: ignorar durante restauración/carga programática
            try
            {
                if (e.Position == 0)
                {
                    prov_clave = "";
                    prov_nombre = "";
                    rch_clave = "";
                    tbl_clave = "";
                    _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                    return;
                }

                Spinner spinner = (Spinner)sender;
                prov_nombre = spinner.GetItemAtPosition(e.Position)?.ToString()?.Trim() ?? "";
                prov_clave = getProv_Clave(prov_nombre);
                rch_clave = "";
                tbl_clave = "";

                // Resetear spinners dependientes
                Spinner spinnerRancho = vwSalidas.FindViewById<Spinner>(Resource.Id.sprRancho);
                Spinner spinnerTabla = vwSalidas.FindViewById<Spinner>(Resource.Id.sprTabla);
                spinnerRancho.SetSelection(0);
                spinnerTabla.SetSelection(0);

                _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                _ = LoadRanchosPorProveedorAsync(prov_clave);
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"sprProveedor_ItemSelected: {ex.Message}");
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
                if (_isDisposed || vwSalidas == null) return; // ✅ Protección si el usuario sale del fragmento mientras carga
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
            if (_isLoadingFlete) return;
            try
            {
                if (e.Position == 0)
                {
                    rch_clave = "";
                    rch_nombre = "";
                    tbl_clave = "";
                    _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                    return;
                }

                Spinner spinner = (Spinner)sender;
                rch_nombre = spinner.GetItemAtPosition(e.Position)?.ToString()?.Trim() ?? "";
                rch_clave = getRch_Clave(rch_nombre);
                tbl_clave = "";

                Spinner spinnerTabla = vwSalidas.FindViewById<Spinner>(Resource.Id.sprTabla);
                spinnerTabla.SetSelection(0);

                _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                _ = LoadTablasPorRanchoAsync(rch_clave, prov_clave);
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"sprRancho_ItemSelected: {ex.Message}");
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
                if (_isDisposed || vwSalidas == null) return; // ✅ Protección si el usuario sale del fragmento mientras carga
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
            if (_isLoadingFlete) return;
            try
            {
                if (e.Position == 0)
                {
                    tbl_clave = "";
                    tbl_nombre = "";
                    _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                    return;
                }

                Spinner spinner = (Spinner)sender;
                tbl_nombre = spinner.GetItemAtPosition(e.Position)?.ToString()?.Trim() ?? "";
                // Resolver por posición (no por nombre): hay nombres de tabla duplicados
                // dentro de un mismo rancho (p. ej. "7 HERMANOS -6" = claves 01 y 76).
                int idxTabla = e.Position - 1;
                tbl_clave = (idxTabla >= 0 && idxTabla < vwTablas.Rows.Count)
                    ? vwTablas.Rows[idxTabla]["tbl_clave"].ToString().Trim()
                    : getTbl_Clave(tbl_nombre);
                Log.Debug(TAG, $"Tabla seleccionada: '{tbl_nombre}' clave='{tbl_clave}'");
                _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(true);
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"sprTabla_ItemSelected: {ex.Message}");
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
            // ✅ Prevenir múltiples suscripciones (patrón aprendido de InventarioFragment)
            btnGuardar.Click -= BtnGuardarInventario_Click;
            btnGuardar.Click += BtnGuardarInventario_Click;
        }

        // ✅ CREAR este método nuevo por separado (mejor que lambda anónima para evitar memory leaks)
        private async void BtnGuardarInventario_Click(object sender, EventArgs e)
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
                // ✅ 1. Snapshot con deduplicación ANTES de pasar a hilo de fondo
                List<TagLeido> snapshot = tagEPCList
                    .GroupBy(t => t.EPC)
                    .Select(g => g.First())
                    .ToList();

                // ✅ 2. INSERT en background iterando el SNAPSHOT
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

                            // ✅ CORREGIDO: Iterar 'snapshot' en lugar de 'tagEPCList'
                            foreach (var tag in snapshot)
                            {
                                cmd.Parameters["@IdClaveTag"].Value = tag.EPC;
                                insertados += cmd.ExecuteNonQuery();
                            }
                        }
                    }
                    return insertados;
                });

                // Validaciones de UI después del INSERT
                if (registrosInsertados > 0)
                {
                    if (sprProveedor.SelectedItemPosition <= 0) { MainActivity.ShowToast("Seleccione un Proveedor."); return; }
                    if (sprRancho.SelectedItemPosition <= 0) { MainActivity.ShowToast("Seleccione un Rancho."); return; }
                    if (sprTabla.SelectedItemPosition <= 0) { MainActivity.ShowToast("Seleccione una Tabla."); return; }

                    await ActualizarCatalogoTagsAsync(
                        snapshot, 
                        "S", 
                        _activity.usuario, 
                        null, 
                        prov_clave, 
                        rch_clave, 
                        int.Parse(_activity.idUnidadNegocio), 
                        24, 
                        "E", 
                        null,
                        AppSettings.ModoPrueba);
                }

                totalAcumuladoINT += registrosInsertados;
                txtTotalAcumulado.Text = totalAcumuladoINT.ToString();

                MainActivity.ShowDialog("INFORMACIÓN ALMACENADA", $"Se han guardado {registrosInsertados} registros exitosamente.");
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
        }

        public async Task<int> ActualizarCatalogoTagsAsync(
    List<TagLeido> tags,
    string tipoMovimiento,
    string usuario,
    string invArea,
    string provClave,
    string ranClave,
    int? idUnidadNegocio,
    int? idUbicacion,
    string tipoUbicacion,
    int? idFlete,
    bool modoPrueba = false)
        {
            if (tags == null || tags.Count == 0)
                return 0;

            const string query = @"
        UPDATE Tb_RFID_Catalogo
        SET
            FechaUltimoMovimiento = @Fecha,
            Tipo = @TipoMovimiento,
            Usuario = @Usuario,

            InvArea = CASE WHEN @TipoMovimiento = 'I' THEN @InvArea ELSE NULL END,
            Prov_Clave = CASE WHEN @TipoMovimiento IN ('E','S') THEN @ProvClave ELSE NULL END,
            Ran_Clave = CASE WHEN @TipoMovimiento IN ('E','S') THEN @RanClave ELSE NULL END,

            IdUnidadNegocioActual = @IdUnidadNegocio,
            IdUbicacionActual = @IdUbicacion,
            TipoUbicacion = @TipoUbicacion,

            id_flete = CASE WHEN @TipoMovimiento = 'E' THEN @IdFlete ELSE NULL END
        WHERE IdClaveTag = @IdClaveTag";

            try
            {
                int actualizados = 0;

                using SqlConnection conn = new SqlConnection(MainActivity.cadenaConexion);
                await conn.OpenAsync();

                using SqlTransaction transaction = conn.BeginTransaction();

                try
                {
                    using SqlCommand cmd = new SqlCommand(query, conn, transaction);

                    cmd.Parameters.Add("@Fecha", SqlDbType.DateTime);
                    cmd.Parameters.Add("@TipoMovimiento", SqlDbType.Char);
                    cmd.Parameters.Add("@Usuario", SqlDbType.VarChar);
                    cmd.Parameters.Add("@InvArea", SqlDbType.VarChar);
                    cmd.Parameters.Add("@ProvClave", SqlDbType.VarChar);
                    cmd.Parameters.Add("@RanClave", SqlDbType.VarChar);
                    cmd.Parameters.Add("@IdUnidadNegocio", SqlDbType.Int);
                    cmd.Parameters.Add("@IdUbicacion", SqlDbType.Int);
                    cmd.Parameters.Add("@TipoUbicacion", SqlDbType.Char);
                    cmd.Parameters.Add("@IdFlete", SqlDbType.Int);
                    cmd.Parameters.Add("@IdClaveTag", SqlDbType.VarChar);

                    var snapshot = tags
                        .GroupBy(t => t.EPC)
                        .Select(g => g.First())
                        .ToList();

                    foreach (var tag in snapshot)
                    {
                        cmd.Parameters["@Fecha"].Value = tag.FechaLectura;
                        cmd.Parameters["@TipoMovimiento"].Value = tipoMovimiento;
                        cmd.Parameters["@Usuario"].Value = usuario;

                        cmd.Parameters["@InvArea"].Value = (object?)invArea ?? DBNull.Value;
                        cmd.Parameters["@ProvClave"].Value = (object?)provClave ?? DBNull.Value;
                        cmd.Parameters["@RanClave"].Value = (object?)ranClave ?? DBNull.Value;

                        cmd.Parameters["@IdUnidadNegocio"].Value = (object?)idUnidadNegocio ?? DBNull.Value;
                        cmd.Parameters["@IdUbicacion"].Value = (object?)idUbicacion ?? DBNull.Value;
                        cmd.Parameters["@TipoUbicacion"].Value = (object?)tipoUbicacion ?? DBNull.Value;

                        cmd.Parameters["@IdFlete"].Value = (object?)idFlete ?? DBNull.Value;
                        cmd.Parameters["@IdClaveTag"].Value = tag.EPC;

                        actualizados += await cmd.ExecuteNonQueryAsync();
                    }

                    if (modoPrueba)
                        transaction.Rollback();
                    else
                        transaction.Commit();

                    return actualizados;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error actualizando catálogo: {ex}");
                return -1;
            }
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
                _epcLeidosSet.Clear(); // ✅ Agregado
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
