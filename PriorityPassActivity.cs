namespace AndroidApp1
{
    [Activity(Label = "@string/priority_pass_title")]
    public class PriorityPassActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_priority_pass);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
            FindViewById<Android.Widget.TextView>(Resource.Id.wander_tour_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(WanderTourActivity)));
            FindViewById<Android.Widget.TextView>(Resource.Id.wander_plus_tour_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(WanderPlusTourActivity)));
            FindViewById<Android.Widget.TextView>(Resource.Id.express_tour_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(ExpressTourActivity)));
            FindViewById<Android.Widget.TextView>(Resource.Id.express_plus_tour_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(ExpressPlusTourActivity)));
            FindViewById<Android.Widget.TextView>(Resource.Id.archaeological_group_tour_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(ArchaeologicalSupplementTourActivity)));
            FindViewById<Android.Widget.TextView>(Resource.Id.pharaohs_tour_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(PharaohsTourActivity)));

            AnimateEntrance(Resource.Id.priority_pass_copy, 0, 24);
            AnimateEntrance(Resource.Id.priority_tour_options, 110, 36);
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
