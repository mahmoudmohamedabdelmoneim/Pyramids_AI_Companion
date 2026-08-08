namespace AndroidApp1
{
    [Activity(Label = "@string/non_egyptian_tickets_title")]
    public class NonEgyptianTicketsActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_non_egyptian_tickets);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();

            var cartButton = FindViewById<Android.Widget.TextView>(Resource.Id.add_to_cart_button)!;
            var ticketSelection = new TicketCartSelection(cartButton);
            var regularTickets = ticketSelection.BindCounter(this,
                Resource.Id.regular_minus_button,
                Resource.Id.regular_ticket_count,
                Resource.Id.regular_plus_button);
            var studentTickets = ticketSelection.BindCounter(this,
                Resource.Id.student_minus_button,
                Resource.Id.student_ticket_count,
                Resource.Id.student_plus_button);
            cartButton.Click += (_, _) =>
                CartActions.AddAndOpenCart(this,
                    new CartSelection("Access Pass - Non-Egyptian Regular (EGP 700)", "Gates 2, 3 or 4", regularTickets),
                    new CartSelection("Access Pass - Non-Egyptian Student (EGP 350)", "Gate 5", studentTickets));

            AnimateEntrance(Resource.Id.ticket_options, 0, 28);
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
