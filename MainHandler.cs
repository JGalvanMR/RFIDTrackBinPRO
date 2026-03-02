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

            // FIX MH-2: Guardia contra mensajes que llegan tras OnDestroy
            if (_activity == null || _activity.IsDestroyed || _activity.IsFinishing)
                return;

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

        void ShowDialog(Bundle dlgData)
        {
            // FIX MH-1: Cerrar diálogo anterior antes de crear uno nuevo para evitar acumulación
            if (alertDialog != null && alertDialog.IsShowing)
            {
                try { alertDialog.Dismiss(); } catch { }
            }

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