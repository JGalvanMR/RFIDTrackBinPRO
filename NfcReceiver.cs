using Android.App;
using Android.Content;
using Android.Nfc;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using RFIDTrackBin.fragment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RFIDTrackBin
{
    public class NfcReceiver : BroadcastReceiver
    {
        private readonly AndroidX.Fragment.App.Fragment _fragment;

        public NfcReceiver(AndroidX.Fragment.App.Fragment fragment)
        {
            _fragment = fragment;
        }

        public override void OnReceive(Context context, Intent intent)
        {
            if (intent.Action == NfcAdapter.ActionTagDiscovered)
            {
                Tag tag = intent.GetParcelableExtra(NfcAdapter.ExtraTag) as Tag;
                if (tag != null)
                {
                    string tagId = BitConverter.ToString(tag.GetId()).Replace("-", "");
                    Toast.MakeText(context, "Tag detectado: " + tagId, ToastLength.Short).Show();

                    // Delegar al fragmento
                    if (_fragment is BaseFragment baseFragment)
                    {
                        baseFragment.OnNfcTagScanned(tagId);
                    }
                }
            }
        }

    }

}