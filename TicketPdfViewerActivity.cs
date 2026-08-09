using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/ticket_pdf_viewer_title")]
public sealed class TicketPdfViewerActivity : Activity
{
    public const string TicketPdfPathExtra = "AndroidApp1.TicketPdfPath";

    private PdfRenderer? _renderer;
    private ParcelFileDescriptor? _fileDescriptor;
    private Bitmap? _pageBitmap;
    private ImageView? _pageImage;
    private TextView? _pageLabel;
    private TextView? _previousButton;
    private TextView? _nextButton;
    private LinearLayout? _pageNavigation;
    private int _pageIndex;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_ticket_pdf_viewer);

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => Finish();
        _pageImage = FindViewById<ImageView>(Resource.Id.ticket_pdf_page_image);
        _pageLabel = FindViewById<TextView>(Resource.Id.ticket_pdf_page_label);
        _previousButton = FindViewById<TextView>(Resource.Id.ticket_pdf_previous_button);
        _nextButton = FindViewById<TextView>(Resource.Id.ticket_pdf_next_button);
        _pageNavigation = FindViewById<LinearLayout>(Resource.Id.ticket_pdf_navigation);
        _previousButton!.Click += (_, _) => RenderPage(_pageIndex - 1);
        _nextButton!.Click += (_, _) => RenderPage(_pageIndex + 1);

        var ticketPdfPath = Intent?.GetStringExtra(TicketPdfPathExtra);
        if (string.IsNullOrWhiteSpace(ticketPdfPath) || !File.Exists(ticketPdfPath))
        {
            ShowUnavailableAndFinish();
            return;
        }

        try
        {
            var fileDescriptor = ParcelFileDescriptor.Open(
                new Java.IO.File(ticketPdfPath),
                ParcelFileMode.ReadOnly)
                ?? throw new InvalidOperationException("Android could not open the saved ticket PDF.");
            _fileDescriptor = fileDescriptor;
            _renderer = new PdfRenderer(fileDescriptor);
            if (_renderer.PageCount == 0)
            {
                ShowUnavailableAndFinish();
                return;
            }

            _pageNavigation!.Visibility = _renderer.PageCount > 1
                ? Android.Views.ViewStates.Visible
                : Android.Views.ViewStates.Gone;
            RenderPage(0);
        }
        catch (Exception)
        {
            ShowUnavailableAndFinish();
        }
    }

    protected override void OnDestroy()
    {
        _pageImage?.SetImageBitmap(null);
        _pageBitmap?.Recycle();
        _pageBitmap?.Dispose();
        _renderer?.Dispose();
        _fileDescriptor?.Dispose();
        base.OnDestroy();
    }

    private void RenderPage(int pageIndex)
    {
        if (_renderer is null || _pageImage is null || pageIndex < 0 || pageIndex >= _renderer.PageCount)
        {
            return;
        }

        using var page = _renderer.OpenPage(pageIndex);
        var bitmap = Bitmap.CreateBitmap(
            Math.Max(1, page.Width * 2),
            Math.Max(1, page.Height * 2),
            Bitmap.Config.Argb8888!);
        bitmap.EraseColor(Color.White);
        page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

        _pageImage.SetImageBitmap(bitmap);
        _pageBitmap?.Recycle();
        _pageBitmap?.Dispose();
        _pageBitmap = bitmap;
        _pageIndex = pageIndex;

        if (_pageLabel is not null)
        {
            _pageLabel.Text = GetString(Resource.String.ticket_pdf_page, pageIndex + 1, _renderer.PageCount);
        }
        if (_previousButton is not null)
        {
            _previousButton.Enabled = pageIndex > 0;
            _previousButton.Alpha = pageIndex > 0 ? 1f : 0.45f;
        }

        if (_nextButton is not null)
        {
            _nextButton.Enabled = pageIndex < _renderer.PageCount - 1;
            _nextButton.Alpha = pageIndex < _renderer.PageCount - 1 ? 1f : 0.45f;
        }
    }

    private void ShowUnavailableAndFinish()
    {
        Toast.MakeText(this, Resource.String.ticket_pdf_unavailable, ToastLength.Long)?.Show();
        Finish();
    }
}
