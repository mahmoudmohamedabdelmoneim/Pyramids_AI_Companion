using Android.Widget;

namespace AndroidApp1
{
    [Activity(Label = "@string/ai_trip_introduction_title")]
    public class TourIntroductionActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_tour_introduction);

            FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
            FindViewById<TextView>(Resource.Id.ok_start_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(AiTourGuideActivity)));
        }
    }
}
