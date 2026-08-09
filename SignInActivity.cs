namespace AndroidApp1
{
    [Activity(Label = "@string/sign_in_title")]
    public class SignInActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_sign_in);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();

            var signInOptionIds = new[]
            {
                Resource.Id.sign_in_google_button,
                Resource.Id.sign_in_microsoft_button,
                Resource.Id.sign_in_phone_button,
                Resource.Id.sign_in_email_button
            };

            foreach (var optionId in signInOptionIds)
            {
                FindViewById<Android.Widget.TextView>(optionId)!.Click += (_, _) =>
                    StartActivity(new Android.Content.Intent(this, typeof(UserWelcomeActivity)));
            }

            AnimateEntrance(Resource.Id.sign_in_copy, 0, 24);
            AnimateEntrance(Resource.Id.sign_in_options, 110, 36);
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
