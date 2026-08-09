using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/giza_plateau_map_title")]
public sealed class TripProgressActivity : Activity, ILocationListener
{
    private const int LocationPermissionRequestCode = 103;
    private const double NorthLatitude = 29.9850;
    private const double SouthLatitude = 29.9590;
    private const double WestLongitude = 31.1090;
    private const double EastLongitude = 31.1460;

    private LocationManager? _locationManager;
    private GizaPlateauMapView? _mapView;
    private TextView? _status;
    private bool _isRequestingLocationUpdates;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_trip_progress);

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
        _status = FindViewById<TextView>(Resource.Id.trip_progress_status);
        _mapView = FindViewById<GizaPlateauMapView>(Resource.Id.trip_progress_map);
        _mapView!.TrackVisitorLocationAutomatically = false;

        EnsureLocationPermission();
    }

    protected override void OnResume()
    {
        base.OnResume();
        StartLocationUpdates();
    }

    protected override void OnPause()
    {
        StopLocationUpdates();
        base.OnPause();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != LocationPermissionRequestCode)
        {
            return;
        }

        if (HasLocationPermission())
        {
            StartLocationUpdates();
        }
        else
        {
            _status?.SetText(Resource.String.giza_map_location_denied);
        }
    }

    public void OnLocationChanged(Location location)
    {
        if (!IsInsideGizaPlateau(location.Latitude, location.Longitude))
        {
            _mapView?.ClearVisitorLocation();
            _status?.SetText(Resource.String.giza_map_location_unavailable);
            return;
        }

        _mapView?.SetVisitorLocation(location.Latitude, location.Longitude);
        _status?.SetText(Resource.String.giza_map_location_displayed);
    }

    public void OnProviderDisabled(string provider) { }

    public void OnProviderEnabled(string provider) { }

#pragma warning disable CS0618
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }
#pragma warning restore CS0618

    private void EnsureLocationPermission()
    {
        if (!HasLocationPermission())
        {
            RequestPermissions(
                [Android.Manifest.Permission.AccessFineLocation, Android.Manifest.Permission.AccessCoarseLocation],
                LocationPermissionRequestCode);
        }
    }

    private bool HasLocationPermission() =>
        CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted ||
        CheckSelfPermission(Android.Manifest.Permission.AccessCoarseLocation) == Permission.Granted;

    private void StartLocationUpdates()
    {
        if (!HasLocationPermission() || _isRequestingLocationUpdates)
        {
            return;
        }

        _locationManager ??= GetSystemService(LocationService) as LocationManager;
        if (_locationManager is null)
        {
            return;
        }

        try
        {
            var registeredWithAProvider = false;
            foreach (var provider in new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider })
            {
                if (_locationManager.IsProviderEnabled(provider))
                {
                    _locationManager.RequestLocationUpdates(provider, 2_000, 3, this);
                    registeredWithAProvider = true;
                }
            }

            _isRequestingLocationUpdates = registeredWithAProvider;

            var lastKnownLocation = GetMostRecentLocation();
            if (lastKnownLocation is not null)
            {
                OnLocationChanged(lastKnownLocation);
            }
        }
        catch (Java.Lang.SecurityException)
        {
            _status?.SetText(Resource.String.giza_map_location_denied);
        }
    }

    private void StopLocationUpdates()
    {
        try
        {
            _locationManager?.RemoveUpdates(this);
            _isRequestingLocationUpdates = false;
        }
        catch (Java.Lang.SecurityException)
        {
            // The user may revoke location permission while this screen is open.
        }
    }

    private Location? GetMostRecentLocation()
    {
        if (_locationManager is null)
        {
            return null;
        }

        try
        {
            Location? latest = null;
            foreach (var provider in new[]
                     {
                         LocationManager.GpsProvider,
                         LocationManager.NetworkProvider,
                         LocationManager.PassiveProvider
                     })
            {
                var candidate = _locationManager.GetLastKnownLocation(provider);
                if (candidate is not null && (latest is null || candidate.Time > latest.Time))
                {
                    latest = candidate;
                }
            }

            return latest;
        }
        catch (Java.Lang.SecurityException)
        {
            return null;
        }
    }

    private static bool IsInsideGizaPlateau(double latitude, double longitude) =>
        latitude is >= SouthLatitude and <= NorthLatitude &&
        longitude is >= WestLongitude and <= EastLongitude;
}
