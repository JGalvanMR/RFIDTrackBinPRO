using Android.App;
using Android.Views;
using Android.Widget;
using RFIDTrackBin.Model;
using System.Collections.Generic;

namespace RFIDTrackBin
{
    public class myGVitemAdapterFP : BaseAdapter<FleteItem>
    {
        Activity context;
        List<FleteItem> items;

        public myGVitemAdapterFP(Activity currentContext, List<FleteItem> lista)
        {
            context = currentContext;
            items = lista;
        }

        public override int Count => items?.Count ?? 0;

        public override FleteItem this[int position] => items[position];

        public override long GetItemId(int position) => position;

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            ViewHolder holder;

            if (convertView == null)
            {
                convertView = context.LayoutInflater
                    .Inflate(Resource.Layout.custGridViewItemFP, parent, false);

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

            var item = items[position];

            holder.txtName.Text = item.Titulo;
            holder.txtAge.Text = item.IdProveedor;

            return convertView;
        }

        class ViewHolder : Java.Lang.Object
        {
            public TextView txtName;
            public TextView txtAge;
        }
    }
}