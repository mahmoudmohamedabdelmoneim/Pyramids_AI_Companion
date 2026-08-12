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
    private const string LogTag = "AI.Support";
    private const string SupportSystemPrompt =
        "You are AI Support in the Giza visitor app. This is a benign customer-service task. " +
        "Use your full conversational judgment and reasoning ability. Understand what the visitor experienced before replying, and acknowledge a completed ordinary incident with specific, proportionate empathy. " +
        "Write naturally in your own words instead of using a canned customer-service script. Be calm, helpful, truthful, and concise. Never refuse an ordinary report. " +
        "Never invent facts, app state, staff actions, transactions, attachments, or report submission. " +
        "Output only the response to the visitor.";

    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "at", "bad", "be", "for", "had", "has", "have", "help",
        "i", "in", "is", "it", "me", "my", "of", "on", "problem", "service", "something",
        "that", "the", "there", "they", "this", "to", "very", "was", "went", "with", "wrong"
    };

    private static readonly LocalQwenRuntime Runtime = OnDeviceTourGuideService.SharedRuntime;

    public async Task PrepareForChatAsync(Context context)
    {
        if (!LocalQwenRuntime.IsSupported)
        {
            return;
        }

        try
        {
            var modelPath = await QwenModelInstaller.TryGetModelPathAsync(context).ConfigureAwait(false);
            if (modelPath is not null)
            {
                await Runtime.PrepareAsync(
                    context,
                    modelPath,
                    systemPrompt: SupportSystemPrompt).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // RespondAsync still has truthful deterministic fallbacks if preparation fails.
        }
    }

    public async Task<SupportChatReply> RespondAsync(
        Context context,
        IReadOnlyList<TourChatMessage> history,
        SupportChatPurpose purpose,
        bool requestAcknowledged = false)
    {
        var visitorReport = string.Join(" ", history.Where(message => message.IsUser).Select(message => message.Text));
        var latestVisitorMessage = history.LastOrDefault(message => message.IsUser)?.Text ?? string.Empty;
        if (requestAcknowledged)
        {
            return await ContinueConversationAsync(context, history, purpose, latestVisitorMessage);
        }

        var feedbackPlan = purpose == SupportChatPurpose.Feedback
            ? FeedbackConversationPlanner.Analyze(history)
            : null;
        var hasReasonableContext = purpose == SupportChatPurpose.Emergency
            ? HasReasonableEmergencyContext(visitorReport)
            : feedbackPlan!.IsComplete;
        if (hasReasonableContext && purpose == SupportChatPurpose.Emergency)
        {
            var completion = BuildEmergencyCompletion(visitorReport);
            return new SupportChatReply(completion, HasReasonableContext: true);
        }

        if (feedbackPlan is { IsGateScam: true, IsComplete: true })
        {
            return new SupportChatReply(
                FeedbackConversationPlanner.BuildCompletionResponse(feedbackPlan),
                HasReasonableContext: true);
        }

        var fallbackQuestion = purpose == SupportChatPurpose.Emergency
            ? BuildEmergencyFallbackResponse(visitorReport, Words().Matches(visitorReport).Count)
            : feedbackPlan!.IsComplete
                ? FeedbackConversationPlanner.BuildCompletionResponse(feedbackPlan)
                : BuildFeedbackFallbackResponse(feedbackPlan);
        var modelPath = await QwenModelInstaller.TryGetModelPathAsync(context);
        if (modelPath is null)
        {
            Android.Util.Log.Warn(LogTag, "Qwen model unavailable; using the support fallback.");
            return new SupportChatReply(fallbackQuestion, HasReasonableContext: hasReasonableContext);
        }

        var prompt = BuildSupportPrompt(history, purpose);
        var modelReply = await Runtime.TryCompleteAsync(
            context,
            modelPath,
            prompt,
            systemPrompt: SupportSystemPrompt);
        var followUp = purpose == SupportChatPurpose.Feedback
            ? NormalizeFeedbackReply(modelReply)
            : NormalizeFollowUp(modelReply);
        if (purpose == SupportChatPurpose.Feedback &&
            feedbackPlan is { IsGateScam: true, IsComplete: true } &&
            !CoversCompleteGateIncident(followUp))
        {
            Android.Util.Log.Info(
                LogTag,
                "Qwen's first gate-incident reply lacked semantic coverage; asking the model to revise it.");
            var revisedReply = await Runtime.TryCompleteAsync(
                context,
                modelPath,
                BuildGateIncidentRevisionPrompt(history),
                systemPrompt: SupportSystemPrompt);
            followUp = NormalizeFeedbackReply(revisedReply);
        }
        if (purpose == SupportChatPurpose.Feedback)
        {
            Android.Util.Log.Info(
                LogTag,
                followUp is not null &&
                (feedbackPlan is not { IsGateScam: true, IsComplete: true } || CoversCompleteGateIncident(followUp))
                    ? "Accepted Qwen support reply."
                    : string.IsNullOrWhiteSpace(modelReply)
                        ? "Qwen returned no support reply; using the fallback."
                        : "Qwen support reply failed a truthfulness or semantic-coverage guard; using the fallback.");
        }
        if (followUp is not null && purpose == SupportChatPurpose.Emergency &&
            !CoversEmergencyResponseRequirements(followUp, visitorReport))
        {
            followUp = null;
        }

        if (followUp is not null && purpose == SupportChatPurpose.Feedback)
        {
            if (feedbackPlan is { IsGateScam: true, IsComplete: true } &&
                !CoversCompleteGateIncident(followUp))
            {
                followUp = null;
            }
        }

        if (followUp is not null && purpose == SupportChatPurpose.Feedback)
        {
            followUp = AppTourKnowledge.ApplyCriticalPolicyGuard(latestVisitorMessage, followUp);
        }

        if (followUp is not null && purpose == SupportChatPurpose.Emergency)
        {
            followUp = AddEmergencyEmpathy(followUp, visitorReport);
        }

        return new SupportChatReply(
            followUp ?? fallbackQuestion,
            HasReasonableContext: hasReasonableContext);
    }

    private static async Task<SupportChatReply> ContinueConversationAsync(
        Context context,
        IReadOnlyList<TourChatMessage> history,
        SupportChatPurpose purpose,
        string latestVisitorMessage)
    {
        var fallback = purpose == SupportChatPurpose.Emergency
            ? "I'm still here. Keep sharing any changes or details, and alert nearby staff immediately if you can."
            : BuildFeedbackFallbackResponse(FeedbackConversationPlanner.Analyze(history));
        if (string.IsNullOrWhiteSpace(fallback))
        {
            fallback = "Thank you for the additional detail. You can keep sharing information or ask another question.";
        }
        var modelPath = await QwenModelInstaller.TryGetModelPathAsync(context);
        if (modelPath is null)
        {
            return new SupportChatReply(fallback, HasReasonableContext: false);
        }

        var conversation = string.Join(
            "\n",
            history.TakeLast(10).Select(message =>
                $"{(message.IsUser ? "Visitor" : "Support")}: {message.Text}"));
        var task = purpose == SupportChatPurpose.Emergency
            ? "Continue an active emergency-support conversation after the help request was acknowledged. " +
              "Respond naturally to the visitor's latest message, acknowledge important changes, and ask at most one useful safety follow-up question. " +
              "Do not diagnose, recommend medication, invent staff actions, or repeat that help was dispatched. "
            : "You are AI Support inside the Something Wrong? Report to Us screen. Continue the active support conversation after the report was acknowledged. " +
              "The visitor is already using the report option, so never tell them to open, choose, or use it. " +
              "Use your full conversational judgment: respond to the latest meaning, acknowledge useful new details, answer relevant questions, and ask at most one genuinely useful follow-up. " +
              "Do not invent actions by staff or claim that a new report was submitted. ";
        var prompt = task +
            "The visitor controls when to leave, so never end, close, conclude, or sign off from the conversation, and never say that no more messages can be sent. " +
            "Output only the response in no more than three short sentences.\n" +
            conversation;
        var modelReply = await Runtime.TryCompleteAsync(
            context,
            modelPath,
            prompt,
            systemPrompt: SupportSystemPrompt);
        var reply = purpose == SupportChatPurpose.Feedback
            ? NormalizeFeedbackReply(modelReply, allowAcknowledgedReport: true)
            : NormalizeOngoingReply(modelReply);
        if (reply is not null && purpose == SupportChatPurpose.Feedback)
        {
            reply = AppTourKnowledge.ApplyCriticalPolicyGuard(latestVisitorMessage, reply);
        }

        return new SupportChatReply(reply ?? fallback, HasReasonableContext: false);
    }

    private static string BuildFeedbackFallbackResponse(FeedbackConversationPlan plan)
    {
        if (FeedbackConversationPlanner.IsReportScreenClarification(plan.LatestVisitorMessage))
        {
            return "Yes, you are already in Something Wrong? Report to Us. If you are reporting a real incident, tell me what happened and I will collect the useful details here.";
        }

        if (VerifiedGateIncidentScenario.AsksAboutPersonCollectingPayment(plan.LatestVisitorMessage))
        {
            return $"No. {VerifiedGateIncidentScenario.AppOnlyPaymentStatement} Do not pay the person or return to them.";
        }

        return FeedbackConversationPlanner.BuildFallbackResponse(plan);
    }

    internal static bool HasReasonableReportContext(string report)
        => FeedbackConversationPlanner.Analyze([new TourChatMessage(true, report)]).IsComplete;

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
        var feedbackPlan = purpose == SupportChatPurpose.Feedback
            ? FeedbackConversationPlanner.Analyze(history)
            : null;
        if (feedbackPlan is { IsGateScam: true, IsComplete: true })
        {
            return "AI SUPPORT: COMPLETE INCIDENT RESPONSE\n" +
                $"Complete recent conversation:\n{conversation}\n\n" +
                "Semantic context supplied by the app: this is a complete ordinary incident report. Understand and acknowledge the specific conduct in the visitor's own words, including redirection only if the visitor actually mentioned it. " +
                "The app already has the Great Gate location and the phone's current local timestamp " +
                $"({DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}), so no detail is missing. The person asking for the additional payment is not a gate officer. " +
                "Entry through the official Great Gate remains available, and gate officers are present there. All payments are handled inside the app. " +
                "The Great Gate and every later official station are safe; this is calm guidance about the person's outside conduct, not a danger warning.\n\n" +
                "Write two or three short sentences directly to the visitor. First respond to what they experienced with warm, proportionate empathy. Then naturally reassure them about Great Gate entry and officers, explain the in-app payment rule, and advise them not to pay the person or return to them. " +
                "Use your own wording and conversational judgment rather than copying an acknowledgment template. Ask no question. Do not mention a seller, provider, invoice, ticket, camera, legitimacy, report submission, review, or staff action. Output only the visitor-facing reply.";
        }

        var task = purpose == SupportChatPurpose.Emergency
            ? BuildEmergencyPromptTask(visitorReport)
            : FeedbackConversationPlanner.BuildPromptInstruction();

        var outputInstruction = purpose == SupportChatPurpose.Emergency
            ? " Output only the emergency response.\n"
            : " Output only your natural reply to the visitor.\n";
        return task + outputInstruction + conversation;
    }

    private static string BuildGateIncidentRevisionPrompt(IReadOnlyList<TourChatMessage> history)
    {
        var visitorWords = string.Join(
            " ",
            history.Where(message => message.IsUser).Select(message => message.Text.Trim()));
        return "Revise your previous reply from scratch because it was generic, asked for information already known, or omitted useful guidance. " +
            $"The visitor said: {visitorWords} " +
            "Address the visitor as you. In two or three short, natural sentences: empathize specifically with being put in the position the visitor described; reassure them that official Great Gate entry remains available and officers are present there; and explain that payments are handled inside the app, so they should not pay the person or return to them. " +
            "Do not ask a question or claim that anything was submitted, reviewed, or acted on. Do not mention a seller, provider, invoice, ticket, camera, or legitimacy. Preserve these meanings but choose the wording yourself. Output only the revised reply.";
    }

    private static bool CoversCompleteGateIncident(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return false;
        }

        var normalized = reply.ToLowerInvariant();
        var hasSpecificEmpathy = ContainsAny(
            normalized,
            "i'm sorry", "i am sorry", "sorry you", "understand how", "understand that", "uncomfortable",
            "frustrating", "frustrated", "unwelcome", "upsetting", "shouldn't have been", "should not have been");
        var reassuresEntry = ContainsAny(normalized, "great gate", "official gate") &&
            ContainsAny(normalized, "entry", "enter", "access") &&
            ContainsAny(normalized, "available", "open", "remains") &&
            normalized.Contains("officer", StringComparison.Ordinal);
        var givesPaymentRule = normalized.Contains("app", StringComparison.Ordinal) &&
            ContainsAny(normalized, "payment", "payments", "pay");
        var saysDoNotPay = ContainsAny(
            normalized,
            "do not pay", "don't pay", "should not pay", "shouldn't pay", "avoid paying", "must not pay");
        var saysDoNotReturn = ContainsAny(
            normalized,
            "do not return", "don't return", "should not return", "shouldn't return", "not go back", "don't go back", "do not go back");
        var containsWrongContext = normalized.Contains('?') || ContainsAny(
            normalized,
            "seller", "provider", "invoice", "show your ticket", "verified ticket", "camera", "photograph",
            "legitimate", "report was sent", "report has been sent", "report was submitted",
            "report has been submitted", "staff were notified", "staff have been notified");

        return hasSpecificEmpathy && reassuresEntry && givesPaymentRule && saysDoNotPay && saysDoNotReturn && !containsWrongContext;
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

    internal static bool InvoiceWasNotProvided(string report) =>
        FeedbackConversationPlanner.InvoiceWasNotProvided(report);

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

    private static string? NormalizeFeedbackReply(
        string? modelReply,
        bool allowAcknowledgedReport = false)
    {
        if (string.IsNullOrWhiteSpace(modelReply))
        {
            return null;
        }

        var reply = modelReply.Trim();
        var normalized = reply.ToLowerInvariant();
        var falselyClaimsSubmission = !allowAcknowledgedReport && ContainsAny(
            normalized,
            "report was sent", "report has been sent", "report was submitted",
            "report has been submitted", "feedback was sent", "feedback has been sent",
            "staff were notified", "staff have been notified");
        var redirectsToCurrentScreen = ContainsAny(
            normalized,
            "use something wrong", "open something wrong", "choose something wrong",
            "select something wrong", "tap something wrong", "go to something wrong",
            "navigate to something wrong");
        if (reply.Length < 2 || falselyClaimsSubmission || redirectsToCurrentScreen)
        {
            return null;
        }

        return reply;
    }

    private static string? NormalizeOngoingReply(string? modelReply)
    {
        if (string.IsNullOrWhiteSpace(modelReply))
        {
            return null;
        }

        var reply = modelReply.Trim();
        var normalized = reply.ToLowerInvariant();
        if (reply.Length < 2 || reply.Length > 600 || ContainsAny(
                normalized,
                "chat is closed", "chat is now closed", "chat has ended", "conversation has ended",
                "conversation is over", "conversation is now over", "this concludes", "goodbye",
                "no more messages", "cannot continue this chat", "can't continue this chat",
                "you may close the chat", "you can close the chat"))
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

}
