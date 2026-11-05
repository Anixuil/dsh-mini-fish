# DPF v1 · 桌宠宠物包规范（DSH Pet Format）

一个宠物包 = 一个目录，里面是 `pet.json`（manifest）+ 全画布透明 PNG 图层。宿主不解释像素内容，只按 manifest 组装：哪个层在什么 `z`、绕哪个 `pivot` 转、跟不跟随父层、什么时候显示。只要符合本规范，任何画风都能直接跑。

- 规范版本：`"spec": "dsh-pet/1"`
- 参考实现：`pets/whale-maid-l1`（39 层，7.09 MB，含眼组、父子骨骼、整图姿态）
- 最小实现：`pets/whale-maid`（单层 `body.png`）
- 起手模板：`pets/_template-l1`（manifest + `canvas-guide.png` + 图层交付 `CHECKLIST.md`，没有图片；复制后补 `parts/`）
- 校验：`python tools/check-pet-pack.py <pack>`（几何/图层/姿态）和 `node dev/smoke.mjs`（仓库自检，含 8 MB 上限）

## 1. 目录

```text
<pet-id>/
├── pet.json          # 必需
├── parts/            # 图层与姿态图（文件名随意，pet.json 里写相对路径）
├── masks/            # 眼组遮罩（可选）
├── LICENSE           # 建议：素材授权
└── README.md         # 建议：来源、版本、改动记录
```

限制：

- 包体 **≤ 8 MB**（DPF v1；`pets/whale-maid-l1` 用掉 7.09 MB，主要花在整图姿态上）
- 图片格式：**PNG（RGBA）**，统一画布尺寸；不支持 JPG（无透明）、不支持不同画布的层
- 图层与姿态都是**全画布**透明图：角色外的像素透明，不要裁成小图再在 manifest 里给 offset（v1 没有 offset 字段）

## 2. 顶层字段

```jsonc
{
  "spec": "dsh-pet/1",
  "id": "whale-maid-l1",                       // 目录名，也是配置里的 petId
  "name": { "zh": "蓝鲸女仆·小蓝", "en": "Lapis the Whale Maid" },
  "version": "0.9.0",
  "author": "dsh-mini-fish",
  "license": "CC-BY-NC-4.0",
  "canvas": { "width": 1024, "height": 1328, "anchor": [0.5, 1.0], "baseScale": 0.24 },
  "idle":   { "breath": 0.010, "sway": 0.7, "bob": 2.2 },
  "layers": [ /* … */ ],
  "eyesRig": { /* 可选：虹膜眼组 */ },
  "poses":  { /* 可选：整图姿态 */ }
}
```

| 字段 | 必需 | 说明 |
| --- | --- | --- |
| `spec` | 是 | 必须是 `dsh-pet/1`，其他值宿主直接报错 |
| `id` | 是 | 包 id；配置 `petId` 指向它 |
| `name` | 建议 | `{ "zh": …, "en": … }`，右键菜单与日志里显示 |
| `version` / `author` / `license` | 建议 | 面板与文档用 |
| `canvas.width` / `height` | 是 | 母版像素尺寸；**所有**图层/姿态图都必须是这个尺寸 |
| `canvas.anchor` | 否 | `[x, y]`（0–1），身体旋转/缩放的锚点，默认 `[0.5, 1.0]`（脚底中心） |
| `canvas.baseScale` | 否 | 绘制缩放：显示尺寸 = `canvas × baseScale × 用户缩放 × config.scale`，默认 `1.0` |
| `idle.breath` | 否 | 呼吸缩放幅度（比例），默认 `0.010` |
| `idle.sway` | 否 | 摇摆角幅度（度），默认 `0.7` |
| `idle.bob` | 否 | 上下浮沉幅度（DIP），默认 `2.2` |
| `idle.speed` | 否 | 保留字段，v1 未参与计算 |
| `gaze` | 否 | 旧整眼变形用的 `centers/radius/shift`，v1 的 `eyesRig` 方案不需要 |

## 3. layers

按 `z` 升序绘制（支持小数，`140` 和 `140.1` 就是同组内的前后）。示例：

```jsonc
{ "id": "tail_base", "z": 0,   "pivot": [0.5957, 0.7304], "image": "parts/tail_base.png", "spring": "tail" },
{ "id": "tail_mid",  "z": 0.1, "pivot": [0.7813, 0.7530], "image": "parts/tail_mid.png",  "spring": "tail", "parent": "tail_base" },
{ "id": "head",      "z": 90,  "pivot": [0.4815, 0.3765], "image": "parts/head.png",
  "variants": { "default": "parts/head.png", "brow_ready": "parts/head_brow_ready.png" } },
{ "id": "gait",      "z": 37.5, "hidden": true, "pivot": [0.485, 0.6777], "image": "parts/walk_1.png",
  "variants": { "walk_1": "parts/walk_1.png", "walk_2": "parts/walk_2.png",
                "walk_3": "parts/walk_3.png", "walk_4": "parts/walk_4.png" } }
```

| 字段 | 必需 | 说明 |
| --- | --- | --- |
| `id` | 是 | 层 id；运行时按部分 id 驱动动作（见 §4），其余自由命名 |
| `image` | 是 | 相对包目录的 PNG 路径（全画布） |
| `z` | 是 | 绘制顺序；相等时保持 manifest 顺序 |
| `pivot` | 否 | `[x, y]` 归一化（0–1）旋转/缩放枢轴，默认 `[0.5, 1.0]` |
| `spring` | 否 | 弹簧组名，见 §5；不给就没有惯性摆动 |
| `parent` | 否 | 父层 id：本层先绕自身 pivot 旋转，再叠加父层的累积矩阵（分段骨骼） |
| `hidden` | 否 | `true` = 默认不显示，也**不解码**；运行时用到才加载、才解码（动作层） |
| `retired` | 否 | `true` = 已退役，永不加载、永不显示（留档旧层用） |
| `variants` | 否 | `{ 变体名: 图片路径 }`，运行时按表情/状态切换；未切换时用 `image` |

`hidden` / `retired` 的区别：`hidden` 是「按需动作层」（走路步态、举手、害羞手、悬空腿、动作后发）；`retired` 是「别加载」（旧眼层、旧单层手臂/尾巴），用来把老素材留在包里但不占内存。

## 4. 运行时按 id 驱动的层（约定）

这些 id 不是强制的，但只要存在，宿主就会在对应状态下接管显示：

| 层 id | 何时显示 | 备注 |
| --- | --- | --- |
| `gait` | 走路 | 必须提供 `variants.walk_1..4`，按 8 rad/s 与颠簸同频切帧；`leg_l/leg_r/shoe_l/shoe_r` 同时隐藏 |
| `legs_air` | 被拖起来 | 悬空腿，配合 `arm_*_up` |
| `arm_l_up` / `arm_r_up` | 被拖起来 | 举手；默认手臂（`upper_arm/forearm/hand`）隐藏 |
| `arm_l_shy` / `arm_r_shy` | 被戳（害羞） | 双手合十 |
| `hair_back_action` | 拖拽 / 害羞 | 动作版后发（修掉旧袖口残片）；`hair_back_upper/lower` 同时隐藏 |

其余层（`body/skirt/apron/head/brow/blush/mouth/hair_*/ear_*/ahoge/headdress/bow_*`、尾巴/腿/鞋）就按 `z` 常驻显示，靠弹簧和表情变体出动画。

## 5. 弹簧组

`spring` 取下面六个名字之一，参数写死在宿主里（可按需调，见 `ConfigureSpring`）：

| 组 | 刚度 | 阻尼 | 角度限幅 | 典型层 |
| --- | --- | --- | --- | --- |
| `tail` | 55 | 7.5 | ±40° | 尾巴各段 |
| `ear` | 120 | 9 | ±25° | 耳鳍 |
| `hair` | 70 | 9 | ±20° | 后发/侧发/刘海 |
| `ahoge` | 160 | 8 | ±30° | 呆毛 |
| `skirt` | 90 | 11 | ±12° | 裙摆/围裙 |
| `arm` | 110 | 10 | ±60° | 手臂各段 |
| （其他 / 缺省） | 90 | 10 | ±30° | — |

弹簧目标 = 当前动作（走路颠簸、拖拽速度、呼吸摇摆）叠加后按组算出的角度；父子链上每段再加一点相位滞后，就形成波浪传导。

## 6. 表情变体约定

宿主按状态选择变体名；缺哪个变体就退回该层的 `image`，所以最小包只给 `image` 也能跑。

| 层 id | 变体名 | 说明 |
| --- | --- | --- |
| `head` | `default` / `brow_ready` | 有眉毛的预设用 `brow_ready`（额头预留），否则 `default` |
| `brow` | `happy` / `sad` / `angry` / `determined` / `surprised` | 眉毛 |
| `blush` | `soft` / `deep` / `lines` | 腮红三档（透明度由运行时再调） |
| `mouth` | `smile` / `open` / `surprised` / `sad` / `pout` / `sleep` / `talk_1..4` / `eat` | 口型；说话时 4 帧循环 |
| `eyes`（旧单层方案） | `open` / `half` / `closed` … | 有 `eyesRig` 时不需要 |

表情预设 → 变体映射（写死在 `ApplyFacePreset`，可作画层时的参考）：

| 预设 | head | brow | blush | mouth | 眼组 |
| --- | --- | --- | --- | --- | --- |
| `neutral` | default | — | soft | smile | open / normal |
| `happy` | default | happy | soft | smile | happy |
| `celebrate` | brow_ready | surprised | lines | smile | open / star |
| `sad` | brow_ready | sad | soft | sad | sad |
| `angry` | brow_ready | angry | soft | pout | angry |
| `shy` | default | — | deep | talk_4 | open / heart |
| `sleepy` | default | — | soft | sleep | sleepy |
| `focused` | brow_ready | determined | soft | smile | open / normal |
| `alert` | brow_ready | surprised | soft | surprised | open / normal |

## 7. eyesRig（虹膜眼组）

把眼睛拆成「固定的眼白+眼线」和「可平移的虹膜/瞳孔/高光」，运行时只移动后者，再按眼眶遮罩裁切——所以眼球不会漂出眼眶，也不会露出皮肤。

```jsonc
"eyesRig": {
  "limit": [12, 6],                                   // 移动组最大平移（源 px，1024 画布）
  "states": {
    "open":   { "static": "parts/eye_static_%s_open.png",   "mask": "masks/eye_mask_%s_open.png" },
    "half":   { "static": "parts/eye_static_%s_half.png",   "mask": "masks/eye_mask_%s_half.png" },
    "sleepy": { "static": "parts/eye_static_%s_sleepy.png", "mask": "masks/eye_mask_%s_sleepy.png" },
    "sad":    { "static": "parts/eye_static_%s_sad.png",    "mask": "masks/eye_mask_%s_sad.png" },
    "angry":  { "static": "parts/eye_static_%s_angry.png",  "mask": "masks/eye_mask_%s_angry.png" },
    "closed": { "static": "parts/eye_static_%s_closed.png" },
    "happy":  { "static": "parts/eye_static_%s_happy.png" }
  },
  "moving": { "image": "parts/eye_moving_%s%s.png" },
  "pupils": { "normal": "", "heart": "_heart", "star": "_star" }
}
```

- 路径里的 `%s`：`static` 一个 `%s` = 左/右（`l` / `r`）；`moving` 两个 `%s` = 侧别 + 瞳孔后缀
- `states.<名字>.open/closed/...` 是眼态；`mask` 缺省时不裁切（会有漂出眼眶的风险，建议都给）
- `pupils.<名字>` 是 `moving` 的瞳孔后缀（`normal` 通常为空串）
- 镜像（面朝左）时平移量乘 `-1`；眨眼的半闭/闭合通过状态切换实现

没有 `eyesRig` 的包可以只用一个常驻 `eyes` 层（旧方案，不跟随鼠标）。

## 8. poses（整图姿态）

整图姿态是「一帧画好的整体姿态」：进入姿态时**隐藏全部角色层**，只显示这张图。适合坐、趴、睡这类拆层很难表达的姿势。

```jsonc
"poses": {
  "sit":  { "image": "parts/sit_pose.png",  "ledgeY": 960 },
  "peek": { "image": "parts/peek_pose.png", "ledgeY": 975 }
}
```

| 姿态 id | 宿主行为 |
| --- | --- |
| `sit` | 前台窗口**稳定 5 秒**且她已趴稳后，自动坐上去：`ledgeY` 那条线（画布像素 y）对齐窗口**上沿**，腿垂在窗口前；窗口一动就起身，等窗口再稳 5 秒重新坐 |
| `peek` | 没有窗台可选、长时间没人理时，溜到工作区**右下角**趴着：`ledgeY` 对齐**底边**，只露上半身；全屏/最大化窗口时也用它（右下角常驻趴）；被拖出来、或前台窗口变化即起身 |

`ledgeY` 是画布像素坐标（不是归一化值），宿主按 `canvas.width` 换算成窗口单位。姿态图同样必须是**全画布**尺寸；`sit`/`peek` 缺哪个，对应行为就自动跳过（不会报错）。

## 9. 校验清单

1. `pet.json` 能被 JSON 解析，`spec` 是 `dsh-pet/1`
2. 所有 `image` / `variants` / `poses.image` 指向的文件存在，且尺寸都等于 `canvas`
3. `parent` 指向存在的层 id，且不成环
4. `z` 顺序符合预期（眼/眉/嘴在 head 之上，后发在身体之下……）
5. 图层外像素确实透明（别把背景烘进图里）
6. 包体 ≤ 8 MB
7. 跑 `python tools/check-pet-pack.py <pack>` 与 `node dev/smoke.mjs` 都通过

## 10. 最小可用包

```jsonc
{
  "spec": "dsh-pet/1",
  "id": "my-pet",
  "name": { "zh": "我的宠物", "en": "My Pet" },
  "canvas": { "width": 512, "height": 720, "baseScale": 0.6 },
  "layers": [
    { "id": "body", "z": 0, "pivot": [0.5, 1.0], "image": "parts/body.png" }
  ]
}
```

一个层也能跑：有呼吸摇摆、可拖拽、可跟随前台窗口（只是没有表情和步态）。想加动作就从 §3 的字段开始逐项叠。
