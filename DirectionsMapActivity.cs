using Android.OS;
using Android.Webkit;
using Android.Widget;
using System.Globalization;
using AndroidUri = Android.Net.Uri;

namespace AndroidApp1
{
    [Activity(Label = "@string/directions_map_title")]
    public class DirectionsMapActivity : Activity
    {
        public const string OriginLatitudeExtra = "origin_latitude";
        public const string OriginLongitudeExtra = "origin_longitude";

        private WebView? _mapView;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_directions_map);

            FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();

            _mapView = FindViewById<WebView>(Resource.Id.directions_map_view)!;
            _mapView.Settings.JavaScriptEnabled = true;
            _mapView.Settings.DomStorageEnabled = true;
            _mapView.Settings.BuiltInZoomControls = true;
            _mapView.Settings.DisplayZoomControls = false;
            _mapView.SetWebViewClient(new InAppMapWebViewClient());
            _mapView.LoadUrl(BuildDirectionsUrl());
        }

        protected override void OnResume()
        {
            base.OnResume();
            _mapView?.OnResume();
        }

        protected override void OnPause()
        {
            _mapView?.OnPause();
            base.OnPause();
        }

        public override void OnBackPressed()
        {
            if (_mapView?.CanGoBack() == true)
            {
                _mapView.GoBack();
                return;
            }

            Finish();
        }

        private string BuildDirectionsUrl()
        {
            const string destination = "29.97544,31.14085";
            var url = $"https://www.google.com/maps/dir/?api=1&destination={AndroidUri.Encode(destination)}&travelmode=driving";

            if (Intent?.HasExtra(OriginLatitudeExtra) == true &&
                Intent.HasExtra(OriginLongitudeExtra))
            {
                var latitude = Intent.GetDoubleExtra(OriginLatitudeExtra, 0);
                var longitude = Intent.GetDoubleExtra(OriginLongitudeExtra, 0);
                var origin = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{latitude},{longitude}");
                url += $"&origin={AndroidUri.Encode(origin)}";
            }

            return url;
        }

        private sealed class InAppMapWebViewClient : WebViewClient
        {
            public override bool ShouldOverrideUrlLoading(WebView? view, string? url)
            {
                return LoadInApp(view, url);
            }

            public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
            {
                return LoadInApp(view, request?.Url?.ToString());
            }

            private static bool LoadInApp(WebView? view, string? url)
            {
                if (string.IsNullOrWhiteSpace(url) ||
                    (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                     !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                view?.LoadUrl(url);
                return true;
            }
        }
    }
}
