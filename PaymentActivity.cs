namespace AndroidApp1
{
    [Activity(Label = "@string/payment_title")]
    public class PaymentActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_payment);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();

            var paymentButtons = new[]
            {
                Resource.Id.gpay_button,
                Resource.Id.apple_pay_button,
                Resource.Id.visa_button
            };

            foreach (var paymentButtonId in paymentButtons)
            {
                FindViewById<Android.Views.View>(paymentButtonId)!.Click += (_, _) =>
                    StartActivity(new Android.Content.Intent(this, typeof(TicketsReadyActivity)));
            }
        }

    }
}
