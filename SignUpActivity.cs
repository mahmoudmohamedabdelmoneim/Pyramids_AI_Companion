namespace AndroidApp1
{
    [Activity(Label = "@string/sign_up_title")]
    public class SignUpActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_sign_up);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();

            var signUpOptionIds = new[]
            {
                Resource.Id.sign_up_google_button,
                Resource.Id.sign_up_microsoft_button,
                Resource.Id.sign_up_phone_button,
                Resource.Id.sign_up_email_button
            };

            foreach (var optionId in signUpOptionIds)
            {
                FindViewById<Android.Widget.TextView>(optionId)!.Click += (_, _) =>
                    StartActivity(new Android.Content.Intent(this, typeof(UserWelcomeActivity)));
            }

            AnimateEntrance(Resource.Id.sign_up_copy, 0, 24);
            AnimateEntrance(Resource.Id.sign_up_options, 110, 36);
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
