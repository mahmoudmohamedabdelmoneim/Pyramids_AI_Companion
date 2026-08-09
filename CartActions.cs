namespace AndroidApp1
{
    internal sealed class CartItem
    {
        public CartItem(string type, string accessGate, int quantity)
        {
            Type = type;
            AccessGate = accessGate;
            Quantity = quantity;
        }

        public string Type { get; }
        public string AccessGate { get; }
        public int Quantity { get; set; }
    }

    internal sealed class CartSelection
    {
        public CartSelection(string type, string accessGate, Func<int> quantity)
        {
            Type = type;
            AccessGate = accessGate;
            Quantity = quantity;
        }

        public string Type { get; }
        public string AccessGate { get; }
        public Func<int> Quantity { get; }
    }

    internal static class CartStore
    {
        private static readonly List<CartItem> Items = new();
        private static IReadOnlyList<ETicket>? _issuedTickets;

        public static IReadOnlyList<CartItem> CurrentItems => Items.AsReadOnly();

        public static int TotalTickets => Items.Sum(item => item.Quantity);

        public static IReadOnlyList<ETicket> IssuedTickets =>
            _issuedTickets ??= TicketFactory.Create(Items);

        public static void Add(IEnumerable<CartSelection> selections)
        {
            _issuedTickets = null;

            foreach (var selection in selections)
            {
                var quantity = selection.Quantity();
                if (quantity == 0)
                {
                    continue;
                }

                var type = TicketText.Normalize(selection.Type);
                var existingItem = Items.FirstOrDefault(item =>
                    item.Type == type && item.AccessGate == selection.AccessGate);
                if (existingItem is null)
                {
                    Items.Add(new CartItem(type, selection.AccessGate, quantity));
                }
                else
                {
                    existingItem.Quantity += quantity;
                }
            }

        }

        public static void Remove(CartItem item)
        {
            if (Items.Remove(item))
            {
                _issuedTickets = null;
            }
        }
    }

    internal static class CartActions
    {
        public static void AddAndOpenCart(Activity activity, params CartSelection[] selections)
        {
            CartStore.Add(selections);
            activity.StartActivity(new Android.Content.Intent(activity, typeof(CartActivity)));
        }
    }

    internal static class TicketText
    {
        public static string Normalize(string type)
        {
            var audienceIndex = type.IndexOf(" Foreigners", StringComparison.Ordinal);
            if (audienceIndex < 0)
            {
                audienceIndex = type.IndexOf(" Egyptians", StringComparison.Ordinal);
            }

            var tourIndex = audienceIndex > 0
                ? type.LastIndexOf("Tour", audienceIndex, StringComparison.Ordinal)
                : -1;
            return tourIndex < 0
                ? type
                : $"{type[..(tourIndex + "Tour".Length)]} -{type[audienceIndex..]}";
        }
    }
}
