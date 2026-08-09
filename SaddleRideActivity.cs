using Android.OS;
using Android.Views;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/saddle_ride_title")]
public sealed class SaddleRideActivity : Activity
{
    private const long PrototypeRideEndMilliseconds = 10_000;
    private static readonly TimeSpan TripDuration = TimeSpan.FromHours(1);

    private Handler? _handler;
    private TextView? _timer;
    private TextView? _startButton;
    private TextView? _codeNotice;
    private TextView? _rideStatus;
    private TextView? _selectedSaddleManName;
    private ScrollView? _feedbackPanel;
    private TextView? _refundMessage;
    private int _vehicleIconResource;
    private DateTimeOffset _tripEndsAt;
    private bool _rideEnded;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        SetContentView(Resource.Layout.activity_saddle_ride);

        var offer = SaddleRideCatalog.FromIntent(Intent);
        _handler = new Handler(Looper.MainLooper!);
        _timer = FindViewById<TextView>(Resource.Id.ride_timer);
        _startButton = FindViewById<TextView>(Resource.Id.start_saddle_ride_button);
        _codeNotice = FindViewById<TextView>(Resource.Id.saddle_ride_code_notice);
        _rideStatus = FindViewById<TextView>(Resource.Id.saddle_ride_status);
        _feedbackPanel = FindViewById<ScrollView>(Resource.Id.ride_feedback_panel);
        _refundMessage = FindViewById<TextView>(Resource.Id.refund_message);

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
        var map = FindViewById<GizaPlateauMapView>(Resource.Id.saddle_ride_map)!;
        map.ShowBusPills = false;
        map.ContentDescription = GetString(Resource.String.saddle_ride_map_description);
        map.SetSaddleManMarker(offer.PortraitResource, offer.Name, offer.RideType);

        FindViewById<ImageView>(Resource.Id.selected_saddle_man_portrait)!.SetImageResource(offer.PortraitResource);
        _selectedSaddleManName = FindViewById<TextView>(Resource.Id.selected_saddle_man_name);
        _selectedSaddleManName!.Text = $"{offer.Name} • {offer.VehicleIcon} {offer.RideType}";
        FindViewById<TextView>(Resource.Id.selected_ride_price)!.Text = $"{offer.PriceEgp} EGP • 1 hour";
        FindViewById<TextView>(Resource.Id.ride_end_message)!.Text =
            $"Your ride has ended and we deducted {offer.PriceEgp} EGP from your account. How did {offer.Name} do?";

        if (offer.VehicleIconResource != 0)
        {
            _vehicleIconResource = offer.VehicleIconResource;
            var selectedSaddleManName = _selectedSaddleManName;
            selectedSaddleManName.Text = $"{offer.Name} \u2022 {offer.RideType}";
            selectedSaddleManName.SetCompoundDrawablesWithIntrinsicBounds(offer.VehicleIconResource, 0, 0, 0);
            selectedSaddleManName.CompoundDrawablePadding = (int)(8 * Resources!.DisplayMetrics!.Density);

            var rideEndMessage = FindViewById<TextView>(Resource.Id.ride_end_message)!;
            rideEndMessage.SetCompoundDrawablesWithIntrinsicBounds(0, offer.VehicleIconResource, 0, 0);
            rideEndMessage.CompoundDrawablePadding = (int)(8 * Resources!.DisplayMetrics!.Density);
        }

        _startButton!.Click += (_, _) => StartTrip();
        var extraChargeYes = FindViewById<RadioButton>(Resource.Id.extra_charge_yes)!;
        var extraChargeNo = FindViewById<RadioButton>(Resource.Id.extra_charge_no)!;
        var fullRideYes = FindViewById<RadioButton>(Resource.Id.full_ride_yes)!;
        var fullRideNo = FindViewById<RadioButton>(Resource.Id.full_ride_no)!;
        void UpdateRefundMessage() => _refundMessage!.Visibility =
            extraChargeNo.Checked && fullRideNo.Checked ? ViewStates.Visible : ViewStates.Gone;

        extraChargeYes.Click += (_, _) => UpdateRefundMessage();
        extraChargeNo.Click += (_, _) => UpdateRefundMessage();
        fullRideYes.Click += (_, _) => UpdateRefundMessage();
        fullRideNo.Click += (_, _) => UpdateRefundMessage();
        FindViewById<TextView>(Resource.Id.submit_ride_feedback_button)!.Click += (_, _) =>
        {
            var message = _refundMessage!.Visibility == ViewStates.Visible
                ? Resource.String.ride_feedback_refund_submitted
                : Resource.String.ride_feedback_submitted;
            Toast.MakeText(this, message, ToastLength.Long)?.Show();

            if (HasAccessPassTicket() || HasPriorityPassTicket())
            {
                var intent = new Android.Content.Intent(this, typeof(AccessPassTransferActivity));
                intent.PutExtra(AccessPassTransferActivity.FromAncestorRideExtra, true);
                StartActivity(intent);
                return;
            }

            StartActivity(new Android.Content.Intent(this, typeof(TripProgressActivity)));
        };
    }

    protected override void OnDestroy()
    {
        _handler?.RemoveCallbacksAndMessages(null);
        base.OnDestroy();
    }

    private void StartTrip()
    {
        if (_startButton is null || _timer is null || _handler is null)
        {
            return;
        }

        _tripEndsAt = DateTimeOffset.UtcNow.Add(TripDuration);
        _rideEnded = false;
        _startButton.Visibility = ViewStates.Gone;
        _codeNotice?.Visibility = ViewStates.Gone;
        _timer.Visibility = ViewStates.Visible;
        CompactActiveRideHeader();
        _rideStatus?.SetText(Resource.String.saddle_ride_in_progress);
        UpdateTimer();
        _handler.PostDelayed(ShowFeedback, PrototypeRideEndMilliseconds);
    }

    private void UpdateTimer()
    {
        if (_timer is null || _handler is null || _rideEnded)
        {
            return;
        }

        var remaining = _tripEndsAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            ShowFeedback();
            return;
        }

        _timer.Text = $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        _handler.PostDelayed(UpdateTimer, 1_000);
    }

    private void CompactActiveRideHeader()
    {
        if (_vehicleIconResource == 0 || _selectedSaddleManName is null)
        {
            return;
        }

        var vehicleIcon = GetDrawable(_vehicleIconResource);
        if (vehicleIcon is null)
        {
            return;
        }

        vehicleIcon.SetBounds(0, 0, Dp(32), Dp(18));
        _selectedSaddleManName.SetCompoundDrawables(vehicleIcon, null, null, null);
        _selectedSaddleManName.CompoundDrawablePadding = Dp(5);
        _selectedSaddleManName.SetMaxLines(1);

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _selectedSaddleManName.SetAutoSizeTextTypeUniformWithConfiguration(
                14,
                18,
                1,
                (int)Android.Util.ComplexUnitType.Sp);
        }
    }

    private int Dp(int value) =>
        (int)Android.Util.TypedValue.ApplyDimension(
            Android.Util.ComplexUnitType.Dip,
            value,
            Resources?.DisplayMetrics);

    private void ShowFeedback()
    {
        if (_rideEnded || _feedbackPanel is null)
        {
            return;
        }

        _rideEnded = true;
        _handler?.RemoveCallbacksAndMessages(null);
        _feedbackPanel.Visibility = ViewStates.Visible;
        _feedbackPanel.ScrollTo(0, 0);
    }

    private static bool HasAccessPassTicket() =>
        CartStore.CurrentItems.Any(item =>
            item.Quantity > 0 &&
            item.Type.StartsWith("Access Pass", StringComparison.OrdinalIgnoreCase));

    private static bool HasPriorityPassTicket() =>
        CartStore.CurrentItems.Any(item =>
            item.Quantity > 0 &&
            !item.Type.StartsWith("Access Pass", StringComparison.OrdinalIgnoreCase));
}
