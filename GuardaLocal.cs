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
using System.Net;
using Java.IO;

namespace RFIDTrackBin
{
    public class GuardaLocal
    {
        public bool HayConexion(string direccionweb)
        {
            try
            {
                using (var client = new WebClient())
                using (client.OpenRead(direccionweb))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public void creartxt(string error)
        {
            Java.IO.File sdCard = Android.App.Application.Context.GetExternalFilesDir(null);

            Java.IO.File dir = new Java.IO.File(sdCard.AbsolutePath + "/MyFolder");
            dir.Mkdirs();
            Java.IO.File file = new Java.IO.File(dir, "errores.txt");

            if (!file.Exists())
            {
                file.CreateNewFile();
                // FIX C4: Eliminado file.Mkdir() — llamar Mkdir() sobre un archivo ya creado
                //         con CreateNewFile() es incorrecto y puede corromper el archivo.

                // FIX C4: FileWriter envuelto en try/finally para garantizar cierre
                //         incluso si Write() lanza una excepción (evita file handle leak).
                FileWriter writer = null;
                try
                {
                    writer = new FileWriter(file);
                    writer.Write(error + System.Environment.NewLine);
                    writer.Flush();
                }
                finally
                {
                    writer?.Close();
                }
            }
            else
            {
                string rutaarchivo = file.ToString();
                System.IO.File.AppendAllText(rutaarchivo, error + System.Environment.NewLine);
            }
        }
    }
}
