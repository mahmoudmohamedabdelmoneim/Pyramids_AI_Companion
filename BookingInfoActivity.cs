namespace AndroidApp1
{
    [Activity(Label = "@string/booking_info_title")]
    public class BookingInfoActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_booking_info);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
            FindViewById<Android.Widget.TextView>(Resource.Id.proceed_with_payment_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(PaymentActivity)));

            RenderBookingInfo();
        }

        private void RenderBookingInfo()
        {
            var container = FindViewById<LinearLayout>(Resource.Id.booking_items_container)!;

            foreach (var item in CartStore.CurrentItems)
            {
                var itemText = new Android.Widget.TextView(this)
                {
                    Text = $"{QuantityWords.For(item.Quantity, "Ticket", "Tickets")}: {item.Quantity}\nType: {item.Type}\nAccess: {item.AccessGate}",
                    TextSize = 16f
                };
                itemText.SetTextColor(Android.Graphics.Color.ParseColor("#D9E9DE"));
                itemText.SetBackgroundResource(Resource.Drawable.photo_card_border);
                itemText.SetPadding(Dp(18), Dp(15), Dp(18), Dp(15));
                itemText.LayoutParameters = new LinearLayout.LayoutParams(-1, -2)
                {
                    BottomMargin = Dp(12)
                };
                container.AddView(itemText);
            }
        }

        private int Dp(int value)
        {
            return (int)Android.Util.TypedValue.ApplyDimension(
                Android.Util.ComplexUnitType.Dip,
                value,
                Resources?.DisplayMetrics);
        }
    }
}
