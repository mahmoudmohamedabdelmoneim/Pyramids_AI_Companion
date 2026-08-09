using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Locations;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;

namespace AndroidApp1;

/// <summary>
/// Draws a bundled geographic map of the Great Gate and Giza Plateau.
/// The basemap, markers, and touch navigation are fully offline at runtime.
/// </summary>
[Register("com.companyname.androidapp1.GizaPlateauMapView")]
public sealed class GizaPlateauMapView : View, ILocationListener
{
    private const int TileSize = 256;
    private const int OfflineMapZoom = 15;
    private const int OfflineMapLeftTile = 19215;
    private const int OfflineMapTopTile = 13521;
    private const int OfflineMapTileColumns = 4;
    private const int OfflineMapTileRows = 3;
    private const int OfflineMapPixelWidth = OfflineMapTileColumns * TileSize;
    private const int OfflineMapPixelHeight = OfflineMapTileRows * TileSize;
    private const float MinimumScale = 1f;
    private const float DefaultScale = 1.2f;
    private const float MaximumScale = 4f;
    private const long DoubleTapWindowMilliseconds = 320;

    // Marker centers are traced in the 4000 x 1848 map.png reference image.
    // A single similarity transform places the whole drawing over the offline basemap,
    // preserving the reference positions, proportions, angles, and route shapes.
    private static readonly ReferencePoint DrawingStartReference = new(859.1f, 206.3f);
    private static readonly ReferencePoint DrawingEndReference = new(3192.4f, 837.8f);
    private static readonly GeoPoint DrawingStartMapAnchor = new(29.965518, 31.110795);
    private static readonly GeoPoint DrawingEndMapAnchor = new(29.97355, 31.14225);

    private static readonly StationMarker[] Stations =
    [
        new("1", "Visitors' Center Station", 859.1f, 206.3f),
        new("2", "Panorama Station", 1596.8f, 207.6f),
        new("3", "King Menkaure Station", 2108.3f, 335.1f),
        new("4", "King Khafre Station", 2666.4f, 449.6f),
        new("5", "Sphinx Station", 3192.4f, 837.8f),
        new("6", "King Khufu Station", 2970.0f, 173.2f)
    ];

    // Additional lettered destinations shown on the supplied shuttle flyer.
    private static readonly DestinationMarker[] Destinations =
    [
        new("K", "King Khufu's Center", 2275.0f, 117.9f, "#315F86"),
        new("P", "9 Arena", 1689.5f, 964.0f, "#7B3146")
    ];

    // These polylines directly trace the four applicable colored paths in map.png.
    private static readonly RouteBranch[] Routes =
    [
        new("9 Arena", "#263E32",
        [
            new(859f, 206f),
            new(850f, 175f),
            new(865f, 145f),
            new(915f, 115f),
            new(970f, 110f),
            new(1035f, 130f),
            new(1140f, 180f),
            new(1260f, 230f),
            new(1380f, 260f),
            new(1430f, 285f),
            new(1415f, 340f),
            new(1360f, 390f),
            new(1280f, 420f),
            new(1220f, 465f),
            new(1195f, 525f),
            new(1170f, 600f),
            new(1130f, 690f),
            new(1210f, 705f),
            new(1320f, 720f),
            new(1420f, 745f),
            new(1490f, 790f),
            new(1550f, 850f),
            new(1610f, 920f),
            new(1689.5f, 964f)
        ]),
        new("Sphinx Station", "#C84D4F",
        [
            new(859f, 206f),
            new(850f, 170f),
            new(860f, 135f),
            new(900f, 105f),
            new(950f, 96f),
            new(1010f, 110f),
            new(1120f, 170f),
            new(1250f, 220f),
            new(1370f, 245f),
            new(1430f, 235f),
            new(1510f, 170f),
            new(1580f, 145f),
            new(1700f, 170f),
            new(1830f, 235f),
            new(1980f, 305f),
            new(2080f, 310f),
            new(2160f, 295f),
            new(2260f, 245f),
            new(2320f, 215f),
            new(2390f, 275f),
            new(2480f, 350f),
            new(2600f, 420f),
            new(2666.4f, 449.6f),
            new(2800f, 565f),
            new(2940f, 680f),
            new(3080f, 775f),
            new(3192.4f, 837.8f)
        ]),
        new("King Khufu's Center", "#4993CC",
        [
            new(859f, 206f),
            new(845f, 170f),
            new(855f, 130f),
            new(900f, 95f),
            new(970f, 90f),
            new(1030f, 110f),
            new(1130f, 160f),
            new(1260f, 215f),
            new(1380f, 235f),
            new(1460f, 210f),
            new(1530f, 150f),
            new(1600f, 130f),
            new(1700f, 150f),
            new(1850f, 220f),
            new(1980f, 285f),
            new(2080f, 290f),
            new(2160f, 270f),
            new(2240f, 230f),
            new(2280f, 180f),
            new(2275f, 117.9f)
        ]),
        new("King Khufu Station", "#67A86F",
        [
            new(2666.4f, 449.6f),
            new(2740f, 400f),
            new(2820f, 345f),
            new(2875f, 285f),
            new(2900f, 225f),
            new(2920f, 190f),
            new(2970f, 173.2f)
        ])
    ];

    private static readonly GeoPoint DefaultFocus = new(29.9738, 31.1268);
    private static readonly ReferencePoint SaddleManReference = new(1515f, 390f);

    // Illustrative only: no live shuttle feed is implied or used.
    private static readonly ReferenceBusMarker[] SampleBuses =
    [
        new("Bus1", 1120f, 170f),
        new("Bus2", 1360f, 390f),
        new("Bus3", 1980f, 285f),
        new("Bus4", 2800f, 565f),
        new("Bus5", 2875f, 285f)
    ];

    private readonly Bitmap? _backgroundMap;
    private readonly Bitmap? _horseCartIcon;
    private readonly Paint _bitmapPaint = new(PaintFlags.FilterBitmap);
    private readonly Paint _markerPaint = new(PaintFlags.AntiAlias);
    private readonly Paint _routePaint = new(PaintFlags.AntiAlias);
    private readonly Android.Graphics.Path _routePath = new();
    private readonly Paint _textPaint = new(PaintFlags.AntiAlias)
    {
        TextAlign = Paint.Align.Center
    };
    private readonly int _touchSlop;

    private LocationManager? _locationManager;
    private GeoPoint? _visitorPosition;
    private Bitmap? _saddleManPortrait;
    private string? _saddleManName;
    private string? _saddleRideType;
    private float _scale = DefaultScale;
    private float _translationX;
    private float _translationY;
    private float _lastTouchX;
    private float _lastTouchY;
    private float _downX;
    private float _downY;
    private float _pinchSpan;
    private long _lastTapUpTime;
    private int _selectedMarkerIndex = -1;
    private bool _gestureMoved;
    private bool _hasInitialViewport;
    private bool _showBusPills = true;
    private bool _focusOnSaddleMan;
    private bool _trackVisitorLocationAutomatically = true;
    private bool _isRequestingLocationUpdates;

    public GizaPlateauMapView(Context context)
        : this(context, null)
    {
    }

    public GizaPlateauMapView(Context context, IAttributeSet? attributes)
        : this(context, attributes, 0)
    {
    }

    public GizaPlateauMapView(Context context, IAttributeSet? attributes, int defStyle)
        : base(context, attributes, defStyle)
    {
        var options = new BitmapFactory.Options
        {
            InPreferredConfig = Bitmap.Config.Rgb565,
            InSampleSize = 1
        };
        _backgroundMap = BitmapFactory.DecodeResource(context.Resources, Resource.Drawable.giza_gate_offline_map, options);
        _horseCartIcon = BitmapFactory.DecodeResource(
            context.Resources,
            Resource.Drawable.ahmed_horse_cart_gold);
        _textPaint.SetTypeface(Typeface.Create(Typeface.Default, TypefaceStyle.Bold));
        _routePaint.SetStyle(Paint.Style.Stroke);
        _routePaint.StrokeCap = Paint.Cap.Round;
        _routePaint.StrokeJoin = Paint.Join.Round;
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 8;
        Clickable = true;
        Focusable = true;
        ContentDescription = context.GetString(Resource.String.giza_map_content_description);
    }

    public void SetVisitorLocation(double latitude, double longitude)
    {
        _visitorPosition = new GeoPoint(latitude, longitude);
        Invalidate();
    }

    public void ClearVisitorLocation()
    {
        if (_visitorPosition is null)
        {
            return;
        }

        _visitorPosition = null;
        Invalidate();
    }

    /// <summary>
    /// Keeps embedded maps synchronized with the visitor's permitted device location.
    /// Screens that already own location updates can disable this to avoid duplicate listeners.
    /// </summary>
    public bool TrackVisitorLocationAutomatically
    {
        get => _trackVisitorLocationAutomatically;
        set
        {
            if (_trackVisitorLocationAutomatically == value)
            {
                return;
            }

            _trackVisitorLocationAutomatically = value;
            if (!IsAttachedToWindow)
            {
                return;
            }

            if (value)
            {
                StartVisitorLocationUpdates();
            }
            else
            {
                StopVisitorLocationUpdates();
            }
        }
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        if (_trackVisitorLocationAutomatically)
        {
            StartVisitorLocationUpdates();
        }
    }

    protected override void OnDetachedFromWindow()
    {
        StopVisitorLocationUpdates();
        base.OnDetachedFromWindow();
    }

    public void OnLocationChanged(Location location) =>
        SetVisitorLocation(location.Latitude, location.Longitude);

    public void OnProviderDisabled(string provider) { }

    public void OnProviderEnabled(string provider) { }

#pragma warning disable CS0618
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }
#pragma warning restore CS0618

    private bool HasLocationPermission() =>
        Context?.CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted ||
        Context?.CheckSelfPermission(Android.Manifest.Permission.AccessCoarseLocation) == Permission.Granted;

    private void StartVisitorLocationUpdates()
    {
        if (!HasLocationPermission() || _isRequestingLocationUpdates || Context is null)
        {
            return;
        }

        _locationManager ??= Context.GetSystemService(Context.LocationService) as LocationManager;
        if (_locationManager is null)
        {
            return;
        }

        try
        {
            foreach (var provider in new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider })
            {
                if (_locationManager.IsProviderEnabled(provider))
                {
                    _locationManager.RequestLocationUpdates(provider, 2_000, 3, this);
                    _isRequestingLocationUpdates = true;
                }
            }

            var latest = GetMostRecentLocation();
            if (latest is not null)
            {
                OnLocationChanged(latest);
            }
        }
        catch (Java.Lang.SecurityException)
        {
            // Location is optional and may be revoked while a map is visible.
        }
    }

    private void StopVisitorLocationUpdates()
    {
        if (!_isRequestingLocationUpdates)
        {
            return;
        }

        try
        {
            _locationManager?.RemoveUpdates(this);
        }
        catch (Java.Lang.SecurityException)
        {
            // Location is optional and may be revoked while a map is visible.
        }
        finally
        {
            _isRequestingLocationUpdates = false;
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

    /// <summary>
    /// Places the booked guide beside Panorama Station for the prototype ride flow.
    /// </summary>
    public void SetSaddleManMarker(int portraitResource, string name, string rideType)
    {
        var options = new BitmapFactory.Options
        {
            InPreferredConfig = Bitmap.Config.Argb8888,
            InSampleSize = 4
        };

        var resources = Context?.Resources ?? throw new InvalidOperationException("Map resources are unavailable.");
        _saddleManPortrait = BitmapFactory.DecodeResource(resources, portraitResource, options);
        _saddleManName = name;
        _saddleRideType = rideType;
        _focusOnSaddleMan = true;
        if (Width > 0 && Height > 0)
        {
            ResetToSaddleManView();
        }
        else
        {
            Invalidate();
        }
    }

    /// <summary>
    /// Controls the illustrative shuttle labels without changing the underlying map,
    /// routes, stations, or destination markers.
    /// </summary>
    public bool ShowBusPills
    {
        get => _showBusPills;
        set
        {
            if (_showBusPills == value)
            {
                return;
            }

            _showBusPills = value;
            Invalidate();
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);

        var viewport = GetViewportBounds();
        if (viewport.Width() <= 0 || viewport.Height() <= 0)
        {
            return;
        }

        var mapBounds = GetFittedMapBounds(viewport);
        _markerPaint.Color = Color.ParseColor("#15231F");
        canvas.DrawRect(viewport, _markerPaint);

        var saveCount = canvas.Save();
        canvas.ClipRect(viewport);
        canvas.Translate(_translationX, _translationY);
        canvas.Scale(_scale, _scale, viewport.CenterX(), viewport.CenterY());

        if (_backgroundMap is not null)
        {
            canvas.DrawBitmap(_backgroundMap, null, mapBounds, _bitmapPaint);
        }
        else
        {
            _markerPaint.Color = Color.Rgb(55, 40, 25);
            canvas.DrawRect(mapBounds, _markerPaint);
        }

        canvas.RestoreToCount(saveCount);

        saveCount = canvas.Save();
        canvas.ClipRect(viewport);

        var stationRadius = Math.Clamp(viewport.Width() * 0.035f, 18f, 36f);
        var busWidth = Math.Clamp(viewport.Width() * 0.112f, 64f, 104f);

        DrawTripRoutes(canvas, mapBounds, viewport, stationRadius);

        if (_showBusPills)
        {
            foreach (var bus in SampleBuses)
            {
                var point = ToScreenPoint(bus, mapBounds, viewport);
                if (IsVisible(point, viewport, busWidth))
                {
                    DrawBus(canvas, point, bus.Label, busWidth);
                }
            }
        }

        foreach (var station in Stations)
        {
            var point = ToScreenPoint(station, mapBounds, viewport);
            if (IsVisible(point, viewport, stationRadius))
            {
                DrawStation(canvas, point, station.Number, stationRadius);
            }
        }

        foreach (var destination in Destinations)
        {
            var point = ToScreenPoint(destination, mapBounds, viewport);
            if (IsVisible(point, viewport, stationRadius))
            {
                DrawDestination(canvas, point, destination.Label, stationRadius, destination.ColorHex);
            }
        }

        if (_visitorPosition is { } visitor)
        {
            var point = ToScreenPoint(visitor, mapBounds, viewport);
            if (IsVisible(point, viewport, stationRadius))
            {
                DrawVisitor(canvas, point, stationRadius);
            }
        }

        if (!string.IsNullOrWhiteSpace(_saddleManName) && !string.IsNullOrWhiteSpace(_saddleRideType))
        {
            DrawSaddleMan(canvas, mapBounds, viewport, stationRadius);
        }

        DrawSelectedMarkerName(canvas, mapBounds, viewport, stationRadius);

        DrawOfflineBadge(canvas, viewport, stationRadius);
        canvas.RestoreToCount(saveCount);
    }

    public override bool OnTouchEvent(MotionEvent? motionEvent)
    {
        if (motionEvent is null)
        {
            return false;
        }

        switch (motionEvent.ActionMasked)
        {
            case MotionEventActions.Down:
                Parent?.RequestDisallowInterceptTouchEvent(true);
                _downX = _lastTouchX = motionEvent.GetX(0);
                _downY = _lastTouchY = motionEvent.GetY(0);
                _pinchSpan = 0f;
                _gestureMoved = false;
                break;

            case MotionEventActions.PointerDown:
                if (motionEvent.PointerCount >= 2)
                {
                    _pinchSpan = GetPinchSpan(motionEvent);
                    _gestureMoved = true;
                }
                break;

            case MotionEventActions.Move:
                if (motionEvent.PointerCount >= 2)
                {
                    var nextSpan = GetPinchSpan(motionEvent);
                    if (_pinchSpan > 0f && nextSpan > 0f)
                    {
                        var focusX = (motionEvent.GetX(0) + motionEvent.GetX(1)) / 2f;
                        var focusY = (motionEvent.GetY(0) + motionEvent.GetY(1)) / 2f;
                        SetScaleAround(_scale * nextSpan / _pinchSpan, focusX, focusY);
                    }

                    _pinchSpan = nextSpan;
                    _gestureMoved = true;
                }
                else if (motionEvent.PointerCount == 1)
                {
                    var x = motionEvent.GetX(0);
                    var y = motionEvent.GetY(0);
                    if (Math.Abs(x - _downX) > _touchSlop || Math.Abs(y - _downY) > _touchSlop)
                    {
                        _gestureMoved = true;
                    }

                    if (_scale > MinimumScale)
                    {
                        _translationX += x - _lastTouchX;
                        _translationY += y - _lastTouchY;
                        ConstrainTranslation();
                        Invalidate();
                    }

                    _lastTouchX = x;
                    _lastTouchY = y;
                }
                break;

            case MotionEventActions.PointerUp:
                _gestureMoved = true;
                _pinchSpan = 0f;
                var remainingIndex = motionEvent.ActionIndex == 0 ? 1 : 0;
                if (remainingIndex < motionEvent.PointerCount)
                {
                    _lastTouchX = motionEvent.GetX(remainingIndex);
                    _lastTouchY = motionEvent.GetY(remainingIndex);
                }
                break;

            case MotionEventActions.Up:
                if (!_gestureMoved)
                {
                    var tappedMarker = SelectMarkerAt(motionEvent.GetX(0), motionEvent.GetY(0));
                    if (tappedMarker)
                    {
                        _lastTapUpTime = 0;
                    }
                    else
                    {
                        var now = SystemClock.UptimeMillis();
                        if (_lastTapUpTime > 0 && now - _lastTapUpTime <= DoubleTapWindowMilliseconds)
                        {
                            if (_scale > DefaultScale + 0.05f)
                            {
                                ResetToDefaultView();
                            }
                            else
                            {
                                SetScaleAround(2f, motionEvent.GetX(0), motionEvent.GetY(0));
                            }

                            _lastTapUpTime = 0;
                        }
                        else
                        {
                            _lastTapUpTime = now;
                        }
                    }

                    PerformClick();
                }

                Parent?.RequestDisallowInterceptTouchEvent(false);
                _pinchSpan = 0f;
                break;

            case MotionEventActions.Cancel:
                Parent?.RequestDisallowInterceptTouchEvent(false);
                _pinchSpan = 0f;
                break;
        }

        return true;
    }

    public override bool PerformClick()
    {
        base.PerformClick();
        return true;
    }

    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);
        if (!_hasInitialViewport && width > 0 && height > 0)
        {
            _hasInitialViewport = true;
            if (_focusOnSaddleMan)
            {
                ResetToSaddleManView();
            }
            else
            {
                ResetToDefaultView();
            }
            return;
        }

        ConstrainTranslation();
    }

    private RectF GetViewportBounds() =>
        new(PaddingLeft, PaddingTop, Width - PaddingRight, Height - PaddingBottom);

    private static RectF GetFittedMapBounds(RectF viewport)
    {
        const float mapAspectRatio = (float)OfflineMapPixelWidth / OfflineMapPixelHeight;
        var viewportAspectRatio = viewport.Width() / viewport.Height();

        if (viewportAspectRatio > mapAspectRatio)
        {
            var width = viewport.Width();
            var height = width / mapAspectRatio;
            var top = viewport.CenterY() - height / 2f;
            return new RectF(viewport.Left, top, viewport.Right, top + height);
        }

        var fittedHeight = viewport.Height();
        var fittedWidth = fittedHeight * mapAspectRatio;
        var left = viewport.CenterX() - fittedWidth / 2f;
        return new RectF(left, viewport.Top, left + fittedWidth, viewport.Bottom);
    }

    private Point ToScreenPoint(ReferenceBusMarker marker, RectF mapBounds, RectF viewport) =>
        ToScreenPoint(new ReferencePoint(marker.ReferenceX, marker.ReferenceY), mapBounds, viewport);

    private Point ToScreenPoint(StationMarker marker, RectF mapBounds, RectF viewport) =>
        ToScreenPoint(new ReferencePoint(marker.ReferenceX, marker.ReferenceY), mapBounds, viewport);

    private Point ToScreenPoint(DestinationMarker marker, RectF mapBounds, RectF viewport) =>
        ToScreenPoint(new ReferencePoint(marker.ReferenceX, marker.ReferenceY), mapBounds, viewport);

    private Point ToScreenPoint(GeoPoint point, RectF mapBounds, RectF viewport)
    {
        var mapPoint = ToMapPoint(point, mapBounds);
        return MapPointToScreen(mapPoint, viewport);
    }

    private Point ToScreenPoint(ReferencePoint point, RectF mapBounds, RectF viewport)
    {
        var mapPoint = ToReferenceMapPoint(point, mapBounds);
        return MapPointToScreen(mapPoint, viewport);
    }

    private Point MapPointToScreen(Point mapPoint, RectF viewport) =>
        new(
            viewport.CenterX() + (mapPoint.X - viewport.CenterX()) * _scale + _translationX,
            viewport.CenterY() + (mapPoint.Y - viewport.CenterY()) * _scale + _translationY);

    private static Point ToReferenceMapPoint(ReferencePoint point, RectF mapBounds)
    {
        var targetStart = ToMapPoint(DrawingStartMapAnchor, mapBounds);
        var targetEnd = ToMapPoint(DrawingEndMapAnchor, mapBounds);
        var sourceDeltaX = DrawingEndReference.X - DrawingStartReference.X;
        var sourceDeltaY = DrawingEndReference.Y - DrawingStartReference.Y;
        var targetDeltaX = targetEnd.X - targetStart.X;
        var targetDeltaY = targetEnd.Y - targetStart.Y;
        var sourceLengthSquared = sourceDeltaX * sourceDeltaX + sourceDeltaY * sourceDeltaY;
        var realPart = (targetDeltaX * sourceDeltaX + targetDeltaY * sourceDeltaY) / sourceLengthSquared;
        var imaginaryPart = (targetDeltaY * sourceDeltaX - targetDeltaX * sourceDeltaY) / sourceLengthSquared;
        var relativeX = point.X - DrawingStartReference.X;
        var relativeY = point.Y - DrawingStartReference.Y;

        return new Point(
            targetStart.X + realPart * relativeX - imaginaryPart * relativeY,
            targetStart.Y + imaginaryPart * relativeX + realPart * relativeY);
    }

    private static Point ToMapPoint(GeoPoint point, RectF mapBounds)
    {
        var worldSize = TileSize * Math.Pow(2d, OfflineMapZoom);
        var sourceX = ((point.Longitude + 180d) / 360d * worldSize) - OfflineMapLeftTile * TileSize;
        var latitudeRadians = point.Latitude * Math.PI / 180d;
        var sourceY = ((1d - Math.Log(Math.Tan(latitudeRadians) + 1d / Math.Cos(latitudeRadians)) / Math.PI) / 2d * worldSize) -
                      OfflineMapTopTile * TileSize;

        return new Point(
            mapBounds.Left + (float)(sourceX / OfflineMapPixelWidth) * mapBounds.Width(),
            mapBounds.Top + (float)(sourceY / OfflineMapPixelHeight) * mapBounds.Height());
    }

    private void SetScaleAround(float requestedScale, float focusX, float focusY)
    {
        var nextScale = Math.Clamp(requestedScale, MinimumScale, MaximumScale);
        if (Math.Abs(nextScale - _scale) < 0.002f)
        {
            return;
        }

        var viewport = GetViewportBounds();
        if (viewport.Width() <= 0 || viewport.Height() <= 0)
        {
            return;
        }

        var ratio = nextScale / _scale;
        _translationX = focusX - viewport.CenterX() - (focusX - viewport.CenterX() - _translationX) * ratio;
        _translationY = focusY - viewport.CenterY() - (focusY - viewport.CenterY() - _translationY) * ratio;
        _scale = nextScale;
        ConstrainTranslation();
        Invalidate();
    }

    private void ResetToDefaultView()
    {
        _scale = DefaultScale;
        var viewport = GetViewportBounds();
        if (viewport.Width() <= 0 || viewport.Height() <= 0)
        {
            _translationX = 0f;
            _translationY = 0f;
            return;
        }

        var mapBounds = GetFittedMapBounds(viewport);
        var focus = ToMapPoint(DefaultFocus, mapBounds);
        _translationX = -(focus.X - viewport.CenterX()) * _scale;
        _translationY = -(focus.Y - viewport.CenterY()) * _scale;
        ConstrainTranslation();
        Invalidate();
    }

    private void ResetToSaddleManView()
    {
        _scale = MinimumScale;
        var viewport = GetViewportBounds();
        if (viewport.Width() <= 0 || viewport.Height() <= 0)
        {
            _translationX = 0f;
            _translationY = 0f;
            return;
        }

        var mapBounds = GetFittedMapBounds(viewport);
        var focus = ToReferenceMapPoint(SaddleManReference, mapBounds);
        _translationX = -(focus.X - viewport.CenterX()) * _scale;
        _translationY = -(focus.Y - viewport.CenterY()) * _scale - viewport.Height() * 0.14f;
        ConstrainTranslation();
        Invalidate();
    }

    private void ConstrainTranslation()
    {
        var viewport = GetViewportBounds();
        if (viewport.Width() <= 0 || viewport.Height() <= 0)
        {
            return;
        }

        var mapBounds = GetFittedMapBounds(viewport);
        _translationX = ConstrainAxis(
            mapBounds.Left,
            mapBounds.Right,
            viewport.Left,
            viewport.Right,
            viewport.CenterX(),
            _translationX);
        _translationY = ConstrainAxis(
            mapBounds.Top,
            mapBounds.Bottom,
            viewport.Top,
            viewport.Bottom,
            viewport.CenterY(),
            _translationY);
    }

    private float ConstrainAxis(
        float mapStart,
        float mapEnd,
        float viewportStart,
        float viewportEnd,
        float center,
        float translation)
    {
        var scaledStart = center + (mapStart - center) * _scale;
        var scaledEnd = center + (mapEnd - center) * _scale;
        var scaledSize = scaledEnd - scaledStart;
        var viewportSize = viewportEnd - viewportStart;

        if (scaledSize <= viewportSize)
        {
            return (viewportStart + viewportEnd - scaledStart - scaledEnd) / 2f;
        }

        var minimum = viewportEnd - scaledEnd;
        var maximum = viewportStart - scaledStart;
        return Math.Clamp(translation, minimum, maximum);
    }

    private static float GetPinchSpan(MotionEvent motionEvent)
    {
        var deltaX = motionEvent.GetX(0) - motionEvent.GetX(1);
        var deltaY = motionEvent.GetY(0) - motionEvent.GetY(1);
        return MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static bool IsVisible(Point point, RectF viewport, float margin) =>
        point.X >= viewport.Left - margin && point.X <= viewport.Right + margin &&
        point.Y >= viewport.Top - margin && point.Y <= viewport.Bottom + margin;

    private bool SelectMarkerAt(float x, float y)
    {
        var viewport = GetViewportBounds();
        var mapBounds = GetFittedMapBounds(viewport);
        var stationRadius = Math.Clamp(viewport.Width() * 0.035f, 18f, 36f);
        var hitRadius = Math.Max(stationRadius * 1.45f, 26f);
        var hitRadiusSquared = hitRadius * hitRadius;
        var nearestDistanceSquared = float.MaxValue;
        var nearestMarkerIndex = -1;

        for (var index = 0; index < Stations.Length; index++)
        {
            var point = ToScreenPoint(Stations[index], mapBounds, viewport);
            var distanceSquared = SquaredDistance(point, x, y);
            if (IsVisible(point, viewport, stationRadius) &&
                distanceSquared <= hitRadiusSquared && distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestMarkerIndex = index;
            }
        }

        for (var index = 0; index < Destinations.Length; index++)
        {
            var point = ToScreenPoint(Destinations[index], mapBounds, viewport);
            var distanceSquared = SquaredDistance(point, x, y);
            if (IsVisible(point, viewport, stationRadius) &&
                distanceSquared <= hitRadiusSquared && distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestMarkerIndex = Stations.Length + index;
            }
        }

        if (_selectedMarkerIndex != nearestMarkerIndex)
        {
            _selectedMarkerIndex = nearestMarkerIndex;
            Invalidate();
        }

        return nearestMarkerIndex >= 0;
    }

    private static float SquaredDistance(Point point, float x, float y)
    {
        var deltaX = point.X - x;
        var deltaY = point.Y - y;
        return deltaX * deltaX + deltaY * deltaY;
    }

    private void DrawTripRoutes(Canvas canvas, RectF mapBounds, RectF viewport, float stationRadius)
    {
        foreach (var route in Routes)
        {
            DrawRoute(canvas, route.Points, route.ColorHex, mapBounds, viewport, stationRadius);
        }
    }

    private void DrawRoute(
        Canvas canvas,
        ReferencePoint[] route,
        string colorHex,
        RectF mapBounds,
        RectF viewport,
        float stationRadius)
    {
        var screenPoints = new Point[route.Length];
        for (var index = 0; index < route.Length; index++)
        {
            screenPoints[index] = ToScreenPoint(route[index], mapBounds, viewport);
        }

        _routePath.Reset();
        _routePath.MoveTo(screenPoints[0].X, screenPoints[0].Y);
        for (var index = 0; index < screenPoints.Length - 1; index++)
        {
            var previous = index == 0 ? screenPoints[index] : screenPoints[index - 1];
            var current = screenPoints[index];
            var next = screenPoints[index + 1];
            var following = index + 2 < screenPoints.Length ? screenPoints[index + 2] : next;
            var firstControlX = current.X + (next.X - previous.X) / 6f;
            var firstControlY = current.Y + (next.Y - previous.Y) / 6f;
            var secondControlX = next.X - (following.X - current.X) / 6f;
            var secondControlY = next.Y - (following.Y - current.Y) / 6f;
            _routePath.CubicTo(
                firstControlX,
                firstControlY,
                secondControlX,
                secondControlY,
                next.X,
                next.Y);
        }

        _routePaint.Color = Color.Argb(120, 24, 28, 27);
        _routePaint.StrokeWidth = Math.Clamp(stationRadius * 0.38f, 7f, 13f);
        canvas.DrawPath(_routePath, _routePaint);
        _routePaint.Color = Color.ParseColor(colorHex);
        _routePaint.StrokeWidth = Math.Clamp(stationRadius * 0.20f, 4f, 7f);
        canvas.DrawPath(_routePath, _routePaint);
    }

    private void DrawStation(Canvas canvas, Point point, string number, float radius)
    {
        _markerPaint.Color = Color.Argb(105, 0, 0, 0);
        canvas.DrawCircle(point.X + 2f, point.Y + 3f, radius + 3f, _markerPaint);
        _markerPaint.Color = Color.ParseColor("#D88727");
        canvas.DrawCircle(point.X, point.Y, radius, _markerPaint);
        DrawMarkerOutline(canvas, point, radius);

        _textPaint.Color = Color.White;
        _textPaint.TextSize = radius * 1.10f;
        canvas.DrawText(number, point.X, CenterTextAt(point.Y), _textPaint);
    }

    private void DrawSelectedMarkerName(Canvas canvas, RectF mapBounds, RectF viewport, float stationRadius)
    {
        if (_selectedMarkerIndex < 0)
        {
            return;
        }

        Point point;
        string name;
        string accentColor;
        float markerRadius;
        if (_selectedMarkerIndex < Stations.Length)
        {
            var station = Stations[_selectedMarkerIndex];
            point = ToScreenPoint(station, mapBounds, viewport);
            name = station.Name;
            accentColor = "#D88727";
            markerRadius = stationRadius;
        }
        else
        {
            var destinationIndex = _selectedMarkerIndex - Stations.Length;
            if (destinationIndex < 0 || destinationIndex >= Destinations.Length)
            {
                return;
            }

            var destination = Destinations[destinationIndex];
            point = ToScreenPoint(destination, mapBounds, viewport);
            name = destination.Name;
            accentColor = destination.ColorHex;
            markerRadius = stationRadius * 0.92f;
        }

        if (!IsVisible(point, viewport, markerRadius))
        {
            return;
        }

        DrawMarkerName(canvas, point, name, accentColor, markerRadius, viewport);
    }

    private void DrawMarkerName(
        Canvas canvas,
        Point point,
        string name,
        string accentColor,
        float radius,
        RectF viewport)
    {
        var labelTextSize = Math.Clamp(radius * 0.72f, 20f, 27f);
        _textPaint.TextSize = labelTextSize;
        var horizontalPadding = Math.Max(8f, radius * 0.30f);
        var outerMargin = Math.Max(8f, radius * 0.30f);
        var labelWidth = Math.Min(
            _textPaint.MeasureText(name) + horizontalPadding * 2f,
            viewport.Width() - outerMargin * 2f);
        var labelHeight = labelTextSize * 1.45f;
        var labelCenterX = Math.Clamp(
            point.X,
            viewport.Left + outerMargin + labelWidth / 2f,
            viewport.Right - outerMargin - labelWidth / 2f);
        var labelTop = point.Y + radius + Math.Max(5f, radius * 0.16f);
        if (labelTop + labelHeight > viewport.Bottom - outerMargin)
        {
            labelTop = point.Y - radius - Math.Max(5f, radius * 0.16f) - labelHeight;
        }

        labelTop = Math.Clamp(labelTop, viewport.Top + outerMargin, viewport.Bottom - outerMargin - labelHeight);
        var labelBounds = new RectF(
            labelCenterX - labelWidth / 2f,
            labelTop,
            labelCenterX + labelWidth / 2f,
            labelTop + labelHeight);

        _markerPaint.Color = Color.Argb(218, 24, 38, 33);
        canvas.DrawRoundRect(labelBounds, labelHeight / 2f, labelHeight / 2f, _markerPaint);
        _markerPaint.Color = Color.ParseColor(accentColor);
        _markerPaint.SetStyle(Paint.Style.Stroke);
        _markerPaint.StrokeWidth = 2f;
        canvas.DrawRoundRect(labelBounds, labelHeight / 2f, labelHeight / 2f, _markerPaint);
        _markerPaint.SetStyle(Paint.Style.Fill);

        _textPaint.Color = Color.White;
        canvas.DrawText(name, labelBounds.CenterX(), CenterTextAt(labelBounds.CenterY()), _textPaint);
    }

    private void DrawDestination(Canvas canvas, Point point, string label, float stationRadius, string colorHex)
    {
        var radius = stationRadius * 0.92f;
        _markerPaint.Color = Color.Argb(115, 0, 0, 0);
        canvas.DrawCircle(point.X + 2f, point.Y + 3f, radius + 4f, _markerPaint);
        _markerPaint.Color = Color.ParseColor(colorHex);
        canvas.DrawCircle(point.X, point.Y, radius, _markerPaint);
        DrawMarkerOutline(canvas, point, radius);

        _textPaint.Color = Color.White;
        _textPaint.TextSize = radius * 1.05f;
        canvas.DrawText(label, point.X, CenterTextAt(point.Y), _textPaint);
    }

    private void DrawMarkerOutline(Canvas canvas, Point point, float radius)
    {
        _markerPaint.Color = Color.White;
        _markerPaint.SetStyle(Paint.Style.Stroke);
        _markerPaint.StrokeWidth = Math.Max(2f, radius * 0.12f);
        canvas.DrawCircle(point.X, point.Y, radius, _markerPaint);
        _markerPaint.SetStyle(Paint.Style.Fill);
    }

    private void DrawBus(Canvas canvas, Point point, string label, float width)
    {
        var height = width * 0.58f;
        var bounds = new RectF(point.X - width / 2f, point.Y - height / 2f, point.X + width / 2f, point.Y + height / 2f);
        _markerPaint.Color = Color.Argb(110, 0, 0, 0);
        canvas.DrawRoundRect(new RectF(bounds.Left + 2f, bounds.Top + 3f, bounds.Right + 2f, bounds.Bottom + 3f), height / 2f, height / 2f, _markerPaint);
        _markerPaint.Color = Color.ParseColor("#1F332D");
        canvas.DrawRoundRect(bounds, height / 2f, height / 2f, _markerPaint);
        _markerPaint.Color = Color.ParseColor("#F4D58D");
        _markerPaint.SetStyle(Paint.Style.Stroke);
        _markerPaint.StrokeWidth = 2f;
        canvas.DrawRoundRect(bounds, height / 2f, height / 2f, _markerPaint);
        _markerPaint.SetStyle(Paint.Style.Fill);

        _textPaint.Color = Color.White;
        _textPaint.TextSize = height * 0.52f;
        canvas.DrawText(label, point.X, CenterTextAt(point.Y), _textPaint);
    }

    private void DrawVisitor(Canvas canvas, Point point, float stationRadius)
    {
        var radius = stationRadius * 0.66f;

        // A symmetric, universal person pictogram avoids gendered hair, clothing,
        // body-shape, or color cues while remaining recognizable at small map sizes.
        _markerPaint.Color = Color.Argb(60, 47, 140, 222);
        canvas.DrawCircle(point.X, point.Y, radius * 1.8f, _markerPaint);
        _markerPaint.Color = Color.White;
        canvas.DrawCircle(point.X, point.Y, radius + 3f, _markerPaint);
        _markerPaint.Color = Color.ParseColor("#2F8CDE");
        canvas.DrawCircle(point.X, point.Y, radius, _markerPaint);

        var headRadius = radius * 0.20f;
        var headCenterY = point.Y - radius * 0.40f;
        _markerPaint.Color = Color.White;
        _markerPaint.SetStyle(Paint.Style.Fill);
        canvas.DrawCircle(point.X, headCenterY, headRadius, _markerPaint);

        _markerPaint.SetStyle(Paint.Style.Stroke);
        _markerPaint.StrokeWidth = Math.Max(2f, radius * 0.16f);
        _markerPaint.StrokeCap = Paint.Cap.Round;
        _markerPaint.StrokeJoin = Paint.Join.Round;

        var shoulderY = point.Y - radius * 0.06f;
        var hipY = point.Y + radius * 0.25f;
        canvas.DrawLine(point.X, headCenterY + headRadius * 1.25f, point.X, hipY, _markerPaint);
        canvas.DrawLine(point.X, shoulderY, point.X - radius * 0.34f, point.Y + radius * 0.10f, _markerPaint);
        canvas.DrawLine(point.X, shoulderY, point.X + radius * 0.34f, point.Y + radius * 0.10f, _markerPaint);
        canvas.DrawLine(point.X, hipY, point.X - radius * 0.27f, point.Y + radius * 0.56f, _markerPaint);
        canvas.DrawLine(point.X, hipY, point.X + radius * 0.27f, point.Y + radius * 0.56f, _markerPaint);
        _markerPaint.SetStyle(Paint.Style.Fill);
    }

    private void DrawSaddleMan(Canvas canvas, RectF mapBounds, RectF viewport, float stationRadius)
    {
        var panoramaPoint = ToScreenPoint(Stations[1], mapBounds, viewport);
        var point = ToScreenPoint(SaddleManReference, mapBounds, viewport);
        var avatarRadius = Math.Clamp(viewport.Width() * 0.065f, 48f, 82f);
        if (!IsVisible(point, viewport, avatarRadius * 2.4f))
        {
            return;
        }

        _routePaint.Color = Color.ParseColor("#F0D188");
        _routePaint.StrokeWidth = Math.Max(3f, stationRadius * 0.12f);
        canvas.DrawLine(panoramaPoint.X, panoramaPoint.Y, point.X, point.Y, _routePaint);

        _markerPaint.Color = Color.Argb(105, 0, 0, 0);
        canvas.DrawCircle(point.X + 4f, point.Y + 6f, avatarRadius + 7f, _markerPaint);
        _markerPaint.Color = Color.White;
        canvas.DrawCircle(point.X, point.Y, avatarRadius + 5f, _markerPaint);
        _markerPaint.Color = Color.ParseColor("#D7AD62");
        canvas.DrawCircle(point.X, point.Y, avatarRadius + 2f, _markerPaint);

        var portraitBounds = new RectF(
            point.X - avatarRadius,
            point.Y - avatarRadius,
            point.X + avatarRadius,
            point.Y + avatarRadius);
        if (_saddleManPortrait is not null)
        {
            var clipPath = new Android.Graphics.Path();
            clipPath.AddCircle(point.X, point.Y, avatarRadius, Android.Graphics.Path.Direction.Cw!);
            var saveCount = canvas.Save();
            canvas.ClipPath(clipPath);
            canvas.DrawBitmap(_saddleManPortrait, null, portraitBounds, _bitmapPaint);
            canvas.RestoreToCount(saveCount);
        }

        DrawSaddleManName(canvas, point, avatarRadius);
        DrawRideTypeBadge(canvas, point, avatarRadius);
    }

    private void DrawSaddleManName(Canvas canvas, Point point, float avatarRadius)
    {
        var textSize = Math.Clamp(avatarRadius * 0.34f, 19f, 27f);
        _textPaint.TextSize = textSize;
        var width = Math.Max(avatarRadius * 1.55f, _textPaint.MeasureText(_saddleManName) + 24f);
        var height = textSize * 1.55f;
        var bounds = new RectF(
            point.X - width / 2f,
            point.Y - avatarRadius - height - 9f,
            point.X + width / 2f,
            point.Y - avatarRadius - 9f);

        _markerPaint.Color = Color.Argb(232, 17, 50, 44);
        canvas.DrawRoundRect(bounds, height / 2f, height / 2f, _markerPaint);
        _textPaint.Color = Color.White;
        canvas.DrawText(_saddleManName!, bounds.CenterX(), CenterTextAt(bounds.CenterY()), _textPaint);
    }

    private void DrawRideTypeBadge(Canvas canvas, Point point, float avatarRadius)
    {
        var isCaret = _saddleRideType!.Equals("Caret", StringComparison.OrdinalIgnoreCase);
        var textSize = Math.Clamp(avatarRadius * 0.29f, 17f, 24f);
        var height = textSize * 1.85f;
        var width = Math.Max(avatarRadius * 1.9f, isCaret ? 140f : 116f);
        var top = point.Y + avatarRadius + 10f;
        var bounds = new RectF(point.X - width / 2f, top, point.X + width / 2f, top + height);

        _markerPaint.Color = Color.ParseColor(isCaret ? "#123E34" : "#F0D188");
        canvas.DrawRoundRect(bounds, height / 2f, height / 2f, _markerPaint);

        var iconCenterX = bounds.Left + height * (isCaret ? 0.80f : 0.62f);
        var iconCenterY = bounds.CenterY();
        DrawRideTypeIcon(
            canvas,
            iconCenterX,
            iconCenterY,
            height * (isCaret ? 0.62f : 0.52f),
            _saddleRideType!);

        _textPaint.TextSize = textSize;
        _textPaint.Color = Color.ParseColor(isCaret ? "#F0D188" : "#09251F");
        canvas.DrawText(
            _saddleRideType!,
            bounds.CenterX() + height * (isCaret ? 0.25f : 0.18f),
            CenterTextAt(bounds.CenterY()),
            _textPaint);
    }

    private void DrawRideTypeIcon(Canvas canvas, float centerX, float centerY, float size, string rideType)
    {
        _markerPaint.Color = Color.ParseColor(
            rideType.Equals("Caret", StringComparison.OrdinalIgnoreCase)
                ? "#F0D188"
                : "#09251F");
        _markerPaint.StrokeWidth = Math.Max(2f, size * 0.11f);
        _markerPaint.StrokeCap = Paint.Cap.Round;
        _markerPaint.StrokeJoin = Paint.Join.Round;

        if (rideType.Equals("Caret", StringComparison.OrdinalIgnoreCase))
        {
            if (_horseCartIcon is not null)
            {
                var iconBounds = new RectF(
                    centerX - size * 0.82f,
                    centerY - size * 0.42f,
                    centerX + size * 0.82f,
                    centerY + size * 0.42f);
                canvas.DrawBitmap(_horseCartIcon, null, iconBounds, _bitmapPaint);
            }

            _markerPaint.SetStyle(Paint.Style.Fill);
            return;
        }

        if (rideType.Equals("Camel", StringComparison.OrdinalIgnoreCase))
        {
            var camel = new Android.Graphics.Path();
            camel.MoveTo(centerX - size * 0.48f, centerY + size * 0.12f);
            camel.CubicTo(centerX - size * 0.30f, centerY - size * 0.48f, centerX - size * 0.05f, centerY - size * 0.48f, centerX + size * 0.05f, centerY - size * 0.10f);
            camel.CubicTo(centerX + size * 0.18f, centerY - size * 0.44f, centerX + size * 0.32f, centerY - size * 0.35f, centerX + size * 0.35f, centerY - size * 0.05f);
            camel.LineTo(centerX + size * 0.50f, centerY - size * 0.40f);
            camel.LineTo(centerX + size * 0.56f, centerY + size * 0.15f);
            camel.LineTo(centerX + size * 0.40f, centerY + size * 0.12f);
            camel.LineTo(centerX + size * 0.36f, centerY + size * 0.50f);
            camel.LineTo(centerX + size * 0.24f, centerY + size * 0.50f);
            camel.LineTo(centerX + size * 0.18f, centerY + size * 0.13f);
            camel.LineTo(centerX - size * 0.25f, centerY + size * 0.13f);
            camel.LineTo(centerX - size * 0.31f, centerY + size * 0.50f);
            camel.LineTo(centerX - size * 0.43f, centerY + size * 0.50f);
            camel.Close();
            canvas.DrawPath(camel, _markerPaint);
            return;
        }

        canvas.DrawOval(
            new RectF(centerX - size * 0.46f, centerY - size * 0.12f, centerX + size * 0.18f, centerY + size * 0.25f),
            _markerPaint);
        canvas.DrawCircle(centerX + size * 0.35f, centerY - size * 0.30f, size * 0.22f, _markerPaint);
        _markerPaint.StrokeWidth = size * 0.15f;
        canvas.DrawLine(centerX + size * 0.12f, centerY - size * 0.05f, centerX + size * 0.29f, centerY - size * 0.25f, _markerPaint);
        canvas.DrawLine(centerX - size * 0.28f, centerY + size * 0.18f, centerX - size * 0.35f, centerY + size * 0.50f, _markerPaint);
        canvas.DrawLine(centerX + size * 0.02f, centerY + size * 0.18f, centerX + size * 0.10f, centerY + size * 0.50f, _markerPaint);
    }

    private void DrawOfflineBadge(Canvas canvas, RectF viewport, float stationRadius)
    {
        var height = Math.Clamp(stationRadius * 1.15f, 28f, 42f);
        var width = height * 4.3f;
        var margin = Math.Max(8f, stationRadius * 0.35f);
        var bounds = new RectF(viewport.Right - width - margin, viewport.Top + margin, viewport.Right - margin, viewport.Top + margin + height);

        _markerPaint.Color = Color.Argb(215, 22, 38, 33);
        canvas.DrawRoundRect(bounds, height / 2f, height / 2f, _markerPaint);
        _textPaint.Color = Color.ParseColor("#F4D58D");
        _textPaint.TextSize = height * 0.43f;
        canvas.DrawText($"OFFLINE  {_scale:0.#}×", bounds.CenterX(), CenterTextAt(bounds.CenterY()), _textPaint);
    }

    private float CenterTextAt(float centerY) =>
        centerY - (_textPaint.Descent() + _textPaint.Ascent()) / 2f;

    private sealed record ReferenceBusMarker(string Label, float ReferenceX, float ReferenceY);

    private sealed record StationMarker(string Number, string Name, float ReferenceX, float ReferenceY);

    private sealed record DestinationMarker(string Label, string Name, float ReferenceX, float ReferenceY, string ColorHex);

    private sealed record RouteBranch(string Name, string ColorHex, ReferencePoint[] Points);

    private readonly record struct GeoPoint(double Latitude, double Longitude);

    private readonly record struct ReferencePoint(float X, float Y);

    private readonly record struct Point(float X, float Y);
}
