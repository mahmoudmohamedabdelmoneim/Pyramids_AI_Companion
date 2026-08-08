using Android.OS;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/ride_details_title")]
public sealed class RideDetailsActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        SetContentView(Resource.Layout.activity_ride_details);

        var offer = SaddleRideCatalog.FromIntent(Intent);
        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
        FindViewById<ImageView>(Resource.Id.ride_detail_portrait)!.SetImageResource(offer.PortraitResource);
        FindViewById<TextView>(Resource.Id.ride_detail_name)!.Text = offer.Name;
        var vehicleIcon = FindViewById<TextView>(Resource.Id.ride_detail_vehicle_icon)!;
        if (offer.VehicleIconResource != 0)
        {
            vehicleIcon.Text = string.Empty;
            var icon = GetDrawable(offer.VehicleIconResource);
            if (icon is not null)
            {
                var density = Resources?.DisplayMetrics?.Density ?? 1f;
                icon.SetBounds(0, 0, (int)(42 * density), (int)(24 * density));
                vehicleIcon.SetCompoundDrawables(icon, null, null, null);
            }
        }
        else
        {
            vehicleIcon.Text = offer.VehicleIcon;
        }
        FindViewById<TextView>(Resource.Id.ride_detail_vehicle)!.Text = $"{offer.RideType} ride";
        FindViewById<TextView>(Resource.Id.ride_detail_rating)!.Text = $"★ {offer.Rating:0.0} rider rating";
        FindViewById<TextView>(Resource.Id.ride_detail_price)!.Text = $"{offer.PriceEgp} EGP";
        FindViewById<TextView>(Resource.Id.ride_detail_description)!.Text = offer.Description;

        var meetButton = FindViewById<TextView>(Resource.Id.meet_saddle_man_button)!;
        meetButton.Text = $"MEET {offer.Name.ToUpperInvariant()} HERE";
        meetButton.Click += (_, _) =>
            StartActivity(SaddleRideCatalog.CreateIntent(this, typeof(SaddleRideActivity), offer));
    }
}
