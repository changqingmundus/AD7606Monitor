
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using System.ComponentModel;

namespace Ad7606Host
{
    internal sealed class LissajousPanel : Panel
    {
        private double[] xs = Array.Empty<double>();
        private double[] ys = Array.Empty<double>();

        private int n;

        // 通道直流中心
        private double offsetX;
        private double offsetY;

        // 通道峰峰值
        private double ppX;
        private double ppY;

        // 通道交流振幅
        private double ampX;
        private double ampY;

        // Lissajous 指标
        private double meanR;
        private double stdR;
        private double cvR;

        private bool signalXValid;
        private bool signalYValid;
        private bool isCircle;

        // 最小峰峰值，低于此值认为没有有效交流信号
        // 可以根据 AD7606 的噪声水平调整
        private double minimumPeakToPeak = 0.05;

        public LissajousPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
            MinimumSize = new Size(180, 180);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ChannelX { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ChannelY { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double MinimumPeakToPeak
        {
            get { return minimumPeakToPeak; }
            set
            {
                minimumPeakToPeak = Math.Max(0, value);
                AnalyzeData();
                Invalidate();
            }
        }

        public string Info
        {
            get
            {
                if (n < 10)
                    return "等待采集数据";

                if (!signalXValid && !signalYValid)
                    return "无有效交流信号";

                if (!signalXValid)
                    return "CH" + (ChannelX + 1) + " 无有效交流信号";

                if (!signalYValid)
                    return "CH" + (ChannelY + 1) + " 无有效交流信号";

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "CH{0} × CH{1} | N={2} | R={3:F4} V | CV={4:F2}% | {5}",
                    ChannelX + 1,
                    ChannelY + 1,
                    n,
                    meanR,
                    cvR * 100,
                    isCircle ? "近似圆形" : "非圆形"
                );
            }
        }

        public void SetData(double[] xData, double[] yData, int count)
        {
            if (xData == null || yData == null)
                return;

            count = Math.Min(count, Math.Min(xData.Length, yData.Length));

            if (count < 0)
                count = 0;

            if (count > xs.Length)
            {
                xs = new double[count];
                ys = new double[count];
            }

            n = 0;

            for (int i = 0; i < count; i++)
            {
                if (double.IsNaN(xData[i]) ||
                    double.IsInfinity(xData[i]) ||
                    double.IsNaN(yData[i]) ||
                    double.IsInfinity(yData[i]))
                {
                    continue;
                }

                xs[n] = xData[i];
                ys[n] = yData[i];
                n++;
            }

            AnalyzeData();
            Invalidate();
        }

        private void AnalyzeData()
        {
            signalXValid = false;
            signalYValid = false;
            isCircle = false;

            offsetX = offsetY = 0;
            ppX = ppY = 0;
            ampX = ampY = 0;

            meanR = stdR = cvR = 0;

            if (n < 10)
                return;

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            for (int i = 0; i < n; i++)
            {
                minX = Math.Min(minX, xs[i]);
                maxX = Math.Max(maxX, xs[i]);

                minY = Math.Min(minY, ys[i]);
                maxY = Math.Max(maxY, ys[i]);
            }

            // 峰峰值
            ppX = maxX - minX;
            ppY = maxY - minY;

            // 直流中心：最大值和最小值的中点
            offsetX = (maxX + minX) / 2.0;
            offsetY = (maxY + minY) / 2.0;

            // 交流振幅
            ampX = ppX / 2.0;
            ampY = ppY / 2.0;

            // 判断是否存在有效交流信号
            signalXValid = ppX >= minimumPeakToPeak;
            signalYValid = ppY >= minimumPeakToPeak;

            if (!signalXValid || !signalYValid)
                return;

            // 使用去除直流偏置后的数据计算半径
            double sumR = 0;
            double sumR2 = 0;

            for (int i = 0; i < n; i++)
            {
                double x = xs[i] - offsetX;
                double y = ys[i] - offsetY;

                double radius = Math.Sqrt(x * x + y * y);

                sumR += radius;
                sumR2 += radius * radius;
            }

            meanR = sumR / n;

            double variance = sumR2 / n - meanR * meanR;

            stdR = Math.Sqrt(Math.Max(0, variance));

            cvR = meanR > 1e-9
                ? stdR / meanR
                : 0;

            // CV 越小，轨迹越接近圆形
            isCircle = cvR < 0.05;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Rectangle client = ClientRectangle;

            if (client.Width < 100 || client.Height < 100)
                return;

            // 背景
            g.Clear(Color.White);

            const int left = 48;
            const int right = 15;
            const int top = 32;
            const int bottom = 42;

            Rectangle plot = new Rectangle(
                left,
                top,
                client.Width - left - right,
                client.Height - top - bottom
            );

            int size = Math.Min(plot.Width, plot.Height);

            if (size <= 0)
                return;

            // 强制正方形坐标区域
            Rectangle sq = new Rectangle(
                plot.Left + (plot.Width - size) / 2,
                plot.Top + (plot.Height - size) / 2,
                size,
                size
            );

            DrawHeader(g, client);
            DrawGrid(g, sq);

            if (n < 10)
            {
                DrawMessage(g, sq, "等待采集数据");
                return;
            }

            if (!signalXValid || !signalYValid)
            {
                string message = "无有效交流信号";

                if (!signalXValid && !signalYValid)
                    message = "CH" + (ChannelX + 1) + " / CH" +
                              (ChannelY + 1) + " 均无有效交流信号";
                else if (!signalXValid)
                    message = "CH" + (ChannelX + 1) + " 无有效交流信号";
                else
                    message = "CH" + (ChannelY + 1) + " 无有效交流信号";

                DrawMessage(g, sq, message);
                DrawChannelInfo(g, sq);
                return;
            }

            DrawLissajous(g, sq);
            DrawChannelInfo(g, sq);
            DrawFooter(g, sq);
        }

        private void DrawHeader(Graphics g, Rectangle client)
        {
            using (Font titleFont = new Font("Segoe UI", 10, FontStyle.Bold))
            using (Brush titleBrush = new SolidBrush(Color.FromArgb(45, 55, 70)))
            {
                string title = "Lissajous Pattern";

                g.DrawString(title, titleFont, titleBrush, 10, 8);
            }
        }

        private void DrawGrid(Graphics g, Rectangle sq)
        {
            using (Pen gridPen = new Pen(Color.FromArgb(235, 238, 243)))
            using (Pen axisPen = new Pen(Color.FromArgb(170, 180, 190), 1f))
            using (Pen borderPen = new Pen(Color.FromArgb(210, 216, 225), 1f))
            using (Font font = new Font("Segoe UI", 7.5f))
            using (Brush textBrush = new SolidBrush(Color.Gray))
            {
                g.FillRectangle(Brushes.WhiteSmoke, sq);
                g.DrawRectangle(borderPen, sq);

                // 网格线
                for (int i = 1; i < 4; i++)
                {
                    float x = sq.Left + sq.Width * i / 4f;
                    float y = sq.Top + sq.Height * i / 4f;

                    g.DrawLine(gridPen, x, sq.Top, x, sq.Bottom);
                    g.DrawLine(gridPen, sq.Left, y, sq.Right, y);
                }

                // 中心坐标轴
                float cx = sq.Left + sq.Width / 2f;
                float cy = sq.Top + sq.Height / 2f;

                g.DrawLine(axisPen, sq.Left, cy, sq.Right, cy);
                g.DrawLine(axisPen, cx, sq.Top, cx, sq.Bottom);

                g.DrawString("CH" + (ChannelX + 1),
                    font, textBrush, sq.Left, sq.Bottom + 5);

                g.DrawString("CH" + (ChannelY + 1),
                    font, textBrush, sq.Left - 35, sq.Top - 14);
            }
        }

        private void DrawLissajous(Graphics g, Rectangle sq)
        {
            // 去除直流偏置后的最大振幅
            double scale = Math.Max(ampX, ampY) * 1.15;

            if (scale < 1e-9)
                return;

            float cx = sq.Left + sq.Width / 2f;
            float cy = sq.Top + sq.Height / 2f;

            float half = Math.Min(sq.Width, sq.Height) / 2f;

            PointF[] points = new PointF[n];

            for (int i = 0; i < n; i++)
            {
                double x = xs[i] - offsetX;
                double y = ys[i] - offsetY;

                float px = cx + (float)(x / scale * half);
                float py = cy - (float)(y / scale * half);

                points[i] = new PointF(px, py);
            }

            // 参考圆
            if (meanR > 1e-9)
            {
                float radius = (float)(meanR / scale * half);

                using (Pen refPen = new Pen(
                    Color.FromArgb(100, 50, 160, 100), 1f))
                {
                    refPen.DashStyle = DashStyle.Dash;

                    g.DrawEllipse(
                        refPen,
                        cx - radius,
                        cy - radius,
                        radius * 2,
                        radius * 2
                    );
                }
            }

            // 轨迹
            using (Pen linePen = new Pen(
                Color.FromArgb(35, 115, 220), 1.5f))
            {
                linePen.LineJoin = LineJoin.Round;

                if (points.Length >= 2)
                    g.DrawLines(linePen, points);
            }

            // 起点标记
            PointF first = points[0];

            using (Brush startBrush = new SolidBrush(Color.SeaGreen))
            {
                g.FillEllipse(
                    startBrush,
                    first.X - 4,
                    first.Y - 4,
                    8,
                    8
                );
            }

            // 终点标记
            PointF last = points[points.Length - 1];

            using (Brush endBrush = new SolidBrush(Color.OrangeRed))
            {
                g.FillEllipse(
                    endBrush,
                    last.X - 4,
                    last.Y - 4,
                    8,
                    8
                );
            }
        }

        private void DrawChannelInfo(Graphics g, Rectangle sq)
        {
            using (Font font = new Font("Consolas", 8f))
            using (Brush brush = new SolidBrush(Color.DimGray))
            {
                string info = string.Format(
                    CultureInfo.InvariantCulture,
                    "CH{0}: Center={1:F3}V  Vpp={2:F3}V",
                    ChannelX + 1,
                    offsetX,
                    ppX
                );

                string info2 = string.Format(
                    CultureInfo.InvariantCulture,
                    "CH{0}: Center={1:F3}V  Vpp={2:F3}V",
                    ChannelY + 1,
                    offsetY,
                    ppY
                );

                g.DrawString(info, font, brush, sq.Left, sq.Top - 27);
                g.DrawString(info2, font, brush, sq.Left, sq.Bottom + 19);
            }
        }

        private void DrawFooter(Graphics g, Rectangle sq)
        {
            using (Font font = new Font("Segoe UI", 8f))
            using (Brush brush = new SolidBrush(Color.DimGray))
            {
                string text = string.Format(
                    CultureInfo.InvariantCulture,
                    "N={0}  Mean R={1:F4}V  CV={2:F2}%  {3}",
                    n,
                    meanR,
                    cvR * 100,
                    isCircle ? "近似圆形" : "非圆形"
                );

                SizeF size = g.MeasureString(text, font);

                g.DrawString(
                    text,
                    font,
                    brush,
                    sq.Right - size.Width,
                    sq.Bottom + 19
                );
            }
        }

        private void DrawMessage(Graphics g, Rectangle sq, string message)
        {
            using (Font font = new Font("Segoe UI", 10f, FontStyle.Bold))
            using (Brush brush = new SolidBrush(Color.FromArgb(140, 150, 160)))
            {
                SizeF size = g.MeasureString(message, font);

                g.DrawString(
                    message,
                    font,
                    brush,
                    sq.Left + (sq.Width - size.Width) / 2f,
                    sq.Top + (sq.Height - size.Height) / 2f
                );
            }
        }
    }
}
