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
    class SpinnerTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly Android.Content.Context context;

        public SpinnerTouchListener(Android.Content.Context context)
        {
            this.context = context;
        }

        public bool OnTouch(View v, MotionEvent e)
        {
            if (e.Action == MotionEventActions.Down)
            {
                //Toast.MakeText(context, "El Spinner está deshabilitado", ToastLength.Short).Show();
                MainActivity.ShowDialog("AVISO", "Debe de dar Inicio a la captura del inventario!");
            }
            return true;
        }
    }
}