# 目标
让 treemap 的选择和进入分离，参考 WizTree 提升边界、颜色与鼠标反馈的辨识度。

# 范围
单击任何块只选中；仅双击文件夹标题进入该文件夹。目录采用紧凑深灰标题和一像素间距，文件按扩展名稳定配色，常态使用纯色，悬停时以跨 cell 的局部柔和光照跟随鼠标位置。按用户四状态截图，悬停用细白框，文件选择用更明显白框，文件夹选择白框覆盖标题及全部子块，提示延迟出现，离开清除悬停。导航或布局更新清理失效选择。

## 目录结构
### 新建
- tests/SpaceMonger.App.Tests/TreemapControlTests.cs
- docs/comet/changes/treemap-wiztree/specs/treemap/spec.md
- docs/design/treemap-wiztree-design.md
### 修改
- src/SpaceMonger.App/Controls/TreemapControl.cs
- src/SpaceMonger.App/Views/TreemapView.xaml 与 .xaml.cs
- src/SpaceMonger.App/ViewModels/TreemapViewModel.cs
- src/SpaceMonger.Core/Services/Treemap/SquarifiedTreemapLayout.cs
- tests/SpaceMonger.Core.Tests 中布局回归测试
### 删除
无。
### 明确不建
不新增绘图依赖，不重写扫描引擎，不引入主题设置系统。

# 非目标
不改动扫描、AI、文件删除和主窗口导航。发布新的独立包但不替换现有包。

# 验收示例
- 单击标题或内容块：出现选中轮廓，根路径与布局不变。
- 双击文件夹标题：当前根切换到该文件夹；双击文件与内容区域不进入。
- 鼠标跨块移动：细亮轮廓跟随，离开清除；选中轮廓继续保留。
- 相同扩展名颜色一致，不同常见类型可区分，文件夹边界紧凑。
- 回归测试通过，输出带可读时间的独立发布包，cua-driver 截图与交互验证。

# 补充的视觉验收
选中粗框向外绘制，内部面积和布局不变；仅文件夹标题不参与光照且悬停不激活光斑，文件标题与内容均响应光照。
