using Android.App;
using Android.Views;
using Android.Widget;
using RFIDTrackBin.Model;
using System;
using System.Collections.Generic;

namespace RFIDTrackBin
{
    public class myGVitemAdapter : BaseAdapter<TagLeido>
    {
        Activity _CurrentContext;
        List<TagLeido> _tagEPCList;

        public myGVitemAdapter(Activity currentContext, List<TagLeido> tagEPCList)
        {
            _CurrentContext = currentContext;
            _tagEPCList = tagEPCList;
        }

        public override long GetItemId(int position)
        {
            return position;
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            try
            {
                var item = _tagEPCList[position];
                if (convertView == null)
                    convertView = _CurrentContext.LayoutInflater.Inflate(Resource.Layout.custGridViewItem, null);

                convertView.FindViewById<TextView>(Resource.Id.txtName).Text = item.EPC;
                convertView.FindViewById<TextView>(Resource.Id.txtAge).Text = $"{item.RSSI:F1} dBm";
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Error en MyGVitemAdapter: {e.Message}");
            }

            return convertView;
        }

        public override int Count => _tagEPCList?.Count ?? 0;

        public override TagLeido this[int position] => _tagEPCList?[position];
    }
}
