using Android.Content;

namespace RFIDTrackBin
{
    public class MainReceiver : BroadcastReceiver
    {
        public static string rfidGunPressed = "com.unitech.RFID_GUN.PRESSED";
        public static string rfidGunReleased = "com.unitech.RFID_GUN.RELEASED";

        public interface IEventLitener
        {
            void OnCustomActionReceived(Context context, Intent intent);
        }
        private IEventLitener _eventLitener;

        public MainReceiver(IEventLitener eventLitener)
        {
            this._eventLitener = eventLitener;
        }


        public override void OnReceive(Context context, Intent intent)
        {
            string action = intent.Action;
            if (action.Equals(rfidGunPressed))
            {
                _eventLitener.OnCustomActionReceived(context, intent);
            }
            else if (action.Equals(rfidGunReleased))
            {
                _eventLitener.OnCustomActionReceived(context, intent);
            }
        }
    }
}