# DreamVR 批量模型一键配置教程

这份教程按“第一次使用 Unity 也能照着做”的方式编写。目标流程只有一句话：

> 选模型文件夹 -> 把 FBX 拖进场景 -> 添加 `AssemblyConfigurator` -> 选择实验条件 -> 点击一键配置 -> Play。

## 一、先认识模型文件夹

模型都在 `Assets/Art/Models`：

- `Easy`：E1、E2、E3、E4。
- `Medium`：M1、M2、M3、M4。
- `Hard`：H1、H2、H3、H4。

正常的模型文件夹里有三样东西：

```text
模型.fbx       真正拖进 Unity 场景的模型
编号.txt       拆卸 round、零件序号和箭头方向
编号.png       给人看的拆卸示意图
```

例如 E2 文件夹：

```text
Assets/Art/Models/Easy/E2/
  world_0095.fbx
  E2.txt
  E2.png
```

PNG 不需要拖进场景，也不需要手动关联。一键配置后，配置器会把同目录 PNG 自动填入 `Reference Image`，方便人工核对；它不参与运行时逻辑。

## 二、准备场景

推荐复制现有 `Assets/Scenes/Main.unity` 作为实验场景，因为它已经包含：

- `OVRComprehensiveInteractionRig`，负责 Meta 手部和手柄交互。
- `BigRedButton`，负责撤回上一次操作。
- 必要的相机、灯光和 XR 配置。

不要删除 Rig。没有 Rig 时，即使模型组件配置正确，也无法用手或手柄抓取。

## 三、把模型拖进场景

以 E2 为例：

1. 在 Project 窗口依次打开 `Assets > Art > Models > Easy > E2`。
2. 找到 `world_0095.fbx`。
3. 把 FBX 拖到 Hierarchy 的空白位置。
4. Hierarchy 会出现一个新的模型根物体。
5. 单击这个最上层根物体。不要选它内部的某个零件。

模型放置位置、旋转和整体缩放可以按场景需要调整。配置器记录的是零件相对模型父物体的数据。

## 四、挂载一键配置脚本

保持模型根物体被选中：

1. 在 Inspector 最下方点击 `Add Component`。
2. 搜索 `AssemblyConfigurator`。
3. 点击搜索结果添加组件。
4. 确认 Inspector 中出现“拆卸数据、撤回按钮、实验条件、自由抓取、高亮、方向箭头”几组设置。

脚本必须挂在完整 FBX 根物体上。挂在某个小零件上会导致索引父物体解析错误。

## 五、关联 TXT 和撤回按钮

### 最简单的自动方式

保持以下设置：

- `Plan Asset`：留空。
- `Find Plan Next To Model`：开启。
- `Undo Button`：留空。

配置器会自动：

- 根据 FBX 路径找到同目录唯一的 `.txt`；
- 把同目录示意 PNG 填入 `Reference Image`；
- 在场景中找到名为 `BigRedButton` 的按钮；
- 绑定它内部的 `InteractableUnityEventWrapper`。

### 自动查找失败时

TXT 失败：把模型目录中的 txt 直接拖到 `Plan Asset`。

示意图失败：不影响运行。需要在 Inspector 核对时，可把正确 PNG 拖到 `Reference Image`。

按钮失败：

1. 在 Hierarchy 展开 `BigRedButton`。
2. 找到挂有 `InteractableUnityEventWrapper` 的按钮对象。
3. 把这个对象拖到 `Undo Button` 槽位。

一个场景建议只放一个当前实验装配体。切换模型重新配置时，同一个红色按钮会自动解除旧装配控制器绑定，再绑定新模型。

## 六、选择实验条件

在 `Condition` 下拉框中三选一，然后再点击一键配置。

### NoGuidance

- 不持续高亮任何拆卸顺序。
- 不显示箭头。
- 手或手柄靠近任意零件时仍显示青色交互反馈。
- 零件交互完成后，再次靠近时显示橙色完成态反馈。

这就是“没有实验指引”，不是关闭基础交互反馈。

### CurrentPartHighlight

- TXT 当前 `round` 的未完成零件持续显示绿色顺序高亮。
- 不显示方向箭头。
- 手靠近高亮零件时，绿色临时切换为青色交互反馈；移开后恢复绿色。
- 当前 round 全部交互完成后，高亮推进到下一 round。

### CurrentPartHighlightAndDirection

- 包含 `CurrentPartHighlight` 的全部行为。
- 当前 round 的每个未完成零件额外显示黄色 Shapes 三维箭头。
- 箭头方向来自 txt，并且相对于模型索引父物体。

注意：三种条件下，所有 txt 零件始终都可以抓取。TXT round 只决定绿色高亮和黄色箭头，不会锁住未来零件。

## 七、建议先保持的默认参数

第一次配置不要改太多：

| 参数                         | 建议值         | 说明                                        |
| ---------------------------- | -------------- | ------------------------------------------- |
| Disable Physical Collisions  | 开启           | Collider 作为 Trigger，不会互相卡住。       |
| Collider Mode                | `ConvexMesh` | 抓取轮廓较准确；性能不足再换`BoxBounds`。 |
| Minimum Operation Distance   | `0.005`      | 位移达到该值才算一次有效操作。              |
| Minimum Operation Angle      | `2`          | 旋转达到 2 度也算有效操作。                 |
| Contact Outline Color        | 青色           | 手靠近/抓取反馈。                           |
| Guidance Outline Color       | 绿色           | 当前拆卸 round 指引。                       |
| Completed Part Outline Color | 橙色           | 已交互零件再次靠近的反馈。                  |
| See Through Intensity        | `0.8`        | 被外壳遮挡时的透视亮度。                    |
| See Through Tint Alpha       | `0.35`       | 被遮挡零件的半透明填充强度。                |
| See Through Border           | `0.8`        | 被遮挡轮廓的边框强度。                      |
| See Through Border Width     | `0.45`       | 被遮挡轮廓的边框宽度。                      |
| Direction Arrow Color        | 黄色           | 方向箭头。                                  |

箭头尺寸会根据每个零件 Renderer 的世界包围盒自动计算。模型特别大或特别小时，再调整箭头倍率、最小长度和粗细比例。

## 八、点击一键配置

1. 确认没有进入 Play Mode。
2. 在 `AssemblyConfigurator` 组件中点击“**一键配置拆卸零件**”。
3. 等待 Console 输出完成信息。
4. 成功日志应包含：
   - 找到的索引父物体；
   - 可动零件数量；
   - 每个 `part[n] -> child[n-1]` 映射；
   - round 和提示方向；
   - Undo 按钮已绑定。
5. 配置器默认会保存场景。

一键配置会为 txt 中的每个零件添加：

- Trigger Collider；
- Kinematic Rigidbody；
- Meta Grabbable、手柄 Grab、手部 HandGrab；
- `GrabFreeTransformer`；
- Highlight Plus；
- Shapes 方向箭头组件；
- 模型根物体下的 `__DreamVR_DirectionVisuals`，其中每个箭头都有 `Shapes.Line` 箭杆和 `Shapes.Cone` 箭头；
- `AssemblyPart`。

txt 未列出的直接子物体保持固定。

## 九、运行和检查

点击 Unity 顶部 Play：

1. 用手柄靠近任意零件，确认出现青色描边。
2. 用手部靠近任意零件，再确认一次。
3. 抓住任意零件，尝试上下左右移动和旋转。
4. 确认没有单轴限制，也不会被其他零件碰撞卡住。
5. 松手，确认零件停在当前位置，不受重力、不被抛出。
6. 再靠近这个已交互零件，确认描边变为橙色。
7. 按红色 `Undo`，确认只撤回最近一次有效操作。
8. 用其他零件或外壳挡住目标，确认高亮仍能以当前状态颜色透视显示。

条件额外检查：

- `NoGuidance`：没有持续绿色高亮，也没有箭头。
- `CurrentPartHighlight`：只有当前 round 持续绿色高亮，没有箭头。
- `CurrentPartHighlightAndDirection`：当前 round 同时有绿色高亮和黄色箭头。

可以故意提前抓取未来 round 的零件：它应正常交互并变为已完成，但当前绿色/箭头指引不能立即跳走。完成当前 round 后，系统才推进；已经提前全部完成的 round 会自动跳过。

## 十、切换到下一个模型

1. 退出 Play Mode。
2. 删除或禁用当前装配模型根物体。
3. 从另一个 Easy/Medium/Hard 文件夹拖入 FBX。
4. 给新根物体添加 `AssemblyConfigurator`。
5. 选择实验条件。
6. 点击一键配置。
7. Play。

不要把旧模型根物体上的 `AssemblyConfigurator` 拖给新模型。每个模型实例都应在自己的根物体上挂一个配置器。

## 十一、TXT 写法

序号从 1 开始：

```text
round1: (1, -Z), (7, +Z)
round2: (2, -Y), (5, +Y)
```

支持复合方向：

```text
round1: (1, +Y-Z), (2, -X, +Z)
```

两种复合写法含义相同。方向向量会自动归一化。不要在同一条目重复同一轴，例如 `+X-X` 会报错。

TXT 中的序号不是 Unity 显示的 `child[n]`：

```text
txt part[1] = Unity child[0]
txt part[2] = Unity child[1]
```

## 十二、常见错误

### 找不到 txt

- 检查 FBX 和 txt 是否在同一专用文件夹。
- 检查该文件夹是否有多个 txt。
- 直接把正确 txt 拖到 `Plan Asset`。

### 序号越界

TXT 写了模型不存在的零件序号。检查示意图、txt 和 FBX 是否属于同一模型，并确认序号从 1 开始。

### 一键配置找不到按钮

把 `BigRedButton` 中带 `InteractableUnityEventWrapper` 的对象拖到 `Undo Button`。

### 未来 round 也能抓

这是新设计，不是错误。所有零件始终可交互，round 只控制视觉指引。

### 手靠近后绿色变青色

这是正确的视觉优先级：青色是当前真实交互反馈，绿色是拆卸顺序指引。移开手后应恢复绿色。

### 被外壳遮住后仍看不到高亮

- 确认已经重新点击一键配置，让零件的 `HighlightEffect` 使用 `See Through = When Highlighted`。
- 适当提高 `See Through Intensity`、`See Through Tint Alpha` 或 `See Through Border`。
- `NoGuidance` 不会主动显示零件；只有手/手柄实际 Hover 或 Select 时才出现透视交互反馈。

### 没有箭头

- 确认条件是 `CurrentPartHighlightAndDirection`。
- 确认已经重新点击一键配置，让零件添加 `AssemblyDirectionIndicator`，并创建 `__DreamVR_DirectionVisuals`。
- 展开 `__DreamVR_DirectionVisuals`，确认当前 round 对应的 `Shapes Shaft` 和 `Shapes Head` 分别挂有 `Line`、`Cone` 组件。
- 当前 round 已全部完成时不会再显示该 round 的箭头。

### 箭头太大或太小

调整配置器的：

- `Direction Arrow Length Multiplier`；
- `Direction Arrow Minimum Length`；
- `Direction Arrow Thickness Ratio`；
- `Direction Arrow Head Length/Radius Ratio`。

修改后需要再次点击一键配置，才能批量写入所有零件。

### 配置后手动改参数会不会被运行时覆盖

不会。运行时不解析 txt、不运行 Validator、不自动重建组件，也不在 `OnEnable` 重写 Collider。只有再次点击一键配置，才会重新应用配置器中的默认值。

## 十三、正式实验前检查

- 保存场景。
- 确认只保留一个当前实验装配体。
- 确认条件选择正确。
- 手部和手柄都测试一次。
- 三种颜色含义没有混淆。
- 后两种条件的 round 推进正确。
- 第三种条件的复合方向箭头正确。
- Undo 连续按多次时严格按后进先出恢复。
- 最后在目标 Meta 头显上验证真实触达范围和性能。
