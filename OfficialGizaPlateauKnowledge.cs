namespace AndroidApp1;

internal static class OfficialGizaPlateauKnowledge
{
    public const string SourceUrl =
        "https://egymonuments.gov.eg/en/archaeological-sites/giza-plateau";

    public const string PromptSource = """
        OFFICIAL GIZA PLATEAU FALLBACK SOURCE
        Source: https://egymonuments.gov.eg/en/archaeological-sites/giza-plateau

        Use this source only if APP KNOWLEDGE does not contain the answer. When this source is used, every factual statement in the answer must come from the facts below. Do not add facts from memory, general knowledge, related pages, search results, or inference. If these facts do not answer the question, say that the answer is not available in the app or the official Giza Plateau page.

        Page facts:
        - The page title is Giza Plateau, and its displayed location is Giza.
        - The Giza pyramids and Great Sphinx are among the world's most popular tourist destinations and were already attractions in Roman times.
        - The royal pyramid structures were final resting places for kings of the Fourth Dynasty, dated on the page to about 2613-2494 BC.
        - The Great Pyramid was built for King Khufu, whose reign is dated on the page to about 2589-2566 BC. The other two main pyramids were built for Khafre and Menkaure, identified as Khufu's son and grandson.
        - Khufu's pyramid is the oldest and largest of the three. According to the page, another building did not exceed its height for 3,800 years.
        - Each king's pyramid was the principal element of a larger complex. These complexes included smaller subsidiary queens' pyramids; a satellite pyramid that served as a second, symbolic tomb for the king; mastaba tombs for nobility and other family members; burials of actual and/or symbolic boats; and two temples connected by a richly decorated causeway.
        - The valley temple formed the entrance to the pyramid complex and stood on or near water where boats could dock.
        - The funerary temple, also called the upper temple, stood near the pyramid's base.
        - Priests maintained the deceased king's mortuary cult in the temples. The king's divine aspect was worshiped there, and rich, varied offerings were presented to his soul for a peaceful and luxurious afterlife.
        - The page displays opening hours of 8:00 AM to 4:00 PM for every day from Sunday through Saturday.
        - The page displays area-entry prices for foreigners of EGP 700 for an adult and EGP 350 for a student, and for Egyptians of EGP 60 for an adult and EGP 30 for a student.
        - The displayed area-entry ticket does not cover the Great Pyramid, Pyramid of Khafre, Pyramid of Menkaure, Tomb of Meresankh III, or Workers' Cemetery.
        - Displayed vehicle-ticket prices are EGP 25 for a car or taxi, EGP 50 for a microbus, EGP 75 for a coaster, and EGP 100 for a bus.
        """;

    public static string? TryAnswer(string question)
    {
        var text = question.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (ContainsAny(text, "car ticket", "taxi ticket", "vehicle ticket", "microbus", "micro-bus", "coaster ticket", "bus ticket price") ||
            (ContainsAny(text, "car", "taxi", "coaster") &&
             ContainsAny(text, "price", "cost", "how much", "ticket")))
        {
            return "The official Giza Plateau page lists vehicle tickets at EGP 25 for a car or taxi, EGP 50 for a microbus, EGP 75 for a coaster, and EGP 100 for a bus.";
        }

        if (ContainsAny(text, "ticket cover", "ticket include", "ticket exclude", "area ticket", "entry include", "entry exclude", "not cover", "excluded from", "separate ticket") &&
            ContainsAny(text, "great pyramid", "khafre", "menkaure", "meresankh", "worker", "cemetery", "pyramid"))
        {
            return "The official page says the area-entry ticket does not cover the Great Pyramid, Pyramid of Khafre, Pyramid of Menkaure, Tomb of Meresankh III, or Workers’ Cemetery.";
        }

        if (ContainsAny(text, "official page hours", "official site hours", "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "every day", "daily hours"))
        {
            return "The official Giza Plateau page displays opening hours of 8:00 AM to 4:00 PM every day, Sunday through Saturday.";
        }

        if (ContainsAny(text, "where is giza plateau", "giza plateau location", "displayed location", "site location"))
        {
            return "The official page lists the Giza Plateau’s location as Giza.";
        }

        if (ContainsAny(text, "roman time", "roman era", "popular tourist", "tourist destination"))
        {
            return "The official page describes the Giza pyramids and Great Sphinx as among the world’s most popular tourist destinations and says they were already attractions in Roman times.";
        }

        if (ContainsAny(text, "khufu", "great pyramid", "three pyramids") &&
            ContainsAny(text, "oldest", "largest", "tallest", "height record", "3,800", "3800", "exceed", "taller building"))
        {
            return "The official page says Khufu’s pyramid is the oldest and largest of the three and that another building did not exceed its height for 3,800 years.";
        }

        if (ContainsAny(text, "who built", "built for", "whose pyramid", "khufu's son", "khufu’s son", "khufu's grandson", "khufu’s grandson") &&
            ContainsAny(text, "great pyramid", "khufu", "khafre", "menkaure", "three pyramid"))
        {
            return "The official page says the Great Pyramid was built for King Khufu, while the other two main pyramids were built for Khafre and Menkaure, identified as Khufu’s son and grandson.";
        }

        if (ContainsAny(text, "fourth dynasty", "4th dynasty", "final resting", "royal burial"))
        {
            return "The official page describes the royal pyramid structures as final resting places for kings of the Fourth Dynasty, dated there to about 2613–2494 BC.";
        }

        if (ContainsAny(text, "satellite pyramid", "symbolic tomb", "second tomb"))
        {
            return "According to the official page, a satellite pyramid in a king’s pyramid complex acted as a second, symbolic tomb for the king.";
        }

        if (ContainsAny(text, "queen's pyramid", "queens' pyramid", "queens’ pyramid", "subsidiary pyramid"))
        {
            return "The official page says a king’s larger pyramid complex included smaller subsidiary queens’ pyramids.";
        }

        if (ContainsAny(text, "mastaba", "nobility tomb", "family tomb"))
        {
            return "The official page says the larger pyramid complexes included mastaba tombs for nobility and other family members.";
        }

        if (ContainsAny(text, "boat burial", "buried boat", "symbolic boat", "actual boat"))
        {
            return "The official page says the larger pyramid complexes included burials of actual and/or symbolic boats.";
        }

        if (ContainsAny(text, "valley temple", "boats dock", "boat dock", "near water", "body of water"))
        {
            return "The official page says the valley temple formed the entrance to the pyramid complex and stood on or near water where boats could dock.";
        }

        if (ContainsAny(text, "funerary temple", "upper temple"))
        {
            return "The official page identifies the funerary temple, also called the upper temple, as the temple near the pyramid’s base.";
        }

        if (ContainsAny(text, "mortuary cult", "divine aspect", "priest", "offering", "king's soul", "king’s soul", "afterlife"))
        {
            return "The official page says priests maintained the deceased king’s mortuary cult in the temples, where his divine aspect was worshiped and rich, varied offerings were presented to his soul for a peaceful and luxurious afterlife.";
        }

        if (ContainsAny(text, "causeway", "pair of temples", "two temples"))
        {
            return "The official page says each larger pyramid complex had a valley temple and a funerary or upper temple linked by a richly decorated causeway.";
        }

        if (ContainsAny(text, "pyramid complex", "surrounding monument", "other monument", "around the pyramid", "besides the pyramid"))
        {
            return "The official page says each king’s pyramid was the main element of a larger complex with subsidiary queens’ pyramids, a satellite pyramid, mastaba tombs, actual and/or symbolic boat burials, and two temples linked by a richly decorated causeway.";
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);
}
