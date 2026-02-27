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
    public enum IDType
    {
        ConnectState = 0,
        Temperature = 1,
        TagTID = 11,
        TagEPC = 12,
        AccessResult = 13,
        TagRSSI = 18,
        Battery = 20,
        Inventory = 21,
        Data = 22,
        Find = 23,
    }
}