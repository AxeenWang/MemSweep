using System.Windows.Controls;

namespace MemSweep.Desktop;

/// <summary>commit 與 RAM 量表、開機尖峰、尖峰紀錄的警告、前五名（F-11）；小面板與完整視窗「總覽」共用。</summary>
public partial class SummaryView : UserControl
{
    public SummaryView()
    {
        InitializeComponent();
    }
}
