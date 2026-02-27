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
    public class Flete
    {
        public string id_flete { get; set; }
        public string nom_proveedor { get; set; }
        public string id_rancho { get; set; }
        public string id_tabla { get; set; }
        public string orde { get; set; }
        public string id_destino { get; set; }
        public string id_proveedor { get; set; }
        public string id_ranchoDET { get; set; }
        public string id_tablaDET { get; set; }

        public override string ToString()
        {
            return $"{id_flete} - {nom_proveedor} - {id_rancho} - {id_tabla} - {orde} - {id_destino} - {id_proveedor} - {id_ranchoDET} - {id_tablaDET}";
        }
    }
}