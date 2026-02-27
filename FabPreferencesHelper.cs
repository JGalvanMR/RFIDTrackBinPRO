using Android.App;
using Android.Content;
using Android.OS;
using Android.Preferences;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RFIDTrackBin
{
    public static class FabPreferencesHelper
    {
        private const string PrefsName = "FabPreferences";
        private const string KeyFabX = "FabX";
        private const string KeyFabY = "FabY";

        public static void SaveFabPosition(Context context, float x, float y)
        {
            var prefs = PreferenceManager.GetDefaultSharedPreferences(context);
            var editor = prefs.Edit();
            editor.PutFloat(KeyFabX, x);
            editor.PutFloat(KeyFabY, y);
            editor.Apply();
        }

        public static (float x, float y) GetFabPosition(Context context)
        {
            var prefs = PreferenceManager.GetDefaultSharedPreferences(context);
            float x = prefs.GetFloat(KeyFabX, -1);
            float y = prefs.GetFloat(KeyFabY, -1);
            return (x, y);
        }

        public static void ClearFabPosition(Context context)
        {
            var prefs = PreferenceManager.GetDefaultSharedPreferences(context);
            var editor = prefs.Edit();
            editor.Remove(KeyFabX);
            editor.Remove(KeyFabY);
            editor.Apply();
        }
    }
}