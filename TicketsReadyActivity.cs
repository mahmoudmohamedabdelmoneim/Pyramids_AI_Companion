namespace AndroidApp1
{
    [Activity(Label = "@string/tickets_ready_title_plural")]
    public class TicketsReadyActivity : Activity
    {
        private IReadOnlyList<ETicket>? _tickets;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_tickets_ready);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();

            _tickets = CartStore.IssuedTickets;
            FindViewById<Android.Widget.TextView>(Resource.Id.tickets_ready_title)!.Text = GetString(
                _tickets.Count == 1
                    ? Resource.String.tickets_ready_title_singular
                    : Resource.String.tickets_ready_title_plural);

            FindViewById<Android.Widget.TextView>(Resource.Id.download_tickets_button)!.Click += (_, _) =>
                DownloadTickets();
            FindViewById<Android.Widget.TextView>(Resource.Id.next_after_pdf_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(GreatGateActivity)));
        }

        private void DownloadTickets()
        {
            if (_tickets is null || _tickets.Count == 0)
            {
                return;
            }

            var path = TicketPdfGenerator.Generate(this, _tickets);
            Android.Widget.Toast.MakeText(
                this,
                $"Tickets saved to {path}",
                Android.Widget.ToastLength.Long)?.Show();

            var nextButton = FindViewById<Android.Widget.TextView>(Resource.Id.next_after_pdf_button)!;
            if (nextButton.Visibility == Android.Views.ViewStates.Visible)
            {
                return;
            }

            nextButton.Alpha = 0f;
            nextButton.TranslationY = 20f;
            nextButton.Visibility = Android.Views.ViewStates.Visible;
            nextButton.Animate()
                ?.Alpha(1f)
                ?.TranslationY(0f)
                ?.SetDuration(350)
                ?.SetInterpolator(new Android.Views.Animations.DecelerateInterpolator())
                ?.Start();
        }
    }
}
