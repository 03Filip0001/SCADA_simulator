using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Contracts;
using DataConcentrator.Model;
using DataConcentrator.Persistence;

namespace ScadaGUI
{
    public partial class HistoryWindow : Window
    {
        private const int YAxisTickCount = 5;
        private const int XAxisTickCount = 5;

        private const double LeftMargin = 55;
        private const double RightMargin = 45;
        private const double TopMargin = 22;
        private const double BottomMargin = 28;

        private readonly AnalogInput analogTag;
        private readonly List<AnalogInputHistoryRecord> historyRecords = new List<AnalogInputHistoryRecord>();

        private bool chartElementsBuilt;
        private Polyline dataPolyline;
        private Line highLimitLine;
        private TextBlock highLimitLabel;
        private Line lowLimitLine;
        private TextBlock lowLimitLabel;
        private TextBlock yAxisTitle;
        private TextBlock xAxisTitle;
        private Line[] yGridLines;
        private TextBlock[] yGridLabels;
        private Line[] xGridLines;
        private TextBlock[] xGridLabels;

        public HistoryWindow(ITag selectedTag)
        {
            InitializeComponent();
            TitleText.Text = selectedTag != null
                ? $"History for {selectedTag.Name} ({selectedTag.Type})"
                : "Tag History";

            analogTag = selectedTag as AnalogInput;
            LoadHistory();
        }

        private void LoadHistory()
        {
            if (analogTag == null)
            {
                ShowMessage("History graph is only available for Analog Input tags.");
                return;
            }

            var loadedRecords = PersistenceService.GetHistory(analogTag.Name, out string errorMessage);
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                MessageBox.Show($"Unable to load history: {errorMessage}", "History", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            historyRecords.AddRange(loadedRecords);

            EnsureChartElementsBuilt();

            analogTag.HistoryRecorded += OnHistoryRecorded;
            Closed += HistoryWindow_Closed;

            if (!historyRecords.Any())
            {
                ShowMessage("No historical data available for this tag yet. The graph updates automatically as new values are recorded.");
            }
            else
            {
                NoDataText.Visibility = Visibility.Collapsed;
            }

            UpdateStatsText();
            UpdateChart();
        }

        private void HistoryWindow_Closed(object sender, EventArgs e)
        {
            if (analogTag != null)
            {
                analogTag.HistoryRecorded -= OnHistoryRecorded;
            }
        }

        private void OnHistoryRecorded(AnalogInput source, AnalogInputHistoryRecord record)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                historyRecords.Add(record);
                NoDataText.Visibility = Visibility.Collapsed;
                UpdateStatsText();
                UpdateChart();
            }));
        }

        private void UpdateStatsText()
        {
            if (!historyRecords.Any())
            {
                MinText.Text = MaxText.Text = AverageText.Text = "n/a";
                return;
            }

            MinText.Text = historyRecords.Min(record => record.Value).ToString("F2");
            MaxText.Text = historyRecords.Max(record => record.Value).ToString("F2");
            AverageText.Text = historyRecords.Average(record => record.Value).ToString("F2");
        }

        private void ShowMessage(string message)
        {
            NoDataText.Text = message;
            NoDataText.Visibility = Visibility.Visible;
            MinText.Text = MaxText.Text = AverageText.Text = "n/a";
        }

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateChart();
        }

        // Chart shapes are created once and reused; every update repositions the
        // existing shapes instead of clearing and rebuilding the canvas.
        private void EnsureChartElementsBuilt()
        {
            if (chartElementsBuilt || analogTag == null)
            {
                return;
            }

            yGridLines = new Line[YAxisTickCount];
            yGridLabels = new TextBlock[YAxisTickCount];
            for (int i = 0; i < YAxisTickCount; i++)
            {
                yGridLines[i] = new Line { Stroke = Brushes.Gray, StrokeThickness = 0.5, Opacity = 0.4 };
                yGridLabels[i] = new TextBlock { FontSize = 10, Foreground = Brushes.Gray };
                ChartCanvas.Children.Add(yGridLines[i]);
                ChartCanvas.Children.Add(yGridLabels[i]);
            }

            xGridLines = new Line[XAxisTickCount];
            xGridLabels = new TextBlock[XAxisTickCount];
            for (int i = 0; i < XAxisTickCount; i++)
            {
                xGridLines[i] = new Line { Stroke = Brushes.Gray, StrokeThickness = 0.5, Opacity = 0.4 };
                xGridLabels[i] = new TextBlock { FontSize = 10, Foreground = Brushes.Gray };
                ChartCanvas.Children.Add(xGridLines[i]);
                ChartCanvas.Children.Add(xGridLabels[i]);
            }

            dataPolyline = new Polyline { StrokeThickness = 2 };
            ChartCanvas.Children.Add(dataPolyline);

            highLimitLine = new Line
            {
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Stroke = Brushes.Red,
                Visibility = Visibility.Collapsed
            };
            highLimitLabel = new TextBlock { FontSize = 11, Foreground = Brushes.Red, Visibility = Visibility.Collapsed };
            lowLimitLine = new Line
            {
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Stroke = Brushes.Orange,
                Visibility = Visibility.Collapsed
            };
            lowLimitLabel = new TextBlock { FontSize = 11, Foreground = Brushes.Orange, Visibility = Visibility.Collapsed };
            ChartCanvas.Children.Add(highLimitLine);
            ChartCanvas.Children.Add(highLimitLabel);
            ChartCanvas.Children.Add(lowLimitLine);
            ChartCanvas.Children.Add(lowLimitLabel);

            var unitsSuffix = string.IsNullOrWhiteSpace(analogTag.Units) ? string.Empty : $" ({analogTag.Units})";
            yAxisTitle = new TextBlock { Text = $"Value{unitsSuffix}", FontSize = 10, FontWeight = FontWeights.SemiBold };
            xAxisTitle = new TextBlock { Text = "Time", FontSize = 10, FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(yAxisTitle, 2);
            Canvas.SetTop(yAxisTitle, 2);
            ChartCanvas.Children.Add(yAxisTitle);
            ChartCanvas.Children.Add(xAxisTitle);

            chartElementsBuilt = true;
        }

        private void UpdateChart()
        {
            if (!chartElementsBuilt || !historyRecords.Any())
            {
                return;
            }

            double width = ChartCanvas.ActualWidth;
            double height = ChartCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            double valueMin = historyRecords.Min(record => record.Value);
            double valueMax = historyRecords.Max(record => record.Value);

            if (analogTag.AlarmEnabled)
            {
                valueMin = Math.Min(valueMin, analogTag.LowLimit);
                valueMax = Math.Max(valueMax, analogTag.HighLimit);
            }

            if (Math.Abs(valueMax - valueMin) < double.Epsilon)
            {
                valueMax += 1;
                valueMin -= 1;
            }

            double range = valueMax - valueMin;
            double padding = range * 0.1;
            valueMin -= padding;
            valueMax += padding;
            range = valueMax - valueMin;

            double plotLeft = LeftMargin;
            double plotWidth = Math.Max(width - LeftMargin - RightMargin, 1);
            double plotTop = TopMargin;
            double plotHeight = Math.Max(height - TopMargin - BottomMargin, 1);

            DateTime timeMin = historyRecords.First().Timestamp;
            DateTime timeMax = historyRecords.Last().Timestamp;
            double timeSpan = (timeMax - timeMin).TotalSeconds;
            if (timeSpan <= 0)
            {
                timeSpan = 1;
            }

            double YFor(double value) => plotTop + (1 - (value - valueMin) / range) * plotHeight;
            double XFor(DateTime timestamp) => plotLeft + (timestamp - timeMin).TotalSeconds / timeSpan * plotWidth;

            var themeResources = Application.Current.Resources;
            var gridLineBrush = themeResources["GridLineBrush"] as Brush ?? Brushes.Gray;
            var axisTextBrush = themeResources["SecondaryForegroundBrush"] as Brush ?? Brushes.Gray;

            dataPolyline.Stroke = themeResources["ChartLineBrush"] as Brush ?? Brushes.SteelBlue;
            dataPolyline.Points.Clear();
            foreach (var record in historyRecords)
            {
                dataPolyline.Points.Add(new Point(XFor(record.Timestamp), YFor(record.Value)));
            }

            for (int i = 0; i < YAxisTickCount; i++)
            {
                double fraction = (double)i / (YAxisTickCount - 1);
                double value = valueMax - fraction * range;
                double y = YFor(value);

                yGridLines[i].Stroke = gridLineBrush;
                yGridLines[i].X1 = plotLeft;
                yGridLines[i].X2 = plotLeft + plotWidth;
                yGridLines[i].Y1 = y;
                yGridLines[i].Y2 = y;

                yGridLabels[i].Foreground = axisTextBrush;
                yGridLabels[i].Text = value.ToString("F1");
                Canvas.SetLeft(yGridLabels[i], 2);
                Canvas.SetTop(yGridLabels[i], y - 7);
            }

            for (int i = 0; i < XAxisTickCount; i++)
            {
                double fraction = (double)i / (XAxisTickCount - 1);
                DateTime timestamp = timeMin.AddSeconds(fraction * timeSpan);
                double x = XFor(timestamp);

                xGridLines[i].Stroke = gridLineBrush;
                xGridLines[i].Y1 = plotTop;
                xGridLines[i].Y2 = plotTop + plotHeight;
                xGridLines[i].X1 = x;
                xGridLines[i].X2 = x;

                xGridLabels[i].Foreground = axisTextBrush;
                xGridLabels[i].Text = timestamp.ToLocalTime().ToString("HH:mm:ss");
                Canvas.SetLeft(xGridLabels[i], Math.Max(0, Math.Min(x - 20, plotLeft + plotWidth - 40)));
                Canvas.SetTop(xGridLabels[i], plotTop + plotHeight + 4);
            }

            Canvas.SetLeft(xAxisTitle, plotLeft + plotWidth + 4);
            Canvas.SetTop(xAxisTitle, plotTop + plotHeight - 6);

            if (analogTag.AlarmEnabled)
            {
                PositionThresholdLine(highLimitLine, highLimitLabel, analogTag.HighLimit, plotLeft, plotWidth, YFor, "High limit");
                PositionThresholdLine(lowLimitLine, lowLimitLabel, analogTag.LowLimit, plotLeft, plotWidth, YFor, "Low limit");
            }
            else
            {
                highLimitLine.Visibility = Visibility.Collapsed;
                highLimitLabel.Visibility = Visibility.Collapsed;
                lowLimitLine.Visibility = Visibility.Collapsed;
                lowLimitLabel.Visibility = Visibility.Collapsed;
            }
        }

        private static void PositionThresholdLine(Line line, TextBlock label, double value, double plotLeft, double plotWidth, Func<double, double> yFor, string text)
        {
            double y = yFor(value);

            line.X1 = plotLeft;
            line.X2 = plotLeft + plotWidth;
            line.Y1 = y;
            line.Y2 = y;
            line.Visibility = Visibility.Visible;

            label.Text = $"{text}: {value:F2}";
            Canvas.SetLeft(label, plotLeft + 4);
            Canvas.SetTop(label, y - 14);
            label.Visibility = Visibility.Visible;
        }

        private void Button_Close(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
