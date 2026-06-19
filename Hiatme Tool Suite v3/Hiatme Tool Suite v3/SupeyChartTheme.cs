using System.Drawing;
using System.Windows.Forms.DataVisualization.Charting;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Themes a <see cref="Chart"/> (System.Windows.Forms.DataVisualization) to the active Supey
    /// palette. The default Chart paints a fixed medium-gray surface and plot area that ignore
    /// MaterialSkin and our palette entirely, which is the big gray box that showed up under the
    /// trip lists. This sets every relevant color — outer background, plot area, gradients, borders,
    /// every axis's line/grid/tick/labels, the series, legends, and titles — so nothing renders gray.
    /// </summary>
    internal static class SupeyChartTheme
    {
        public static void Apply(Chart chart)
        {
            if (chart == null || chart.IsDisposed) return;

            chart.BackColor = SupeyTheme.SurfaceBase;
            chart.BackSecondaryColor = SupeyTheme.SurfaceBase;
            chart.BackGradientStyle = GradientStyle.None;
            chart.BorderlineColor = SupeyTheme.Divider;
            chart.BorderlineWidth = 0;
            chart.ForeColor = SupeyTheme.TextSecondary;

            foreach (ChartArea area in chart.ChartAreas)
            {
                area.BackColor = SupeyTheme.SurfaceBase;
                area.BackSecondaryColor = SupeyTheme.SurfaceBase;
                area.BackGradientStyle = GradientStyle.None;
                area.BorderColor = SupeyTheme.Divider;
                area.ShadowColor = Color.Transparent;

                foreach (Axis axis in area.Axes)
                {
                    axis.LineColor = SupeyTheme.Divider;
                    axis.InterlacedColor = SupeyTheme.Surface;
                    axis.TitleForeColor = SupeyTheme.TextSecondary;
                    axis.LabelStyle.ForeColor = SupeyTheme.TextSecondary;
                    axis.MajorGrid.LineColor = SupeyTheme.Divider;
                    axis.MinorGrid.LineColor = SupeyTheme.Divider;
                    axis.MajorTickMark.LineColor = SupeyTheme.Divider;
                    axis.MinorTickMark.LineColor = SupeyTheme.Divider;
                }
            }

            foreach (Series series in chart.Series)
            {
                // Only recolor series that are still on the chart's default blue/auto; keep any
                // series that was deliberately colored (semantic profit/loss reds & greens).
                if (series.Color == Color.Empty || series.Color.ToArgb() == Color.Black.ToArgb())
                    series.Color = SupeyTheme.AccentPrimary;
                series.LabelForeColor = SupeyTheme.TextPrimary;
            }

            foreach (Legend legend in chart.Legends)
            {
                legend.BackColor = Color.Transparent;
                legend.ForeColor = SupeyTheme.TextSecondary;
                legend.TitleForeColor = SupeyTheme.TextPrimary;
            }

            foreach (Title title in chart.Titles)
                title.ForeColor = SupeyTheme.TextPrimary;

            chart.Invalidate();
        }
    }
}
