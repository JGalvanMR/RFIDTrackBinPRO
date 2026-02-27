using Android.Content;
using Android.OS;
using Android.Views;
using AndroidX.Fragment.App;

namespace RFIDTrackBin.fragment
{
    public abstract class BaseFragment : Fragment
    {
        protected MainActivity _activity;

        public override void OnAttach(Context context)
        {
            base.OnAttach(context);
            if (context is MainActivity mainActivity)
            {
                _activity = mainActivity;
            }
        }

        public abstract void ReceiveHandler(Bundle bundle);

        public override void OnDestroy()
        {
            base.OnDestroy();
        }

        public virtual void OnNfcTagScanned(string tagId)
        {
            // Override en cada fragmento
        }
    }
}