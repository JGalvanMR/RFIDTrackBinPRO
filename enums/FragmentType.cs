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

namespace RFIDTrackBin.enums
{
    public enum FragmentType
    {
        None = 0,
        Home = 1,
        Entradas = 2,
        Salidas = 3,
        Inventario = 4,
        Verificacion = 5,
    }
}