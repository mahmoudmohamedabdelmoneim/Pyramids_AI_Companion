namespace AndroidApp1
{
    internal sealed class TicketCartSelection
    {
        private readonly Android.Widget.TextView _cartButton;
        private int _totalSelections;

        public TicketCartSelection(Android.Widget.TextView cartButton)
        {
            _cartButton = cartButton;
            UpdateCartButton();
        }

        public Func<int> BindCounter(Activity activity, int decreaseButtonId, int countId, int increaseButtonId)
        {
            var decreaseButton = activity.FindViewById<Android.Widget.TextView>(decreaseButtonId)!;
            var countView = activity.FindViewById<Android.Widget.TextView>(countId)!;
            var increaseButton = activity.FindViewById<Android.Widget.TextView>(increaseButtonId)!;
            decreaseButton.Text = "-";
            var count = 0;

            decreaseButton.Click += (_, _) =>
            {
                if (count == 0)
                {
                    return;
                }

                count--;
                _totalSelections--;
                countView.Text = count.ToString();
                UpdateCartButton();
            };

            increaseButton.Click += (_, _) =>
            {
                count++;
                _totalSelections++;
                countView.Text = count.ToString();
                UpdateCartButton();
            };

            return () => count;
        }

        private void UpdateCartButton()
        {
            _cartButton.Enabled = _totalSelections > 0;
        }
    }
}
