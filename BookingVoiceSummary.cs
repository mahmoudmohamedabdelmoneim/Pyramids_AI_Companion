using System.Text.RegularExpressions;

namespace AndroidApp1;

internal static partial class BookingVoiceSummary
{
    public static string BuildDisplay(Activity activity)
    {
        var todayTickets = CartStore.IssuedTickets
            .Where(ticket => ticket.BookingDate.Date == DateTime.Today)
            .GroupBy(ticket => new { ticket.Type, ticket.AccessGate })
            .OrderBy(group => group.Key.Type, StringComparer.Ordinal)
            .ToList();

        var totalTicketCount = todayTickets.Sum(group => group.Count());
        var lines = new List<string>
        {
            activity.GetString(totalTicketCount > 1
                ? Resource.String.ticket_access_intro_plural
                : Resource.String.ticket_access_intro_singular)
        };

        if (todayTickets.Count == 0)
        {
            lines.Add(string.Empty);
            lines.Add(activity.GetString(Resource.String.ticket_access_none));
            return string.Join("\n", lines);
        }

        foreach (var ticketGroup in todayTickets)
        {
            var ticketCount = ticketGroup.Count();
            lines.Add(string.Empty);
            lines.Add($"{QuantityWords.For(ticketCount, "Ticket type:", "Tickets types:")} {FormatTicketType(ticketGroup.Key.Type)}");
            lines.Add($"{QuantityWords.For(ticketCount, "Ticket count:", "Tickets count:")} {ticketCount}");
            lines.Add($"{QuantityWords.For(ticketCount, "Access gate number:", "Access gates number:")} {FormatGateNumbers(ticketGroup.Key.AccessGate)}");
        }

        lines.Add(string.Empty);
        lines.Add(activity.GetString(Resource.String.ticket_access_ready));
        return string.Join("\n", lines);
    }

    public static string BuildForSpeech(Activity activity) => ToSpeechText(BuildDisplay(activity));

    private static string FormatTicketType(string ticketType)
    {
        var priceStart = ticketType.LastIndexOf(" (", StringComparison.Ordinal);
        return priceStart > 0 ? ticketType[..priceStart] : ticketType;
    }

    private static string FormatGateNumbers(string accessGate) =>
        accessGate
            .Replace("Gates ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Gate ", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string ToSpeechText(string text) => NumberRegex().Replace(text, match =>
        int.TryParse(match.Value, out var number) && number is >= 0 and <= 2000
            ? ToEnglishNumber(number)
            : match.Value);

    private static string ToEnglishNumber(int number)
    {
        string[] units =
        [
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen",
            "eighteen", "nineteen"
        ];
        string[] tens =
        [
            string.Empty, string.Empty, "twenty", "thirty", "forty", "fifty", "sixty", "seventy",
            "eighty", "ninety"
        ];

        if (number < 20)
        {
            return units[number];
        }

        if (number < 100)
        {
            return number % 10 == 0
                ? tens[number / 10]
                : $"{tens[number / 10]} {units[number % 10]}";
        }

        if (number < 1000)
        {
            var remainder = number % 100;
            return remainder == 0
                ? $"{units[number / 100]} hundred"
                : $"{units[number / 100]} hundred {ToEnglishNumber(remainder)}";
        }

        var thousandsRemainder = number % 1000;
        return thousandsRemainder == 0
            ? $"{units[number / 1000]} thousand"
            : $"{units[number / 1000]} thousand {ToEnglishNumber(thousandsRemainder)}";
    }

    [GeneratedRegex(@"\b\d+\b")]
    private static partial Regex NumberRegex();
}
