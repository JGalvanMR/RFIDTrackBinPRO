using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Com.Unitech.Api.Keymap;
using Com.Unitech.Lib.Diagnositics;
using Com.Unitech.Lib.Htx;
using Com.Unitech.Lib.Reader;
using Com.Unitech.Lib.Reader.Event;
using Com.Unitech.Lib.Reader.Params;
using Com.Unitech.Lib.Reader.Types;
using Com.Unitech.Lib.Rgx;
using Com.Unitech.Lib.Rpx;
using Com.Unitech.Lib.Transport;
using Com.Unitech.Lib.Transport.Types;
using Com.Unitech.Lib.Types;
using Com.Unitech.Lib.Uhf;
using Com.Unitech.Lib.Uhf.Event;
using Com.Unitech.Lib.Uhf.Params;
using Com.Unitech.Lib.Uhf.Types;
using Com.Unitech.Lib.Util.Diagnotics;
using Android.App;
using Android.Runtime;
using Com.Unitech.StuhflBridge;
using Java.Lang;
using Java.Util;
using Java.Util.Logging;

using RFIDTrackBin.enums;

using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using Exception = System.Exception;
using Math = Java.Lang.Math;
using StringBuilder = System.Text.StringBuilder;
using Thread = System.Threading.Thread;

namespace RFIDTrackBin.fragment
{
    public class HomeFragment : BaseFragment, IReaderEventListener, IRfidUhfEventListener, MainReceiver.IEventLitener
    {
        static string TAG = typeof(HomeFragment).Name;

        static string keymappingPath = "/storage/emulated/0/Android/data/com.unitech.unitechrfidsample";
        static string android12keymappingPath = "/storage/emulated/0/Unitech/unitechrfidsample/";
        static string systemUssTriggerScan = "unitech.scanservice.software_scankey";
        static string ExtraScan = "scan";

        public int MAX_MASK = 2;
        private int NIBLE_SIZE = 4;

        bool accessTagResult;

        private bool _isFindTag = false;

        private LinearLayout layoutDisplay;
        #region Button
        private Button buttonInfo;
        private Button buttonSettings;
        private Button buttonInventory;
        private Button buttonRead;
        private Button buttonWrite;
        private Button buttonLock;
        private Button buttonUnlock;
        private Button buttonFind;
        private Button buttonDisplay;
        #endregion

        #region Histogram
        private HistogramView histogramView;
        //private bool _isFindTag = false;
        private static int histogramSize = 12;
        private int histogramMax = -30;
        private int histogramMin = -60;
        private HistogramData histogramData = new HistogramData(histogramSize);
        #endregion

        LinearLayout layoutTagTID;
        Switch switchFastID;
        #region TextView
        TextView connectState;
        TextView temperature;
        TextView result;
        TextView tagEPC;
        TextView tagTID;
        TextView tagRSSI;
        TextView battery;
        TextView tagData;
        #endregion

        private EditText editDisplay;

        MainReceiver mReceiver;

        Bundle tempKeyCode = null;

        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // Create your fragment here
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            return inflater.Inflate(Resource.Layout.HomeFragment, container, false);
        }

        public override async void OnViewCreated(View view, Bundle savedInstanceState)
        {
            base.OnViewCreated(view, savedInstanceState);
            _activity.mainModel.deviceType = DeviceType.Rg768;

            FindViewById(view);

            if (_activity.mainModel.deviceType == DeviceType.Rp902)
            {
                histogramMax = -30;
                histogramMin = -60;
                layoutDisplay.Visibility = ViewStates.Visible;
                layoutTagTID.Visibility = ViewStates.Gone;
                switchFastID.Visibility = ViewStates.Gone;
            }
            else if (_activity.mainModel.deviceType == DeviceType.Ht730 || _activity.mainModel.deviceType == DeviceType.Rg768)
            {
                histogramMax = -45;
                histogramMin = -75;
                layoutDisplay.Visibility = ViewStates.Gone;
                layoutTagTID.Visibility = ViewStates.Visible;
                switchFastID.Visibility = ViewStates.Visible;
            }
            //Log.Error("TAG", "histogramMax:" + histogramMax + ", histogramMin:" + histogramMin);

            SetButtonClick();

            mReceiver = new MainReceiver(this);
            IntentFilter filter = new IntentFilter();
            filter.AddAction(MainReceiver.rfidGunPressed);
            filter.AddAction(MainReceiver.rfidGunReleased);
            _activity.RegisterReceiver(mReceiver, filter);

            //await Task.Run(ConnectTask);
        }

        public override void OnPause()
        {
            if (_activity.baseReader != null)
            {
                if (_activity.baseReader.Action != ActionState.Stop)
                {
                    _activity.baseReader.RfidUhf.Stop();
                }
            }

            try
            {
                _activity.UnregisterReceiver(mReceiver);
            }
            catch (Exception e)
            {

            }

            base.OnPause();
        }

        public override void OnDestroy()
        {
            restoreGunKeyCode();
            base.OnDestroy();
        }

        public void OnNotificationState(NotificationState state, Object @params)
        {
            //throw new System.NotImplementedException();
        }

        public void OnReaderActionChanged(BaseReader reader, ResultCode retCode, ActionState state, Object @params)
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
                    UpdateText(IDType.Inventory, GetString(Resource.String.inventory));
                    UpdateText(IDType.Find, GetString(Resource.String.find));
                }
            }
            catch (Exception e)
            {
                Log.Error(TAG, e.Message);
            }
        }

        public void OnReaderBatteryState(BaseReader reader, int batteryState, Object @params)
        {
            UpdateText(IDType.Battery, batteryState.ToString());
        }

        public void OnReaderKeyChanged(BaseReader reader, KeyType type, KeyState state, Object @params)
        {
            if (type == KeyType.Trigger)
            {
                if (state == KeyState.KeyDown && _activity.baseReader.Action == ActionState.Stop)
                {
                    DoInventory();
                }
                else if (state == KeyState.KeyUp && _activity.baseReader.Action == ActionState.Inventory6c)
                {
                    DoStop();
                }
            }
        }

        public void OnReaderStateChanged(BaseReader reader, ConnectState state, Object @params)
        {
            UpdateText(IDType.ConnectState, state.ToString());

            if (state == ConnectState.Connected)
            {
                if (_activity.baseReader.RfidUhf != null)
                {
                    _activity.baseReader.RfidUhf.AddListener(this);
                }

                setUseGunKeyCode();
            }
        }

        public void OnReaderTemperatureState(BaseReader reader, double temperatureState, Object @params)
        {
            UpdateText(IDType.Temperature, temperatureState.ToString());
        }

        public void OnRfidUhfAccessResult(BaseUHF uhf, ResultCode code, ActionState action, string epc, string data, Object @params)
        {
            if (code == ResultCode.NoError)
            {
                UpdateText(IDType.AccessResult, "Success");
            }
            else
            {
                UpdateText(IDType.AccessResult, code.ToString());
            }

            if (StringUtil.IsNullOrEmpty(data))
            {
                UpdateText(IDType.Data, "");
            }
            else
            {
                UpdateText(IDType.Data, data);
            }
            accessTagResult = (code == ResultCode.NoError);
        }

        public void OnRfidUhfReadTag(BaseUHF uhf, string tag, Object @params)
        {
            if (StringUtil.IsNullOrEmpty(tag))
            {
                return;
            }

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
            else
            {
                int size = (int)((rssi - histogramMin) / (histogramMax - histogramMin) * (histogramSize));
                ////Log.Error("TAG", "rssi: " + rssi + ", size: " + (rssi - histogramMin) + "/" + (histogramMax - histogramMin) + "*" + histogramSize + "=" + size);
                size = Math.Min(size, histogramSize);

                histogramData.setData(size, rssi + "dBm");
                histogramView.update(histogramData);
            }

            UpdateText(IDType.TagRSSI, rssi.ToString());
        }

        private void FindViewById(View view)
        {
            layoutDisplay = view.FindViewById<LinearLayout>(Resource.Id.layout_display);

            #region Button
            buttonInfo = view.FindViewById<Button>(Resource.Id.button_info);
            buttonSettings = view.FindViewById<Button>(Resource.Id.button_settings);
            buttonInventory = view.FindViewById<Button>(Resource.Id.button_inventory);
            buttonRead = view.FindViewById<Button>(Resource.Id.button_read);
            buttonWrite = view.FindViewById<Button>(Resource.Id.button_write);
            buttonLock = view.FindViewById<Button>(Resource.Id.button_lock);
            buttonUnlock = view.FindViewById<Button>(Resource.Id.button_unlock);
            buttonFind = view.FindViewById<Button>(Resource.Id.button_find);
            buttonDisplay = view.FindViewById<Button>(Resource.Id.button_display);
            #endregion

            histogramView = view.FindViewById<HistogramView>(Resource.Id.histogram_view);

            layoutTagTID = view.FindViewById<LinearLayout>(Resource.Id.layout_tagTID);

            switchFastID = view.FindViewById<Switch>(Resource.Id.switch_fastId);

            #region TextView
            connectState = view.FindViewById<TextView>(Resource.Id.connectState);
            temperature = view.FindViewById<TextView>(Resource.Id.temperature);
            result = view.FindViewById<TextView>(Resource.Id.result);
            tagEPC = view.FindViewById<TextView>(Resource.Id.tagEPC);
            tagTID = view.FindViewById<TextView>(Resource.Id.tagTID);
            tagRSSI = view.FindViewById<TextView>(Resource.Id.tagRSSI);
            battery = view.FindViewById<TextView>(Resource.Id.battery);
            tagData = view.FindViewById<TextView>(Resource.Id.tagData);
            #endregion

            editDisplay = view.FindViewById<EditText>(Resource.Id.edit_display);
        }

        public bool connectRFID()
        {
            try
            {
                AssertReader();
            }
            catch (Exception e)
            {
                MainActivity.ShowToast(e.Message);
                return true;
            }

            //Timeout value is from 0 to 255 and unit is 0.1 second
            FindDevice findDevice = new FindDevice(FindDeviceMode.VibrateBeep, 10);

            try
            {
                _activity.baseReader.FindDevice = findDevice;
            }
            catch (ReaderException e)
            {
                MainActivity.ShowToast(e.Code.ToString());
            }
            return true;
        }

        private void SetButtonClick()
        {
            buttonInfo.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (System.Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                StringBuilder data = new StringBuilder();

                try
                {
                    data.Append(GetString(Resource.String.deviceName)).Append(_activity.baseReader.DeviceName).Append("\n");
                    data.Append(GetString(Resource.String.sku)).Append(_activity.baseReader.SKU.ToString()).Append("\n");
                    data.Append(GetString(Resource.String.region)).Append(_activity.baseReader.RfidUhf.GlobalBand.ToString()).Append("\n");
                    data.Append(GetString(Resource.String.version)).Append(_activity.baseReader.Version).Append("\n");

                    data.Append(GetString(Resource.String.temperature)).Append(_activity.baseReader.Temperature).Append("\n");

                    if (_activity.mainModel.deviceType == DeviceType.Rp902)
                    {
                        data.Append(GetString(Resource.String.time)).Append(_activity.baseReader.Time.ToString()).Append("\n");
                        data.Append(GetString(Resource.String.readMode)).Append(_activity.baseReader.ReadMode.ToString()).Append("\n");
                        data.Append(GetString(Resource.String.operatingMode)).Append(_activity.baseReader.OperatingMode.ToString()).Append("\n");
                    }
                }
                catch (Com.Unitech.Lib.Diagnositics.ReaderException e)
                {
                    Log.Error(TAG, e.Message);
                }

                MainActivity.ShowDialog("Info", data.ToString());
            };

            buttonSettings.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                StringBuilder data = new StringBuilder();

                try
                {
                    PowerRange powerRange = _activity.baseReader.RfidUhf.PowerRange;
                    data.Append(GetString(Resource.String.power)).Append(_activity.baseReader.RfidUhf.Power).Append(string.Format(" ({0} - {1})", powerRange.Min, powerRange.Max)).Append("\n");

                    data.Append(GetString(Resource.String.inventoryTime)).Append(_activity.baseReader.RfidUhf.InventoryTime).Append("\n");
                    data.Append(GetString(Resource.String.idleTime)).Append(_activity.baseReader.RfidUhf.IdleTime).Append("\n");

                    data.Append(GetString(Resource.String.algorithm)).Append(_activity.baseReader.RfidUhf.AlgorithmType.ToString()).Append("\n");
                    data.Append(GetString(Resource.String.qValue));
                    data.Append(GetString(Resource.String.startQ)).Append(_activity.baseReader.RfidUhf.StartQ).Append(", ");
                    data.Append(GetString(Resource.String.minQ)).Append(_activity.baseReader.RfidUhf.MinQ).Append(", ");
                    data.Append(GetString(Resource.String.maxQ)).Append(_activity.baseReader.RfidUhf.MaxQ).Append("\n");

                    data.Append(GetString(Resource.String.session)).Append(_activity.baseReader.RfidUhf.Session.ToString()).Append(", ");
                    data.Append(GetString(Resource.String.target)).Append(_activity.baseReader.RfidUhf.Target.ToString()).Append("\n");

                    data.Append(GetString(Resource.String.toggleTarget)).Append(_activity.baseReader.RfidUhf.ToggleTarget).Append("\n");

                    data.Append(GetString(Resource.String.continuousMode)).Append(_activity.baseReader.RfidUhf.ContinuousMode).Append("\n");

                    if (_activity.mainModel.deviceType == DeviceType.Rp902)
                    {
                        data.Append(GetString(Resource.String.autoOffTime)).Append(_activity.baseReader.AutoOffTime).Append(_activity.baseReader.GetAutoOffTimeList().ToString()).Append("\n");

                        data.Append(GetString(Resource.String.beep)).Append(_activity.baseReader.Beeper.ToString()).Append(", ");
                        data.Append(GetString(Resource.String.vibrator)).Append(_activity.baseReader.Vibrator.ToString()).Append("\n");

                        data.Append(GetString(Resource.String.tari)).Append(_activity.baseReader.RfidUhf.TARI.ToString()).Append(", ");
                        data.Append(GetString(Resource.String.blf)).Append(_activity.baseReader.RfidUhf.BLF.ToString()).Append("\n");
                        data.Append(GetString(Resource.String.fastMode)).Append(_activity.baseReader.RfidUhf.FastMode).Append("\n");
                    }
                    else if (_activity.mainModel.deviceType == DeviceType.Ht730)
                    {

                        data.Append(GetString(Resource.String.profile)).Append(_activity.baseReader.RfidUhf.ModuleProfile).Append("\n");
                        data.Append(GetString(Resource.String.powerMode)).Append(_activity.baseReader.RfidUhf.PowerMode.ToString()).Append("\n");
                        data.Append(GetString(Resource.String.fastId)).Append(_activity.baseReader.RfidUhf.FastID).Append("\n");
                    }
                    else if (_activity.mainModel.deviceType == DeviceType.Rg768)
                    {
                        data.Append(GetString(Resource.String.fastId)).Append(_activity.baseReader.RfidUhf.FastID).Append("\n");
                    }
                }
                catch (ReaderException e)
                {
                    Log.Error(TAG, e.Message);
                }

                MainActivity.ShowDialog("Settings", data.ToString());
            };

            buttonInventory.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                if (_activity.baseReader.Action == ActionState.Stop)
                {
                    DoInventory();
                }
                else if (_activity.baseReader.Action == ActionState.Inventory6c)
                {
                    DoStop();
                }
            };

            switchFastID.CheckedChange += delegate (object sender, CompoundButton.CheckedChangeEventArgs e)
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e1)
                {
                    MainActivity.ShowToast(e1.Message);
                    return;
                }
                _activity.baseReader.RfidUhf.FastID = switchFastID.Checked;
            };

            buttonFind.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                if (_activity.baseReader.Action == ActionState.Stop)
                {
                    DoFind();
                }
                else if (_activity.baseReader.Action == ActionState.Inventory6c)
                {
                    DoStop();
                }
            };

            buttonRead.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                if (_activity.baseReader.Action == ActionState.Stop)
                {
                    ClearResult();
                    DoRead();
                }
            };

            buttonWrite.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                if (_activity.baseReader.Action == ActionState.Stop)
                {
                    ClearResult();
                    DoWrite();
                }
            };

            buttonLock.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                if (_activity.baseReader.Action == ActionState.Stop)
                {
                    ClearResult();
                    _ = LockUnlockProc(true);
                }
            };

            buttonUnlock.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                if (_activity.baseReader.Action == ActionState.Stop)
                {
                    ClearResult();
                    _ = LockUnlockProc(false);
                }
            };

            buttonDisplay.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                if (_activity.baseReader.Action == ActionState.Stop)
                {
                    SetDisplayOutput(2, true, editDisplay.Text);
                }
            };
        }

        public override void ReceiveHandler(Bundle bundle)
        {
            UpdateUIType updateUIType = (UpdateUIType)bundle.GetInt(ExtraName.Type);

            switch (updateUIType)
            {
                case UpdateUIType.Text:
                    {
                        string data = bundle.GetString(ExtraName.Text);
                        IDType idType = (IDType)bundle.GetInt(ExtraName.TargetID);

                        switch (idType)
                        {
                            case IDType.ConnectState:
                                connectState.Text = data;
                                break;
                            case IDType.Temperature:
                                temperature.Text = data;
                                break;
                            case IDType.AccessResult:
                                result.Text = data;
                                break;
                            case IDType.TagEPC:
                                tagEPC.Text = data;
                                break;
                            case IDType.TagTID:
                                tagTID.Text = data;
                                break;
                            case IDType.TagRSSI:
                                tagRSSI.Text = data;
                                break;
                            case IDType.Battery:
                                battery.Text = data;
                                break;
                            case IDType.Inventory:
                                buttonInventory.Text = data;
                                break;
                            case IDType.Data:
                                tagData.Text = data;
                                break;
                            case IDType.Find:
                                buttonFind.Text = data;
                                break;
                        }
                    }
                    break;
            }
        }

        private void AssertReader()
        {
            _activity.AssertReader();
        }

        private void AssertTagEPC(string epc)
        {
            if (StringUtil.IsNullOrEmpty(epc))
            {
                Log.Error(TAG, "EPC is empty");
                throw new Exception("EPC is empty");
            }
        }

        private void DoInventory()
        {
            try
            {
                InitSetting();

                ClearSelectMask();

                _isFindTag = false;
                _activity.baseReader.SetDisplayTags(new DisplayTags(ReadOnceState.Off, BeepAndVibrateState.On));
                _activity.baseReader.RfidUhf.Inventory6c();

            }
            catch (ReaderException e)
            {
                MainActivity.ShowToast(e.Message);
            }
        }

        private void DoStop()
        {
            _isFindTag = false;
            _activity.baseReader.RfidUhf.Stop();
        }

        private void DoFind()
        {
            string targetTag = tagEPC.Text;

            try
            {
                AssertTagEPC(targetTag);
            }
            catch (Exception e)
            {
                MainActivity.ShowToast(e.Message);
                return;
            }

            if (SetSelectMask(targetTag))
            {
                _isFindTag = true;
                _activity.baseReader.RfidUhf.Inventory6c();
            }
        }

        private void DoRead()
        {
            string targetTag = tagEPC.Text;

            try
            {
                AssertTagEPC(targetTag);
            }
            catch (Exception e)
            {
                MainActivity.ShowToast(e.Message);
                return;
            }

            if (SetSelectMask(targetTag))
            {
                string accessPassword = "00000000";
                int offset = 2;
                int length = 6;

                if (!ReadTag(BankType.Epc, offset, length, accessPassword))
                {
                    MainActivity.ShowToast("Failed to read memory");
                }
            }
        }

        private void DoWrite()
        {
            string targetTag = tagEPC.Text;

            try
            {
                AssertTagEPC(targetTag);
            }
            catch (Exception e)
            {
                MainActivity.ShowToast(e.Message);
                return;
            }

            if (SetSelectMask(targetTag))
            {
                string accessPassword = "00000000";
                BankType bank = BankType.Epc;
                int offset = 2;

                //region Change the data for test
                if (targetTag.StartsWith("1234"))
                {
                    targetTag = "4321" + targetTag.Substring(4);
                }
                else
                {
                    targetTag = "1234" + targetTag.Substring(4);
                }
                //endregion

                if (!WriteTag(BankType.Epc, offset, accessPassword, targetTag))
                {
                    MainActivity.ShowToast("Failed to write memory");
                }
            }
        }

        async Task LockUnlockProc(bool locked)
        {
            string targetTag = tagEPC.Text;

            try
            {
                AssertTagEPC(targetTag);
            }
            catch (Exception e)
            {
                MainActivity.ShowToast(e.Message);
                return;
            }

            if (SetSelectMask(targetTag))
            {
                string accessPassword = "00000000";
                string data = "12345678";
                int offset = 2;

                //region Write the password for lock/unlock test
                accessTagResult = false;

                if (!WriteTag(BankType.Reserved, offset, accessPassword, data))
                {
                    MainActivity.ShowToast("Write password fail");
                    return;
                }

                long startTime = JavaSystem.CurrentTimeMillis();
                bool timeout = false;

                while (_activity.baseReader.Action != ActionState.Stop)
                {
                    if (JavaSystem.CurrentTimeMillis() - startTime > 3000)
                    {
                        timeout = true;
                        break;
                    }
                    Thread.Sleep(10);
                }

                if (timeout)
                {
                    MainActivity.ShowToast("Write password timeout");
                    return;
                }

                if (!accessTagResult)
                {
                    MainActivity.ShowToast("Write password fail from access result");
                    return;
                }
                //endregion

                accessPassword = data;

                Lock6cParam lockParam = new Lock6cParam();
                lockParam.Epc = locked ? LockState.Lock : LockState.Unlock;

                ResultCode res = _activity.baseReader.RfidUhf.Lock6c(lockParam, accessPassword);

                if (res != ResultCode.NoError)
                {
                    Log.Error(TAG, "Failed to lock/unlock tag - " + res);
                }
            }
        }

        public bool ReadTag(BankType bank, int offset, int length, string password)
        {
            ResultCode res = _activity.baseReader.RfidUhf.ReadMemory6c(
                    bank, offset, length, password);

            if (res != ResultCode.NoError)
            {
                Log.Error(TAG, "Failed to read memory - " + res);
                return false;
            }
            return true;
        }

        public bool WriteTag(BankType bank, int offset, string password, string data)
        {
            ResultCode res = _activity.baseReader.RfidUhf.WriteMemory6c(
                    bank, offset, data, password);
            if (res != ResultCode.NoError)
            {
                Log.Error(TAG, "Failed to write memory - " + res);
                return false;
            }
            return true;
        }

        void InitSetting()
        {
            try
            {
                _activity.baseReader.RfidUhf.Session = Session.S0;
                _activity.baseReader.RfidUhf.ContinuousMode = true;
                _activity.baseReader.RfidUhf.InventoryTime = 200;
                _activity.baseReader.RfidUhf.IdleTime = 20;

                _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.DynamicQ;

                _activity.baseReader.RfidUhf.StartQ = 4;
                _activity.baseReader.RfidUhf.MaxQ = 15;
                _activity.baseReader.RfidUhf.MinQ = 0;

                _activity.baseReader.RfidUhf.Target = Target.A;

                _activity.baseReader.RfidUhf.ToggleTarget = true;

                if (_activity.mainModel.deviceType == DeviceType.Rp902)
                {
                    _activity.baseReader.RfidUhf.Power = 22;

                    _activity.baseReader.AutoOffTime = 2;

                    _activity.baseReader.Beeper = BeeperState.Medium;
                    _activity.baseReader.Vibrator = VibratorState.On;

                    _activity.baseReader.RfidUhf.TARI = TARIType.T2500;
                    _activity.baseReader.RfidUhf.BLF = BLFType.Blf256;
                    _activity.baseReader.RfidUhf.FastMode = true;

                    Date currentTime = Calendar.Instance.Time;
                    _activity.baseReader.Time = currentTime;
                }
                else if (_activity.mainModel.deviceType == DeviceType.Ht730)
                {
                    _activity.baseReader.RfidUhf.Power = 30;
                    _activity.baseReader.RfidUhf.ModuleProfile = 0;
                    _activity.baseReader.RfidUhf.PowerMode = PowerMode.Optimized;
                }
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, e.Message);
            }
        }

        //private async Task ConnectTask()
        //{
        //    try
        //    {
        //        if (_activity.mainModel.deviceType == DeviceType.Rp902)
        //        {
        //            TransportBluetooth tb = new TransportBluetooth(DeviceType.Rp902, "RP902", _activity.mainModel.bluetoothMACAddress);
        //            _activity.baseReader = new RP902Reader(tb);
        //            _activity.baseReader.AddListener(this);
        //            _activity.baseReader.Connect();
        //        }
        //        else if (_activity.mainModel.deviceType == DeviceType.Ht730)
        //        {
        //            _activity.baseReader = new HT730Reader(_activity.ApplicationContext);
        //            _activity.baseReader.AddListener(this);
        //            _activity.baseReader.Connect();
        //        }
        //        else if (_activity.mainModel.deviceType == DeviceType.Rg768)
        //        {
        //            _activity.baseReader = new RG768Reader(_activity.ApplicationContext);
        //            _activity.baseReader.AddListener(this);
        //            _activity.baseReader.Connect();
        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        Log.Error(TAG, e.ToString());
        //        MainActivity.ShowToast("Connect exception: " + e.Message);
        //    }
        //}

        public bool SetSelectMask(string maskEpc)
        {
            SelectMask6cParam param = new SelectMask6cParam(
                    true,
                    Mask6cTarget.Sl,
                    Mask6cAction.Ab,
                    BankType.Epc,
                    0,
                    maskEpc,
                    maskEpc.Length * NIBLE_SIZE);
            try
            {
                for (int i = 0; i < MAX_MASK; i++)
                {
                    _activity.baseReader.RfidUhf.SetSelectMask6cEnabled(i, false);
                }
                _activity.baseReader.RfidUhf.SetSelectMask6c(0, param);
                Log.Debug(TAG, "setSelectMask success: " + param.ToString());
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "setSelectMask failed: \n" + e.Code.Message);
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
                    Log.Debug(TAG, "ClearSelectMask successful");
                }
                catch (ReaderException e)
                {
                    throw e;
                }
            }
        }

        private void UpdateText(IDType id, string data)
        {
            Utilities.UpdateUIText(FragmentType.Home, (int)id, data);
        }

        void ClearResult()
        {
            UpdateText(IDType.AccessResult, "");
            UpdateText(IDType.Data, "");
        }

        public void OnCustomActionReceived(Context context, Intent intent)
        {
            string action = intent.Action;
            if (action.Equals(MainReceiver.rfidGunPressed))
            {
                if (_activity.baseReader != null)
                {
                    OnReaderKeyChanged(null, KeyType.Trigger, KeyState.KeyDown, null);
                }
            }
            else if (action.Equals(MainReceiver.rfidGunReleased))
            {
                if (_activity.baseReader != null)
                {
                    OnReaderKeyChanged(null, KeyType.Trigger, KeyState.KeyUp, null);
                }
            }
            //throw new NotImplementedException();
        }

        private void SetDisplayOutput(int pLine, bool bClear, string data)
        {
            const int MAX_CHARS = 16;
            DisplayOutput display = null;
            byte param = 0x00;

            if (data.Length < 16)
            {
                int pChar = (int)Math.Floor((MAX_CHARS - data.Length) / 2);
                param |= (byte)pChar;
            }

            if (bClear) param |= (byte)0x20;

            if (pLine < 1 || pLine > 3) param |= (byte)0x80;
            else param |= (byte)(pLine << 6);

            display = new DisplayOutput((sbyte)param, data);

            try
            {
                _activity.baseReader.SetDisplayOutput(display);
                Log.Debug(TAG, "SetDisplayOutput Success");
            }
            catch (ReaderException e)
            {
                throw new RuntimeException(e);
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
        {
            string defaultKeyConfigPath = keymappingPath;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
            {
                defaultKeyConfigPath = android12keymappingPath;
            }
            Log.Warn(TAG, defaultKeyConfigPath);
            return defaultKeyConfigPath;
        }

        private Bundle[] getParams(Bundle bundle)
        {
            if (bundle == null)
            {
                return null;
            }
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
            if (tempKeyCode == null)
            {
                Task.Run(async () =>
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
                            Log.Debug(TAG, "Skip to set gun key code");
                            return;
                    }

                    sendUssScan(false);

                    Log.Debug(TAG, "Export keyMappings");
                    Bundle exportBundle = KeymappingCtrl.GetInstance(MainActivity.getInstance().ApplicationContext).ExportKeyMappings(getKeymappingPath());
                    Log.Debug(TAG, "Export keyMappings, result: " + exportBundle.GetString("errorMsg"));

                    Log.Debug(TAG, "Enable KeyMapping");
                    Bundle enableBundle = KeymappingCtrl.GetInstance(MainActivity.getInstance().ApplicationContext).EnableKeyMapping(true);
                    Log.Debug(TAG, "Enable KeyMapping, result: " + enableBundle.GetString("errorMsg"));

                    tempKeyCode = KeymappingCtrl.GetInstance(MainActivity.getInstance().ApplicationContext).GetKeyMapping(keyName);

                    Log.Debug(TAG, "Set Gun Key Code: " + keyCode);
                    bool wakeup = tempKeyCode.GetBoolean("wakeUp");
                    Bundle[] broadcastDownParams = getParams(tempKeyCode.GetBundle("broadcastDownParams"));
                    Bundle[] broadcastUpParams = getParams(tempKeyCode.GetBundle("broadcastUpParams"));
                    Bundle[] startActivityParams = getParams(tempKeyCode.GetBundle("startActivityParams"));

                    Bundle resultBundle = KeymappingCtrl.GetInstance(MainActivity.getInstance().ApplicationContext).AddKeyMappings(
                        keyName,
                        keyCode,
                        wakeup,
                        MainReceiver.rfidGunPressed,
                        broadcastDownParams,
                        MainReceiver.rfidGunReleased,
                        broadcastUpParams,
                        startActivityParams
                );
                    if (resultBundle.GetInt("errorCode") == 0)
                    {
                        Log.Debug(TAG, "Set Gun Key Code success");
                    }
                    else
                    {
                        Log.Error(TAG, "Set Gun Key Code failed: " + resultBundle.GetString("errorMsg"));
                    }
                });
            }
        }

        private void restoreGunKeyCode()
        {
            if (tempKeyCode != null)
            {
                Task.Run(async () =>
                {
                    Log.Debug(TAG, "restoreGunKeyCode start");
                    string keymappingPath = getKeymappingPath();
                    Bundle resultBundle = KeymappingCtrl.GetInstance(MainActivity.getInstance().ApplicationContext).ImportKeyMappings(keymappingPath);
                    Log.Debug(TAG, resultBundle.GetString("errorMsg"));

                    if (resultBundle.GetInt("errorCode") == 0)
                    {
                        Log.Debug(TAG, "restoreGunKeyCode success");
                    }
                    else
                    {
                        Log.Error(TAG, "restoreGunKeyCode failed: " + resultBundle.GetString("errorMsg"));
                    }
                    tempKeyCode = null;
                });
            }
        }
    }
}