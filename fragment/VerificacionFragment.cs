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
using RFIDTrackBin.enums;
using RFIDTrackBin.Modal;
using RFIDTrackBin.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Exception = System.Exception;

namespace RFIDTrackBin.fragment
{
    public class VerificacionFragment : BaseFragment, IReaderEventListener, IRfidUhfEventListener, MainReceiver.IEventLitener, View.IOnTouchListener
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

        // Para optimizar actualizaciones del GridView
        private readonly List<TagLeido> _pendingTags = new List<TagLeido>();
        private readonly Handler _uiHandler = new Handler(Looper.MainLooper);
        private bool _updateScheduled = false;
        private int totalCajasLeidasINT = 0;

        // Carga del catálogo
        private Task _catalogoLoadTask;

        #region Views
        TextView connectedState;
        TextView areaLectura;
        TextView totalCajasLeidas;
        TextView txtTotalAcumulado;
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

        IMenu _menu;
        ProgressBar progressBar;
        RelativeLayout loadingOverlay;

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            return inflater.Inflate(Resource.Layout.VerificacionFragment, container, false);
        }

        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _catalogoLoadTask = Task.Run(() => _activity?.getTb_RFID_Catalogo());
            _stopHandler = new Handler(Looper.MainLooper);
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
        }

        private void PlayBeepSound()
        {
            if (beepSoundId != 0)
                soundPool.Play(beepSoundId, 1.0f, 1.0f, 0, 0, 1.0f);
        }

        private void FindViews(View view)
        {
            connectedState = null;
            areaLectura = null;
            totalCajasLeidas = view.FindViewById<TextView>(Resource.Id.txtNumTotalCajas);
            txtTotalAcumulado = view.FindViewById<TextView>(Resource.Id.txtNumTotalAcumulado);
            gvObject = view.FindViewById<GridView>(Resource.Id.gvleidoVerificacion);
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
            base.OnPause();
            Log.Debug(TAG, "OnPause completado");
        }

        public override void OnDestroy()
        {
            _isDisposed = true;
            _stopHandler.RemoveCallbacksAndMessages(null);
            DetenerInventarioInmediato();

            try
            {
                _activity?.baseReader?.RemoveListener(this);
                _activity?.baseReader?.RfidUhf?.RemoveListener(this);
            }
            catch { }

            try
            {
                _activity?.UnregisterReceiver(mReceiver);
            }
            catch { }

            try
            {
                soundPool?.Release();
                soundPool = null;
                tagEPCList?.Clear();
                tagEPCList = null;
                if (gvObject != null) { gvObject.Adapter = null; gvObject.Dispose(); gvObject = null; }
                adapter?.Dispose();
                adapter = null;
            }
            catch { }

            base.OnDestroy();
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

            if (_activity?.baseReader?.RfidUhf == null)
            {
                MainActivity.ShowToast("Lector no disponible");
                return;
            }

            if (type != KeyType.Trigger) return;

            if (state == KeyState.KeyDown)
            {
                // Cancelar cualquier parada pendiente
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
                    // Programar parada con un pequeño retraso para evitar rebotes
                    if (!_stopPending)
                    {
                        _stopPending = true;
                        _stopHandler.PostDelayed(() =>
                        {
                            if (_stopPending)
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
                if (_isInventoryRunning) return;
                if ((DateTime.Now - _lastInventoryStop).TotalMilliseconds < INVENTORY_DEBOUNCE_MS)
                    return;
                _isInventoryRunning = true;
                _lastInventoryStart = DateTime.Now;
            }

            try
            {
                _activity.baseReader.RfidUhf.Inventory6c();
                _activity.baseReader.SetDisplayTags(new DisplayTags(ReadOnceState.Off, BeepAndVibrateState.On));
                Log.Debug(TAG, "Inventario iniciado");
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error al iniciar inventario: {ex.Message}");
                lock (_inventoryLock) { _isInventoryRunning = false; }
            }
        }

        private void DetenerInventario()
        {
            lock (_inventoryLock)
            {
                if (!_isInventoryRunning) return;
                if ((DateTime.Now - _lastInventoryStart).TotalMilliseconds < 200) // Mínimo 200ms
                    return;
                _isInventoryRunning = false;
                _lastInventoryStop = DateTime.Now;
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
        }

        private void DetenerInventarioInmediato()
        {
            _stopHandler.RemoveCallbacksAndMessages(null);
            _stopPending = false;
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

            if (!ValidaEPC(tag)) return;

            lock (_pendingTags)
            {
                if (_pendingTags.Any(t => t.EPC == tag) || tagEPCList.Any(t => t.EPC == tag))
                    return;
                _pendingTags.Add(new TagLeido { EPC = tag, RSSI = rssi, FechaLectura = DateTime.Now });
            }

            ScheduleGridUpdate();

            Interlocked.Increment(ref totalCajasLeidasINT);
            _activity.RunOnUiThread(() =>
            {
                if (totalCajasLeidas != null)
                    totalCajasLeidas.Text = totalCajasLeidasINT.ToString();
            });

            UpdateText(IDType.TagRSSI, rssi.ToString());
        }

        //private void ScheduleGridUpdate()
        //{
        //    if (_updateScheduled) return;
        //    _updateScheduled = true;
        //    _uiHandler.PostDelayed(() =>
        //    {
        //        _updateScheduled = false;
        //        List<TagLeido> tagsToAdd;
        //        lock (_pendingTags)
        //        {
        //            if (_pendingTags.Count == 0) return;
        //            tagsToAdd = new List<TagLeido>(_pendingTags);
        //            _pendingTags.Clear();
        //        }
        //        _activity.RunOnUiThread(() =>
        //        {
        //            if (_isDisposed || tagEPCList == null || adapter == null) return;
        //            tagEPCList.AddRange(tagsToAdd);
        //            adapter.NotifyDataSetChanged();
        //        });
        //    }, 200);
        //}
        private readonly HashSet<string> _epcSet = new HashSet<string>();

        private void ScheduleGridUpdate()
        {
            if (_updateScheduled)
                return;

            _updateScheduled = true;

            _uiHandler.PostDelayed(() =>
            {
                _updateScheduled = false;

                List<TagLeido> tagsToAdd;

                lock (_pendingTags)
                {
                    if (_pendingTags.Count == 0)
                        return;

                    tagsToAdd = new List<TagLeido>(_pendingTags);
                    _pendingTags.Clear();
                }

                if (_isDisposed || tagEPCList == null || adapter == null)
                    return;

                int added = 0;

                foreach (var tag in tagsToAdd)
                {
                    // evita duplicados
                    if (_epcSet.Add(tag.EPC))
                    {
                        tagEPCList.Add(tag);
                        added++;
                    }
                }

                if (added > 0)
                {
                    adapter.NotifyDataSetChanged();
                }

            }, 200); // batch update cada 200ms
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
                adapter?.NotifyDataSetChanged();
                totalCajasLeidasINT = 0;
                if (totalCajasLeidas != null)
                    totalCajasLeidas.Text = "0";
            });
        }

        public bool OnTouch(View v, MotionEvent e) => false;

        #region VALIDAR TAG VS CATÁLOGO
        private bool ValidaEPC(string EPC)
        {
            if (_catalogoLoadTask != null && !_catalogoLoadTask.IsCompleted)
            {
                _catalogoLoadTask.Wait(2000);
            }
            if (_activity.CatalogoEPCSet == null || _activity.CatalogoEPCSet.Count == 0)
                return true;
            return _activity.CatalogoEPCSet.Contains(EPC.Trim());
        }
        #endregion
    }
}