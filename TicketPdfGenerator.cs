namespace AndroidApp1
{
    internal sealed class ETicket
    {
        public ETicket(string ticketNumber, string type, string accessGate, string price, DateTime bookingDate)
        {
            TicketNumber = ticketNumber;
            Type = type;
            AccessGate = accessGate;
            Price = price;
            BookingDate = bookingDate;
        }

        public string TicketNumber { get; }
        public string Type { get; }
        public string AccessGate { get; }
        public string Price { get; }
        public DateTime BookingDate { get; }
    }

    internal static class TicketFactory
    {
        public static IReadOnlyList<ETicket> Create(IEnumerable<CartItem> cartItems)
        {
            var tickets = new List<ETicket>();
            var bookingDate = DateTime.Now;

            foreach (var cartItem in cartItems)
            {
                for (var index = 0; index < cartItem.Quantity; index++)
                {
                    tickets.Add(new ETicket(
                        $"GZ-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                        cartItem.Type,
                        cartItem.AccessGate,
                        ExtractPrice(cartItem.Type),
                        bookingDate));
                }
            }

            return tickets;
        }

        private static string ExtractPrice(string type)
        {
            var openingBracket = type.LastIndexOf('(');
            var closingBracket = type.LastIndexOf(')');
            return openingBracket >= 0 && closingBracket > openingBracket
                ? type[(openingBracket + 1)..closingBracket]
                : "Included";
        }
    }

    internal static class TicketPdfGenerator
    {
        private const string TicketPdfSearchPattern = "giza-e-tickets-*.pdf";

        public static string Generate(Activity activity, IReadOnlyList<ETicket> tickets)
        {
            var documentsDirectory = activity.GetExternalFilesDir(Android.OS.Environment.DirectoryDocuments)
                ?? activity.FilesDir!;
            var outputFile = new Java.IO.File(
                documentsDirectory,
                $"giza-e-tickets-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");

            using var document = new Android.Graphics.Pdf.PdfDocument();
            var pageNumber = 1;
            foreach (var ticket in tickets)
            {
                var pageInfo = new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(595, 842, pageNumber++).Create();
                using var page = document.StartPage(pageInfo)
                    ?? throw new InvalidOperationException("Unable to create the ticket PDF page.");
                DrawTicket(page.Canvas!, ticket);
                document.FinishPage(page);
            }

            using var stream = new FileStream(outputFile.AbsolutePath!, FileMode.Create, FileAccess.Write);
            document.WriteTo(stream);
            return outputFile.AbsolutePath ?? outputFile.Path ?? string.Empty;
        }

        /// <summary>
        /// Finds the most recently saved ticket document in this app's Documents directory.
        /// The document is recreated only by the explicit download action, never here.
        /// </summary>
        public static string? GetLatestSavedTicketPdfPath(Activity activity)
        {
            var documentsDirectory = activity.GetExternalFilesDir(Android.OS.Environment.DirectoryDocuments)
                ?? activity.FilesDir;
            var directoryPath = documentsDirectory?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return null;
            }

            return Directory.EnumerateFiles(directoryPath, TicketPdfSearchPattern, SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => file.Length > 0)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }

        private static void DrawTicket(Android.Graphics.Canvas canvas, ETicket ticket)
        {
            canvas.DrawColor(Android.Graphics.Color.Rgb(7, 19, 17));

            using var titlePaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = Android.Graphics.Color.Rgb(217, 181, 106),
                TextSize = 28f,
            };
            using var bodyPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = Android.Graphics.Color.Rgb(217, 233, 222),
                TextSize = 15f,
            };
            using var accentPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = Android.Graphics.Color.Rgb(217, 181, 106),
                TextSize = 16f,
            };

            titlePaint.SetTypeface(Android.Graphics.Typeface.Create("serif", Android.Graphics.TypefaceStyle.Normal));
            bodyPaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Normal));
            accentPaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif-medium", Android.Graphics.TypefaceStyle.Normal));

            canvas.DrawText("AI TOURIST GUIDE | GIZA PLATEAU", 42, 58, accentPaint);
            canvas.DrawText("E-TICKET", 42, 110, titlePaint);
            canvas.DrawText($"Ticket No. {ticket.TicketNumber}", 42, 140, bodyPaint);

            using var qrCode = QrCodeGenerator.Create(ticket.TicketNumber, 150);
            canvas.DrawBitmap(qrCode, 395, 50, null);

            var englishLines = new[]
            {
                "One time use for one day",
                $"Booking date: {ticket.BookingDate:dd MMM yyyy}",
                $"Access pass type: {ticket.Type}",
                $"Access gate: {ticket.AccessGate}",
                $"Price: {ticket.Price}"
            };
            DrawLines(canvas, englishLines, 42, 210, 25, bodyPaint);

            canvas.DrawLine(42, 370, 553, 370, accentPaint);
            canvas.DrawText("معلومات التذكرة", 42, 405, titlePaint);

            var arabicLines = new[]
            {
                "استخدام لمرة واحدة ليوم واحد",
                $"تاريخ الحجز: {ticket.BookingDate:dd MMM yyyy}",
                $"نوع التذكرة: {ticket.Type}",
                $"بوابة الدخول: {ticket.AccessGate}",
                $"السعر: {ticket.Price}"
            };
            DrawLines(canvas, arabicLines, 42, 440, 25, bodyPaint);
            DrawCorrectArabicTicketSection(canvas, ticket);
        }

        private static void DrawCorrectArabicTicketSection(Android.Graphics.Canvas canvas, ETicket ticket)
        {
            using var coverPaint = new Android.Graphics.Paint
            {
                Color = Android.Graphics.Color.Rgb(7, 19, 17)
            };
            using var titlePaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = Android.Graphics.Color.Rgb(217, 181, 106),
                TextSize = 28f,
                TextAlign = Android.Graphics.Paint.Align.Right
            };
            using var bodyPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = Android.Graphics.Color.Rgb(217, 233, 222),
                TextSize = 15f,
                TextAlign = Android.Graphics.Paint.Align.Right
            };
            titlePaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Normal));
            bodyPaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Normal));

            canvas.DrawRect(0, 372, 595, 600, coverPaint);
            canvas.DrawText("\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u062A\u0630\u0643\u0631\u0629", 553, 405, titlePaint);
            var arabicLines = new[]
            {
                "\u0627\u0633\u062A\u062E\u062F\u0627\u0645 \u0644\u0645\u0631\u0629 \u0648\u0627\u062D\u062F\u0629 \u0644\u064A\u0648\u0645 \u0648\u0627\u062D\u062F",
                $"\u062A\u0627\u0631\u064A\u062E \u0627\u0644\u062D\u062C\u0632: {ticket.BookingDate:dd MMM yyyy}",
                $"\u0646\u0648\u0639 \u0627\u0644\u062A\u0630\u0643\u0631\u0629: {ticket.Type}",
                $"\u0628\u0648\u0627\u0628\u0629 \u0627\u0644\u062F\u062E\u0648\u0644: {ticket.AccessGate}",
                $"\u0627\u0644\u0633\u0639\u0631: {ticket.Price}"
            };
            DrawLines(canvas, arabicLines, 553, 440, 25, bodyPaint);
        }

        private static void DrawLines(
            Android.Graphics.Canvas canvas,
            IEnumerable<string> lines,
            float left,
            float top,
            float lineHeight,
            Android.Graphics.Paint paint)
        {
            var y = top;
            foreach (var line in lines)
            {
                foreach (var wrappedLine in WrapLine(line, paint, 500f))
                {
                    canvas.DrawText(wrappedLine, left, y, paint);
                    y += lineHeight;
                }
            }
        }

        private static IEnumerable<string> WrapLine(string text, Android.Graphics.Paint paint, float maxWidth)
        {
            var words = text.Split(' ');
            var line = string.Empty;
            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
                if (paint.MeasureText(candidate) <= maxWidth || string.IsNullOrEmpty(line))
                {
                    line = candidate;
                }
                else
                {
                    yield return line;
                    line = word;
                }
            }

            if (!string.IsNullOrEmpty(line))
            {
                yield return line;
            }
        }
    }
}
