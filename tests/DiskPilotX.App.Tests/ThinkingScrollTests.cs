using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DiskPilotX.App.Views;

namespace DiskPilotX.App.Tests;

public class ThinkingScrollTests
{
    [Fact]
    public void 长思考底部跟随且上翻后追加内容不抢走阅读位置()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var text = new TextBlock { Text = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"第 {i} 步核对缓存")) };
                var scroll = new ThinkingScrollViewer { Content = text, Width = 280, Height = 160 };
                void Layout()
                {
                    for (var i = 0; i < 3; i++)
                    {
                        scroll.Measure(new Size(280, 160));
                        scroll.Arrange(new Rect(0, 0, 280, 160));
                        scroll.UpdateLayout();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    }
                }
                Layout();
                Assert.True(scroll.ScrollableHeight > 0);
                scroll.ScrollToEnd();
                Layout();
                text.Text += "\n新增思考内容";
                Layout();
                Assert.InRange(scroll.ScrollableHeight - scroll.VerticalOffset, 0, 1);
                scroll.ScrollToVerticalOffset(40);
                Layout();
                var readingOffset = scroll.VerticalOffset;
                text.Text += string.Concat(Enumerable.Repeat("\n继续追加思考", 20));
                Layout();
                Assert.Equal(readingOffset, scroll.VerticalOffset);
                scroll.ScrollToEnd();
                Layout();
                text.Text += "\n恢复底部跟随";
                Layout();
                Assert.InRange(scroll.ScrollableHeight - scroll.VerticalOffset, 0, 1);
            }
            catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "思考滚动布局应及时完成");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
