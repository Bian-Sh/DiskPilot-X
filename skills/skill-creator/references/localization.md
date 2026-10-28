# 技能动态界面本地化

## 响应宿主语言

在 `package.localizations` 中提交文案目录，并在显示属性使用 `{loc key}`：

```json
{"localizations":{"en-US":{"title":"Inventory","safe":"Safe mode"},"zh-CN":{"title":"盘点","safe":"安全模式"},"zh-TW":{"title":"盤點","safe":"安全模式"}}}
```

```xml
<StackPanel>
  <TextBlock Text="{loc title}" />
  <ComboBox Name="mode" SelectedValue="safe">
    <ComboBoxItem Value="safe" Content="{loc safe}" />
  </ComboBox>
</StackPanel>
```

- `{loc key}` 仅可出现在 TextBlock.Text、CheckBox.Content、ComboBoxItem.Content 和 Button.Content。不允许用于路径、输入框默认值、绑定字段或命令。
- ComboBoxItem 的 `Value` 是不随语言变化的参数值；`Content` 只是标签。旧未声明 Value 的项仍以原始 Content 作为值。
- 首次显示按宿主当前语言解析；语言切换仅更新文案，不重建控件、不清空用户输入/勾选/结果，也不重跑任务。窗口隐藏仍可重开，真正关闭后解除订阅。
- 文案目录纳入版本摘要，修改后旧源码授权失效。用户包最多 32 个文化、每文化 1024 项、单文件 128 KiB、总计 512 KiB，键最长 256 字符、值最长 16384 字符；UI 本地化键采用不含空格的字母数字及 `_.-`，长度不超过 128。
- `render_view` 及源码的 `SkillUiSpec.Localizations` 同样支持显示文字 `{loc key}`。JSON 视图目录最多 8 种文化、每文化 256 项、总计 256 KiB；不翻译 Path、AppId、命令种类或动作目标。
