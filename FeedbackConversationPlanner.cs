using System.Text.RegularExpressions;

namespace AndroidApp1;

internal enum FeedbackDetailState
{
    NotNeeded,
    Missing,
    Provided,
    Unavailable
}

internal enum FeedbackNextStep
{
    Incident,
    ScamDetails,
    Location,
    Person,
    Complete
}

internal enum FeedbackCameraAssistance
{
    None,
    Person,
    SellerOrProvider,
    VisibleIssue
}

internal sealed record FeedbackConversationPlan(
    string Report,
    string LatestVisitorMessage,
    bool HasIncidentDescription,
    bool IsInvoiceIssue,
    bool IsGateScam,
    bool ScammerMayBePresent,
    bool InvolvesPerson,
    FeedbackDetailState ScamDetails,
    FeedbackDetailState Location,
    FeedbackDetailState Person,
    FeedbackNextStep NextStep,
    int UserTurnCount,
    int IncidentQuestionCount,
    int ScamDetailsQuestionCount,
    int LocationQuestionCount,
    int PersonQuestionCount,
    bool LatestReplyDeclinesRequestedDetail)
{
    public bool IsComplete => NextStep == FeedbackNextStep.Complete;
}

/// <summary>
/// Supplies advisory report-state context to the language model and deterministic
/// fallbacks when the model is unavailable. The model owns the conversation.
/// </summary>
internal static partial class FeedbackConversationPlanner
{
    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "at", "bad", "be", "do", "for", "had", "has", "have",
        "he", "help", "i", "id", "in", "is", "it", "know", "me", "my", "name", "no",
        "not", "number", "of", "on", "person", "problem", "provide", "service", "she",
        "something", "sure", "that", "the", "their", "there", "they", "this", "to", "very",
        "was", "were", "with", "wrong"
    };

    private static readonly HashSet<string> NonNameCapitalizedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "agent", "an", "at", "attendant", "boy", "cairo", "cashier", "child", "driver",
        "employee", "from", "gate", "girl", "giza", "great", "group", "guard", "guide", "he",
        "i", "invoice", "khafre", "khufu", "kiosk", "man", "maybe", "menkaure", "my", "near", "no", "none", "officer",
        "panorama", "person", "photos", "professional", "provider", "pyramid", "seller", "service",
        "okay", "ok", "she", "shop", "someone", "somebody", "sphinx", "staff", "station", "the", "there", "they", "this",
        "today", "tonight", "tourist", "unknown", "vendor", "visitor", "woman", "yes", "yesterday",
        "are", "can", "could", "did", "do", "does", "how", "is", "should", "what", "when", "where", "why", "would"
    };

    internal static FeedbackConversationPlan Analyze(IReadOnlyList<TourChatMessage> history)
    {
        var allUserMessages = history
            .Where(message => message.IsUser)
            .Select(message => message.Text.Trim())
            .Where(message => message.Length > 0)
            .ToArray();
        var userMessages = allUserMessages
            .Where(IsPotentialReportDetail)
            .ToArray();
        var report = string.Join(" ", userMessages);
        var latestVisitorMessage = allUserMessages.LastOrDefault() ?? string.Empty;
        var assistantMessages = history
            .Where(message => !message.IsUser)
            .Select(message => message.Text)
            .ToArray();
        var lastAssistantMessage = assistantMessages.LastOrDefault() ?? string.Empty;

        var incidentQuestionCount = assistantMessages.Count(AsksForIncident);
        var scamDetailsQuestionCount = assistantMessages.Count(AsksForScamDetails);
        var locationQuestionCount = assistantMessages.Count(AsksForLocation);
        var personQuestionCount = assistantMessages.Count(AsksForPerson);
        var isInvoiceIssue = InvoiceWasNotProvided(report);
        var isGateScam = IsGateScamReport(report);
        var scammerMayBePresent = isGateScam && ScammerMayBePresent(report);
        var involvesPerson = isInvoiceIssue || LikelyInvolvesAnotherPerson(Normalize(report));
        var hasIncidentDescription = userMessages.Any(HasIncidentDescription);

        // A gate-redirection report that already says an additional charge was demanded
        // contains the relevant conduct. The person's exact wording, payment mechanism,
        // or requested amount is not useful enough to make the visitor repeat it.
        var hasScamDetails = isGateScam;
        var scamDetailsUnavailable = isGateScam && !hasScamDetails &&
            ((AsksForScamDetails(lastAssistantMessage) && IsUnavailableReply(latestVisitorMessage)) ||
             scamDetailsQuestionCount >= 2);
        var scamDetailsState = !isGateScam
            ? FeedbackDetailState.NotNeeded
            : hasScamDetails
                ? FeedbackDetailState.Provided
                : scamDetailsUnavailable
                    ? FeedbackDetailState.Unavailable
                    : FeedbackDetailState.Missing;

        // This report originates in the guided Great Gate entry context, so the app
        // already knows the location and must not ask the visitor to repeat it.
        var hasLocation = isGateScam || userMessages.Any(message =>
            HasReportLocation(message) &&
            !IsExplicitlyUnavailableLocation(message) &&
            (!isGateScam || HasSpecificScamLocation(message)));
        var locationUnavailable = !hasLocation &&
            (userMessages.Any(IsExplicitlyUnavailableLocation) ||
             (AsksForLocation(lastAssistantMessage) && IsUnavailableReply(latestVisitorMessage)) ||
             locationQuestionCount >= 2);
        var locationState = hasLocation
            ? FeedbackDetailState.Provided
            : locationUnavailable
                ? FeedbackDetailState.Unavailable
                : FeedbackDetailState.Missing;

        var hasPersonDetail = involvesPerson && userMessages.Any(message =>
            HasSpecificPersonDetail(message, isInvoiceIssue, isGateScam) &&
            !IsExplicitlyUnavailablePerson(message));
        if (!hasPersonDetail &&
            involvesPerson &&
            AsksForPerson(lastAssistantMessage) &&
            LooksLikeConcreteAnswer(latestVisitorMessage))
        {
            hasPersonDetail = true;
        }

        var personUnavailable = involvesPerson && !hasPersonDetail &&
            (userMessages.Any(IsExplicitlyUnavailablePerson) ||
             (AsksForPerson(lastAssistantMessage) && IsUnavailableReply(latestVisitorMessage)) ||
             personQuestionCount >= 2);
        // A name or description is optional for this scenario. Photo controls remain
        // available without turning identity into a required follow-up question.
        var personState = !involvesPerson || isGateScam
            ? FeedbackDetailState.NotNeeded
            : hasPersonDetail
                ? FeedbackDetailState.Provided
                : personUnavailable
                    ? FeedbackDetailState.Unavailable
                    : FeedbackDetailState.Missing;

        var nextStep = !hasIncidentDescription
            ? FeedbackNextStep.Incident
            : scamDetailsState == FeedbackDetailState.Missing
                ? FeedbackNextStep.ScamDetails
                : locationState == FeedbackDetailState.Missing
                    ? FeedbackNextStep.Location
                    : personState == FeedbackDetailState.Missing
                        ? FeedbackNextStep.Person
                        : FeedbackNextStep.Complete;

        var latestReplyDeclinesRequestedDetail =
            IsUnavailableReply(latestVisitorMessage) &&
            (AsksForIncident(lastAssistantMessage) ||
             AsksForScamDetails(lastAssistantMessage) ||
             AsksForLocation(lastAssistantMessage) ||
             AsksForPerson(lastAssistantMessage));

        return new FeedbackConversationPlan(
            report,
            latestVisitorMessage,
            hasIncidentDescription,
            isInvoiceIssue,
            isGateScam,
            scammerMayBePresent,
            involvesPerson,
            scamDetailsState,
            locationState,
            personState,
            nextStep,
            allUserMessages.Length,
            incidentQuestionCount,
            scamDetailsQuestionCount,
            locationQuestionCount,
            personQuestionCount,
            latestReplyDeclinesRequestedDetail);
    }

    internal static string BuildFallbackResponse(FeedbackConversationPlan plan)
    {
        var acknowledgement = plan.LatestReplyDeclinesRequestedDetail
            ? "That's okay. "
            : plan.UserTurnCount > 1
                ? "Thank you. "
                : string.Empty;

        return plan.NextStep switch
        {
            FeedbackNextStep.Incident when plan.IncidentQuestionCount > 0 =>
                acknowledgement + "I need only a short description to create the report. What part of the problem can you describe?",
            FeedbackNextStep.Incident =>
                acknowledgement + "Could you briefly describe what happened?",
            // These gate-only branches are defensive. Analysis normally marks the
            // conduct and Great Gate location as already provided, so no question is due.
            FeedbackNextStep.ScamDetails when plan.IsGateScam =>
                acknowledgement + BuildGateScamSafetyPrefix(),
            FeedbackNextStep.Location when plan.IsGateScam =>
                acknowledgement + BuildGateScamSafetyPrefix(),
            FeedbackNextStep.Location when plan.LocationQuestionCount > 0 =>
                acknowledgement + "If the exact location is unclear, what nearby station, landmark, or service area do you remember?",
            FeedbackNextStep.Location =>
                acknowledgement + "Where did this happen? The nearest station, landmark, or service area is enough.",
            FeedbackNextStep.Person when plan.IsGateScam =>
                acknowledgement + BuildGateScamSafetyPrefix(),
            FeedbackNextStep.Person when plan.PersonQuestionCount > 0 =>
                acknowledgement + "If the name or ID is unavailable, what role or short description do you remember?",
            FeedbackNextStep.Person when plan.IsInvoiceIssue =>
                acknowledgement + "Do you know the seller/provider's name, ID number, or a short description? It is okay if you do not.",
            FeedbackNextStep.Person =>
                acknowledgement + "Do you know the involved person's name, role, or a short description? It is okay if you do not.",
            _ => string.Empty
        };
    }

    internal static FeedbackCameraAssistance GetCameraAssistance(FeedbackConversationPlan plan)
    {
        if (!VerifiedGateIncidentScenario.ShouldUnlockCamera(plan.Report, plan.LatestVisitorMessage))
        {
            return FeedbackCameraAssistance.None;
        }

        if (plan.IsInvoiceIssue)
        {
            return FeedbackCameraAssistance.SellerOrProvider;
        }

        return VerifiedGateIncidentScenario.IsSuspiciousReport(plan.Report) || plan.InvolvesPerson
            ? FeedbackCameraAssistance.Person
            : FeedbackCameraAssistance.VisibleIssue;
    }

    internal static string BuildPromptInstruction()
    {
        return "You are AI Support inside the Something Wrong? Report to Us screen. The visitor is already using the report option, so never tell them to open, choose, or use this same option. " +
            "Use your full conversational and reasoning ability with the complete recent conversation. Reason silently about the visitor's intent, what is already known, what is genuinely missing, and the human impact before replying; do not output that analysis. Respond to what the visitor actually means, whether it is a question, hypothetical, correction, clarification, or an actual incident. " +
            "When the visitor is making a real report, help them describe the useful details naturally and decide for yourself what response or follow-up, if any, is most helpful. Do not force questions, repeat answered points, or treat ordinary questions as report evidence. " +
            VerifiedGateIncidentScenario.AppOnlyPaymentStatement + " " +
            "For a gate-redirection or additional-charge report, refer to the individual only as the person. They are not one of the authorized gate officers and are not a seller or provider. Entry through the official gates remains available, and officers are present at those gates. The Great Gate and every official station afterward are safe places. Treat this only as preventative guidance about outside conduct, never as a threat or danger at a station. Never tell the visitor to show that person a ticket. Calmly advise the visitor not to pay the person or return to them. The redirection and additional-charge demand already describe the relevant conduct: never ask what the person said, requested, offered, or tried to charge. The app already knows this happened at the Great Gate and records the phone's current local time, so never ask where or when it happened. Do not require another detail. If mentioning a photo, only offer assistance adding one to the report; never tell the visitor how, when, where, or what to photograph. " +
            "Seller/provider and invoice language belongs only to a report specifically about a seller or provider failing to issue an invoice. " +
            "Never invent facts, app actions, staff actions, or claim that a report was sent, submitted, delivered, or reviewed.";
    }

    internal static bool IsReportScreenClarification(string message)
    {
        var normalized = Normalize(message);
        return ContainsAny(
            normalized,
            "this is report option", "this is the report option", "this is report page",
            "this is the report page", "this is report screen", "this is the report screen",
            "this is report chat", "this is the report chat", "already in the report",
            "already on the report", "already using the report", "we are in the report",
            "we're in the report", "i am in the report", "i'm in the report");
    }

    internal static string BuildCompletionResponse(FeedbackConversationPlan plan) =>
        plan.IsGateScam
            ? $"Thank you. {BuildGateScamGuidanceResponse()}"
            : "Thank you. I have enough information for this report.";

    internal static string BuildGateScamGuidanceResponse() =>
        BuildGateScamSafetyPrefix().Trim();

    private static bool IsPotentialReportDetail(string message) =>
        !VerifiedGateIncidentScenario.LooksLikeHypotheticalOrPolicyQuestion(message) &&
        !IsReportScreenClarification(message);

    internal static bool AcceptsModelFollowUp(
        string followUp,
        FeedbackConversationPlan plan,
        IReadOnlyList<TourChatMessage> history)
    {
        if (plan.IsComplete || followUp.Count(character => character == '?') != 1)
        {
            return false;
        }

        var asksIncident = AsksForIncident(followUp);
        var asksScamDetails = AsksForScamDetails(followUp);
        var asksLocation = AsksForLocation(followUp);
        var asksPerson = AsksForPerson(followUp);
        if (plan.Location != FeedbackDetailState.Missing && asksLocation)
        {
            return false;
        }

        if (plan.Person != FeedbackDetailState.Missing && asksPerson)
        {
            return false;
        }

        if (plan.ScamDetails != FeedbackDetailState.Missing && asksScamDetails)
        {
            return false;
        }

        var asksOnlyForNextStep = plan.NextStep switch
        {
            FeedbackNextStep.Incident => asksIncident && !asksScamDetails && !asksLocation && !asksPerson,
            FeedbackNextStep.ScamDetails => asksScamDetails && !asksIncident && !asksLocation && !asksPerson,
            FeedbackNextStep.Location => asksLocation && !asksScamDetails && !asksPerson && !asksIncident,
            FeedbackNextStep.Person => asksPerson && !asksScamDetails && !asksLocation && !asksIncident,
            _ => false
        };
        if (!asksOnlyForNextStep)
        {
            return false;
        }

        var normalizedFollowUp = NormalizeQuestionForComparison(followUp);
        return !history
            .Where(message => !message.IsUser)
            .TakeLast(3)
            .Select(message => NormalizeQuestionForComparison(message.Text))
            .Any(previous => previous == normalizedFollowUp);
    }

    internal static bool InvoiceWasNotProvided(string report)
    {
        var normalized = Normalize(report);
        return normalized.Contains("invoice", StringComparison.Ordinal) &&
            ContainsAny(
                normalized,
                "didn't provide", "did not provide", "not provide", "didn't give", "did not give",
                "not give", "no invoice", "without an invoice", "never received", "didn't receive",
                "did not receive", "haven't received", "have not received", "wasn't given",
                "was not given", "refused", "wouldn't give", "would not give", "won't give",
                "will not give", "didn't issue", "did not issue", "failed to issue", "not issued",
                "invoice wasn't provided", "invoice was not provided", "invoice wasn't given",
                "invoice was not given", "missing invoice");
    }

    private static bool HasIncidentDescription(string message)
    {
        var normalized = Normalize(message);
        if (InvoiceWasNotProvided(normalized))
        {
            return true;
        }

        var words = Words().Matches(normalized).Select(match => match.Value).ToArray();
        var meaningfulWordCount = words.Count(word => word.Length > 1 && !GenericWords.Contains(word));
        var hasIncidentSignal = ContainsAny(
            normalized,
            "assault", "attack", "broke", "broken", "cancel", "charged", "closed", "damag",
            "delay", "demand", "didn't work", "did not work", "dirty", "fell", "follow",
            "forced", "fraud", "harass", "hit ", "injur", "insult", "kick", "late", "lost",
            "missing", "not working", "overcharg", "pressure", "push", "refus", "rob", "rude",
            "scam", "shout", "sick", "stalk", "steal", "stole", "threat", "touch", "unsafe",
            "wait", "yell");

        if (IsUnavailableReply(message) && !hasIncidentSignal)
        {
            return false;
        }

        if (HasReportLocation(message) && words.Length <= 6 && !hasIncidentSignal)
        {
            return false;
        }

        if (HasSpecificPersonDetail(message, isInvoiceIssue: false) &&
            words.Length <= 5 &&
            !hasIncidentSignal)
        {
            return false;
        }

        return normalized.Trim().Length >= 8 &&
            words.Length >= 2 &&
            (meaningfulWordCount >= 2 || (hasIncidentSignal && meaningfulWordCount >= 1));
    }

    private static bool HasReportLocation(string report)
    {
        var normalized = Normalize(report);
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

    private static bool IsGateScamReport(string report)
    {
        var normalized = Normalize(report);
        var describesScam = VerifiedGateIncidentScenario.IsSuspiciousReport(report);
        var mentionsGateArea = ContainsAny(
            normalized,
            "gate", "entrance", "ticket office", "great gate", "entry", "access");
        return describesScam && mentionsGateArea;
    }

    private static bool ScammerMayBePresent(string report)
    {
        var normalized = Normalize(report);
        return ContainsAny(
            normalized,
            "scammer at", "scammer is at", "still here", "right now", "currently",
            "standing", "waiting", "person is near", "person is at");
    }

    private static bool HasScamTransactionDetails(string message)
    {
        var normalized = Normalize(message);
        return ContainsAny(
            normalized,
            "said", "told me", "claimed", "pretend", "offered", "offering", "asked me for",
            "asked me to", "requested", "wanted", "demand", "blocked", "stopped", "refused",
            "pay ", "payment", "money", "cash", "card", "charged", "price", "amount", "sold",
            "fake ticket", "invalid ticket", "counterfeit", "ticket", "pass", "qr code", "receipt");
    }

    private static bool HasSpecificScamLocation(string message)
    {
        if (!HasReportLocation(message))
        {
            return false;
        }

        var normalized = Normalize(message);
        if (!normalized.Contains("gate", StringComparison.Ordinal))
        {
            return true;
        }

        return SpecificGate().IsMatch(message) || ContainsAny(
            normalized,
            "great gate", "main gate", "north gate", "south gate", "east gate", "west gate",
            "ticket office", "visitor center", "entrance", "exit", "station", "sphinx",
            "pyramid", "panorama", "parking");
    }

    private static bool HasSpecificPersonDetail(
        string message,
        bool isInvoiceIssue,
        bool isGateScam = false)
    {
        var normalized = Normalize(message);
        var hasPersonId = isGateScam
            ? ExplicitPersonIdNumber().IsMatch(message)
            : PersonIdNumber().IsMatch(message);
        if (hasPersonId || HasPersonName(message))
        {
            return true;
        }

        if (ContainsAny(
                normalized,
                "wearing", "shirt", "jacket", "uniform", "badge", "hat ", "glasses", "tall",
                "short man", "short woman", "hair", "beard", "person photo attached",
                "person photo attached", "seller/provider photo attached"))
        {
            return true;
        }

        return !isInvoiceIssue && ContainsAny(
            normalized,
            "staff", "employee", "guide", "driver", "saddle-man", "saddle man", "caret",
            "photographer", "guard", "officer", "cashier", "attendant", "agent", "tourist",
            "visitor", "man ", "woman", "boy", "girl", "child", "group");
    }

    private static bool HasPersonName(string message)
    {
        if (NamedPerson().IsMatch(message))
        {
            return true;
        }

        return CapitalizedWord()
            .Matches(message)
            .Select(match => match.Value)
            .Any(word => !NonNameCapitalizedWords.Contains(word));
    }

    private static bool LooksLikeConcreteAnswer(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || IsUnavailableReply(message))
        {
            return false;
        }

        var normalized = Normalize(message).Trim(' ', '.', '!', '?', ',');
        if (normalized is "yes" or "okay" or "ok" or "maybe" or "why" or "what")
        {
            return false;
        }

        return Words().Matches(normalized).Any(match =>
            match.Value.Length > 1 && !GenericWords.Contains(match.Value));
    }

    private static bool IsExplicitlyUnavailablePerson(string message) =>
        IsUnavailableReply(message) && MentionsPersonIdentity(message);

    private static bool IsExplicitlyUnavailableLocation(string message) =>
        IsUnavailableReply(message) && MentionsLocation(message);

    private static bool IsUnavailableReply(string message)
    {
        var normalized = Normalize(message).Trim(' ', '.', '!', '?', ',');
        return normalized is "no" or "nope" or "idk" or "i don't" or "i do not" or "none" ||
            ContainsAny(
                normalized,
                "don't have", "do not have", "didn't have", "did not have", "haven't got",
                "have not got", "don't know", "do not know", "didn't know", "did not know",
                "can't provide", "cannot provide", "couldn't provide", "could not provide",
                "can't identify", "cannot identify", "couldn't identify", "could not identify",
                "can't remember", "cannot remember", "couldn't remember", "could not remember",
                "not sure", "no idea", "unknown", "unavailable", "no visible", "wasn't shown",
                "was not shown", "rather not", "prefer not", "forgot", "that's all", "nothing else");
    }

    private static bool MentionsPersonIdentity(string message)
    {
        var padded = $" {Normalize(message)} ";
        return ContainsAny(
            padded,
            " name", " id ", "id number", "identity", "identify", "who ", "person",
            "description", "badge");
    }

    private static bool MentionsLocation(string message)
    {
        var normalized = Normalize(message);
        return ContainsAny(
            normalized,
            "where", "location", "place", "landmark", "station", "area", "site", "spot",
            "gate", "entrance", "ticket office");
    }

    private static bool LikelyInvolvesAnotherPerson(string normalizedReport) =>
        VerifiedGateIncidentScenario.IsSuspiciousReport(normalizedReport) ||
        ContainsAny(
            normalizedReport,
            "seller", "provider", "vendor", "staff", "employee", "guide", "driver", "guard",
            "harass", "assault", "attack", "threat", "push", "hit ", "punched", "kick", "touch",
            "follow", "stalk", "rob", "stole", "steal", "scam", "fraud", "overcharg", "charged me",
            "rude", "insult", "shout", "yell", "fight", "refus", "demand", "pressure", "forced",
            "did this", "someone", "somebody", "a person", "this person") ||
        PersonPronoun().IsMatch(normalizedReport);

    private static bool AsksForIncident(string text)
    {
        var normalized = Normalize(text);
        return ContainsAny(
            normalized,
            "what happened", "what went wrong", "what was wrong", "what is the issue",
            "what was the issue", "what is the problem", "describe what", "describe the issue",
            "describe the problem", "tell me what", "more about what", "more detail about the incident",
            "explain what", "part of the problem");
    }

    private static bool AsksForScamDetails(string text)
    {
        var normalized = Normalize(text);
        return ContainsAny(
            normalized,
            "what did the person offer", "what did they offer", "what were they offering",
            "what did the person request", "what did they request", "what did the person sell",
            "what did they sell", "ask you to pay", "what did you pay", "how much",
            "what payment", "payment, ticket, service, or item", "what was involved",
            "what item was involved", "what service was involved", "what ticket was involved",
            "did the person ask you to pay", "did you receive a ticket", "did you receive a pass",
            "did you receive a receipt", "did you receive a qr code", "what amount",
            "what entry proof", "what did the person say", "what did they say",
            "what did the person do", "what did they do", "what the person said",
            "what the person requested", "what the person did");
    }

    private static bool AsksForLocation(string text)
    {
        var normalized = Normalize(text);
        return ContainsAny(
            normalized,
            "where", "location", "landmark", "station", "service area", "which area", "what area",
            "which place", "what place", "which site", "what site", "nearby spot", "which gate",
            "what gate", "gate number", "numbered gate", "which entrance", "what entrance",
            "ticket office");
    }

    private static bool AsksForPerson(string text)
    {
        var padded = $" {Normalize(text)} ";
        return ContainsAny(
            padded,
            "who ", " name", " id ", "id number", "identity", "identify", "which person",
            "person's role", "their role", "description", "describe the person", "describe the seller",
            "describe the provider", "wearing", "badge");
    }

    private static string Describe(FeedbackDetailState state) => state switch
    {
        FeedbackDetailState.NotNeeded => "not needed",
        FeedbackDetailState.Missing => "missing",
        FeedbackDetailState.Provided => "provided",
        FeedbackDetailState.Unavailable => "the visitor cannot provide it",
        _ => "unknown"
    };

    private static string BuildGateScamSafetyPrefix() =>
        "Entry through the official Great Gate remains available, and gate officers are present there. All payments are handled inside the app. Please do not pay the person or return to them. ";

    private static string Normalize(string text) => text
        .ToLowerInvariant()
        .Replace('\u2019', '\'')
        .Replace('\u2018', '\'');

    private static string NormalizeQuestionForComparison(string text) =>
        string.Join(" ", Words().Matches(Normalize(text)).Select(match => match.Value));

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Words();

    [GeneratedRegex(
        @"\b(?:at|near|beside|behind|inside|outside|by|around|opposite|next to|in front of)\s+(?:the\s+)?(?!\d{1,2}(?::\d{2})?\s*(?:am|pm)\b)[\p{L}\p{N}]",
        RegexOptions.IgnoreCase)]
    private static partial Regex ReportLocation();

    [GeneratedRegex(@"\b(?:[A-Z]{1,3}-?)?\d{3,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex PersonIdNumber();

    [GeneratedRegex(@"\b(?:id|identity|badge)\s*(?:number|no\.?|#)?\s*(?:[A-Z]{1,3}-?)?\d{3,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitPersonIdNumber();

    [GeneratedRegex(@"\bgate\s*(?:(?:number|no\.?)\s*)?(?:\d+|one|two|three|four|five|six)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpecificGate();

    [GeneratedRegex(@"\b(?:name(?:\s+is|\s+was)?|named|called)\s+[\p{L}'-]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex NamedPerson();

    [GeneratedRegex(@"\b\p{Lu}[\p{L}'-]{2,}\b")]
    private static partial Regex CapitalizedWord();

    [GeneratedRegex(@"\b(?:he|she|they)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PersonPronoun();
}
