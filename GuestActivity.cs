namespace AndroidApp1
{
    [Activity(Label = "@string/guest_screen_title")]
    public class GuestActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_guest);

            FindViewById<Android.Widget.TextView>(Resource.Id.guest_sign_up_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(SignUpActivity)));

            ConfigurePyramidsVideo();
        }

        private void ConfigurePyramidsVideo()
        {
            var video = FindViewById<VideoView>(Resource.Id.pyramids_video);
            var placeholder = FindViewById<Android.Widget.TextView>(Resource.Id.video_placeholder);
            var videoResourceId = Resources?.GetIdentifier("pyramids_video", "raw", PackageName) ?? 0;

            if (video is null || videoResourceId == 0)
            {
                return;
            }

            video.SetVideoURI(Android.Net.Uri.Parse($"android.resource://{PackageName}/{videoResourceId}"));
            video.Prepared += (_, _) =>
            {
                if (placeholder is not null)
                {
                    placeholder.Visibility = Android.Views.ViewStates.Gone;
                }
                video.Start();
            };
            video.Completion += (_, _) =>
            {
                video.SeekTo(0);
                video.Start();
            };
        }

    }
}
