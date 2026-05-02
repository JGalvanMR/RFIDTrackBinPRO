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
    public static class AppSettings
    {
#if DEBUG
        public static bool ModoPrueba => true;
#else
        public static bool ModoPrueba => false; // En Release SIEMPRE guarda
#endif
    }
}