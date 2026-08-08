using Android.OS;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/lease_extension_title")]
public sealed class GolfCartLeaseExtensionActivity : Activity
{
    private const int HourlyRateUsd = 10;
    private const int MaximumHours = 24;

    private int _hours = 1;
    private TextView? _decreaseButton;
    private TextView? _hoursText;
    private TextView? _totalText;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_golf_cart_lease_extension);

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();

        _decreaseButton = FindViewById<TextView>(Resource.Id.lease_extension_decrease_button);
        _hoursText = FindViewById<TextView>(Resource.Id.lease_extension_hours_text);
        _totalText = FindViewById<TextView>(Resource.Id.lease_extension_total_text);

        _decreaseButton!.Click += (_, _) =>
        {
            if (_hours > 1)
            {
                _hours--;
                UpdateExtensionSummary();
            }
        };

        FindViewById<TextView>(Resource.Id.lease_extension_increase_button)!.Click += (_, _) =>
        {
            if (_hours < MaximumHours)
            {
                _hours++;
                UpdateExtensionSummary();
            }
        };

        var paymentButtons = new[]
        {
            Resource.Id.gpay_button,
            Resource.Id.apple_pay_button,
            Resource.Id.visa_button
        };

        foreach (var paymentButtonId in paymentButtons)
        {
            FindViewById<Android.Views.View>(paymentButtonId)!.Click += (_, _) => CompleteExtension();
        }

        UpdateExtensionSummary();
    }

    private void UpdateExtensionSummary()
    {
        var unit = GetString(_hours == 1
            ? Resource.String.lease_extension_hour
            : Resource.String.lease_extension_hours);
        _hoursText!.Text = $"{_hours} {unit}";
        _totalText!.Text = $"{GetString(Resource.String.lease_extension_total_label)} {_hours * HourlyRateUsd} USD";

        _decreaseButton!.Enabled = _hours > 1;
        _decreaseButton.Alpha = _hours > 1 ? 1f : 0.45f;
    }

    private void CompleteExtension()
    {
        Toast.MakeText(this, Resource.String.lease_extension_confirmation, ToastLength.Long)?.Show();
        Finish();
    }
}
