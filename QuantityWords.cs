namespace AndroidApp1
{
    internal static class QuantityWords
    {
        public static string For(int count, string singular, string plural)
        {
            return count > 1 ? plural : singular;
        }
    }
}
