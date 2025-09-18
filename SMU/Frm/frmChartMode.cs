using Helper;
using ScottPlot;
using ScottPlot.WinForms;
using ScottPlot.TickGenerators;
using SMU.Consts;
using SMU.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SMU.Frm
{
    /// <summary>
    /// 图表模式展示内容
    /// </summary>
    public partial class frmChartMode : Form
    {
        private const double DefaultSamplesPerSecondEstimate = 50_000;

        int pictureTitle1 = 1;
        int pictureTitle2 = 1;
        BindingList<TableAnalysis> tableList;
        // Y轴
        double maxVolValue = 0;
        double minVolValue = 0;
        double maxCurValue = 0;
        double minCurValue = 0;

        // X轴
        double minXValue = 0;
        double maxXvalue = 0;

        // 绘图控件定义
        Plot volPlot;
        Plot curPlot;

        ScottPlot.TickGenerators.NumericFixedInterval tickGenVolY;
        ScottPlot.TickGenerators.NumericFixedInterval tickGenCurY;

        // 定义DataLogger曲线集合
        readonly List<ScottPlot.Plottables.SignalXY> volSignals = new();
        readonly List<ScottPlot.Plottables.SignalXY> curSignals = new();
        // 定义数据字典
        readonly Dictionary<int, List<double>> dicVolX = new();
        readonly Dictionary<int, List<double>> dicVolY = new();
        readonly Dictionary<int, List<double>> dicCurX = new();
        readonly Dictionary<int, List<double>> dicCurY = new();
        // 定义图例集合
        readonly List<LegendItem> legendItems = new();
        // 定义X轴显示时间范围（ms）
        double xShowRange = 30000;

        private readonly Dictionary<int, double> _channelMinVoltage = new();
        private readonly Dictionary<int, double> _channelMaxVoltage = new();
        private readonly Dictionary<int, double> _channelMinCurrent = new();
        private readonly Dictionary<int, double> _channelMaxCurrent = new();

        private readonly Stopwatch _refreshStopwatch = Stopwatch.StartNew();
        private readonly TimeSpan _refreshInterval = TimeSpan.FromMilliseconds(100);
        private bool _isVoltageAxisAuto = true;
        private bool _isCurrentAxisAuto = true;
        private double _samplesPerSecondEstimate = DefaultSamplesPerSecondEstimate;
        private int _maxSamplesPerChannel;

        /// <summary>
        /// 构造函数
        /// </summary>
        public frmChartMode()
        {
            InitializeComponent();
            _maxSamplesPerChannel = CalculateMaxSamples(xShowRange, _samplesPerSecondEstimate);
            // 加载ScottPlot控件样式
            LoadFormPlotStyle();
            // 初始化ScottPlot绑定数据
            InitFormPlotData();
        }

        /// <summary>
        /// 估算的每秒采样点数，用于限制缓冲区大小。
        /// </summary>
        public double SamplesPerSecondEstimate
        {
            get => _samplesPerSecondEstimate;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                _samplesPerSecondEstimate = value;
                _maxSamplesPerChannel = CalculateMaxSamples(xShowRange, _samplesPerSecondEstimate);
            }
        }

        /// <summary>
        /// 锁对象
        /// </summary>
        private readonly object LockObj = new object();

        /// <summary>
        /// 加载事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void frmChartMode_Load(object sender, EventArgs e)
        {
            // 表格样式加载
            dt_DataAnalysis.RowsDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(235, 240, 244);
            dt_DataAnalysis.AlternatingRowsDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(247, 247, 247);

            // 启用双缓存
            var prop1 = formsPlost_Voltage.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            prop1?.SetValue(formsPlost_Voltage, true, null);
            var prop2 = formsPlost_Current.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            prop2?.SetValue(formsPlost_Current, true, null);
        }

        /// <summary>
        /// chart展示内容 收起/展开 方法
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void pictureBox_CE_2_Click(object sender, EventArgs e)
        {
            if (pictureTitle2 % 2 != 0)
            {
                pictureBox_CE_2.Image = SMU.Properties.Resources.上__2_;
                tableLayoutPanel1.RowStyles[6] = new RowStyle(SizeType.Percent, 0);
            }
            else
            {
                pictureBox_CE_2.Image = SMU.Properties.Resources.下__1_;
                tableLayoutPanel1.RowStyles[6] = new RowStyle(SizeType.Percent, (float)24.24);
            }
            pictureTitle2++;
        }

        /// <summary>
        /// DataTable展示内容 收起/展开 方法
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void pictureBox_CE_1_Click(object sender, EventArgs e)
        {
            if (pictureTitle1 % 2 != 0)
            {
                pictureBox_CE_1.Image = SMU.Properties.Resources.上__2_;
                tableLayoutPanel1.RowStyles[1] = new RowStyle(SizeType.Percent, 0);
                tableLayoutPanel1.RowStyles[2] = new RowStyle(SizeType.Absolute, 0);
                tableLayoutPanel1.RowStyles[3] = new RowStyle(SizeType.Percent, 0);
            }
            else
            {
                pictureBox_CE_1.Image = SMU.Properties.Resources.下__1_;
                tableLayoutPanel1.RowStyles[1] = new RowStyle(SizeType.Percent, (float)37.88);
                tableLayoutPanel1.RowStyles[2] = new RowStyle(SizeType.Absolute, 4);
                tableLayoutPanel1.RowStyles[3] = new RowStyle(SizeType.Percent, (float)37.88);
            }
            pictureTitle1++;
        }

        /// <summary>
        /// 清空展示数据区域的内容
        /// </summary>
        public void ClearShowContent()
        {
            maxXvalue = xShowRange;
            minXValue = 0;
            for (int i = 0; i < volSignals.Count; i++)
            {
                volPlot.Remove(volSignals[i]);
                dicVolX[i] = new List<double>();
                dicVolY[i] = new List<double>();
                volSignals[i] = volPlot.Add.SignalXY(dicVolX[i], dicVolY[i]);
                curPlot.Remove(curSignals[i]);
                dicCurX[i] = new List<double>();
                dicCurY[i] = new List<double>();
                curSignals[i] = curPlot.Add.SignalXY(dicCurX[i], dicCurY[i]);
                _channelMinVoltage[i] = double.PositiveInfinity;
                _channelMaxVoltage[i] = double.NegativeInfinity;
                _channelMinCurrent[i] = double.PositiveInfinity;
                _channelMaxCurrent[i] = double.NegativeInfinity;
            }
            _refreshStopwatch.Restart();
        }

        /// <summary>
        /// 设置电流y轴刻度范围及标题
        /// </summary>
        /// <param name="maxYdata"></param>
        /// <param name="minYdata"></param>
        /// <param name="isYaxisAuto"></param>
        public void SetCurrentAxis(double maxYdata, double minYdata, string yTitle, bool isYaxisAuto)
        {
            if (!string.IsNullOrEmpty(yTitle))
                curPlot.Axes.Left.Label.Text = yTitle;
            _isCurrentAxisAuto = isYaxisAuto;
            if (isYaxisAuto)
            {
                var tickGen_CY1 = new ScottPlot.TickGenerators.NumericAutomatic();
                tickGen_CY1.LabelFormatter = value => value.ToString("F3");
                curPlot.Axes.Left.TickGenerator = tickGen_CY1;
            }
            else
            {
                curPlot.Axes.SetLimitsY(minYdata, maxYdata);
                tickGenCurY = new ScottPlot.TickGenerators.NumericFixedInterval((curPlot.Axes.Left.Max - curPlot.Axes.Left.Min) / 4);
                tickGenCurY.LabelFormatter = value => value.ToString("F3");
                curPlot.Axes.Left.TickGenerator = tickGenCurY;
            }
            #region
            curPlot.Axes.SetLimitsX(0, xShowRange);
            var curxTick = CreateTick(xShowRange, 0, 6);
            curPlot.Axes.Bottom.SetTicks(curxTick.Select(n => n.Position).ToArray(), curxTick.Select(n => n.Label).ToArray());
            curPlot.Axes.Bottom.Label.Text = "Time";
            #endregion
            maxCurValue = maxYdata;
            minCurValue = minYdata;
        }

        /// <summary>
        /// 设置电压y轴刻度范围及标题
        /// </summary>
        /// <param name="maxYdata"></param>
        /// <param name="minYdata"></param>
        /// <param name="yTitle"></param>
        /// <param name="isYaxisAuto"></param>
        public void SetVoltageAxis(double maxYdata, double minYdata, string yTitle, bool isYaxisAuto)
        {
            if (!string.IsNullOrEmpty(yTitle))
                volPlot.Axes.Left.Label.Text = yTitle;
            _isVoltageAxisAuto = isYaxisAuto;
            if (isYaxisAuto)
            {
                var tickGen_VY1 = new ScottPlot.TickGenerators.NumericAutomatic();
                tickGen_VY1.LabelFormatter = value => value.ToString("F3");
                volPlot.Axes.Left.TickGenerator = tickGen_VY1;
            }
            else
            {
                volPlot.Axes.SetLimitsY(minYdata, maxYdata);
                tickGenVolY = new ScottPlot.TickGenerators.NumericFixedInterval((volPlot.Axes.Left.Max - volPlot.Axes.Left.Min) / 4);
                tickGenVolY.LabelFormatter = value => value.ToString("F3");
                volPlot.Axes.Left.TickGenerator = tickGenVolY;
            }
            #region
            volPlot.Axes.SetLimitsX(0, xShowRange);
            var volxTick = CreateTick(xShowRange, 0, 6);
            volPlot.Axes.Bottom.SetTicks(volxTick.Select(n => n.Position).ToArray(), volxTick.Select(n => n.Label).ToArray());
            volPlot.Axes.Bottom.Label.Text = "Time";
            #endregion
            maxVolValue = maxYdata;
            minVolValue = minYdata;
        }

        /// <summary>
        /// 设置电流X轴刻度范围及标题
        /// </summary>
        /// <param name="maxXdata"></param>
        /// <param name="minXdata"></param>
        /// <param name="xTitle"></param>
        public void SetCurrentBottomAxis(double maxXdata, double minXdata, string xTitle)
        {
            // 由自动模式管理
        }

        /// <summary>
        /// 设置电压X轴刻度范围及标题
        /// </summary>
        /// <param name="maxXdata"></param>
        /// <param name="minXdata"></param>
        /// <param name="xTitle"></param>
        public void SetVoltageBottomAxis(double maxXdata, double minXdata, string xTitle)
        {
            // 由自动模式管理
        }

        /// <summary>
        /// 添加多条电流数据并展示曲线
        /// </summary>
        public void ShowDataContent(List<double> xData, List<double> volData, List<double> curData, int index)
        {
            if (xData == null)
                throw new ArgumentNullException(nameof(xData));
            if (volData == null)
                throw new ArgumentNullException(nameof(volData));
            if (curData == null)
                throw new ArgumentNullException(nameof(curData));
            if (xData.Count != volData.Count || xData.Count != curData.Count)
                throw new ArgumentException("X、Y 数据点数量不一致");
            if (!dicVolX.ContainsKey(index) || !dicCurX.ContainsKey(index))
                throw new ArgumentOutOfRangeException(nameof(index));
            if (xData.Count == 0)
                return;

            try
            {
                lock (LockObj)
                {
                    AppendChannelData(index, xData, volData, curData);

                    double chunkMaxX = xData[xData.Count - 1];
                    if (xData.Count > 1)
                        chunkMaxX = Math.Max(chunkMaxX, xData.Max());

                    maxXvalue = Math.Max(maxXvalue, chunkMaxX);
                    double axisMin = Math.Max(0, maxXvalue - xShowRange);
                    minXValue = axisMin;
                    double axisMax = Math.Max(xShowRange, maxXvalue);

                    int removedVol = TrimOutOfRange(dicVolX, dicVolY, index, axisMin);
                    removedVol += ClampToMaxSamples(dicVolX, dicVolY, index);

                    int removedCur = TrimOutOfRange(dicCurX, dicCurY, index, axisMin);
                    removedCur += ClampToMaxSamples(dicCurX, dicCurY, index);

                    if (removedVol > 0)
                        RecalculateChannelExtremes(dicVolY[index], _channelMinVoltage, _channelMaxVoltage, index);
                    else
                        UpdateChannelExtremes(_channelMinVoltage, _channelMaxVoltage, index, volData);

                    if (removedCur > 0)
                        RecalculateChannelExtremes(dicCurY[index], _channelMinCurrent, _channelMaxCurrent, index);
                    else
                        UpdateChannelExtremes(_channelMinCurrent, _channelMaxCurrent, index, curData);

                    UpdateAxesFromChannelExtremes();
                    UpdateXAxisRange(volPlot, axisMin, axisMax);
                    UpdateXAxisRange(curPlot, axisMin, axisMax);

                    UpdateRenderIndices(index);

                    RequestPlotRefresh();
                }
            }
            catch (Exception exp)
            {
                LogManager.LogNetManagment.LogNet.WriteError(exp.Message);
            }
        }

        /// <summary>
        /// 是否显示图例Legend
        /// </summary>
        /// <param name="index"></param>
        /// <param name="isShow"></param>
        public void ShowLegend(int index, bool isShow)
        {
            if (index == -1)
            {
                // 显示所有曲线的Legend
                if (isShow)
                {
                    volPlot.Legend.ManualItems = legendItems;
                    curPlot.Legend.ManualItems = legendItems;
                }
                else
                {
                    volPlot.Legend.ManualItems = new List<LegendItem>();
                    curPlot.Legend.ManualItems = new List<LegendItem>();
                }
            }
            else
            {
                // 显示当前曲线的Legend
                if (isShow)
                {
                    volPlot.Legend.ManualItems.Add(legendItems[index]);
                    curPlot.Legend.ManualItems.Add(legendItems[index]);
                }
                else
                {
                    if (volPlot.Legend.ManualItems.Contains(legendItems[index]))
                    {
                        volPlot.Legend.ManualItems.Remove(legendItems[index]);
                    }
                    if (curPlot.Legend.ManualItems.Contains(legendItems[index]))
                    {
                        curPlot.Legend.ManualItems.Remove(legendItems[index]);
                    }
                }
            }
            volPlot.ShowLegend();
            curPlot.ShowLegend();
        }

        /// <summary>
        /// 开启/关闭 绘图和表格渲染（UI渲染性能优化）
        /// </summary>
        public void EnableControlDraw(bool status)
        {
            try
            {
                // 控件可见性在此处管理
            }
            catch (Exception exp)
            {
                LogManager.LogNetManagment.LogNet.WriteError(exp.Message);
            }
        }

        /// <summary>
        /// 绑定DataTable数据源
        /// </summary>
        /// <param name="_tableList"></param>
        public void BindingDataSource(BindingList<TableAnalysis> _tableList)
        {
            tableList = _tableList;
            dt_DataAnalysis.DataSource = tableList;
        }

        /// <summary>
        /// 加载Plot控件样式
        /// </summary>
        private void LoadFormPlotStyle()
        {
            maxXvalue = xShowRange;
            #region 电压曲线图样式绘制
            // 定义电压曲线图变量
            volPlot = formsPlost_Voltage.Plot;

            #region 刻度
            // 设置刻度范围
            volPlot.Axes.SetLimitsX(0, xShowRange);
            volPlot.Axes.SetLimitsY(0, 30.00);
            tickGenVolY = new ScottPlot.TickGenerators.NumericFixedInterval((volPlot.Axes.Left.Max - volPlot.Axes.Left.Min) / 4);
            tickGenVolY.LabelFormatter = value => value.ToString("F3");
            volPlot.Axes.Left.TickGenerator = tickGenVolY;
            var volxTick = CreateTick(xShowRange, 0, 6);
            volPlot.Axes.Bottom.SetTicks(volxTick.Select(n => n.Position).ToArray(), volxTick.Select(n => n.Label).ToArray());
            // 移除X轴主刻度线和次刻度线
            volPlot.Axes.Bottom.MajorTickStyle.Length = 0;
            volPlot.Axes.Bottom.MinorTickStyle.Length = 0;
            // 移除y轴主刻度线和次刻度线
            volPlot.Axes.Left.MajorTickStyle.Length = 0;
            volPlot.Axes.Left.MinorTickStyle.Length = 0;
            // 设置轴刻度值与边框的边距
            volPlot.Axes.Bottom.TickLabelStyle.OffsetY = 5;
            volPlot.Axes.Left.TickLabelStyle.OffsetX = -2;
            // 设置轴刻度值字体样式
            volPlot.Axes.Bottom.TickLabelStyle.FontSize = 11;
            #endregion

            #region 颜色
            // 外部图形颜色
            volPlot.FigureBackground.Color = ScottPlot.Color.FromHex("#3C3C3C");
            // 内部图形颜色
            volPlot.DataBackground.Color = ScottPlot.Color.FromHex("#262626");
            // 更改轴和网格颜色
            volPlot.Axes.Color(ScottPlot.Color.FromHex("#FFFFFF"));
            volPlot.Axes.FrameColor(ScottPlot.Color.FromHex("#808080"));
            volPlot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#808080");
            #endregion

            #region 轴标题
            // X设置轴标题
            volPlot.Axes.Bottom.Label.Text = "Time";
            volPlot.Axes.Bottom.Label.FontSize = 13;
            volPlot.Axes.Bottom.Label.ForeColor = ScottPlot.Color.FromHex("#747474"); ;
            volPlot.Axes.Bottom.Label.FontName = "Microsoft YaHei";
            volPlot.Axes.Bottom.Label.OffsetY = 2;
            // Y设置轴标题
            volPlot.Axes.Left.Label.Text = "Voltage(V)";
            volPlot.Axes.Left.Label.FontSize = 13;
            volPlot.Axes.Left.Label.ForeColor = ScottPlot.Color.FromHex("#747474"); ;
            volPlot.Axes.Left.Label.FontName = "Microsoft YaHei";
            volPlot.Axes.Left.Label.OffsetX = -5;
            #endregion

            #region 外边距
            // 设置图形与最外侧的边距
            PixelPadding paddingV = new PixelPadding(78, 35, 50, 20);
            volPlot.Layout.Fixed(paddingV);
            #endregion

            #region 图例
            volPlot.Legend.FontSize = 11;       // 字体大小
            volPlot.Legend.OutlineColor = ScottPlot.Color.FromColor(System.Drawing.Color.Transparent);  // 图例边框颜色
            volPlot.Legend.ShadowColor = ScottPlot.Color.FromColor(System.Drawing.Color.Transparent);   // 阴影部分颜色
            volPlot.Legend.BackgroundColor = ScottPlot.Color.FromColor(System.Drawing.Color.Transparent);   // 背景颜色
            volPlot.Legend.InterItemPadding = new PixelPadding(2, 2, 2, 2);     // 每条图例之间的边距
            volPlot.Legend.Alignment = Alignment.UpperRight;        // 图例显示位置
            volPlot.Legend.Orientation = ScottPlot.Orientation.Vertical;        // 图例内部展现方式-垂直/水平
            #endregion

            #endregion

            #region 电流曲线图样式绘制
            // 定义电流曲线图变量
            curPlot = formsPlost_Current.Plot;

            #region 刻度
            // 设置刻度范围
            curPlot.Axes.SetLimitsX(0, xShowRange);
            curPlot.Axes.SetLimitsY(0, 500);
            tickGenCurY = new ScottPlot.TickGenerators.NumericFixedInterval((curPlot.Axes.Left.Max - curPlot.Axes.Left.Min) / 4);
            tickGenCurY.LabelFormatter = value => value.ToString("F3");
            curPlot.Axes.Left.TickGenerator = tickGenCurY;
            var curxTick = CreateTick(xShowRange, 0, 6);
            curPlot.Axes.Bottom.SetTicks(curxTick.Select(n => n.Position).ToArray(), curxTick.Select(n => n.Label).ToArray());
            // 移除X轴主刻度线和次刻度线
            curPlot.Axes.Bottom.MajorTickStyle.Length = 0;
            curPlot.Axes.Bottom.MinorTickStyle.Length = 0;
            // 移除y轴主刻度线和次刻度线
            curPlot.Axes.Left.MajorTickStyle.Length = 0;
            curPlot.Axes.Left.MinorTickStyle.Length = 0;
            // 设置轴刻度值与边框的边距
            curPlot.Axes.Bottom.TickLabelStyle.OffsetY = 5;
            curPlot.Axes.Left.TickLabelStyle.OffsetX = -2;
            // 设置轴刻度值字体样式
            curPlot.Axes.Bottom.TickLabelStyle.FontSize = 11;
            curPlot.Axes.Left.TickLabelStyle.FontSize = 11;
            #endregion

            #region 颜色
            // 外部图形颜色
            curPlot.FigureBackground.Color = ScottPlot.Color.FromHex("#3C3C3C");
            // 内部图形颜色
            curPlot.DataBackground.Color = ScottPlot.Color.FromHex("#262626");
            // 更改轴和网格颜色
            curPlot.Axes.Color(ScottPlot.Color.FromHex("#FFFFFF"));
            curPlot.Axes.FrameColor(ScottPlot.Color.FromHex("#808080"));
            curPlot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#808080");
            #endregion

            #region 轴标题
            // X设置轴标题
            curPlot.Axes.Bottom.Label.Text = "Time";
            curPlot.Axes.Bottom.Label.FontSize = 13;
            curPlot.Axes.Bottom.Label.ForeColor = ScottPlot.Color.FromHex("#747474"); ;
            curPlot.Axes.Bottom.Label.FontName = "Microsoft YaHei";
            curPlot.Axes.Bottom.Label.OffsetY = 2;
            // Y设置轴标题
            curPlot.Axes.Left.Label.Text = "Current(mA)";
            curPlot.Axes.Left.Label.FontSize = 13;
            curPlot.Axes.Left.Label.ForeColor = ScottPlot.Color.FromHex("#747474"); ;
            curPlot.Axes.Left.Label.FontName = "Microsoft YaHei";
            curPlot.Axes.Left.Label.OffsetX = -1;
            #endregion

            #region 外边距
            // 设置图形与最外侧的边距
            PixelPadding paddingC = new PixelPadding(78, 35, 50, 20);
            curPlot.Layout.Fixed(paddingC);
            #endregion

            #region 图例
            curPlot.Legend.FontSize = 11;       // 字体大小
            curPlot.Legend.OutlineColor = ScottPlot.Color.FromColor(System.Drawing.Color.Transparent);  // 图例边框颜色
            curPlot.Legend.ShadowColor = ScottPlot.Color.FromColor(System.Drawing.Color.Transparent);   // 阴影部分颜色
            curPlot.Legend.BackgroundColor = ScottPlot.Color.FromColor(System.Drawing.Color.Transparent);   // 背景颜色
            curPlot.Legend.InterItemPadding = new PixelPadding(2, 2, 2, 2);      // 每条图例之间的边距
            curPlot.Legend.Alignment = Alignment.UpperRight;        // 图例显示位置
            curPlot.Legend.Orientation = ScottPlot.Orientation.Vertical;         // 图例内部展现方式-垂直/水平
            #endregion

            #endregion
        }

        /// <summary>
        /// 初始化绘图控件数据源
        /// </summary>
        private void InitFormPlotData()
        {
            for (int i = 0; i < Global.ChNumber + 2; i++)
            {
                // 初始化数据字典
                dicVolX.Add(i, new List<double>());
                dicVolY.Add(i, new List<double>());
                dicCurX.Add(i, new List<double>());
                dicCurY.Add(i, new List<double>());

                _channelMinVoltage[i] = double.PositiveInfinity;
                _channelMaxVoltage[i] = double.NegativeInfinity;
                _channelMinCurrent[i] = double.PositiveInfinity;
                _channelMaxCurrent[i] = double.NegativeInfinity;

                // 绑定电压曲线数据源
                volSignals.Add(volPlot.Add.SignalXY(dicVolX[i], dicVolY[i]));
                volSignals[i].Color = ScottPlot.Color.FromHex(StaticConsts.CHART_LINE_COLOR[i]);
                volSignals[i].LineWidth = 3;

                // 绑定电流曲线数据源
                curSignals.Add(curPlot.Add.SignalXY(dicCurX[i], dicCurY[i]));
                curSignals[i].Color = ScottPlot.Color.FromHex(StaticConsts.CHART_LINE_COLOR[i]);
                curSignals[i].LineWidth = 3;

                // 创建图例对象保存至图例集合当中
                LegendItem item = new LegendItem
                {
                    LabelText = (i < 2 ? "M" : "") + "CH " + (i - 1),
                    LabelFontSize = 11,
                    LabelFontColor = ScottPlot.Color.FromHex(StaticConsts.CHART_LINE_COLOR[i]),
                    LineColor = ScottPlot.Color.FromHex(StaticConsts.CHART_LINE_COLOR[i]),
                    LineWidth = 2,
                };
                legendItems.Add(item);
            }
        }

        /// <summary>
        /// 创建X轴刻度器
        /// </summary>
        /// <param name="max"></param>
        /// <param name="min"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private List<Tick> CreateTick(double max, double min, int count)
        {
            List<Tick> tickList = new List<Tick>();
            double interval = (max - min) / count;
            for (int i = 0; i <= count; i++)
            {
                if (i == 0)
                {
                    TimeSpan ts = TimeSpan.FromMilliseconds(min);
                    Tick tick = new Tick(min, $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}", true);
                    tickList.Add(tick);
                }
                else if (i == count / 2)
                {
                    TimeSpan ts = TimeSpan.FromMilliseconds(min + interval * i);
                    Tick tick = new Tick(min + interval * i, $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}", true);
                    tickList.Add(tick);
                }
                else if (i == count)
                {
                    TimeSpan ts = TimeSpan.FromMilliseconds(max);
                    Tick tick = new Tick(max, $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}", true);
                    tickList.Add(tick);
                }
                else
                {
                    Tick tick = new Tick(min + interval * i, "", true);
                    tickList.Add(tick);
                }
            }
            return tickList.OrderByDescending(n => n.Position).ToList();
        }

        private void AppendChannelData(int index, List<double> xData, List<double> volData, List<double> curData)
        {
            dicVolX[index].AddRange(xData);
            dicVolY[index].AddRange(volData);
            dicCurX[index].AddRange(xData);
            dicCurY[index].AddRange(curData);
        }

        private int TrimOutOfRange(Dictionary<int, List<double>> xDict, Dictionary<int, List<double>> yDict, int index, double minThreshold)
        {
            List<double> xs = xDict[index];
            if (xs.Count == 0)
                return 0;

            if (xs[0] >= minThreshold)
                return 0;

            int removeCount = FindFirstIndexAtOrAbove(xs, minThreshold);
            if (removeCount <= 0)
                return 0;

            xs.RemoveRange(0, removeCount);
            yDict[index].RemoveRange(0, removeCount);
            TrimIfExcessive(xs, yDict[index]);
            return removeCount;
        }

        private int ClampToMaxSamples(Dictionary<int, List<double>> xDict, Dictionary<int, List<double>> yDict, int index)
        {
            if (_maxSamplesPerChannel <= 0)
                return 0;

            List<double> xs = xDict[index];
            int overflow = xs.Count - _maxSamplesPerChannel;
            if (overflow <= 0)
                return 0;

            xs.RemoveRange(0, overflow);
            yDict[index].RemoveRange(0, overflow);
            TrimIfExcessive(xs, yDict[index]);
            return overflow;
        }

        private void UpdateChannelExtremes(Dictionary<int, double> minDict, Dictionary<int, double> maxDict, int index, IList<double> newValues)
        {
            if (newValues.Count == 0)
                return;

            double min = minDict[index];
            double max = maxDict[index];

            for (int i = 0; i < newValues.Count; i++)
            {
                double value = newValues[i];
                if (value < min)
                    min = value;
                if (value > max)
                    max = value;
            }

            minDict[index] = min;
            maxDict[index] = max;
        }

        private void RecalculateChannelExtremes(List<double> values, Dictionary<int, double> minDict, Dictionary<int, double> maxDict, int index)
        {
            if (values.Count == 0)
            {
                minDict[index] = double.PositiveInfinity;
                maxDict[index] = double.NegativeInfinity;
                return;
            }

            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;
            for (int i = 0; i < values.Count; i++)
            {
                double value = values[i];
                if (value < min)
                    min = value;
                if (value > max)
                    max = value;
            }

            minDict[index] = min;
            maxDict[index] = max;
        }

        private void UpdateAxesFromChannelExtremes()
        {
            if (_isVoltageAxisAuto)
                UpdateAxisFromChannelExtremes(volPlot, _channelMinVoltage, _channelMaxVoltage, ref minVolValue, ref maxVolValue);

            if (_isCurrentAxisAuto)
                UpdateAxisFromChannelExtremes(curPlot, _channelMinCurrent, _channelMaxCurrent, ref minCurValue, ref maxCurValue);
        }

        private void UpdateAxisFromChannelExtremes(Plot plot, Dictionary<int, double> channelMins, Dictionary<int, double> channelMaxs, ref double minValue, ref double maxValue)
        {
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            foreach (var kvp in channelMins)
            {
                if (kvp.Value < min)
                    min = kvp.Value;
            }

            foreach (var kvp in channelMaxs)
            {
                if (kvp.Value > max)
                    max = kvp.Value;
            }

            if (double.IsPositiveInfinity(min) || double.IsNegativeInfinity(max))
            {
                return;
            }

            minValue = min;
            maxValue = max;

            double span = maxValue - minValue;
            double padding = span == 0 ? Math.Max(Math.Abs(maxValue), 1) * 0.05 : span * 0.05;
            plot.Axes.SetLimitsY(minValue - padding, maxValue + padding);
        }

        private void UpdateXAxisRange(Plot plot, double axisMin, double axisMax)
        {
            if (axisMax <= axisMin)
                axisMax = axisMin + 1;

            var ticks = CreateTick(axisMax, axisMin, 6);
            double minTick = ticks.Min(n => n.Position);
            double maxTick = ticks.Max(n => n.Position);
            plot.Axes.SetLimitsX(minTick, maxTick);
            plot.Axes.Bottom.SetTicks(ticks.Select(n => n.Position).ToArray(), ticks.Select(n => n.Label).ToArray());
        }

        private void UpdateRenderIndices(int index)
        {
            int volCount = dicVolX[index].Count;
            if (volCount > 0)
            {
                volSignals[index].MinRenderIndex = 0;
                volSignals[index].MaxRenderIndex = volCount - 1;
            }
            else
            {
                volSignals[index].MinRenderIndex = 0;
                volSignals[index].MaxRenderIndex = -1;
            }

            int curCount = dicCurX[index].Count;
            if (curCount > 0)
            {
                curSignals[index].MinRenderIndex = 0;
                curSignals[index].MaxRenderIndex = curCount - 1;
            }
            else
            {
                curSignals[index].MinRenderIndex = 0;
                curSignals[index].MaxRenderIndex = -1;
            }
        }

        private void RequestPlotRefresh()
        {
            if (_refreshStopwatch.Elapsed < _refreshInterval)
                return;

            if (formsPlost_Current.IsHandleCreated)
            {
                formsPlost_Current.BeginInvoke(new Action(() => formsPlost_Current.Refresh()));
            }
            if (formsPlost_Voltage.IsHandleCreated)
            {
                formsPlost_Voltage.BeginInvoke(new Action(() => formsPlost_Voltage.Refresh()));
            }

            _refreshStopwatch.Restart();
        }

        private static int FindFirstIndexAtOrAbove(List<double> values, double threshold)
        {
            int left = 0;
            int right = values.Count - 1;
            int result = values.Count;

            while (left <= right)
            {
                int mid = left + ((right - left) / 2);
                if (values[mid] < threshold)
                {
                    left = mid + 1;
                }
                else
                {
                    result = mid;
                    right = mid - 1;
                }
            }

            return result;
        }

        private static void TrimIfExcessive(List<double> xs, List<double> ys)
        {
            const double ratioThreshold = 1.5;

            if (xs.Count == 0)
            {
                if (xs.Capacity > 1024)
                    xs.TrimExcess();
                if (ys.Capacity > 1024)
                    ys.TrimExcess();
                return;
            }

            if (xs.Capacity > xs.Count * ratioThreshold)
                xs.TrimExcess();
            if (ys.Capacity > ys.Count * ratioThreshold)
                ys.TrimExcess();
        }

        private static int CalculateMaxSamples(double milliseconds, double samplesPerSecond)
        {
            if (samplesPerSecond <= 0)
                return 0;

            double seconds = milliseconds / 1000.0;
            double total = Math.Ceiling(seconds * samplesPerSecond);
            if (double.IsNaN(total) || double.IsInfinity(total) || total >= int.MaxValue)
                return int.MaxValue;
            if (total < 1)
                return 1;
            return (int)total;
        }
    }
}
