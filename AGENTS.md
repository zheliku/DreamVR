# DreamVR 项目指南

## 项目目标

DreamVR 是一个 Unity VR 零件拆卸实验项目。所有列入拆卸 txt 的零件在任何轮次、任何完成状态下都可以自由抓取、平移和旋转。txt 的 `round` 与方向只控制实验视觉指引，绝不控制抓取权限或运动约束。

实验条件：

- `NoGuidance`：不显示拆卸顺序和方向。仍保留手/手柄接近时的交互反馈。
- `CurrentPartHighlight`：持续高亮当前指引轮次的未完成零件，不显示方向箭头。
- `CurrentPartHighlightAndDirection`：在顺序高亮基础上，用 Shapes 箭头显示每个当前零件的父空间拆卸方向。

“交互反馈”和“实验指引”是两套独立状态：

- 交互反馈：手或手柄 Hover/Select 时显示，默认蓝色；优先级最高。
- 顺序指引：仅后两种实验条件显示，默认绿色，持续标识当前 round。
- 已交互反馈：零件首次产生有效抓放后成为已交互状态，再次 Hover/Select 时默认显示橙色。
- 方向指引：仅 `CurrentPartHighlightAndDirection` 显示，默认半透明琥珀色 Shapes 三维箭头。

所有零件级高亮均启用 Highlight Plus `SeeThroughMode.WhenHighlighted`。零件被外壳或其他零件遮挡时，遮挡区域仍使用当前状态颜色显示透视轮廓和半透明着色；未处于交互反馈或顺序指引状态的零件不显示透视效果。

## 技术栈

- Unity `6000.3.11f1`。
- URP 17.3.0。
- Meta XR SDK All `203.0.0`、OpenXR、XR Plug-in Management、Input System。
- Highlight Plus：`Assets/HighlightPlus`。
- Shapes：`Assets/Plugins/Shapes`，运行时程序集名 `ShapesRuntime`。
- 项目代码：`Assets/Scripts/Assembly`。
- 场景：`Assets/Scenes/Main.unity`。
- 交互 Rig：`OVRComprehensiveInteractionRig`。

禁止修改 Meta SDK、Highlight Plus、Shapes 或其他第三方源码。

## 批量模型目录

模型位于 `Assets/Art/Models`：

```text
Assets/Art/Models/
  Easy/
    E1/  71.fbx + E1.txt + E1.png
    E2/  *.fbx + E2.txt + E2.png
    ...
  Medium/
    M1/  *.fbx + M1.txt + M1.png
    ...
  Hard/
    H1/  *.fbx + H1.txt + H1.png
    ...
```

每个模型目录应只有一个 FBX、一个拆卸 txt 和一个示意 PNG。PNG 只供实验人员核对，不参与运行时逻辑；一键配置会把同目录 PNG 自动回填到 `Reference Image`。配置器默认从 FBX 所在目录自动查找唯一 txt；目录中没有 txt 或存在多个 txt 时，必须显式指定 `Plan Asset`。

`Extra/43` 目前直接把三个文件放在 `Extra` 下，也能自动配置，因为该目录只有一个 txt。`Assets/Art/Models/world_0155.fbx` 同目录没有 txt，不能自动判断数据，必须先整理到独立目录或显式指定 txt。

## TXT 语义

txt 零件序号从 **1** 开始：

```text
childIndex = partNumber - 1
```

例如 `part[1]` 对应 Unity `child[0]`。序号 `0` 非法。未列入 txt 的直接子物体保持固定，不添加装配抓取组件。

支持方向格式：

```text
(1, -Z)
(2, +Y-Z)
(3, -X, +Z)
```

多轴方向会相加并归一化为索引父物体局部向量。一个条目内同一轴不得重复。方向只提供给 Shapes 箭头，不约束 `GrabFreeTransformer`。

`round` 只定义视觉指引顺序：

- 同一 round 的未完成零件同时获得顺序高亮。
- 任意零件都可提前交互；首次有效操作立即标记为已交互。
- 提前操作未来 round 不改变当前指引。
- 当前 round 全部完成后推进；若下一 round 已提前全部完成，则自动继续跳过。
- 撤回会恢复操作前的零件姿态、完成快照和当前指引 round。

## 零件运行时组件

每个 txt 零件配置：

- 一个或多个 Collider；默认凸 `MeshCollider`，无 Mesh 时回退 `BoxCollider`。
- Collider 默认设为 Trigger，不产生零件、手、手柄或环境物理阻挡，但保留 Meta 候选几何查询。
- Kinematic `Rigidbody`，关闭重力和抛掷。
- Meta `Grabbable`，`MaxGrabPoints = 1`，不配置双手 Transformer。
- Meta `GrabInteractable` 与 `HandGrabInteractable`。
- Meta `GrabFreeTransformer`：位置、旋转不约束，缩放锁定。
- 零件级 Highlight Plus `HighlightEffect`。
- `AssemblyDirectionIndicator`，驱动真实的 Shapes `Line` 与 `Cone` 组件组成三维箭头。
- `AssemblyPart`。

禁止使用或残留 `OneGrabTranslateTransformer`，禁止在 `LateUpdate` 回写位置或旋转。

所有 `AssemblyPart` 的手部和手柄 Interactable 在正常运行时始终启用。只有执行撤回/初始化的安全跨帧阶段可以短暂挂起交互。

## 视觉优先级

`AssemblyPart` 用一个零件级 `HighlightEffect` 合成两类高亮状态：

1. Hover/Select 时使用交互色；如果零件已完成则使用完成色。
2. 没有 Hover/Select、但属于当前指引 round 时使用顺序指引色。
3. 其他情况关闭高亮。

因此当前指引零件被手靠近时应由绿色明确切换为蓝色，移开手后恢复绿色。由于 Highlight Plus 在持续高亮期间不会因字段赋值自动重建透视材质，每次状态色变化后必须调用 `UpdateMaterialProperties()`，保证描边和透视填充同时切色。已交互零件不持续显示橙色，只在再次 Hover/Select 时显示橙色，保持现有交互体验。

描边色、透视填充色和透视边框色必须同步切换，避免可见部分与被遮挡部分呈现不同的状态颜色。透视强度、填充透明度、边框强度和边框宽度由 `AssemblyConfigurator` 集中配置；配置完成后允许用户在具体 `HighlightEffect` 上继续微调，运行时不得覆盖这些渲染参数。

`AssemblyDirectionIndicator` 的锚点跟随零件 Renderer 中心，方向使用索引父物体 `TransformDirection(localDirection)`。零件被自由旋转后，箭头仍保持 txt 定义的父空间方向。箭头仅对当前 round 的未完成零件启用。一键配置会在模型根物体下创建 `__DreamVR_DirectionVisuals`，每个提示箭头必须包含真实的 `Shapes.Line` 箭杆和 `Shapes.Cone` 箭头。箭头从零件世界包围盒沿提示方向的表面之外开始，再增加按零件尺寸计算的间隙；默认长度倍率 `0.55`、最小长度 `0.05`、表面外间隙倍率 `0.08`、箭杆粗细倍率 `0.0225`、箭头长度倍率 `0.24`、箭头半径倍率 `0.075`。两者采用半透明混合并使用 `ZTest Always`，降低注意力占用同时避免被装配外壳遮挡。禁止退回依赖 URP `ShapesRenderFeature` 的 Immediate Mode 绘制。

## 操作历史与撤回

一次操作定义为 Select 到 Unselect。释放时局部位移或旋转至少达到一个阈值才提交：

- 默认最小局部位移 `0.005`。
- 默认最小旋转角 `2` 度。

两项都未达到或 Meta 取消交互时，恢复本次抓取起点，不留下无法撤回的变化。

历史记录使用 LIFO，保存零件、操作前局部姿态、当前指引 round 和所有零件完成状态。`UndoLastOperation()` 恢复完整快照。抓取尚未释放时按 Undo，只取消当前未提交操作，不再多弹出一条历史。

`ResetAll()` 只用于会话初始化与测试，不绑定红色按钮。

## 一键配置器

把 `AssemblyConfigurator` 挂在拖入场景的 FBX 根物体上。VInspector 按钮：

- “一键配置拆卸零件”：解析同目录 txt、清理旧配置、添加全部组件、建立引用并绑定 Undo 按钮。
- “验证当前配置”：只在 Editor 中检查，不修改运行时。
- “重新应用无碰撞策略”：主动把可动零件 Collider 恢复为 Trigger。
- “撤回上一次操作”：Play Mode 调试入口。
- “初始化所有零件”：调试入口。

`Undo Button` 可以手动拖入 `BigRedButton` 的 `InteractableUnityEventWrapper`；留空时自动查找名为 `BigRedButton` 的对象。一键配置会移除该按钮上其他 `AssemblyController.ResetAll/UndoLastOperation` 旧监听，只保留当前装配控制器，同时保留按钮动画、音效等非装配监听。

配置器只在点击按钮时写入默认值。运行时不解析 txt、不执行 Validator、不重建组件、不在 `OnEnable` 重写 Collider。用户配置后手动调整的序列化参数应被尊重；再次点击一键配置才会覆盖配置器管理的值。

## 验收

- 所有导入 txt 均能解析，包括单轴、连续多轴和逗号分隔多轴方向。
- 1 基序号正确映射，重复/零/越界序号有明确错误。
- 所有 txt 零件始终可抓，未来 round 和已完成零件也可抓。
- 自由平移和旋转，缩放不变，无物理阻挡，松手不抛掷。
- `NoGuidance` 无持续顺序高亮、无箭头，但保留接近反馈和完成态反馈。
- `CurrentPartHighlight` 只持续高亮当前 round，不显示箭头。
- `CurrentPartHighlightAndDirection` 同时显示当前 round 高亮和 Shapes 箭头。
- 每个方向提示在场景中均存在可检查的 `Shapes.Line` 和 `Shapes.Cone`，不依赖 Shapes Immediate Mode Renderer Feature。
- Hover/Select 交互色覆盖顺序指引色，移开后恢复顺序色。
- 零件被其他几何体遮挡时，当前交互色、完成色或顺序指引色仍能透视显示。
- 提前操作未来 round 会记录完成但不抢跑当前指引；推进时可跳过已完成 round。
- Undo 正确恢复姿态、完成状态、指引 round、高亮和箭头。
- BigRedButton 只绑定当前控制器的 `UndoLastOperation`，不绑定 `ResetAll`。

## 仓库规则

- 保留用户已有修改，尤其是 `Assets/Scenes/Main.unity`、URP 和插件资源。
- 不修改 `Library/PackageCache`、`Assets/Samples` 或第三方源码。
- 使用 Unity MCP 前必须核对实例与 `projectRoot`，只有 `P:/Unity-Project/Work/DreamVR` 才允许操作。
- 未经用户再次明确允许，不得自动启动或调用 Unity；此前错误实例曾造成弹窗。当前可使用静态 C# 编译，但 Unity Test Runner 与头显验证交给正确 DreamVR Editor 执行。
