using Android.Content;

namespace AndroidApp1;

public sealed record SaddleRideOffer(
    string Id,
    string Name,
    string RideType,
    string VehicleIcon,
    int VehicleIconResource,
    float Rating,
    int PriceEgp,
    int PortraitResource,
    string Description);

public static class SaddleRideCatalog
{
    public const string OfferIdExtra = "saddle_ride_offer_id";

    public static readonly IReadOnlyList<SaddleRideOffer> Offers =
    [
        new(
            "ahmed-caret",
            "Ahmed",
            "Caret",
            "🛒",
            Resource.Drawable.ahmed_horse_cart_gold,
            4.9f,
            650,
            Resource.Drawable.saddle_man_ahmed,
            "A relaxed one-hour Caret ride with wide desert views and time for photos."),
        new(
            "waleed-camel",
            "Waleed",
            "Camel",
            "🐪",
            0,
            4.8f,
            550,
            Resource.Drawable.saddle_man_waleed,
            "A traditional one-hour camel ride around the Panorama Station desert trail."),
        new(
            "joseph-horse",
            "Joseph",
            "Horse",
            "🐎",
            0,
            4.7f,
            700,
            Resource.Drawable.saddle_man_joseph,
            "A lively one-hour horse ride led at a comfortable pace around the plateau."),
    ];

    public static SaddleRideOffer FromIntent(Intent? intent)
    {
        var id = intent?.GetStringExtra(OfferIdExtra);
        return Offers.FirstOrDefault(offer => offer.Id == id) ?? Offers[0];
    }

    public static Intent CreateIntent(Context context, Type activityType, SaddleRideOffer offer) =>
        new Intent(context, activityType).PutExtra(OfferIdExtra, offer.Id);
}
