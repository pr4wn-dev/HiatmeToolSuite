using System.Drawing;
using System.Drawing.Imaging;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Recolors monochrome UI icons to match the active <see cref="SupeyTheme"/> palette.</summary>
    internal static class SupeyIconTint
    {
        public static Bitmap Tint(Image src, Size size, Color tint)
        {
            if (src == null || size.Width <= 0 || size.Height <= 0) return null;

            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            var cm = new ColorMatrix(new[]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { tint.R / 255f, tint.G / 255f, tint.B / 255f, 0, 1 }
            });
            using (var g = Graphics.FromImage(bmp))
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(src, new Rectangle(0, 0, size.Width, size.Height),
                    0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return bmp;
        }
    }
}
