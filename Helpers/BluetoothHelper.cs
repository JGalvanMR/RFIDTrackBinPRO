using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Android;
using Android.Bluetooth;
using AndroidX.Core.Content;
using System.Threading.Tasks;

namespace RFIDTrackBin.Helpers
{
    public class BluetoothHelper
    {
        public const int RequestEnableBtCode = 0xB100;   // 0xB100 == 45312
        public const int RequestPermBtCode = 0xB101;

        private readonly Activity _activity;
        private TaskCompletionSource<bool>? _tcsEnable;

        public BluetoothHelper(Activity activity) => _activity = activity;

        // Llamado desde fragments: await BluetoothHelper.Instance.EnsureBluetoothAsync();
        public Task<bool> EnsureBluetoothAsync()
        {
            var adapter = BluetoothAdapter.DefaultAdapter;

            if (adapter == null)
                return Task.FromResult(false);   // No soporta BT

            // Android 12+: verificar BLUETOOTH_CONNECT
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S &&
                ContextCompat.CheckSelfPermission(_activity, Manifest.Permission.BluetoothConnect) !=
                Android.Content.PM.Permission.Granted)
            {
                AndroidX.Core.App.ActivityCompat.RequestPermissions(
                    _activity,
                    new[] { Manifest.Permission.BluetoothConnect, Manifest.Permission.BluetoothScan },
                    RequestPermBtCode);

                // Devolvemos una tarea que completaremos desde OnRequestPermissionsResult
                _tcsEnable = new TaskCompletionSource<bool>();
                return _tcsEnable.Task;
            }

            return TurnOnIfNeededAsync(adapter);
        }

        internal Task<bool> TurnOnIfNeededAsync(BluetoothAdapter adapter)
        {
            if (adapter.IsEnabled)
                return Task.FromResult(true);

            _tcsEnable = new TaskCompletionSource<bool>();

            var intent = new Intent(BluetoothAdapter.ActionRequestEnable);
            _activity.StartActivityForResult(intent, RequestEnableBtCode);

            return _tcsEnable.Task;   // Se completa en OnActivityResult
        }

        // Expone ganchos para que MainActivity delegue los callbacks.
        public void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
        {
            if (requestCode != RequestPermBtCode || _tcsEnable == null)
                return;

            bool granted = grantResults.All(r => r == Android.Content.PM.Permission.Granted);
            if (!granted)
                _tcsEnable.TrySetResult(false);
            else
                _ = TurnOnIfNeededAsync(BluetoothAdapter.DefaultAdapter); // continúa flujo normal
        }

        public void OnActivityResult(int requestCode, Result resultCode)
        {
            if (requestCode != RequestEnableBtCode || _tcsEnable == null)
                return;

            _tcsEnable.TrySetResult(resultCode == Result.Ok);
        }
    }
}