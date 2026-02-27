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

namespace RFIDTrackBin
{
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { "unitech.scanservice.data" })]
    public class ScanReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if ("unitech.scanservice.data".Equals(intent.Action))
            {
                Bundle bundle = intent.Extras;
                if (bundle != null)
                {
                    string qrText = bundle.GetString("text");
                    

                    if (!string.IsNullOrEmpty(qrText))
                    {
                        // Llamamos al MainActivity para procesar el QR
                        MainActivity.Instance?.ProcessQR(qrText);
                    }
                }
            }
        }
    }

}