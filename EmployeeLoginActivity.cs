namespace AndroidApp1
{
    [Activity(Label = "@string/employee_login_title")]
    public class EmployeeLoginActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_employee_login);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();

            AnimateEntrance(Resource.Id.employee_copy, 0, 24);
            AnimateEntrance(Resource.Id.employee_options, 110, 36);
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
