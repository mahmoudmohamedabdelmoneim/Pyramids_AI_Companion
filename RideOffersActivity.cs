using Android.OS;
using Android.Views;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/ride_offers_title")]
public sealed class RideOffersActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        SetContentView(Resource.Layout.activity_ride_offers);

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
        BindOffer(Resource.Id.ahmed_offer_card, SaddleRideCatalog.Offers[0]);
        BindOffer(Resource.Id.waleed_offer_card, SaddleRideCatalog.Offers[1]);
        BindOffer(Resource.Id.joseph_offer_card, SaddleRideCatalog.Offers[2]);
    }

    private void BindOffer(int cardResource, SaddleRideOffer offer)
    {
        var card = FindViewById<View>(cardResource)!;
        card.ContentDescription = $"{offer.Name}, {offer.RideType}, {offer.Rating:0.0} stars, {offer.PriceEgp} EGP";
        card.Click += (_, _) =>
            StartActivity(SaddleRideCatalog.CreateIntent(this, typeof(RideDetailsActivity), offer));
    }
}
