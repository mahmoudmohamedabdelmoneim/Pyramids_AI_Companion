using Android.Content;
using System.Text.RegularExpressions;

namespace AndroidApp1;

internal sealed record SupportChatReply(string Text, bool HasReasonableContext);

internal enum SupportChatPurpose
{
    Feedback,
    Emergency
}

internal enum EmergencyType
{
    Sickness,
    Injury,
    SafetyThreat,
    FireOrSmoke,
    MissingPerson,
    Other
}

internal sealed partial class OnDeviceSupportService
{
    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "at", "bad", "be", "for", "had", "has", "have", "help",
        "i", "in", "is", "it", "me", "my", "of", "on", "problem", "service", "something",
        "that", "the", "there", "they", "this", "to", "very", "was", "went", "with", "wrong"
    };

    private static readonly HashSet<string> NonNameCapitalizedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "at", "from", "gate", "giza", "he", "i", "invoice", "kiosk", "my", "near",
        "didn't", "did", "haven't", "have", "never", "not", "panorama", "photos", "professional",
        "provider", "seller", "service", "she", "shop", "sphinx", "station", "souvenir", "the",
        "they", "this", "today", "tonight", "vendor", "wasn't", "was", "yesterday"
    };

    private static readonly LocalQwenRuntime Runtime = OnDeviceTourGuideService.SharedRuntime;

    public async Task<SupportChatReply> RespondAsync(
        Context context,
        IReadOnlyList<TourChatMessage> history,
        SupportChatPurpose purpose)
    {
        var visitorReport = string.Join(" ", history.Where(message => message.IsUser).Select(message => message.Text));
        var hasReasonableContext = purpose == SupportChatPurpose.Emergency
            ? HasReasonableEmergencyContext(visitorReport)
            : HasReasonableReportContext(visitorReport);
        if (hasReasonableContext)
        {
            var completion = purpose == SupportChatPurpose.Emergency
                ? BuildEmergencyCompletion(visitorReport)
                : string.Empty;
            return new SupportChatReply(completion, HasReasonableContext: true);
        }

        var fallbackQuestion = BuildFallbackQuestion(visitorReport, purpose);
        var modelPath = await QwenModelInstaller.TryGetModelPathAsync(context);
        if (modelPath is null)
        {
            return new SupportChatReply(fallbackQuestion, HasReasonableContext: false);
        }

        var prompt = BuildSupportPrompt(history, purpose);
        var modelReply = await Runtime.TryCompleteAsync(context, modelPath, prompt);
        var followUp = NormalizeFollowUp(modelReply);
        if (followUp is not null && purpose == SupportChatPurpose.Emergency &&
            !CoversEmergencyResponseRequirements(followUp, visitorReport))
        {
            followUp = null;
        }

        if (followUp is not null && purpose == SupportChatPurpose.Feedback &&
            !CoversMissingReportDetails(followUp, visitorReport))
        {
            followUp = null;
        }

        if (followUp is not null && purpose == SupportChatPurpose.Emergency)
        {
            followUp = AddEmergencyEmpathy(followUp, visitorReport);
        }

        return new SupportChatReply(
            followUp ?? fallbackQuestion,
            HasReasonableContext: false);
    }

    internal static bool HasReasonableReportContext(string report)
    {
        var words = Words().Matches(report).Select(match => match.Value).ToArray();
        if (report.Trim().Length < 20 || words.Length < 5)
        {
            return false;
        }

        var meaningfulWordCount = words.Count(word => word.Length > 1 && !GenericWords.Contains(word));
        if (meaningfulWordCount < 3 || !HasReportLocation(report))
        {
            return false;
        }

        var normalized = report.ToLowerInvariant();
        if (InvoiceWasNotProvided(normalized) &&
            !HasSellerProviderIdNumber(report) && !HasSellerProviderName(report))
        {
            return false;
        }

        return !LikelyInvolvesAnotherPerson(normalized) || HasInvolvedPersonDetail(normalized);
    }

    internal static bool HasReasonableEmergencyContext(string report)
    {
        var normalized = report.ToLowerInvariant();
        var words = Words().Matches(report).Select(match => match.Value).ToArray();
        var meaningfulWordCount = words.Count(word => word.Length > 1 && !GenericWords.Contains(word));
        var emergencyType = DetectEmergencyType(normalized);
        var hasLocation = HasReportLocation(report);

        if (!hasLocation || words.Length < 3)
        {
            return false;
        }

        return emergencyType != EmergencyType.Other
            ? meaningfulWordCount >= 2
            : report.Trim().Length >= 20 && meaningfulWordCount >= 4;
    }

    private static string BuildSupportPrompt(
        IReadOnlyList<TourChatMessage> history,
        SupportChatPurpose purpose)
    {
        var conversation = string.Join(
            "\n",
            history.TakeLast(8).Select(message => $"{(message.IsUser ? "Visitor" : "Support")}: {message.Text}"));

        var visitorReport = string.Join(
            " ",
            history.Where(message => message.IsUser).Select(message => message.Text));
        var task = purpose == SupportChatPurpose.Emergency
            ? BuildEmergencyPromptTask(visitorReport)
            : BuildFeedbackPromptTask(visitorReport);

        var outputInstruction = purpose == SupportChatPurpose.Emergency
            ? " Output only the emergency response.\n"
            : " Output only the follow-up question.\n";
        return task + outputInstruction + conversation;
    }

    private static string BuildFallbackQuestion(string report, SupportChatPurpose purpose)
    {
        var wordCount = Words().Matches(report).Count;
        if (purpose == SupportChatPurpose.Emergency)
        {
            return BuildEmergencyFallbackResponse(report, wordCount);
        }

        var normalizedReport = report.ToLowerInvariant();
        if (InvoiceWasNotProvided(normalizedReport))
        {
            return BuildInvoiceFallbackQuestion(report);
        }

        var isPersonalIncident = LikelyInvolvesAnotherPerson(normalizedReport);
        var needsLocation = !HasReportLocation(report);
        var needsInvolvedPerson = isPersonalIncident && !HasInvolvedPersonDetail(normalizedReport);

        if (needsLocation && needsInvolvedPerson)
        {
            return "Where was the person you're reporting when this happened, and do you know who did this or who else was involved?";
        }

        if (needsLocation)
        {
            return isPersonalIncident
                ? "Where was the person you're reporting when this happened?"
                : wordCount < 4
                    ? "Could you tell me what happened and where it happened?"
                    : "Where did this happen, such as the nearest station, landmark, or service area?";
        }

        if (needsInvolvedPerson)
        {
            return "Do you know who did this or who else was involved, such as their name, role, or description?";
        }

        return "Could you share one more specific detail about what happened?";
    }

    private static string BuildEmergencyPromptTask(string visitorReport)
    {
        var emergencyType = DetectEmergencyType(visitorReport.ToLowerInvariant());
        var hasLocation = HasReportLocation(visitorReport);
        var affectedPerson = GetAffectedPersonReference(visitorReport);
        var locationInstruction = hasLocation
            ? "The location was already provided earlier, so do not ask for it again. "
            : emergencyType == EmergencyType.Sickness
                ? $"The affected person is {affectedPerson}. Ask exactly: \"{BuildAffectedPersonLocationQuestion(visitorReport)}\" "
                : "Ask where the visitor or affected person is now. ";
        var incidentInstruction = emergencyType switch
        {
            EmergencyType.Sickness when hasLocation =>
                $"Include this guidance: \"{BuildShadeInstruction(visitorReport)}\" If the location is already known, ask the most important missing health detail, such as whether {affectedPerson} is conscious and breathing. ",
            EmergencyType.Sickness =>
                "Ask only for the affected person's location. Do not give the shade instruction until the location is known and help can be dispatched. ",
            EmergencyType.Injury =>
                "If the location is already known, ask whether the person is bleeding, conscious, or unable to move. ",
            EmergencyType.SafetyThreat =>
                "If the location is already known, ask whether the visitor is somewhere safe. ",
            EmergencyType.FireOrSmoke =>
                "If the location is already known, ask whether the visitor is safely away from the fire or smoke. ",
            EmergencyType.MissingPerson =>
                "If the visitor's location is already known, ask where they last saw the missing person. ",
            _ =>
                "Ask for the most important missing detail about what happened. "
        };
        var dispatchInstruction = hasLocation
            ? "Clearly say, \"We're on our way to you.\" "
            : "Do not say that help is on the way until the location is provided. ";

        return "Emergency assistance chat task. Start with a brief, natural, empathetic acknowledgement. " +
            locationInstruction + incidentInstruction + dispatchInstruction +
            "Ask only for the affected person's current location when it is missing. " +
            "Do not repeat a location already supplied, diagnose, recommend medication, or answer a sightseeing question.";
    }

    private static string BuildEmergencyFallbackResponse(string report, int wordCount)
    {
        var emergencyType = DetectEmergencyType(report.ToLowerInvariant());
        var hasLocation = HasReportLocation(report);
        return (emergencyType, hasLocation) switch
        {
            (EmergencyType.Sickness, false) =>
                $"I'm sorry this is happening. {BuildAffectedPersonLocationQuestion(report)}",
            (EmergencyType.Sickness, true) =>
                $"I'm sorry this is happening. We're on our way to you. {BuildShadeInstruction(report)} {BuildConsciousnessQuestion(report)}",
            (EmergencyType.Injury, false) =>
                $"I'm sorry the person is hurt. {BuildAffectedPersonLocationQuestion(report)}",
            (EmergencyType.Injury, true) =>
                "I'm sorry the person is hurt. We're on our way to you. Are they bleeding, conscious, or unable to move?",
            (EmergencyType.SafetyThreat, false) =>
                "I'm sorry this is happening. Are you somewhere safe, and where are you now?",
            (EmergencyType.SafetyThreat, true) =>
                "I'm sorry this is happening. We're on our way to you. Are you somewhere safe right now?",
            (EmergencyType.FireOrSmoke, false) =>
                "That sounds frightening. Are you away from the fire or smoke, and where are you now?",
            (EmergencyType.FireOrSmoke, true) =>
                "That sounds frightening. We're on our way to you. Are you safely away from the fire or smoke?",
            (EmergencyType.MissingPerson, false) =>
                "I'm sorry, that must be worrying. Where are you now, and where did you last see the person?",
            (EmergencyType.MissingPerson, true) =>
                "I'm sorry, that must be worrying. We're on our way to you. Where did you last see the person?",
            (EmergencyType.Other, false) when wordCount < 4 =>
                "Please tell me what happened and where you are now.",
            (EmergencyType.Other, false) =>
                "Where are you now, and is anyone sick or injured?",
            _ =>
                "We're on our way to you. Is anyone sick or injured?"
        };
    }

    private static string GetAffectedPersonReference(string report)
    {
        var normalized = report.ToLowerInvariant();
        if (normalized.Contains("my father", StringComparison.Ordinal)) return "your father";
        if (normalized.Contains("my dad", StringComparison.Ordinal)) return "your dad";
        if (normalized.Contains("my mother", StringComparison.Ordinal)) return "your mother";
        if (ContainsAny(normalized, "my mom", "my mum")) return "your mom";
        if (normalized.Contains("my husband", StringComparison.Ordinal)) return "your husband";
        if (normalized.Contains("my wife", StringComparison.Ordinal)) return "your wife";
        if (normalized.Contains("my son", StringComparison.Ordinal)) return "your son";
        if (normalized.Contains("my daughter", StringComparison.Ordinal)) return "your daughter";
        if (normalized.Contains("my brother", StringComparison.Ordinal)) return "your brother";
        if (normalized.Contains("my sister", StringComparison.Ordinal)) return "your sister";
        if (ContainsAny(normalized, "my child", "my kid")) return "your child";
        if (ContainsAny(normalized, "my grandfather", "my grandpa")) return "your grandfather";
        if (ContainsAny(normalized, "my grandmother", "my grandma")) return "your grandmother";
        if (normalized.Contains("my friend", StringComparison.Ordinal)) return "your friend";
        if (normalized.Contains("my partner", StringComparison.Ordinal)) return "your partner";
        if (normalized.Contains("my uncle", StringComparison.Ordinal)) return "your uncle";
        if (normalized.Contains("my aunt", StringComparison.Ordinal)) return "your aunt";
        if (ContainsAny(
                normalized,
                "i'm sick", "i am sick", "i feel", "i have", "i had", "my chest", "my heart",
                "help me", "i can't breathe", "i cannot breathe"))
        {
            return "you";
        }

        return "the affected person";
    }

    private static string BuildAffectedPersonLocationQuestion(string report)
    {
        var affectedPerson = GetAffectedPersonReference(report);
        if (affectedPerson == "you")
        {
            return "Where are you now?";
        }

        return $"Where is {affectedPerson}'s location?";
    }

    private static string BuildShadeInstruction(string report)
    {
        var affectedPerson = GetAffectedPersonReference(report);
        return affectedPerson == "you"
            ? "Please stay shaded if possible."
            : $"Please keep {affectedPerson} shaded if possible.";
    }

    private static string BuildConsciousnessQuestion(string report)
    {
        var affectedPerson = GetAffectedPersonReference(report);
        return affectedPerson == "you"
            ? "Are you conscious and breathing?"
            : $"Is {affectedPerson} conscious and breathing?";
    }

    private static string BuildFeedbackPromptTask(string visitorReport)
    {
        var normalizedReport = visitorReport.ToLowerInvariant();
        if (InvoiceWasNotProvided(normalizedReport))
        {
            return "Visitor support form task. The seller or provider did not issue an invoice. " +
                BuildInvoicePromptInstruction(visitorReport) +
                "Ask one short, empathetic follow-up question. Do not claim the report was sent.";
        }

        var isPersonalIncident = LikelyInvolvesAnotherPerson(normalizedReport);
        var needsLocation = !HasReportLocation(visitorReport);
        var needsInvolvedPerson = isPersonalIncident && !HasInvolvedPersonDetail(normalizedReport);

        var requiredQuestion = needsLocation && needsInvolvedPerson
            ? "Ask where the person being reported was when this happened and also ask who did it or who else was involved, such as their name, role, or description if known. "
            : needsLocation
                ? isPersonalIncident
                    ? "Ask where the person being reported was when this happened. "
                    : "Ask where the incident happened, using a station, landmark, or service area. "
                : needsInvolvedPerson
                    ? "Ask who did this or who else was involved, such as their name, role, or description if known. "
                    : "Ask for the most important missing detail about what happened. ";

        return "Visitor support form task. The report does not have enough detail yet. " +
            requiredQuestion +
            "Ask one short, empathetic follow-up question. Do not answer a sightseeing question and do not claim the report was sent.";
    }

    private static bool CoversMissingReportDetails(string followUp, string report)
    {
        var normalizedFollowUp = followUp.ToLowerInvariant();
        var normalizedReport = report.ToLowerInvariant();
        var asksForLocation = ContainsAny(
            normalizedFollowUp,
            "where", "location", "landmark", "station", "area", "place", "site", "spot");
        var asksWhoWasInvolved = ContainsAny(
            normalizedFollowUp,
            "who", "name", "role", "description", "describe", "identify", "which person");
        var paddedFollowUp = $" {normalizedFollowUp} ";
        var asksForIdNumber = ContainsAny(
            paddedFollowUp,
            " id ", "id number", "identification number", "badge number", "employee number");

        if (InvoiceWasNotProvided(normalizedReport))
        {
            if (!HasSellerProviderIdNumber(report) && !HasSellerProviderName(report) &&
                !asksForIdNumber && !asksWhoWasInvolved)
            {
                return false;
            }

            return HasReportLocation(report) || asksForLocation;
        }

        if (!HasReportLocation(report) && !asksForLocation)
        {
            return false;
        }

        return !LikelyInvolvesAnotherPerson(normalizedReport) ||
            HasInvolvedPersonDetail(normalizedReport) ||
            asksWhoWasInvolved;
    }

    private static bool CoversEmergencyResponseRequirements(string followUp, string report)
    {
        var normalizedFollowUp = followUp.ToLowerInvariant();
        var normalizedReport = report.ToLowerInvariant();
        if (normalizedFollowUp.Contains("landmark", StringComparison.Ordinal))
        {
            return false;
        }

        var hasLocation = HasReportLocation(report);
        var saysOnOurWay = ContainsAny(normalizedFollowUp, "we're on our way", "we are on our way");
        var claimsDispatch = ContainsAny(
            normalizedFollowUp,
            "on our way", "on the way to you", "help is coming", "help is on the way");
        if (hasLocation ? !saysOnOurWay : claimsDispatch)
        {
            return false;
        }

        var asksForLocation = ContainsAny(
            normalizedFollowUp,
            "where are you", "where is the person", "where are they", "where is he", "where is she",
            "where is the injured", "where is your", "what is your location", "tell me where");
        if (hasLocation == asksForLocation)
        {
            return false;
        }

        if (DetectEmergencyType(normalizedReport) == EmergencyType.Sickness)
        {
            var mentionsShade = ContainsAny(normalizedFollowUp, "shade", "shaded");
            if (hasLocation != mentionsShade)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasReportLocation(string report)
    {
        var normalized = report.ToLowerInvariant();
        if (ContainsAny(
                normalized,
                "station", "bus stop", "gate", "entrance", "exit", "hall", "cinema", "toilet",
                "clinic", "atm", "observatory", "cafe", "shop", "kiosk", "pyramid", "sphinx",
                "courtyard", "lounge", "locker", "parking", "panorama", "ticket office", "on the bus",
                "desert", "plateau", " on the road", " by the road", " trail", " path", " tent",
                "horse stable"))
        {
            return true;
        }

        return ReportLocation().IsMatch(report);
    }

    internal static bool InvoiceWasNotProvided(string normalizedReport) =>
        normalizedReport.Contains("invoice", StringComparison.Ordinal) &&
        ContainsAny(
            normalizedReport,
            "didn't provide", "did not provide", "not provide", "didn't give", "did not give",
            "not give", "no invoice", "without an invoice", "never received", "didn't receive",
            "did not receive", "haven't received", "have not received", "wasn't given",
            "was not given", "refused", "wouldn't give", "would not give", "won't give",
            "will not give", "didn't issue", "did not issue", "failed to issue", "not issued",
            "invoice wasn't provided", "invoice was not provided", "invoice wasn't given",
            "invoice was not given", "missing invoice");

    private static string BuildInvoiceFallbackQuestion(string report)
    {
        var needsIdOrName = !HasSellerProviderIdNumber(report) && !HasSellerProviderName(report);
        var needsPlace = !HasReportLocation(report);

        return (needsIdOrName, needsPlace) switch
        {
            (true, true) => "What is the seller/provider's ID number or name, and where are they located?",
            (true, false) => "What is the seller/provider's ID number or name?",
            (false, true) => "Where was the seller/provider located?",
            _ => "Could you share one more detail about the missing invoice?"
        };
    }

    private static string BuildInvoicePromptInstruction(string report)
    {
        var needsIdOrName = !HasSellerProviderIdNumber(report) && !HasSellerProviderName(report);
        var needsPlace = !HasReportLocation(report);
        if (needsIdOrName && needsPlace)
        {
            return "Ask exactly: \"What is the seller/provider's ID number or name, and where are they located?\" ";
        }

        var missingDetails = new List<string>();
        if (needsIdOrName)
        {
            missingDetails.Add("either the seller/provider's ID number or their name");
        }

        if (needsPlace)
        {
            missingDetails.Add("where they were located");
        }

        return missingDetails.Count > 0
            ? $"Ask for {string.Join(", and ", missingDetails)}. "
            : "Ask for one important missing detail without asking for their ID number, name, or location again. ";
    }

    private static bool HasSellerProviderIdNumber(string report)
    {
        var normalized = report.ToLowerInvariant();
        var padded = $" {normalized} ";
        return ContainsAny(
                padded,
                " id ", "id number", "identification number", "badge number", "employee id",
                "employee number", "staff number", "seller number", "provider number") ||
            ContainsAny(
                normalized,
                "don't know the id", "do not know the id", "didn't see an id", "did not see an id",
                "id is unknown", "id was unknown", "no visible id", "no id visible") ||
            SellerProviderIdNumber().IsMatch(report);
    }

    private static bool HasSellerProviderName(string report)
    {
        var normalized = report.ToLowerInvariant();
        if (ContainsAny(
                normalized,
                "seller's name", "seller name", "provider's name", "provider name", "vendor's name",
                "vendor name", "named ", "called ",
                "don't know the name", "do not know the name", "didn't know the name",
                "did not know the name", "not sure of the name", "name is unknown"))
        {
            return true;
        }

        return CapitalizedWord()
            .Matches(report)
            .Select(match => match.Value)
            .Any(word => !NonNameCapitalizedWords.Contains(word));
    }

    private static bool LikelyInvolvesAnotherPerson(string normalizedReport) =>
        ContainsAny(
            normalizedReport,
            "harass", "assault", "attack", "threat", "push", "hit ", "punched", "kick", "touch",
            "follow", "stalk", "rob", "stole", "steal", "scam", "fraud", "overcharg", "charged me",
            "rude", "insult", "shout", "yell", "fight", "refus", "demand", "pressure", "forced",
            "did this", "someone", "somebody", "a person", "this person", "he ", "she ", "they ");

    private static bool HasInvolvedPersonDetail(string normalizedReport) =>
        ContainsAny(
            normalizedReport,
            "seller", "provider", "vendor", "staff", "employee", "guide", "driver", "saddle-man",
            "saddle man", "caret", "photographer", "guard", "officer", "cashier", "attendant", "agent",
            "tourist", "visitor", "man ", "woman", "boy", "girl", "child", "group", "named ",
            "name is", "name was", "wearing", "uniform", "badge", "don't know", "do not know",
            "didn't know", "did not know", "couldn't identify", "could not identify", "not sure who",
            "unknown person", "didn't see", "did not see", "cannot remember", "can't remember");

    private static string? NormalizeFollowUp(string? modelReply)
    {
        if (string.IsNullOrWhiteSpace(modelReply))
        {
            return null;
        }

        var reply = modelReply.Trim();
        if (reply.Length < 8 || reply.Length > 240 ||
            reply.Contains("sent", StringComparison.OrdinalIgnoreCase) ||
            reply.Contains("submitted", StringComparison.OrdinalIgnoreCase) ||
            reply.Contains("notified", StringComparison.OrdinalIgnoreCase) ||
            reply.Contains("at this stop", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return reply;
    }

    private static EmergencyType DetectEmergencyType(string normalizedReport)
    {
        if (ContainsAny(normalizedReport,
                "sick", "ill", "dizzy", "faint", "fainted", "unconscious", "nausea", "nauseous",
                "vomit", "fever", "chest pain", "can't breathe", "cannot breathe", "breathing", "allerg",
                "seizure", "diabetic", "heart", "asthma", "medical emergency", "health emergency",
                "heat exhaustion", "heatstroke", "heat stroke", "dehydrat", "overheat"))
        {
            return EmergencyType.Sickness;
        }

        if (ContainsAny(normalizedReport,
                "injur", "bleed", "broken", "broke", "fell", "fallen", "hurt", "wound", "accident",
                "bitten", "kicked", "sprain", "cut "))
        {
            return EmergencyType.Injury;
        }

        if (ContainsAny(normalizedReport,
                "attack", "threat", "harass", "unsafe", "fight", "weapon", "gun", "knife", "assault",
                "stalk", "rob", "steal", "stole"))
        {
            return EmergencyType.SafetyThreat;
        }

        if (ContainsAny(normalizedReport, "fire", "smoke", "burning", "flame"))
        {
            return EmergencyType.FireOrSmoke;
        }

        if (ContainsAny(normalizedReport, "missing", "lost child", "lost person", "can't find", "cannot find"))
        {
            return EmergencyType.MissingPerson;
        }

        return EmergencyType.Other;
    }

    private static string AddEmergencyEmpathy(string followUp, string report)
    {
        if (ContainsAny(followUp.ToLowerInvariant(), "i'm sorry", "i understand", "that sounds", "thank you"))
        {
            return followUp;
        }

        var acknowledgement = DetectEmergencyType(report.ToLowerInvariant()) switch
        {
            EmergencyType.Sickness => "I'm sorry you're feeling unwell. ",
            EmergencyType.Injury => "I'm sorry you're hurt. ",
            EmergencyType.SafetyThreat => "I'm sorry this is happening. ",
            EmergencyType.FireOrSmoke => "That sounds frightening. ",
            EmergencyType.MissingPerson => "I'm sorry, that must be worrying. ",
            _ => "I understand you need help. "
        };
        return acknowledgement + followUp;
    }

    private static string BuildEmergencyCompletion(string report)
    {
        return DetectEmergencyType(report.ToLowerInvariant()) switch
        {
            EmergencyType.Sickness => $"We're on our way to you. {BuildShadeInstruction(report)}",
            EmergencyType.Injury => "I'm sorry the person is hurt. We're on our way to you.",
            EmergencyType.SafetyThreat => "I'm sorry this is happening. We're on our way to you.",
            EmergencyType.FireOrSmoke => "Thank you for telling me. We're on our way to you.",
            EmergencyType.MissingPerson => "I'm sorry, that must be worrying. We're on our way to you.",
            _ => "Thank you for explaining. We're on our way to you."
        };
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Words();

    [GeneratedRegex(
        @"\b(?:at|near|beside|behind|inside|outside|by|around|opposite|next to|in front of)\s+(?:the\s+)?(?!\d{1,2}(?::\d{2})?\s*(?:am|pm)\b)[\p{L}\p{N}]",
        RegexOptions.IgnoreCase)]
    private static partial Regex ReportLocation();

    [GeneratedRegex(@"\b\p{Lu}[\p{L}'-]{2,}\b")]
    private static partial Regex CapitalizedWord();

    [GeneratedRegex(@"\b(?:[A-Z]{1,3}-?)?\d{3,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex SellerProviderIdNumber();
}
