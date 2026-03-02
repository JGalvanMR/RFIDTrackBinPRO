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
using RFIDTrackBin.enums;
using RFIDTrackBin.fragment;

namespace RFIDTrackBin
{
    public class MainHandler : Handler
    {
        private MainActivity _activity;
        private AlertDialog alertDialog;

        public MainHandler(MainActivity activity) : base(Looper.MainLooper)
        {
            this._activity = activity;
        }

        public override void HandleMessage(Message msg)
        {
            base.HandleMessage(msg);

            var fragmentType = (FragmentType)msg.What;
            var baseFragment = _activity.GetFragment(fragmentType);

            if (baseFragment != null)
            {
                baseFragment.ReceiveHandler(msg.Data);
            }
            else if (fragmentType == FragmentType.None)
            {
                ProcessHandlerMessage(msg.Data);
            }
        }

        private void ProcessHandlerMessage(Bundle bundle)
        {
            var msgType = (HandlerMsg)bundle.GetInt(ExtraName.HandleMsg);

            switch (msgType)
            {
                case HandlerMsg.Toast:
                    ShowToast(bundle);
                    break;
                case HandlerMsg.Dialog:
                    ShowDialog(bundle);
                    break;
            }
        }

        private void ShowToast(Bundle bundle)
        {
            string message = bundle.GetString(ExtraName.Text);
            bool isLongDuration = bundle.GetInt(ExtraName.Number, 1) == 1;
            Toast.MakeText(_activity.ApplicationContext, message, isLongDuration ? ToastLength.Long : ToastLength.Short).Show();
        }

        // FIX M1: Eliminado HandlerProcess — era código muerto (nunca se llamaba)
        //         que duplicaba exactamente la lógica de ProcessHandlerMessage + ShowToast.
        //         Mantener código duplicado genera riesgo de divergencia en el futuro.

        void ShowDialog(Bundle dlgData)
        {
            InitAlertDlg();
            alertDialog.SetTitle(dlgData.GetString(ExtraName.Title));
            alertDialog.SetMessage(dlgData.GetString(ExtraName.Text));
            alertDialog.Show();
        }

        void InitAlertDlg()
        {
            alertDialog = new AlertDialog.Builder(_activity).Create();
        }
    }
}
