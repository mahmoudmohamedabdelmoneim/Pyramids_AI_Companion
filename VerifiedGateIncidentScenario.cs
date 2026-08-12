namespace AndroidApp1;

using System.Text.RegularExpressions;

/// <summary>
/// Shared recognition and response facts for suspicious-person reports made through ASK ME
/// while the visitor is at the verified Great Gate entry flow.
/// </summary>
internal static class VerifiedGateIncidentScenario
{
    internal const string VerifiedAccessStatement =
        "Your booked ticket is already verified and grants access through its assigned Great Gate entry.";

    internal const string AppOnlyPaymentStatement =
        "All payments are done inside the app. Do not pay anyone outside the app or give them card details.";

    internal static bool IsMatch(string message)
    {
        var normalized = Normalize(message);
        var explicitlyNamesGate = Regex.IsMatch(normalized, @"\bgate\b", RegexOptions.CultureInvariant) ||
            normalized.Contains("checkpoint", StringComparison.Ordinal);
        return explicitlyNamesGate && IsSuspiciousReport(normalized);
    }

    internal static bool IsSuspiciousReport(string message)
    {
        var normalized = Normalize(message);
        return DescribesActualPersonPaymentDemand(normalized) || ContainsAny(
            normalized,
            "scam", "scammer", "fraud", "suspicious person", "fake staff", "fake employee",
            "pretending to be staff", "impersonat", "con artist", "rip off", "ripping me off",
            "trying to be paid extra", "trying to get paid extra", "wants extra money",
            "asking for extra money", "asked for extra money", "pay extra", "paid extra",
            "extra payment", "additional payment", "extra charge", "charged extra", "overcharg",
            "pay again", "charge me again", "another payment", "asked me to pay", "asking me to pay",
            "wants me to pay", "told me to pay", "demanded payment", "demanding payment",
            "collecting payment", "trying to collect payment", "asking for payment", "asked for payment",
            "asking for a payment", "asked for a payment", "wants payment", "wants cash",
            "asking for gate access charge", "tried to charge me", "trying to charge me",
            "asked for cash", "asking for cash", "give him money", "give her money", "give them money");
    }

    internal static bool AsksAboutPersonCollectingPayment(string message)
    {
        var normalized = Normalize(message);
        var mentionsPayment = ContainsAny(
            normalized,
            "pay", "paid", "payment", "charge", "fee", "cash", "card", "money", "collect", "bill");
        var mentionsPersonOrGate = ContainsAny(
            normalized,
            "someone", "person", "staff", "employee", "seller", "provider", "guide", "guard",
            "attendant", "worker", "cashier", "vendor", "restaurant", "kiosk", "shop", "counter", "gate",
            "checkpoint", "who do i pay", "where do i pay");
        var asksPolicy = ContainsAny(
            normalized,
            "should", "can ", "could", "would", "may ", "is it okay", "is it normal",
            "is it allowed", "normal for", "allowed to", "supposed to", "do i have to",
            "must i", "who do i pay", "where do i pay");
        return mentionsPayment && mentionsPersonOrGate && asksPolicy;
    }

    private static bool DescribesActualPersonPaymentDemand(string normalized)
    {
        if (AsksAboutPersonCollectingPayment(normalized) || ContainsAny(
                normalized,
                "inside the app", "in the app", "through the app", "do not pay", "don't pay"))
        {
            return false;
        }

        var mentionsPerson = ContainsAny(
            normalized,
            "someone", "person", "staff", "employee", "seller", "provider", "guide", "guard",
            "attendant", "cashier", "vendor", "worker", "a man", "the man", "a woman", "the woman",
            "guy", " he ", " she ", " they ");
        var requestsOrCollects = ContainsAny(
            normalized,
            "ask", "want", "tell", "told", "demand", "request", "collect", "charge", "insist",
            "require", "hand over", "give ");
        var mentionsPayment = ContainsAny(
            normalized,
            "pay", "paid", "payment", "charge", "fee", "cash", "card", "money", "bill");
        return mentionsPerson && requestsOrCollects && mentionsPayment;
    }

    internal static bool RequestsCamera(string message)
    {
        var normalized = Normalize(message);
        var mentionsImage = ContainsAny(normalized, "photo", "picture", "image", "camera");
        var requestsAction = ContainsAny(
            normalized,
            "take ", "upload", "attach", "add ", "send ", "open ", "use ", "can i",
            "could i", "may i", "i want", "i need", "let me", "allow me", "camera option");
        return mentionsImage && requestsAction;
    }

    internal static bool LacksReportInformation(string message)
    {
        var normalized = Normalize(message).Trim(' ', '.', '!', '?', ',');
        return normalized is "no" or "nope" or "idk" or "i don't know" or "i do not know" or "not sure" ||
            ContainsAny(
                normalized,
                "don't have information", "do not have information", "no information",
                "don't have details", "do not have details", "no details", "nothing to add",
                "nothing else", "that's all i know", "that is all i know", "can't identify",
                "cannot identify", "don't know the person's", "do not know the person's",
                "don't have the person's", "do not have the person's", "don't know their name",
                "do not know their name", "don't have their name", "do not have their name");
    }

    internal static bool ExplicitlyLacksReportInformation(string message)
    {
        var normalized = Normalize(message);
        return LacksReportInformation(normalized) && ContainsAny(
            normalized,
            "report", "information", "details", "person", "their name", "their id", "identity",
            "identify", "description", "nothing to add", "nothing else", "that's all", "that is all");
    }

    internal static bool ShouldUnlockCamera(string report, string latestVisitorMessage) =>
        IsSuspiciousReport(report) ||
        RequestsCamera(report) ||
        LacksReportInformation(latestVisitorMessage);

    internal static bool LooksLikeHypotheticalOrPolicyQuestion(string message)
    {
        var normalized = Normalize(message).TrimStart();
        return ContainsAny(
            normalized,
            "what if ", "suppose ", "supposing ", "hypothetically", "in case ",
            "should ", "shouldn't ", "can ", "could ", "would ", "may ", "might ",
            "is it ", "is there ", "are we ", "are visitors ", "do i ", "do we ",
            "does ", "will ", "must ", "am i ", "what should ", "how would ");
    }

    private static string Normalize(string text) => text
        .Trim()
        .ToLowerInvariant()
        .Replace('\u2019', '\'')
        .Replace('\u2018', '\'');

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.Ordinal));
}
