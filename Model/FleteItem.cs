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

namespace RFIDTrackBin.Model
{
    public class FleteItem
    {
        public string IdFlete { get; set; }
        public string Orde { get; set; }
        public string IdProveedor { get; set; }
        public string IdRancho { get; set; }
        public string IdTabla { get; set; }

        public string Titulo => $"{IdFlete} - {Orde}";
    }
}