# 可重开的任务表单（ui.xaml）

先读取本文件，再生成 `manage_cleanup_skill save` 的 `package.ui_xaml`。表单只收集参数，不执行任意 WPF 或 C# 事件。保存后 `show_skill_panel(id)` 发布可重开的任务卡。

## 最小示例

```xml
<StackPanel>
  <TextBlock Text="搜索根目录（多个用分号分隔）" />
  <TextBox Name="roots" MaxLength="4000" />
  <TextBlock Text="向下搜索层数" />
  <TextBox Name="depth" Text="6" MaxLength="2" />
</StackPanel>
```

支持：StackPanel（Orientation）、Grid（Columns）、Border、TextBlock（Text/FontSize/FontWeight）、TextBox（Text/Name/MaxLength）、CheckBox（Content/Name/IsChecked）、ComboBox（Name/SelectedValue）与 ComboBoxItem（Content）、Button（Content/Command）、DataGrid（ItemsSource）。Name 用小写字母、数字和下划线，首位为字母；绑定仅支持 `{Binding 字段名}`。

容器不能携带任意 Style、CLR 类型、事件或代码；颜色、字号与磨砂继承宿主。按钮命令仅 `preview`、`continue`、`close`；宿主已提供默认按钮，通常不要再重复声明。

- `preview` 将字段通过 `parameters` 交给已保存源码；源码读取强类型 `context.Parameters`，不要反射或探测未知方法。
- 当前任务面板会补充 `scope_path` 输入，并要求有效的本地范围。它不同于直接 `run_skill_source` 的 Provider（可不传范围）；向用户披露该限制，不宣称表单可无范围运行。
- 多根目录放在 `roots` 等独立字段中，由 skill 解析并验证；指定目录无效时明确报错或返回空结果，**不能静默扩大到所有磁盘**。未提供目录是否枚举磁盘须先获用户确认。
- 参数预览返回的 Provider 视图可以使用 `selectable=true` 和 `continue` 收集选择，再交回助手。表单内 DataGrid 只读，不是可勾选的结果视图。
- Provider 结果只负责只读观测。清理候选仍经宿主分析与确认；不可根据按钮名称自动删除。

`package.source` 保存完整 C#；`package.ui_xaml` 保存上述 XML；不支持附带 i18n 文件或任意代码文件。UI 的说明必须与实际生成的文件相符。