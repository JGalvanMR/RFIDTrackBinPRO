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
using Com.Unitech.Lib.Transport.Types;
using Com.Unitech.Lib.Types;
using Com.Unitech.Lib.Uhf;
using Com.Unitech.Lib.Uhf.Event;
using Com.Unitech.Lib.Uhf.Params;
using Com.Unitech.Lib.Uhf.Types;
using Com.Unitech.Lib.Util.Diagnotics;
using Google.Android.Material.FloatingActionButton;
using RFIDTrackBin.enums;
using RFIDTrackBin.Modal;
using RFIDTrackBin.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Exception = System.Exception;

namespace RFIDTrackBin.fragment
{
    public class VerificacionFragment : BaseFragment, IReaderEventListener, IRfidUhfEventListener, MainReceiver.IEventLitener, View.IOnTouchListener, IDisposable
    {
        static string TAG = typeof(VerificacionFragment).Name;

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
        private bool _isInventoryRunning = false;
        private DateTime _lastInventoryStart = DateTime.MinValue;
        private DateTime _lastInventoryStop = DateTime.MinValue;
        private const int INVENTORY_DEBOUNCE_MS = 500;
        private DateTime _lastTriggerTime = DateTime.MinValue;
        private Handler _stopHandler;
        private bool _stopPending = false;
        private CancellationTokenSource _inventoryCts;

        // Para optimizar actualizaciones del GridView - Thread-safe
        private readonly ConcurrentQueue<TagLeido> _pendingTags = new ConcurrentQueue<TagLeido>();
        private readonly Handler _uiHandler = new Handler(Looper.MainLooper);
        private readonly object _updateLock = new object();
        private bool _updateScheduled = false;
        private int totalCajasLeidasINT = 0;

        // Thread-safe HashSet para evitar duplicados
        private readonly ConcurrentDictionary<string, byte> _epcSet = new ConcurrentDictionary<string, byte>();

        // Carga del catálogo - Async sin bloqueo
        private Task _catalogoLoadTask;
        private CancellationTokenSource _catalogoCts;

        #region Views
        TextView connectedState;
        TextView areaLectura;
        TextView totalCajasLeidas;
        TextView txtTotalAcumulado;
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
        private myGVitemAdapter adapter;

        IMenu _menu;
        ProgressBar progressBar;
        RelativeLayout loadingOverlay;

        private FloatingActionButton fabScanManual;
        private bool _isScanManualActive = false;

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            return inflater.Inflate(Resource.Layout.VerificacionFragment, container, false);
        }

        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _catalogoCts = new CancellationTokenSource();
            _catalogoLoadTask = Task.Run(() => _activity?.getTb_RFID_Catalogo(), _catalogoCts.Token);
            _stopHandler = new Handler(Looper.MainLooper);
            _inventoryCts = new CancellationTokenSource();
        }

        public override void OnViewCreated(View view, Bundle savedInstanceState)
        {
            base.OnViewCreated(view, savedInstanceState);
            InitializeAsync(view);
        }

        private async void InitializeAsync(View view)
        {
            try
            {
                bool ok = await MainActivity.BtHelper.EnsureBluetoothAsync();
                if (!ok)
                {
                    Toast.MakeText(Activity, "Bluetooth es obligatorio para el inventario.", ToastLength.Short).Show();
                    return;
                }

                FindViews(view);
                InitializeSoundPool();

                HasOptionsMenu = true;

                mReceiver = new MainReceiver(this);
                IntentFilter filter = new IntentFilter();
                filter.AddAction(MainReceiver.rfidGunPressed);
                filter.AddAction(MainReceiver.rfidGunReleased);
                _activity.RegisterReceiver(mReceiver, filter);

                adapter = new myGVitemAdapter(_activity, tagEPCList);
                gvObject.Adapter = adapter;

                _activity.EnableNavigationItems(Resource.Id.navigation_entradas, Resource.Id.navigation_salidas);

                progressBar = view.FindViewById<ProgressBar>(Resource.Id.progressBarGuardar);
                loadingOverlay = view.FindViewById<RelativeLayout>(Resource.Id.loadingOverlay);

                // Configurar FAB según configuración
                if (MainActivity.UseManualScan)
                    fabScanManual.Visibility = ViewStates.Visible;
                else
                    fabScanManual.Visibility = ViewStates.Gone;

                fabScanManual.Click += FabScanManual_Click;
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error en InitializeAsync: {ex.Message}");
                MainActivity.ShowToast("Error al inicializar fragmento");
            }
        }

        private void FabScanManual_Click(object sender, EventArgs e)
        {
            if (_isScanManualActive)
            {
                DetenerInventario();
            }
            else
            {
                IniciarInventario();
            }
        }

        private void UpdateFabState(bool isScanning)
        {
            _activity.RunOnUiThread(() =>
            {
                if (fabScanManual == null) return;

                if (isScanning)
                {
                    fabScanManual.SetImageResource(Android.Resource.Drawable.IcMediaPause);
                    _isScanManualActive = true;
                }
                else
                {
                    fabScanManual.SetImageResource(Android.Resource.Drawable.IcMediaPlay);
                    _isScanManualActive = false;
                }
            });
        }

        private void PlayBeepSound()
        {
            lock (_soundLock)
            {
                if (beepSoundId != 0 && soundPool != null && !_isDisposed)
                {
                    try
                    {
                        soundPool.Play(beepSoundId, 1.0f, 1.0f, 0, 0, 1.0f);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(TAG, $"Error al reproducir sonido: {ex.Message}");
                    }
                }
            }
        }

        private void FindViews(View view)
        {
            connectedState = null;
            areaLectura = null;
            totalCajasLeidas = view.FindViewById<TextView>(Resource.Id.txtNumTotalCajas);
            txtTotalAcumulado = view.FindViewById<TextView>(Resource.Id.txtNumTotalAcumulado);
            gvObject = view.FindViewById<GridView>(Resource.Id.gvleidoVerificacion);
            fabScanManual = view.FindViewById<FloatingActionButton>(Resource.Id.fabScanManual);
        }

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

                    beepSoundId = soundPool.Load(_activity, Resource.Drawable.beep, 1);
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Error inicializando SoundPool: {ex.Message}");
                }
            }
        }

        #region CICLO DE VIDA
        public override void OnResume()
        {
            base.OnResume();
            ((AndroidX.AppCompat.App.AppCompatActivity)Activity).SupportActionBar.Title = "VERIFICACION";

            _activity?.OcultarElementosNavegacion();
            _activity.currentRfidFragment = this;

            if (mReceiver != null)
            {
                try
                {
                    IntentFilter filter = new IntentFilter();
                    filter.AddAction(MainReceiver.rfidGunPressed);
                    filter.AddAction(MainReceiver.rfidGunReleased);
                    _activity.RegisterReceiver(mReceiver, filter);
                }
                catch (Java.Lang.IllegalArgumentException) { }
                catch (Exception ex) { Log.Warn(TAG, $"RegisterReceiver: {ex.Message}"); }
            }

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
            DetenerInventarioInmediato();
            _stopHandler.RemoveCallbacksAndMessages(null);
            CancelPendingUpdates();
            base.OnPause();
            Log.Debug(TAG, "OnPause completado");
        }

        public override void OnDestroy()
        {
            Dispose(true);
            base.OnDestroy();
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

                // Cancelar tokens
                _inventoryCts?.Cancel();
                _catalogoCts?.Cancel();

                // Detener handlers
                _stopHandler?.RemoveCallbacksAndMessages(null);
                CancelPendingUpdates();

                // Detener inventario inmediatamente
                DetenerInventarioInmediato();

                // Remover listeners
                try
                {
                    _activity?.baseReader?.RemoveListener(this);
                    _activity?.baseReader?.RfidUhf?.RemoveListener(this);
                }
                catch { }

                // Unregister receiver
                try
                {
                    if (mReceiver != null)
                        _activity?.UnregisterReceiver(mReceiver);
                }
                catch { }

                // Liberar recursos de audio
                lock (_soundLock)
                {
                    try
                    {
                        soundPool?.Release();
                        soundPool = null;
                    }
                    catch { }
                }

                // Limpiar colecciones
                tagEPCList?.Clear();
                while (_pendingTags.TryDequeue(out _)) { }
                _epcSet?.Clear();

                // Liberar vistas
                try
                {
                    if (gvObject != null)
                    {
                        gvObject.Adapter = null;
                        gvObject.Dispose();
                        gvObject = null;
                    }
                    adapter?.Dispose();
                    adapter = null;
                    fabScanManual = null;
                }
                catch { }

                // Disponer tokens
                _inventoryCts?.Dispose();
                _catalogoCts?.Dispose();
            }
        }
        #endregion

        #region MenuInflater
        public override void OnCreateOptionsMenu(IMenu menu, MenuInflater inflater)
        {
            inflater.Inflate(Resource.Menu.menu_verificacion, menu);
            _menu = menu;
            menu.FindItem(Resource.Id.inicio_verificacion).SetEnabled(true);
            base.OnCreateOptionsMenu(menu, inflater);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.inicio_verificacion:
                    _menu?.FindItem(Resource.Id.inicio_verificacion)?.SetEnabled(true);
                    ClearGridView();
                    return true;
                default:
                    return base.OnOptionsItemSelected(item);
            }
        }
        #endregion

        #region RFID EVENTOS
        public void OnNotificationState(NotificationState state, Java.Lang.Object @params) { }

        public void OnReaderActionChanged(BaseReader reader, ResultCode retCode, ActionState state, Java.Lang.Object @params)
        {
            if (state == ActionState.Stop)
            {
                UpdateText(IDType.Inventory, GetString(Resource.String.inventory));
                UpdateFabState(false);
            }
            else if (state == ActionState.Inventory6c)
            {
                UpdateText(IDType.Inventory, GetString(Resource.String.stop));
                UpdateFabState(true);
            }
        }

        public void OnReaderBatteryState(BaseReader reader, int batteryState, Java.Lang.Object @params) { }

        public void OnReaderKeyChanged(BaseReader reader, KeyType type, KeyState state, Java.Lang.Object @params)
        {
            var now = DateTime.Now;
            if ((now - _lastTriggerTime).TotalMilliseconds < 200) return;
            _lastTriggerTime = now;

            if (_activity?.baseReader?.RfidUhf == null)
            {
                MainActivity.ShowToast("Lector no disponible");
                return;
            }

            if (type != KeyType.Trigger) return;

            if (state == KeyState.KeyDown)
            {
                _stopHandler.RemoveCallbacksAndMessages(null);
                _stopPending = false;

                if (_activity.baseReader.Action == ActionState.Stop)
                {
                    IniciarInventario();
                }
            }
            else if (state == KeyState.KeyUp)
            {
                if (_activity.baseReader.Action == ActionState.Inventory6c)
                {
                    if (!_stopPending)
                    {
                        _stopPending = true;
                        _stopHandler.PostDelayed(() =>
                        {
                            if (_stopPending && !_isDisposed)
                            {
                                DetenerInventario();
                                _stopPending = false;
                            }
                        }, 300);
                    }
                }
            }
        }

        private void IniciarInventario()
        {
            lock (_inventoryLock)
            {
                if (_isInventoryRunning || _isDisposed) return;
                if ((DateTime.Now - _lastInventoryStop).TotalMilliseconds < INVENTORY_DEBOUNCE_MS)
                    return;
                _isInventoryRunning = true;
                _lastInventoryStart = DateTime.Now;
                _inventoryCts = new CancellationTokenSource();
            }

            try
            {
                _activity.baseReader.RfidUhf.Inventory6c();
                _activity.baseReader.SetDisplayTags(new DisplayTags(ReadOnceState.Off, BeepAndVibrateState.On));
                Log.Debug(TAG, "Inventario iniciado");
                UpdateFabState(true);
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al iniciar inventario: {ex.Message}");
                lock (_inventoryLock) { _isInventoryRunning = false; }
                UpdateFabState(false);
            }
        }

        private void DetenerInventario()
        {
            lock (_inventoryLock)
            {
                if (!_isInventoryRunning || _isDisposed) return;
                if ((DateTime.Now - _lastInventoryStart).TotalMilliseconds < 200)
                    return;
                _isInventoryRunning = false;
                _lastInventoryStop = DateTime.Now;
                _inventoryCts?.Cancel();
            }

            try
            {
                if (_activity?.baseReader?.RfidUhf != null && _activity.baseReader.Action == ActionState.Inventory6c)
                {
                    _activity.baseReader.RfidUhf.Stop();
                    Log.Debug(TAG, "Inventario detenido");
                }
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al detener inventario: {ex.Message}");
            }
            finally
            {
                UpdateFabState(false);
            }
        }

        private void DetenerInventarioInmediato()
        {
            _stopHandler.RemoveCallbacksAndMessages(null);
            _stopPending = false;
            _inventoryCts?.Cancel();

            lock (_inventoryLock)
            {
                if (!_isInventoryRunning) return;
                _isInventoryRunning = false;
                _lastInventoryStop = DateTime.Now;
            }

            try
            {
                if (_activity?.baseReader?.RfidUhf != null && _activity.baseReader.Action == ActionState.Inventory6c)
                {
                    _activity.baseReader.RfidUhf.Stop();
                }
            }
            catch { }
        }

        private void CancelPendingUpdates()
        {
            lock (_updateLock)
            {
                _updateScheduled = false;
                _uiHandler.RemoveCallbacksAndMessages(null);
            }
        }

        public void OnReaderStateChanged(BaseReader reader, ConnectState state, Java.Lang.Object @params)
        {
            UpdateText(IDType.ConnectState, state.ToString());

            if (_activity?.baseReader?.RfidUhf != null)
            {
                try { _activity.baseReader.RfidUhf.RemoveListener(this); } catch { }
                _activity.baseReader.RfidUhf.AddListener(this);
            }

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
            if (_isDisposed || !IsAdded || _activity == null || tagEPCList == null) return;
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

            // Validación async sin bloquear
            _ = ProcessTagAsync(tag, rssi);
        }

        private async Task ProcessTagAsync(string tag, float rssi)
        {
            try
            {
                // Validar contra catálogo de forma async
                bool isValid = await ValidaEPCAsync(tag);
                if (!isValid) return;

                // Verificar duplicados de forma thread-safe
                if (!_epcSet.TryAdd(tag, 0))
                    return;

                var tagLeido = new TagLeido
                {
                    EPC = tag,
                    RSSI = rssi,
                    FechaLectura = DateTime.Now
                };

                _pendingTags.Enqueue(tagLeido);
                ScheduleGridUpdate();

                Interlocked.Increment(ref totalCajasLeidasINT);

                _activity.RunOnUiThread(() =>
                {
                    if (totalCajasLeidas != null && !_isDisposed)
                        totalCajasLeidas.Text = totalCajasLeidasINT.ToString();
                });

                UpdateText(IDType.TagRSSI, rssi.ToString());
                PlayBeepSound();
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error procesando tag: {ex.Message}");
            }
        }

        private void ScheduleGridUpdate()
        {
            lock (_updateLock)
            {
                if (_updateScheduled || _isDisposed) return;
                _updateScheduled = true;
            }

            _uiHandler.PostDelayed(() =>
            {
                try
                {
                    if (_isDisposed) return;

                    List<TagLeido> tagsToAdd = new List<TagLeido>();
                    while (_pendingTags.TryDequeue(out var tag))
                    {
                        tagsToAdd.Add(tag);
                    }

                    if (tagsToAdd.Count == 0) return;
                    if (tagEPCList == null || adapter == null) return;

                    _activity.RunOnUiThread(() =>
                    {
                        try
                        {
                            if (_isDisposed || tagEPCList == null || adapter == null) return;

                            tagEPCList.AddRange(tagsToAdd);
                            adapter.NotifyDataSetChanged();
                        }
                        catch (Exception ex)
                        {
                            Log.Error(TAG, $"Error actualizando UI: {ex.Message}");
                        }
                    });
                }
                finally
                {
                    lock (_updateLock)
                    {
                        _updateScheduled = false;
                    }
                }
            }, 200);
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
                Log.Error(TAG, $"Error en InitSetting: {e.Message}");
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
                catch (ReaderException) { throw; }
            }
        }

        public void UpdateText(IDType id, string data)
            => Utilities.UpdateUIText(FragmentType.Verificacion, (int)id, data);

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
                try
                {
                    string keyName = "", keyCode = "";
                    switch (Build.Device)
                    {
                        case "HT730": keyName = "TRIGGER_GUN"; keyCode = "298"; break;
                        case "PA768": keyName = "SCAN_GUN"; keyCode = "294"; break;
                        default: return;
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
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Error en setUseGunKeyCode: {ex.Message}");
                }
            });
        }

        private void restoreGunKeyCode()
        {
            if (tempKeyCode == null) return;
            Task.Run(() =>
            {
                try
                {
                    Bundle result = KeymappingCtrl.GetInstance(
                        MainActivity.getInstance().ApplicationContext).ImportKeyMappings(getKeymappingPath());
                    if (result.GetInt("errorCode") == 0) Log.Debug(TAG, "restoreGunKeyCode success");
                    else Log.Error(TAG, "restoreGunKeyCode failed: " + result.GetString("errorMsg"));
                    tempKeyCode = null;
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Error en restoreGunKeyCode: {ex.Message}");
                }
            });
        }

        public void ClearGridView()
        {
            _activity.RunOnUiThread(() =>
            {
                try
                {
                    tagEPCList?.Clear();
                    _epcSet?.Clear();
                    while (_pendingTags.TryDequeue(out _)) { }
                    adapter?.NotifyDataSetChanged();
                    totalCajasLeidasINT = 0;
                    if (totalCajasLeidas != null)
                        totalCajasLeidas.Text = "0";
                }
                catch (Exception ex)
                {
                    Log.Error(TAG, $"Error en ClearGridView: {ex.Message}");
                }
            });
        }

        public bool OnTouch(View v, MotionEvent e) => false;

        #region VALIDAR TAG VS CATÁLOGO
        private async Task<bool> ValidaEPCAsync(string EPC)
        {
            try
            {
                // Esperar carga del catálogo de forma async (sin bloquear UI)
                if (_catalogoLoadTask != null && !_catalogoLoadTask.IsCompleted)
                {
                    await Task.WhenAny(_catalogoLoadTask, Task.Delay(2000));
                }

                if (_activity.CatalogoEPCSet == null || _activity.CatalogoEPCSet.Count == 0)
                    return true;

                return _activity.CatalogoEPCSet.Contains(EPC.Trim());
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error en ValidaEPCAsync: {ex.Message}");
                return true; // Fallback: permitir si hay error
            }
        }
        #endregion
    }
}
