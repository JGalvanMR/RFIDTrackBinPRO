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

        public override long GetItemId(int position) => position;
        public override int Count => _tagEPCList?.Count ?? 0;
        public override TagLeido this[int position] => _tagEPCList?[position];

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            ViewHolder holder;

            try
            {
                if (convertView == null)
                {
                    convertView = _CurrentContext.LayoutInflater
                        .Inflate(Resource.Layout.custGridViewItem, parent, false);

                    holder = new ViewHolder
                    {
                        txtName = convertView.FindViewById<TextView>(Resource.Id.txtName),
                        txtAge = convertView.FindViewById<TextView>(Resource.Id.txtAge)
                    };

                    convertView.Tag = holder;
                }
                else
                {
                    holder = (ViewHolder)convertView.Tag;
                }

                var item = _tagEPCList[position];
                holder.txtName.Text = item.EPC;
                holder.txtAge.Text = $"{item.RSSI:F1} dBm";
            }
            catch (Exception e)
            {
                // FIX A-1: Loguear error en lugar de silenciarlo.
                AppLogger.LogError(e);

                // Garantizar que GetView nunca devuelva null — causa NullReferenceException
                // en el sistema de GridView si convertView fue null antes del inflate.
                if (convertView == null)
                {
                    convertView = _CurrentContext.LayoutInflater
                        .Inflate(Android.Resource.Layout.SimpleListItem1, parent, false);
                }
            }

            return convertView;
        }

        class ViewHolder : Java.Lang.Object
        {
            public TextView txtName;
            public TextView txtAge;
        }
    }
}