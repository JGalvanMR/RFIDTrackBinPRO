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
            // FIX A3: Implementado patrón ViewHolder idéntico al que ya existía en
            //         myGVitemAdapterFP. Antes, FindViewById se ejecutaba en CADA bind
            //         incluso cuando convertView era reutilizado, causando lag en scroll.
            //         Con ViewHolder, FindViewById solo se llama una vez por celda (inflate),
            //         y en rebinds se recupera el holder directamente desde convertView.Tag.
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
                System.Diagnostics.Debug.WriteLine($"Error en myGVitemAdapter: {e.Message}");
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
