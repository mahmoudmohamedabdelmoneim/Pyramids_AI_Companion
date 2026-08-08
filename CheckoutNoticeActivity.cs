namespace AndroidApp1
{
    [Activity(Label = "@string/checkout_title")]
    public class CheckoutNoticeActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_checkout_notice);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
            FindViewById<Android.Widget.TextView>(Resource.Id.confirm_checkout_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(BookingInfoActivity)));
        }
    }
}
