using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DiskPilotX.App;
using DiskPilotX.Core.Models;
using FluentAssertions;
namespace DiskPilotX.App.Tests;
public class RejectedPublishDiagnosticsTests
{
    [Fact] public void 全部未扫描候选返回准确计数且不伪装成功()
    {
        // 无批次时该只读格式化路径不访问控件或服务，避免为诊断测试构造整套WPF窗口。
        var window=(MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var method=typeof(MainWindow).GetMethod("RejectedPublish",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var result=(JsonObject)method.Invoke(window,[new AnalysisDiagnostics { InputRecommendationCount=10,MissingEntryCount=10 }])!;
        result["ok"]!.GetValue<bool>().Should().BeFalse();
        result["accepted_count"]!.GetValue<int>().Should().Be(0);
        result["validation"]!["input_count"]!.GetValue<int>().Should().Be(10);
        result["validation"]!["not_scanned"]!.GetValue<int>().Should().Be(10);
        result["code"]!.GetValue<string>().Should().Be("no_accepted_candidates");
    }
}
