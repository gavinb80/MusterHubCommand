using Microsoft.Maui.Graphics;
using MusterHubCommandTablet.ViewModels;

namespace MusterHubCommandTablet.Views;

// A fresh instance is built on every layout rebuild rather than mutating
// one in place -- GraphicsView.Drawable is bindable, and reassigning it
// is what triggers MAUI to invalidate and redraw, so there's no need for
// any manual Invalidate() plumbing from code-behind.
public class HierarchyEdgesDrawable(List<Edge> edges, Color strokeColor) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.StrokeColor = strokeColor;
        canvas.StrokeSize = 2;
        foreach (var edge in edges)
        {
            var path = new PathF();
            path.MoveTo((float)edge.X1, (float)edge.Y1);
            path.LineTo((float)edge.X1, (float)edge.MidY);
            path.LineTo((float)edge.X2, (float)edge.MidY);
            path.LineTo((float)edge.X2, (float)edge.Y2);
            canvas.DrawPath(path);
        }
    }
}
