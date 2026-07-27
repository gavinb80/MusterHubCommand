#if ANDROID
using Android.Graphics;
#elif IOS || MACCATALYST
using CoreGraphics;
using UIKit;
#endif

namespace MusterHubCommandTablet.Services;

// Renders a native platform view straight to a PNG rather than going
// through Microsoft.Maui.Media.Screenshot, which captures the whole
// window/page and would need cropping to the target view's on-screen
// bounds. This works generically on any VisualElement's native handler --
// used both for the map WebView (the base captured image) and, later, the
// Grid stacking that image with the drawing overlay (the final annotated
// composite) -- one capture path for both steps.
public static class NativeViewCapture
{
    public static Task<byte[]> CaptureAsync(VisualElement element)
    {
        var platformView = element.Handler?.PlatformView
            ?? throw new InvalidOperationException($"{element.GetType().Name} has no platform view to capture -- it hasn't been laid out yet.");

#if ANDROID
        var view = (Android.Views.View)platformView;
        using var bitmap = Bitmap.CreateBitmap(view.Width, view.Height, Bitmap.Config.Argb8888!);
        using var canvas = new Canvas(bitmap);
        view.Draw(canvas);
        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        return Task.FromResult(stream.ToArray());
#elif IOS || MACCATALYST
        var view = (UIView)platformView;
        var renderer = new UIGraphicsImageRenderer(view.Bounds.Size);
        var image = renderer.CreateImage(context => view.DrawViewHierarchy(view.Bounds, afterScreenUpdates: true));
        using var data = image.AsPNG() ?? throw new InvalidOperationException("Couldn't encode the captured view as PNG.");
        var bytes = new byte[data.Length];
        System.Runtime.InteropServices.Marshal.Copy(data.Bytes, bytes, 0, (int)data.Length);
        return Task.FromResult(bytes);
#else
        throw new PlatformNotSupportedException("View capture isn't implemented on this platform.");
#endif
    }
}
