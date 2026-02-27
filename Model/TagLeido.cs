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
    public class TagLeido
    {
        public string EPC { get; set; }
        public float RSSI { get; set; }
        public DateTime FechaLectura { get; set; }
    }
}