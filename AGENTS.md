# DreamVR 项目指南

## 项目目标

DreamVR 是一个 Unity VR 零件装配项目。研究目标是比较三种拆卸交互配置对用户的影响：无主动提示、仅高亮当前轮次零件、同时高亮当前轮次零件并显示拆卸方向。当前里程碑先实现第一种 `NoGuidance`：让 `Assets/Scenes/Main.unity` 中 `71` 模型的零件支持 Meta XR Interaction SDK 抓取；将每个可动零件限制在父物体局部坐标系的单一轴向和最大行程内；在手部或手柄接近可抓取零件时显示交互反馈；并通过场景中已有的红色按钮重置所有零件。

实验中的“主动提示”和“可抓取反馈”是两套独立机制：

- `NoGuidance`：不主动暴露当前轮次零件或方向，但接近当前可抓取零件时仍显示高亮反馈。
- `CurrentPartHighlight`：持续高亮当前轮次可拆卸零件，并保留接近反馈。
- `CurrentPartHighlightAndDirection`：持续高亮当前轮次零件并显示方向，同时保留接近反馈。

## 已确认的项目状态

- Unity 版本：`6000.3.11f1`。
- 渲染管线：URP 17.3.0。
- XR 技术栈：Meta XR SDK All `203.0.0`、OpenXR、XR Plug-in Management 和 Input System。
- `Main.unity` 中的交互设备：`OVRComprehensiveInteractionRig`。
- `Main.unity` 中的装配模型实例：`71`，来源为 `Assets/Art/Models/E1/71.fbx`。
- `71` 根物体当前已经挂载 Highlight Plus 的 `HighlightEffect`，而且序列化状态为已高亮。实现零件级反馈时，必须替换或禁用这个始终开启的整体高亮。
- `Main.unity` 中的重置控件是 Meta 示例预制体实例 `BigRedButton`。它已经包含 `PokeInteractable` 和 `InteractableUnityEventWrapper`，重置功能应接入其 `WhenSelect` 事件。场景中已经为按钮添加了 `Reset` 文本。
- 高亮方案：位于 `Assets/HighlightPlus` 的 Highlight Plus。
- 除导入的插件、示例和教程内容外，项目目前没有自有运行时代码。

## 源数据

移动方向文件为 `Assets/Art/Models/E1/E1.txt`：

```text
round1: (1, -Z), (7, +Z)
round2: (2, -Z), (6, +Z)
round3: (3, -Z), (4, +Z)
```

方向相对于模型父物体，而不是世界坐标系。`E1.txt` 使用从 0 开始的直接子物体下标。`round` 定义拆卸顺序：同一轮次内的零件可按任意先后拆卸，只有当前轮次全部到达拆卸终点并松手后，下一轮次才可交互。

下标 `5` 是固定件，不添加抓取组件。其他未列入 `E1.txt` 的子物体同样保持固定，除非后续数据明确指定。

最大移动距离由 Editor 配置工具根据 Renderer 包围盒自动计算：零件移动到终点时必须完全越过装配体沿目标轴的外边界，并增加与零件尺寸相关的安全间隙。随后按相同轴向和正负方向从内层向外层执行单调约束，保证外层轮次的最大距离不小于任何内层轮次，同时每个零件仍有足够行程脱离装配体。

添加组件前，必须在 Unity Editor 中根据导入模型的直接子物体解析并确认零基下标。若 FBX 根物体包含额外容器层，配置工具应选择能解析所有下标且 Renderer 命中率最高的直接父物体。解析后把实际 `Transform` 引用序列化到场景配置中，避免运行时依赖层级顺序。

## 计划中的运行时设计

项目自有代码统一放在 `Assets/Scripts/Assembly` 下。禁止修改 Meta SDK 包文件或 Highlight Plus 源码。

### AssemblyPart

每个可动零件挂载一个 `AssemblyPart` 组件，负责管理：

- 序列化的零件引用、轮次、父物体局部轴向及正负方向、最大移动距离。
- 初始化时记录的局部位置和局部旋转。
- Meta Interactable 与零件级 Highlight Plus 效果的引用。
- `ResetPart()`：取消或临时禁用当前交互，清除刚体速度，恢复初始局部姿态，然后恢复交互和高亮状态。

高亮状态必须综合所有已配置的 Interactable。只要任意手部或手柄 Interactable 处于 Hover 或 Select 状态，就保持零件高亮；只有全部恢复 Normal 后才关闭高亮。应在组件生命周期方法中订阅和取消订阅 Meta 状态事件，不要使用面向鼠标射线的 Highlight Plus 悬停逻辑处理 VR 手部交互。

### AssemblyController

在装配体根物体或独立场景物体上挂载一个 `AssemblyController`。它保存已经解析的 `AssemblyPart` 列表、实验条件和当前轮次，并公开 `ResetAll()` 方法。第一阶段实验条件固定为 `NoGuidance`。把现有 `BigRedButton` 子物体上的 `InteractableUnityEventWrapper.WhenSelect` 绑定到该方法，同时保留按钮已有的动画和音效监听器。

### Editor 配置工具

提供一个仅在 Editor 中运行的配置命令，以便可重复地配置场景。该工具应解析并验证 E1 方向数据，一次性解析每个零件，添加和配置所需组件，建立序列化引用，并报告缺失、重复或越界的下标。运行时代码不得反复解析 `E1.txt`，也不得通过子物体下标搜索零件。

## Meta Grab 配置

每个可动零件至少需要：

- 一个或多个 Collider。Quest 上优先使用经过验证、开销较低的组合基础形状 Collider；只有在烘焙成功且复杂度合理时才使用凸 `MeshCollider`。
- 一个关闭重力、禁止抛掷的 `Rigidbody`。松手后零件不得变成自由运动的物理物体。
- Meta `Grabbable`，最大抓取点数设置为 1，不配置双手 Transformer。
- Meta `GrabInteractable`，用于手柄近距离抓取。
- Meta `HandGrabInteractable`，用于手部近距离抓取。
- Meta `OneGrabTranslateTransformer`，作为单手抓取 Transformer。

`OneGrabTranslateTransformer` 使用相对约束，因为它的约束在父物体坐标系中计算：

- 两个非移动轴的最小值和最大值都约束为 `0`。
- 正方向移动时，目标轴限制在 `0` 到 `maxDistance`。
- 负方向移动时，目标轴限制在 `-maxDistance` 到 `0`。
- 保持初始旋转，不允许缩放或双手变换。

当前 E1 数据经 Unity Renderer 包围盒计算后的实际配置如下，距离单位为索引父物体 `71` 的局部单位：

| 轮次 | 零件下标 | 零件名称 | 父物体局部坐标行程 | 最大距离 |
| ---- | -------: | -------- | ------------------ | -------: |
| 1 | 1 | Bottom Spacer | Z 轴从 `0` 到 `-maxDistance` | 0.72484 |
| 1 | 7 | Top Pentagon | Z 轴从 `0` 到 `+maxDistance` | 0.40442 |
| 2 | 2 | Bulb holder | Z 轴从 `0` 到 `-maxDistance` | 0.72484 |
| 2 | 6 | Surface 1.005 | Z 轴从 `0` 到 `+maxDistance` | 0.40442 |
| 3 | 3 | Flange | Z 轴从 `0` 到 `-maxDistance` | 0.72484 |
| 3 | 4 | Middle Pentagon | Z 轴从 `0` 到 `+maxDistance` | 0.40442 |

## 高亮配置

- 为每个可动零件单独添加 Highlight Plus `HighlightEffect`，或明确指定该零件自身的 Renderer 作为效果目标。
- 初始状态必须关闭高亮。
- 使用共享的 `HighlightProfile`，确保颜色和描边宽度一致。
- 为保证 Quest 性能，优先采用清晰的描边，并谨慎使用 Glow 和 Overlay。
- 完成零件级效果配置后，禁用或移除当前根物体上始终开启的 `HighlightEffect`。

## 重置语义

无论零件处于空闲、悬停还是正在被抓取的状态，`ResetAll()` 都必须正确工作。重置过程必须：

1. 安全结束或取消当前抓取，防止 Interactor 立即把零件拉回重置前的位置。
2. 清零线速度和角速度。
3. 恢复每个零件记录的局部位置和局部旋转。
4. 恢复预先配置的运动学和交互状态。
5. 清除残留高亮，然后恢复正常的悬停状态判断。

## 验收清单

- 修改脚本后，Unity 编译无错误。
- 配置工具报告 6 个可动零件，不存在无法解析的下标；下标 `5` 保持固定。
- 手部接触或接近时，只高亮当前目标零件。
- 手柄和手部近距离抓取均可正常使用。
- 每个零件只能沿配置的父物体局部轴移动。
- 两端均为硬限制；横向拉动或旋转手部不能使零件偏离指定轴或改变旋转。
- 松开零件后不会产生抛掷、重力漂移或自由运动。
- 未完成当前轮次时，后续轮次零件不可抓取；当前轮次全部完成后，下一轮次解锁。
- 第一阶段不会主动高亮当前轮次零件，也不会显示方向提示；只有接近可抓取零件时才高亮。
- 按下 `BigRedButton` 后，所有零件精确恢复到初始局部姿态。
- 正在抓取时执行重置也能正常工作，且不会残留高亮。
- 先在 Editor Play Mode 中验证，再在目标 Meta 头显上验证。

为方向数据解析、约束配置和重置姿态恢复添加集中的 EditMode 测试。PlayMode 测试必须跨帧检查父空间单轴约束、旋转锁定、同轮次完成门槛和重置协程。由于手部追踪和实际触达范围无法完全通过自动化测试验证，必须保留头显真机测试。

当前自动验证基线：Unity EditMode 测试 `5/5`、PlayMode 测试 `3/3` 通过。PlayMode 测试会实际加载 `Main.unity`，使用真实 Meta `GrabInteractor` 对第一轮零件执行 Hover、Select、Unselect 和 Unhover，并验证 Highlight Plus 随状态开启和关闭；同时验证 `71` 的序列化配置、第一轮交互状态、无主动高亮、第一轮完成后推进到第二轮，以及 `BigRedButton` 重置。场景验证器还会确认 6 个可动零件、实际直接子物体与 txt 下标对应、Meta 父空间单轴约束数值正确、同方向外层行程不短于内层、初始 `round=1`、`child[5]` 固定、根物体整体高亮关闭、各零件必需组件完整、后续轮次初始禁用，且按钮已持久化绑定 `AssemblyController.ResetAll`。

命令行运行 PlayMode 测试时不要使用 `-nographics`：Meta/OpenXR 原生插件在当前 Windows 环境切换 Play Mode 时会崩溃。保留 `-batchmode` 并启用正常图形初始化时，测试可正常完成。

## 仓库工作规则

- 保留用户已有修改。`Assets/Scenes/Main.unity`、URP Renderer 资源和 Highlight Plus 导入内容可能已经处于修改状态。
- 禁止通过修改 `Library/PackageCache`、`Assets/Samples` 或第三方插件目录中的文件来实现项目功能。
- 优先使用场景预制体 Override，以及项目自有脚本和资源。
- 修改脚本后，等待 Unity 编译完成并检查 Console 错误，再配置新增的组件类型。
- 修改场景后，保存 `Assets/Scenes/Main.unity`，并确认所有序列化引用均未丢失。
- 使用 Unity MCP 前，必须读取当前连接实例和工程路径。本文档创建时，Unity MCP 连接的是 `EgoAnchor_Unity`，不是 `DreamVR`；禁止通过不匹配的 Unity 实例修改 DreamVR。
