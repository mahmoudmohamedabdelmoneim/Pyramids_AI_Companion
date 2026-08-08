namespace AndroidApp1
{
    [Activity(Label = "@string/access_pass_title")]
    public class AccessPassActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_access_pass);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
            FindViewById<Android.Widget.TextView>(Resource.Id.egyptians_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(EgyptianTicketsActivity)));
            FindViewById<Android.Widget.TextView>(Resource.Id.non_egyptians_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(NonEgyptianTicketsActivity)));

            AnimateEntrance(Resource.Id.access_pass_copy, 0, 24);
            AnimateEntrance(Resource.Id.visitor_type_options, 120, 36);
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
