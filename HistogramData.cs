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
    public class HistogramData
    {
        public int size;
        public int value;
        public string name;

        public HistogramData(int size)
        {
            this.size = size;
        }

        public void setData(int value, string name)
        {
            this.value = value;
            this.name = name;
        }
    }
}