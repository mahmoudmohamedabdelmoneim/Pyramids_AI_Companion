using Android.OS;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/panorama_station_title")]
public sealed class PanoramaArrivalActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_panorama_arrival);

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
        FindViewById<TextView>(Resource.Id.request_ancestor_ride_button)!.Click += (_, _) =>
            StartActivity(new Android.Content.Intent(this, typeof(RideOffersActivity)));
        FindViewById<TextView>(Resource.Id.skip_and_proceed_button)!.Click += (_, _) => SkipAndProceed();
        FindViewById<TextView>(Resource.Id.report_something_wrong_button)!.Click += (_, _) =>
            StartActivity(new Android.Content.Intent(this, typeof(SupportChatActivity)));
        FindViewById<TextView>(Resource.Id.ask_for_help_report_emergency_button)!.Click += (_, _) =>
        {
            var intent = new Android.Content.Intent(this, typeof(SupportChatActivity));
            intent.PutExtra(SupportChatActivity.EmergencyModeExtra, true);
            StartActivity(intent);
        };
    }

    private void SkipAndProceed()
    {
        StartActivity(new Android.Content.Intent(this, typeof(AccessPassTransferActivity)));
    }
}
