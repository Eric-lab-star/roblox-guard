using System.Drawing.Drawing2D;

namespace RobloxGuard.Tray;

/// <summary>자물쇠 모양 트레이 아이콘을 GDI 로 그린다. 차단=빨강(잠김), 허용=초록(열림), 끊김=회색.</summary>
internal static class IconFactory
{
    public static Icon Locked() => Build(Color.FromArgb(200, 60, 60), shackleOpen: false);
    public static Icon Unlocked() => Build(Color.FromArgb(60, 170, 90), shackleOpen: true);
    public static Icon Disconnected() => Build(Color.FromArgb(130, 130, 130), shackleOpen: false);

    private static Icon Build(Color body, bool shackleOpen)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var shacklePen = new Pen(body, 4f);
            if (shackleOpen)
                g.DrawArc(shacklePen, 7, 4, 14, 16, 120, 300);
            else
                g.DrawArc(shacklePen, 9, 4, 14, 16, 180, 180);

            using var bodyBrush = new SolidBrush(body);
            g.FillRectangle(bodyBrush, 7, 14, 18, 14);

            using var hole = new SolidBrush(Color.White);
            g.FillEllipse(hole, 14, 18, 4, 4);
        }

        var hIcon = bmp.GetHicon();
        using var tmp = Icon.FromHandle(hIcon);
        return (Icon)tmp.Clone();
    }
}
