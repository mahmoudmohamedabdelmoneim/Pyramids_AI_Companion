using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Android.Util;
using Android.Widget;

namespace AndroidApp1
{
    [Activity(Label = "@string/great_gate_title")]
    public class GreatGateActivity : Activity, ILocationListener
    {
        private const int LocationPermissionRequestCode = 102;
        private const double TicketOfficeLatitude = 29.97544;
        private const double TicketOfficeLongitude = 31.14085;
        private const float ArrivalRadiusMetres = 300f;
        private const long CurrentLocationMaxAgeMilliseconds = 30_000;
        private const long LocationLookupTimeoutMilliseconds = 8_000;

        private TextView? _message;
        private TextView? _locationPrompt;
        private TextView? _bringMeThereButton;
        private Location? _currentLocation;
        private bool _openDirectionsWhenLocationAvailable;
        private readonly LocalTourVoiceService _voiceService = LocalTourVoiceService.Shared;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_great_gate);

            FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
            FindViewById<TextView>(Resource.Id.start_ai_tour_guide_button)!.Click += (_, _) =>
            {
                // Begin preparation immediately, while the visitor reads the next screen.
                _ = WarmUpVoiceAsync();
                StartActivity(new Android.Content.Intent(this, typeof(TourIntroductionActivity)));
            };
            FindViewById<TextView>(Resource.Id.bring_me_there_button)!.Click += (_, _) =>
                RequestLocationThenOpenMaps();

            _message = FindViewById<TextView>(Resource.Id.great_gate_message);
            _locationPrompt = FindViewById<TextView>(Resource.Id.great_gate_location_prompt);
            _bringMeThereButton = FindViewById<TextView>(Resource.Id.bring_me_there_button);
        }

        protected override void OnResume()
        {
            base.OnResume();
            UpdateArrivalState();
            StartLocationUpdates();
        }

        protected override void OnPause()
        {
            StopLocationUpdates();
            base.OnPause();
        }

        public override void OnRequestPermissionsResult(
            int requestCode,
            string[] permissions,
            Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode != LocationPermissionRequestCode)
            {
                return;
            }

            UpdateArrivalState();

            if (!_openDirectionsWhenLocationAvailable)
            {
                return;
            }

            if (HasLocationPermission())
            {
                OpenDirectionsFromCurrentLocation();
                return;
            }

            _openDirectionsWhenLocationAvailable = false;
            Toast.MakeText(this, Resource.String.location_required_for_directions, ToastLength.Long)?.Show();
        }

        private void RequestLocationThenOpenMaps()
        {
            if (HasLocationPermission())
            {
                OpenDirectionsFromCurrentLocation();
                return;
            }

            _openDirectionsWhenLocationAvailable = true;
            RequestPermissions(
                new[]
                {
                    Android.Manifest.Permission.AccessFineLocation,
                    Android.Manifest.Permission.AccessCoarseLocation
                },
                LocationPermissionRequestCode);
        }

        private void UpdateArrivalState()
        {
            if (_message is null || _locationPrompt is null || _bringMeThereButton is null)
            {
                return;
            }

            if (!HasLocationPermission())
            {
                _message.SetText(Resource.String.great_gate_location_disabled);
                _locationPrompt.Visibility = Android.Views.ViewStates.Visible;
                _bringMeThereButton.Visibility = Android.Views.ViewStates.Visible;
                return;
            }

            var location = _currentLocation ?? GetLastKnownLocation();
            var isAtTicketOffice = location is not null &&
                location.DistanceTo(new Location("great_gate")
                {
                    Latitude = TicketOfficeLatitude,
                    Longitude = TicketOfficeLongitude
                }) <= ArrivalRadiusMetres;

            _message.SetText(isAtTicketOffice
                ? Resource.String.great_gate_welcome
                : Resource.String.great_gate_too_far);
            _locationPrompt.Visibility = Android.Views.ViewStates.Gone;
            _bringMeThereButton.Visibility = isAtTicketOffice
                ? Android.Views.ViewStates.Gone
                : Android.Views.ViewStates.Visible;
        }

        private bool HasLocationPermission()
        {
            return CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted ||
                CheckSelfPermission(Android.Manifest.Permission.AccessCoarseLocation) == Permission.Granted;
        }

        private Location? GetLastKnownLocation()
        {
            if (!HasLocationPermission())
            {
                return null;
            }

            var locationManager = GetSystemService(LocationService) as LocationManager;
            if (locationManager is null)
            {
                return null;
            }

            try
            {
                Location? latestLocation = null;
                foreach (var provider in new[]
                         {
                             LocationManager.GpsProvider,
                             LocationManager.NetworkProvider,
                             LocationManager.PassiveProvider
                         })
                {
                    var candidate = locationManager.GetLastKnownLocation(provider);
                    if (candidate is not null &&
                        (latestLocation is null || candidate.Time > latestLocation.Time))
                    {
                        latestLocation = candidate;
                    }
                }

                return latestLocation;
            }
            catch (Java.Lang.SecurityException)
            {
                // Permission may be denied or revoked while the activity is active.
                return null;
            }
        }

        private void StartLocationUpdates()
        {
            if (!HasLocationPermission())
            {
                return;
            }

            var locationManager = GetSystemService(LocationService) as LocationManager;
            if (locationManager is null)
            {
                return;
            }

            try
            {
                foreach (var provider in new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider })
                {
                    if (locationManager.IsProviderEnabled(provider))
                    {
                        locationManager.RequestLocationUpdates(provider, 1_000, 1, this);
                    }
                }
            }
            catch (Java.Lang.SecurityException)
            {
                // The user can revoke permission from the system dialog or Settings at any time.
            }
        }

        private void StopLocationUpdates()
        {
            try
            {
                (GetSystemService(LocationService) as LocationManager)?.RemoveUpdates(this);
            }
            catch (Java.Lang.SecurityException)
            {
                // No further action is needed when permission was revoked.
            }
        }

        public void OnLocationChanged(Location location)
        {
            _currentLocation = location;
            UpdateArrivalState();

            if (_openDirectionsWhenLocationAvailable)
            {
                _openDirectionsWhenLocationAvailable = false;
                OpenInAppDirections(location);
            }
        }

        public void OnProviderDisabled(string provider)
        {
        }

        public void OnProviderEnabled(string provider)
        {
        }

        private async Task WarmUpVoiceAsync()
        {
            try
            {
                await _voiceService.WarmUpAsync(ApplicationContext!);
            }
            catch (Exception exception)
            {
                // Loading voice must never prevent the visitor from entering the tour flow.
                Log.Error("GreatGateVoice", exception.ToString());
            }
        }

#pragma warning disable CS0618
        public void OnStatusChanged(string? provider, Availability status, Bundle? extras)
        {
        }
#pragma warning restore CS0618

        private void OpenDirectionsFromCurrentLocation()
        {
            if (_currentLocation is { } currentLocation && HasRecentLocation(currentLocation))
            {
                _openDirectionsWhenLocationAvailable = false;
                OpenInAppDirections(currentLocation);
                return;
            }

            _openDirectionsWhenLocationAvailable = true;
            StartLocationUpdates();
            Toast.MakeText(this, Resource.String.finding_current_location, ToastLength.Short)?.Show();

            new Handler(Looper.MainLooper!).PostDelayed(() =>
            {
                if (IsFinishing || !_openDirectionsWhenLocationAvailable)
                {
                    return;
                }

                _openDirectionsWhenLocationAvailable = false;
                Toast.MakeText(this, Resource.String.location_required_for_directions, ToastLength.Long)?.Show();
            }, LocationLookupTimeoutMilliseconds);
        }

        private static bool HasRecentLocation(Location location)
        {
            return Java.Lang.JavaSystem.CurrentTimeMillis() - location.Time <=
                CurrentLocationMaxAgeMilliseconds;
        }

        private void OpenInAppDirections(Location origin)
        {
            var intent = new Android.Content.Intent(this, typeof(DirectionsMapActivity));
            intent.PutExtra(DirectionsMapActivity.OriginLatitudeExtra, origin.Latitude);
            intent.PutExtra(DirectionsMapActivity.OriginLongitudeExtra, origin.Longitude);

            StartActivity(intent);
        }
    }
}
