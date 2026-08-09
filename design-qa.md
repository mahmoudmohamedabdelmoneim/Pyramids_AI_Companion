# Menkaure Guide Flow Design QA

## Comparison Target

- Source visual truth: `C:\Users\mahmo\AppData\Local\Temp\codex-clipboard-b1e2d025-28d5-4082-ba8a-22e734d2cbd4.png`
- Implementation screenshots:
  - `C:\Users\mahmo\source\repos\AndroidApp1\.qa\menkaure-history-top.png`
  - `C:\Users\mahmo\source\repos\AndroidApp1\.qa\menkaure-history-bottom.png`
  - `C:\Users\mahmo\source\repos\AndroidApp1\.qa\menkaure-departure.png`
  - `C:\Users\mahmo\source\repos\AndroidApp1\.qa\menkaure-departure-priority.png`
  - `C:\Users\mahmo\source\repos\AndroidApp1\.qa\khafre-transfer.png`
- Source pixels: 368 x 621. The source is a cropped view of the existing Menkaure station page; its device density is not available.
- Implementation pixels: 1080 x 2400 at Android density 3, corresponding to a 360 x 800 dp app viewport.
- Density normalization: the implementation was reviewed at its native 3x emulator density. Exact pixel normalization was not used because the source is a partial crop and the new page intentionally contains different, longer copy. Comparison was limited to the shared card, control, type, spacing, and color system.
- State: the source shows the existing station guide near its controls. The implementation was captured at the top and bottom of the history guide, on both Access Pass and Priority Pass variants of the pyramid-safety guide, and on the new King Khafre station-4 transfer guide.

## Findings

- No actionable P0, P1, or P2 differences were found.
- Fonts and typography: the history body uses the existing light sans-serif guide style, and the action labels preserve the existing medium-weight, letter-spaced treatment. The new serif title follows the app's existing station hierarchy.
- Spacing and layout rhythm: the rounded guide card, centered controls, button sizes, and vertical spacing follow the source screen. The long guide scrolls naturally, and all bottom controls remain fully reachable.
- Colors and visual tokens: the dark green background, translucent green card, teal border, pale body text, gold replay outline, and gold primary buttons match the established Menkaure screen palette.
- Image quality and asset fidelity: the reference contains no photographic or illustrative assets. The implementation reuses the app's existing drawable resources without placeholders or approximations.
- Copy and content: the Menkaure history and transfer texts are present in two-paragraph cards. Access Pass shows the Hop-On bus instructions; Priority Pass shows the Golf Cart instructions. The King Khafre transfer copy is present in full. Replay Guide, Stop AI Speech, Next, and ASK ME retain the requested labels, and both travel guides include a clearly labeled press-and-hold microphone control.
- Map consistency: every guide containing the audited travel phrases now includes the established 240 dp in-window Giza Plateau map and the existing VIEW FULL SCREEN control. Access Pass retains bus labels; Priority Pass suppresses them.

## Full-View Comparison Evidence

The history captures verify its hierarchy, guide copy, and controls. The Access Pass and Priority Pass departure captures verify that both transport variants, the microphone row, and the existing guide controls fit in the same visual system without clipping. The Khafre capture verifies the station-4 message, embedded map, narration controls, microphone, Next, and ASK ME in the same view.

## Focused Region Comparison Evidence

The bottom implementation capture was compared directly with the source crop because both clearly show the card edge, speech control, Next button, and ASK ME button. No separate crop was needed; these controls are large and legible in the full images.

## Interaction Verification

- The existing Menkaure Station Next button opened `MenkaureHistoryActivity`, whose final Next button opened `MenkaureDepartureActivity`.
- Automatic guide narration was invoked when the history page opened.
- Stop AI Speech changed to its checked state and stopped narration.
- Replay Guide was exercised in muted and unmuted states without a crash.
- ASK ME opened the chat. The history-screen-specific next action now identifies the pyramid-safety and station-4 transfer guide as the next page.
- On the departure guide, ASK ME returned the page-specific pyramid safety and station 4 instructions.
- The Priority Pass departure variant rendered the complete Golf Cart wording without clipping; Access Pass retains the original Hop-On bus wording.
- Holding and releasing the microphone started and completed a recognition session; silence produced the expected retry state. On each page, recognized `next` results and the on-screen Next button call the same navigation method.
- The Menkaure departure guide's Next button opens `KhafreTransferActivity` for both Access Pass and Priority Pass wording.
- On the Khafre guide, the Next button and the VIEW FULL SCREEN map control were both exercised and opened `TripProgressActivity`.
- ASK ME opened on the Khafre guide and answered "where am I" with the screen-specific King Khafre Station 4 location.
- The in-window map is present on `AccessPassTransferActivity`, `MenkaureDepartureActivity`, and `KhafreTransferActivity`, covering every current guide with the requested travel phrases.
- Android logcat was checked after the interaction run; no fatal runtime error was present.

## Comparison History

- Pass 1: no P0/P1/P2 visual issues were found, so no visual fixes or repeat capture were required.

## Follow-up Polish

- P3: the source crop omits Android system and action-bar chrome while the emulator capture includes it. This is runtime-owned framing and is not a page-level mismatch.

final result: passed
