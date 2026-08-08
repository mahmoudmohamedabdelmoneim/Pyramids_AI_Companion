namespace AndroidApp1
{
    [Activity(Label = "@string/cart_title")]
    public class CartActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_cart);

            FindViewById<Android.Widget.TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
            FindViewById<Android.Widget.TextView>(Resource.Id.proceed_to_checkout_button)!.Click += (_, _) =>
                StartActivity(new Android.Content.Intent(this, typeof(CheckoutNoticeActivity)));

            RenderCart();
        }

        private void RenderCart()
        {
            var container = FindViewById<LinearLayout>(Resource.Id.cart_items_container)!;
            var total = FindViewById<Android.Widget.TextView>(Resource.Id.cart_total)!;
            var checkoutButton = FindViewById<Android.Widget.TextView>(Resource.Id.proceed_to_checkout_button)!;
            container.RemoveAllViews();

            foreach (var item in CartStore.CurrentItems)
            {
                var itemCard = new LinearLayout(this)
                {
                    Orientation = Android.Widget.Orientation.Vertical
                };
                itemCard.SetBackgroundResource(Resource.Drawable.photo_card_border);
                itemCard.SetPadding(Dp(18), Dp(15), Dp(18), Dp(15));
                itemCard.LayoutParameters = new LinearLayout.LayoutParams(-1, -2)
                {
                    BottomMargin = Dp(12)
                };

                var itemText = new Android.Widget.TextView(this)
                {
                    Text = $"{item.Quantity} - {item.Type}\nAccess: {item.AccessGate}",
                    TextSize = 16f
                };
                itemText.SetTextColor(Android.Graphics.Color.ParseColor("#D9E9DE"));
                itemText.LayoutParameters = new LinearLayout.LayoutParams(-1, -2);
                itemCard.AddView(itemText);

                var removeButton = new Android.Widget.TextView(this)
                {
                    Clickable = true,
                    Focusable = true,
                    Gravity = Android.Views.GravityFlags.Center,
                    TextSize = 11f,
                    ContentDescription = $"Remove {item.Type} from cart"
                };
                removeButton.SetText(Resource.String.remove_from_cart);
                removeButton.SetTextColor(Android.Graphics.Color.ParseColor("#F4D58D"));
                removeButton.SetBackgroundResource(Resource.Drawable.video_window_action);
                removeButton.SetPadding(Dp(16), 0, Dp(16), 0);
                removeButton.LayoutParameters = new LinearLayout.LayoutParams(-2, Dp(48))
                {
                    Gravity = Android.Views.GravityFlags.End,
                    TopMargin = Dp(10)
                };
                removeButton.Click += (_, _) =>
                {
                    CartStore.Remove(item);
                    RenderCart();
                };
                itemCard.AddView(removeButton);
                container.AddView(itemCard);
            }

            if (CartStore.CurrentItems.Count == 0)
            {
                total.Text = GetString(Resource.String.cart_empty);
                checkoutButton.Enabled = false;
                return;
            }

            checkoutButton.Enabled = true;
            total.Text = $"{CartStore.TotalTickets} {QuantityWords.For(CartStore.TotalTickets, "ticket", "tickets")} selected";
        }

        private int Dp(int value)
        {
            return (int)Android.Util.TypedValue.ApplyDimension(
                Android.Util.ComplexUnitType.Dip,
                value,
                Resources?.DisplayMetrics);
        }
    }
}
