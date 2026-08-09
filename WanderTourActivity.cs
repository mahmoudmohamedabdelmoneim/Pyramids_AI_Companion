namespace AndroidApp1
{
    [Activity(Label = "@string/wander_tour_title")]
    public class WanderTourActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_wander_tour);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();

            var cartButton = FindViewById<Android.Widget.TextView>(Resource.Id.add_to_cart_button)!;
            var ticketSelection = new TicketCartSelection(cartButton);
            var foreignersTickets = ticketSelection.BindCounter(this,
                Resource.Id.foreigners_minus_button,
                Resource.Id.foreigners_ticket_count,
                Resource.Id.foreigners_plus_button);
            var egyptiansTickets = ticketSelection.BindCounter(this,
                Resource.Id.egyptians_minus_button,
                Resource.Id.egyptians_ticket_count,
                Resource.Id.egyptians_plus_button);
            cartButton.Click += (_, _) =>
                CartActions.AddAndOpenCart(this,
                    new CartSelection("Wander Tour ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â Foreigners (USD 75)", "Gate 6", foreignersTickets),
                    new CartSelection("Wander Tour ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â Egyptians (USD 60)", "Gate 6", egyptiansTickets));

            AnimateEntrance(Resource.Id.ticket_options, 0, 28);
            AnimateEntrance(Resource.Id.tour_includes, 130, 20);
        }

        private void BindTicketCounter(int decreaseButtonId, int countId, int increaseButtonId)
        {
            var decreaseButton = FindViewById<Android.Widget.TextView>(decreaseButtonId)!;
            var countView = FindViewById<Android.Widget.TextView>(countId)!;
            var increaseButton = FindViewById<Android.Widget.TextView>(increaseButtonId)!;
            var count = 0;

            decreaseButton.Click += (_, _) =>
            {
                if (count == 0)
                {
                    return;
                }

                count--;
                countView.Text = count.ToString();
            };

            increaseButton.Click += (_, _) =>
            {
                count++;
                countView.Text = count.ToString();
            };
        }

        private void AnimateEntrance(int viewId, long delay, float offset)
        {
            var view = FindViewById<Android.Views.View>(viewId);
            if (view is null)
            {
                return;
            }

            view.Alpha = 0f;
            view.TranslationY = offset;
            view.Animate()
                ?.Alpha(1f)
                ?.TranslationY(0f)
                ?.SetStartDelay(delay)
                ?.SetDuration(700)
                ?.SetInterpolator(new Android.Views.Animations.DecelerateInterpolator(1.65f))
                ?.Start();
        }
    }
}
