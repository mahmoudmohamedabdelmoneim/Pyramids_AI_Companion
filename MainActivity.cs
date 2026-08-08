namespace AndroidApp1
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            // Add giza_photo_one.jpg and giza_photo_two.jpg to Resources/drawable.
            // If they exist, they replace the designed placeholders automatically.
            LoadTourImage(Resource.Id.photo_one, "giza_photo_one");
            LoadTourImage(Resource.Id.photo_two, "giza_photo_two");

            AnimateEntrance(Resource.Id.photo_card_one, 0, 28);
            AnimateEntrance(Resource.Id.photo_card_two, 110, 32);
            AnimateEntrance(Resource.Id.copy_block, 220, 38);
            AnimateEntrance(Resource.Id.gold_rule, 305, 18);
            AnimateEntrance(Resource.Id.next_button, 380, 22);

            // Resolve the action as a base View. This remains valid if the XML control is
            // later upgraded from a TextView to another button implementation.
            var nextButton = FindViewById(Resource.Id.next_button);
            if (nextButton is not null)
            {
                nextButton.Click += (_, _) =>
                    StartActivity(new Android.Content.Intent(this, typeof(AccountActivity)));
            }
        }

        private void LoadTourImage(int imageViewId, string resourceName)
        {
            var resourceId = Resources?.GetIdentifier(resourceName, "drawable", PackageName) ?? 0;
            if (resourceId != 0)
            {
                FindViewById<ImageView>(imageViewId)?.SetImageResource(resourceId);
            }
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
                ?.SetDuration(760)
                ?.SetInterpolator(new Android.Views.Animations.DecelerateInterpolator(1.7f))
                ?.Start();
        }
    }
}
