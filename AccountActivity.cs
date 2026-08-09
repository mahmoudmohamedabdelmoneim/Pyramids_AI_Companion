namespace AndroidApp1
{
    [Activity(Label = "@string/account_screen_title")]
    public class AccountActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_account);

            AnimateEntrance(Resource.Id.account_brand, 0, 18);
            AnimateEntrance(Resource.Id.account_copy, 100, 34);
            AnimateEntrance(Resource.Id.account_actions, 210, 42);

            FindViewById<Android.Widget.TextView>(Resource.Id.guest_continue_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(GuestActivity)));

            FindViewById<Android.Widget.TextView>(Resource.Id.create_account_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(SignUpActivity)));

            FindViewById<Android.Widget.TextView>(Resource.Id.sign_in_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(SignInActivity)));

            FindViewById<Android.Widget.TextView>(Resource.Id.employee_login_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(EmployeeLoginActivity)));

            FindViewById<Android.Widget.TextView>(Resource.Id.admin_login_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(AdminLoginActivity)));
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
                ?.SetDuration(720)
                ?.SetInterpolator(new Android.Views.Animations.DecelerateInterpolator(1.65f))
                ?.Start();
        }
    }
}
