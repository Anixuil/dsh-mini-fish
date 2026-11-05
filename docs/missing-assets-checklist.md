# 行为素材补齐清单（M3 当前实现对照）

> 本文是素材制作阶段的内部工作记录：引用的 `art-source/`、`assets/` 源文件（母版分层、补充包 ZIP、三视图）体积太大，**不在本仓库内**，因此相关条目只保留文字引用。

> **v2 返工记录（2026-11-05）**：用户明确以已确认母版为唯一外观基准。新版表情锁定原头发/脸轮廓/服装，结构改为原像素拆分；动作中必需的新绘区域单独说明。见 v2 说明 和 母版对照图。v2 为返工交付，未记录用户外观通过或运行时验收；v1 的未通过结论继续保留。

更新时间：2026-11-05
规范基准：`docs/dsh-pet-feasibility.md`（DPF v1）· 画布 1024×1328 · 全画布透明 PNG · 交付 PSD

> **2026-11-05 返工状态：v1 未通过外观一致性验收。** 用户指出补充包与已确认母版相差明显，包括刘海、脸型、服装及尾巴的变化。此前“素材完成/通过”的判断撤回，v1 不可作为母版一致的正式接入依据。v1 制作说明 与 v1 ZIP 仅保留作历史记录，详见 未通过说明。现以用户明确选定的 `face-remake-v1/master.png` 为唯一外观基准制作 v2，保留母版可见像素，只改动作所需区域。下文仍作为待完成需求清单；返工产物及运行时接入须分别说明，不能以文件数量代替外观验收。

> 说明：当前运行时已经能跑「呼吸/眨眼/眼嘴切换/弹簧/拖拽/散步/窗口趴台/任务联动/余额」，
> 但其中不少动作是**用现有素材+程序化变形硬撑**的。下面按行为列出缺口，交付后我接进运行时。

---

## 0. 已有素材（不需要重做）

`tail` `hair_back` `arm_l/r` `body` `skirt` `apron` `ear_l/r` `head` `brow` `eyes_{open,half,closed}`
`blush` `mouth_{smile,open,surprised}` `hair_side_l/r` `hair_front` `ahoge` `headdress` `bow_r`

---

## 1. 按行为对照缺口

| 当前行为 | 现在的实现 | 缺的素材 | 优先级 |
|---|---|---|---|
| **待机 / 眨眼** | 3 种眼态 + 程序化呼吸 | 闭眼微笑眼 `eyes_happy`、困倦眼 `eyes_sleepy` | P0 |
| **睡觉**（当前已关闭） | 闭眼 + 文字 zzz | 睡觉嘴 `mouth_sleep`、睡姿差分 `pose_sleep`、zzz 图标 | P2（关闭中，低） |
| **情绪（委屈/生气/开心）** | 只有 3 眼 3 嘴，靠组合 | 眉差分、嘴差分、眼差分（见下表） | **P0** |
| **点击害羞 / 双击庆祝** | 心跳+星星文字 + 腮红透明度 | 害羞表情组合、爱心/星星 PNG 粒子 | P0 |
| **拖拽被领着走** | 身体倾斜 + 手臂旋转 + 尾巴甩 | 举手/张开手臂差分、悬空腿差分 | P1 |
| **自主散步** | 整体上下颠簸 + 左右翻转 | **腿/鞋分层**（`leg_l` `leg_r` `shoe_l` `shoe_r`）、4 帧步态 | **P0** |
| **窗口趴台** | 站在窗口右上角，无坐姿 | 坐姿差分 `sit_pose`（腿垂下）、探头 `peek` | P1 |
| **任务开始/进行/完成** | 台词 + 星星文字 | 键盘/便签道具、勾选/完成图标、专注表情 | P1 |
| **余额徽标** | 纯文字 `¥118.42` | 可选：钱包/¥ 小图标 | P2 |
| **特效** | 用文字字符 `♥ ✦ z` 代替 | 正式粒子 PNG：爱心/星星/水滴/音符/气泡/汗滴/生气符号 | P0 |

---

## 2. 表情差分（任务联动与日常最关键）

现有：`eyes_open/half/closed`、`mouth_smile/open/surprised`、`brow` 单张、`blush` 单张。

### 2.1 眼睛（同画布，只画眼部，替换 `eyes` 变体）

| 文件 | 内容 | 用途 |
|---|---|---|
| `eyes_happy.png` | 闭眼弯月笑（^^） | 开心、被摸头、任务完成 |
| `eyes_sleepy.png` | 半睁困倦眼 | 打哈欠、长时间无操作 |
| `eyes_sad.png` | 下垂眼/含泪 | 报错、委屈 |
| `eyes_angry.png` | 皱眉眼 | 失败、被戳烦 |
| `eyes_heart.png` | 爱心眼 | 害羞/比心 |
| `eyes_star.png` | 星星眼 | 庆祝/期待 |

### 2.2 眉毛

`brow_happy.png` `brow_sad.png` `brow_angry.png` `brow_surprised.png` `brow_determined.png`

### 2.3 嘴

| 文件 | 内容 |
|---|---|
| `mouth_sad.png` | 下弯嘴/瘪嘴 |
| `mouth_pout.png` | 嘟嘴 |
| `mouth_sleep.png` | 睡觉小圆嘴 |
| `mouth_talk_1..4.png` | 说话四帧口型（闭/半开/开/合） |
| `mouth_eat.png` | 吃东西（喂白饭彩蛋） |

### 2.4 腮红

`blush_soft.png`（默认）、`blush_deep.png`（害羞）、`blush_lines.png`（斜线害羞纹）

---

## 3. 走路（自主散步）需要腿部分层

现在 `body.png` 里包含腿和鞋，所以散步只能整体颠。要真正「走」起来，需要把身体拆开：

```
body.png        躯干 + 裙上身（不含腿/鞋）
leg_l.png       左腿（含袜子，补全被裙摆挡住的大腿）
leg_r.png       右腿
shoe_l.png      左鞋
shoe_r.png      右鞋
```

- 枢轴：`leg_*` 在髋部（约 x=0.40/0.60, y=0.80），`shoe_*` 在脚踝
- 可选进阶：`walk_1..4.png` 四帧整腿步态（左右腿交替），比单层旋转更自然
- 交付后我加「腿部摆动 + 步态切换 + 身体重心前倾」，散步才不像飘

---

## 4. 互动 & 窗台

| 文件 | 内容 | 用途 |
|---|---|---|
| `arm_l_up.png` / `arm_r_up.png` | 手臂上举 | 被拖起来、打招呼 |
| `arm_l_shy.png` / `arm_r_shy.png` | 手放胸前/捂脸 | 害羞 |
| `hand_heart.png` | 比心手势（可选独立层） | 点击互动 |
| `sit_pose.png` | 坐姿整图差分（腿垂下、手扶边） | 窗口趴台时坐着 |
| `peek_pose.png` | 半身探头 | 窗口边缘探头 |

> 坐姿/探头用「整图姿态差分」也可以：我按状态切换整张 pose 层，不改骨骼。

---

## 5. 道具与特效 PNG

### 5.1 粒子（透明背景，64–128px，越简洁越好）

`fx_heart.png` `fx_star.png` `fx_sparkle.png` `fx_water.png` `fx_note.png` `fx_zzz.png`
`fx_sweat.png` `fx_tear.png` `fx_anger.png` `fx_question.png` `fx_exclaim.png`

> 现在这些是文字字符，换成 PNG 后观感会明显提升；每种 1–3 帧可选。

### 5.2 任务道具

`prop_keyboard.png`（小键盘）、`prop_note.png`（便签/清单）、`prop_check.png`（勾选）、
`prop_coffee.png` / `prop_rice.png`（喂白饭彩蛋）

---

## 6. 分层升级（让弹簧更自然，P1）

| 现状 | 建议拆分 | 好处 |
|---|---|---|
| `tail` 整条 | `tail_base` / `tail_mid` / `tail_tip` | 尾巴分段弯曲，甩起来像活的 |
| `hair_back` 整片 | `hair_back_upper` / `hair_back_lower` | 后发惯性更自然 |
| `hair_side_l/r` 单段 | 各拆 2 段 | 侧发跟随头部摆动 |
| `skirt` 整片 | `skirt_front` / `skirt_back`（或左/右片） | 走路/转身时裙摆分离 |
| `arm` 整条 | `upper_arm` / `forearm` / `hand` | 挥手、托腮、比心 |

> 拆分原则：每段独立 PNG、同画布、枢轴在关节、被遮挡部分补全。

---

## 7. 交付规格（沿用现有规范）

- **1024×1328**，全画布透明 PNG，sRGB
- 所有层共用同一坐标，**不许移动/裁切**
- 被其他层遮住的部分要补全（否则旋转露洞）
- 命名：`eyes_<state>.png`、`mouth_<state>.png`、`brow_<state>.png`、
  `arm_<side>_<pose>.png`、`leg_<side>.png`、`fx_<name>.png`、`prop_<name>.png`
- 最好交付 **PSD 分层源文件**；图层名后可用 `@pivot: x,y` 标注枢轴
- 表情类只画「眼睛/眉/嘴/腮红」区域，不要带脸和头发

---

## 8. 建议交付顺序（最小可用包）

### P0（先交这些，观感立刻上一个台阶）
1. `eyes_happy` / `eyes_sad` / `eyes_angry` / `eyes_sleepy`
2. `brow_happy` / `brow_sad` / `brow_angry`
3. `mouth_sad` / `mouth_pout` / `mouth_talk_1..4`
4. `fx_heart` / `fx_star` / `fx_sparkle` / `fx_tear` / `fx_sweat`
5. 走路：`body` 拆出 `leg_l` `leg_r` `shoe_l` `shoe_r`
6. `sit_pose`（窗口趴台坐姿）

### P1
- `eyes_heart` `eyes_star`、`blush_deep` `blush_lines`
- `arm_*_up`、`arm_*_shy`、`hand_heart`
- 尾巴 3 段、后发/侧发分段
- 键盘/便签/勾选道具

### P2
- 睡姿、探头、手扶、3/4 侧身/背面、服装差分、余额图标

---

## 9. 交付后我会做的接入

1. 把新图放入 `pets/whale-maid-l1/parts/`
2. `pet.json` 增加对应 `variants`（eyes/mouth/brow/blush）
3. 新增行为映射：
   - `eyes_happy`+`brow_happy`+`mouth_smile` → 开心/完成
   - `eyes_sad`+`brow_sad`+`mouth_sad`+`fx_tear` → 委屈
   - `eyes_angry`+`brow_angry`+`mouth_pout`+`fx_anger` → 生气
   - 走路切腿层/步态，趴台切 `sit_pose`
4. 实机截图验收，再更新文档

---

## 10. 优先级判断说明

- **P0 不做会明显「假」**：表情只有 3 眼 3 嘴，任务失败/开心/生气看起来一样；走路没有腿像飘；特效是文字符号。
- **P1 是「更好看」**：坐姿、分段尾巴/头发、道具。
- **P2 是「更丰富」**：睡姿（当前睡觉已关闭）、探头、侧身、服装。
