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
using Com.Unitech.Lib.Types;

namespace RFIDTrackBin
{
    public class MainModel
    {
        public string bluetoothMACAddress = "";
        //public DeviceType deviceType = DeviceType.Unknown;
        public DeviceType deviceType = DeviceType.Rg768;
    }
}