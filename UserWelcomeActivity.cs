namespace AndroidApp1
{
    [Activity(Label = "@string/user_welcome_title")]
    public class UserWelcomeActivity : Activity
    {
        private const int LocationPermissionRequestCode = 101;

        private Android.Views.View? _locationPrompt;
        private Android.Views.View? _passSelection;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_user_welcome);

            _locationPrompt = FindViewById<Android.Views.View>(Resource.Id.location_prompt);
            _passSelection = FindViewById<Android.Views.View>(Resource.Id.pass_selection);

            FindViewById<Android.Widget.TextView>(Resource.Id.share_location_button)!.Click += (_, _) =>
                RequestLocationThenContinue();
            FindViewById<Android.Widget.TextView>(Resource.Id.continue_without_location_button)!.Click += (_, _) =>
                ShowPassSelection();
            FindViewById<Android.Widget.TextView>(Resource.Id.access_pass_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(AccessPassActivity)));
            FindViewById<Android.Widget.TextView>(Resource.Id.priority_pass_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(PriorityPassActivity)));

            AnimateEntrance(Resource.Id.welcome_copy, 0, 24);
            AnimateEntrance(Resource.Id.location_prompt, 110, 34);
        }

        public override void OnRequestPermissionsResult(
            int requestCode,
            string[] permissions,
            Android.Content.PM.Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == LocationPermissionRequestCode)
            {
                ShowPassSelection();
            }
        }

        private void RequestLocationThenContinue()
        {
            if (HasLocationPermission())
            {
                ShowPassSelection();
                return;
            }

            RequestPermissions(
                new[]
                {
                    Android.Manifest.Permission.AccessFineLocation,
                    Android.Manifest.Permission.AccessCoarseLocation
                },
                LocationPermissionRequestCode);
        }

        private bool HasLocationPermission()
        {
            return CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) ==
                Android.Content.PM.Permission.Granted ||
                CheckSelfPermission(Android.Manifest.Permission.AccessCoarseLocation) ==
                Android.Content.PM.Permission.Granted;
        }

        private void ShowPassSelection()
        {
            if (_passSelection?.Visibility == Android.Views.ViewStates.Visible)
            {
                return;
            }

            if (_locationPrompt is not null)
            {
                _locationPrompt.Visibility = Android.Views.ViewStates.Gone;
            }

            if (_passSelection is null)
            {
                return;
            }

            _passSelection.Visibility = Android.Views.ViewStates.Visible;
            _passSelection.Alpha = 0f;
            _passSelection.TranslationY = 28f;
            _passSelection.Animate()
                ?.Alpha(1f)
                ?.TranslationY(0f)
                ?.SetDuration(620)
                ?.SetInterpolator(new Android.Views.Animations.DecelerateInterpolator(1.65f))
                ?.Start();
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
