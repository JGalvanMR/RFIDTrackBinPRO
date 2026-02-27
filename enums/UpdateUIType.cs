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
    public enum UpdateUIType
    {
        Reset = 0,
        Enable = 1,
        SetValue = 2,
        Connected = 3,
        Disconnected = 4,
        Icon = 5,
        RSSI = 6,
        Text = 7,
        Dialog = 8,
        Custom = 255,
    }
}