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
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Exception = System.Exception;

namespace RFIDTrackBin.fragment
{
    public class VerificacionFragment : BaseFragment, IReaderEventListener, IRfidUhfEventListener, MainReceiver.IEventLitener, View.IOnTouchListener
    {
        // FIX CRÍTICO #1: TAG usaba typeof(InventarioFragment) — todos los logs eran incorrectos
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
        private bool _isInventoryInProgress = false;
        private DateTime _lastTriggerTime = DateTime.MinValue;

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

        int totalCajasLeidasINT = 0;

        // FIX #7: Eliminado campo "SqlConnection thisConnection" que no se cerraba

        IMenu _menu;
        ProgressBar progressBar;
        RelativeLayout loadingOverlay;

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            return inflater.Inflate(Resource.Layout.VerificacionFragment, container, false);
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
            connectedState = view.FindViewById<TextView>(Resource.Id.txtConnectedStateInventario);
            areaLectura = view.FindViewById<TextView>(Resource.Id.txtAreaLecturaInventario);
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

            try { _activity?.baseReader?.RfidUhf?.Stop(); } catch { }
            try { _activity?.baseReader?.RemoveListener(this); } catch { }

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

            if (_activity?.baseReader?.RfidUhf == null)
            {
                MainActivity.ShowToast("Lector no disponible");
                return;
            }

            if (type != KeyType.Trigger) return;

            if (state == KeyState.KeyDown && _activity.baseReader.Action == ActionState.Stop)
            {
                lock (_inventoryLock)
                {
                    if (_isInventoryInProgress) return;

                    try
                    {
                        _isInventoryInProgress = true;
                        InitSetting();
                        _activity.baseReader.RfidUhf.Inventory6c();
                        _activity.baseReader.SetDisplayTags(new DisplayTags(ReadOnceState.Off, BeepAndVibrateState.On));
                    }
                    catch (Exception ex)
                    {
                        _isInventoryInProgress = false;
                        Log.Error(TAG, $"Error iniciando inventario: {ex.Message}");
                    }
                }
            }
            else if (state == KeyState.KeyUp && _activity.baseReader.Action == ActionState.Inventory6c)
            {
                try { _activity.baseReader.RfidUhf.Stop(); } catch { }
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

                // FIX #8: validaEPC usa HashSet O(1)
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

        public override void ReceiveHandler(Bundle bundle)
        {
            UpdateUIType updateUIType = (UpdateUIType)bundle.GetInt(ExtraName.Type);
            if (updateUIType == UpdateUIType.Text)
            {
                string data = bundle.GetString(ExtraName.Text);
                IDType idType = (IDType)bundle.GetInt(ExtraName.TargetID);
                // Nota: txtConnectedStateInventario puede ser null si no existe en el layout actual
                if (idType == IDType.ConnectState && connectedState != null)
                    connectedState.Text = data;
            }
        }

        private bool TryAssertReader() => _activity.TryAssertReader();

        private void DoStop()
        {
            try
            {
                if (_activity?.baseReader?.RfidUhf == null) return;
                if (_activity.baseReader.Action == ActionState.Inventory6c)
                {
                    _isFindTag = false;
                    _activity.baseReader.RfidUhf.Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Error en DoStop: {ex.Message}");
            }
            finally
            {
                _isInventoryInProgress = false;
            }
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
                catch (ReaderException e) { throw e; }
            }
        }

        // FIX CRÍTICO #2: UpdateText usaba FragmentType.Inventario — los mensajes iban al fragmento equivocado
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
                adapter?.NotifyDataSetChanged();
                totalCajasLeidasINT = 0;
                if (totalCajasLeidas != null)
                    totalCajasLeidas.Text = "0";
            });
        }

        public bool OnTouch(View v, MotionEvent e) => false;

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
