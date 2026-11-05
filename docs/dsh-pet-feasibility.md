# DSH 桌宠插件 · 方案与规范 v0.2

> 目标：DSH 桌面端启动时，一只高帧率、可交互、动作丰富的「蓝鲸女仆」桌宠随之出现；
> **关掉 DSH 主窗口后它依然留在桌面上**；同时沉淀一套**宠物包规范（DSH Pet Format, DPF）**，
> 让其他作者也能制作自己的桌宠。

状态：**M3 行为层已实现并在本机验证**（Windows / DSH Desktop 0.2.1-rc.5；含 Phase 1–3 素材接入）。
本文档同时是「方案 + 规范 + M1 验收记录」。

![M1 桌面效果](images/pet-preview.png)

---

## 0. 结论速览

需求在实现过程中从「窗口内浮层」升级为「**独立置顶透明窗口，关掉 DSH 主窗口后仍显示**」。
这推翻了初版浮层方案，最终架构是：

> **DSH Host 插件管生命周期 + 独立 C# WPF 进程管窗口与渲染 + 文件握手（pid / 心跳 / 状态）。**

已验证成立的关键事实：

| 能力 | 结论 | 证据 |
|---|---|---|
| 透明、无边框、置顶窗口 | ✅ WPF `AllowsTransparency + Topmost` 显示在其他应用（企业微信/IDE/DSH）之上 | 截图 |
| 关窗后桌宠仍在 | ✅ 桌面端「关闭窗口」= **隐藏到托盘**（进程继续运行），桌宠进程与窗口解耦 | 主进程源码 `window.hide()`、「Running tasks will continue… reopen from the system tray」 |
| 逐像素点击穿透 | ✅ 透明像素 `WM_NCHITTEST → HTTRANSPARENT`，宠物身体 `HTCLIENT` | 命中测试：`(2590,1300)→-1`、`(2827,1450)→1` |
| 逐帧高帧率 | ✅ `CompositionTarget.Rendering`（跟随显示器 vsync），仅更新矩阵变换，开销极低 | 运行观察 |
| 随应用退出 | ✅ 插件 dispose 杀子进程 + 伙伴进程「父进程看门狗」2s 兜底 | 代码路径 |
| 其他作者做宠物 | ✅ L0 只需一张透明 PNG + `pet.json` | 参考宠物包 |

---

## 1. 架构

```
┌─ DSH 桌面端（Electron）──────────────────────────────────────────────┐
│  主窗口（可隐藏到托盘）        桌面托盘                              │
│        │                                                            │
│        ▼  profile 启动即加载                                        │
│  Host 插件 @local/dsh-pet（Node / Cordis）                          │
│   ├─ 启动时 ensureCompanion()：exe 缺失则用系统 csc 自动编译          │
│   ├─ 拉起 PetHost.exe（--pet 包 --state 状态 --parent-pid 宿主）      │
│   ├─ 10s 看门狗：进程死了就重启（尊重「用户主动关闭」标记）            │
│   ├─ pid / spawn 日志 / 配置；插件禁用或应用退出时杀进程              │
│   └─ 后续：DSH 事件 → 宠物行为信号（M2）                             │
└───────────────┬──────────────────────────────────────────────────────┘
                │ spawn + 文件握手
┌───────────────▼──────────────────────────────────────────────────────┐
│  PetHost.exe（C# WPF，独立进程，不随窗口关闭而退出）                  │
│   ├─ 透明/无边框/置顶/不在任务栏/不抢焦点                             │
│   ├─ 加载宠物包：pet.json → 分层 PNG → 逐帧合成                       │
│   ├─ 程序化动画：呼吸/重心/悬浮/视线跟随 + 点击弹跳/双击转圈/拖拽倾斜  │
│   ├─ 逐像素命中测试：透明处穿透，身体处可交互                         │
│   ├─ 右键菜单 / 全局热键（Ctrl+Alt+P 穿透、Ctrl+Alt+Q 退出）           │
│   ├─ 状态持久化：位置/大小/穿透/置顶；用户退出写 pet.closed 标记        │
│   └─ 父进程看门狗：宿主退出 2s 内自行退出                             │
└──────────────────────────────────────────────────────────────────────┘
```

为什么不是窗口内浮层：浮层随页面/窗口消失，且无法覆盖其他软件。独立窗口是唯一同时满足
「关窗后仍在 + 桌面级置顶」的方案。代价是平台相关（Windows 首版）与一套额外进程。

为什么不用 DSH 自己的 Electron 建窗：插件 API 没有窗口服务，Electron 主进程不对插件开放；
WPF 方案零依赖（系统自带 .NET Framework + csc），无需 SDK、无需下载运行时。

---

## 2. 可行性核查（基于本机实测）

| 项 | 结果 |
|---|---|
| 桌面端关窗行为 | 隐藏到托盘，主进程与 Host 继续运行（源码确认），因此桌宠可继续存活 |
| 窗口能力 | 插件侧无窗口 Service（98 个 Host Service 中无 window/tray）；必须自带宿主进程 |
| 编译器 | `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` + WPF 程序集齐全，可离线编译 |
| 显示环境 | 3120×2080 @200% 缩放；DPI 不感知工具会把它看成 1560×1040，验证脚本已做 DPI 感知 |
| 点击穿透 | 需在 `HwndSource` 钩子中把 `WM_NCHITTEST` 标记 `handled=true`，否则 `DefWindowProc` 会返回 HTCLIENT（M1 踩过） |
| 单实例 | 命名互斥量；插件以 pid 文件 + 10s 看门狗避免重复启动 |

**验证工具**（`tools/`）：`screenshot.exe`（DPI 感知截屏）、`nchittest.exe`（命中测试）、
`capture-window.ps1`（PrintWindow）、`extract-asar.mjs`（从 app.asar 取主进程源码）。

---

## 3. DSH Pet Format（DPF）v1 · 规范

设计原则：**纯数据、可校验、可移植、向后兼容、零代码执行**。作者只交付文件，运行时只读数据。

### 3.1 三级规范

| 等级 | 作者需要做 | 运行时提供 | 状态 |
|---|---|---|---|
| **L0 静帧宠物** | 1 张透明底立绘 PNG + `pet.json` | 呼吸、重心摆动、悬浮、视线跟随、点击/拖拽反应、点击穿透 | ✅ M1 已实现 |
| **L1 分层骨骼** | 分层 PNG + 枢轴 + 弹簧参数 + 动作片段 | 尾巴/耳朵/头发弹簧、眨眼、表情差分、状态机 | M2 |
| **L2 完全体** | L1 + 网格变形 / 口型 / 道具 / 粒子 | DSH 事件联动、场景特效 | M3/M4 |

### 3.2 宠物包目录

```
whale-maid/
  pet.json            # 清单
  parts/              # 透明 PNG（M1 必须 PNG；WebP 留给 Web 端渲染器）
    body.png
  anim/               # L1：动作片段（M2）
  behaviors.json      # L1：状态机与反应（M2）
  preview.png         # 512×512 选择器缩略图
  README.md           # 角色设定
  LICENSE             # 必填
```

### 3.3 `pet.json`

```jsonc
{
  "spec": "dsh-pet/1",
  "id": "whale-maid",                 // ^[a-z0-9][a-z0-9-]{1,63}$
  "name": { "zh": "蓝鲸女仆·小蓝", "en": "Lapis, the Whale Maid" },
  "version": "0.1.0",
  "author": "dsh-mini-fish",
  "license": "CC-BY-NC-4.0",          // 必填
  "conformance": "L0",
  "canvas": { "width": 343, "height": 444, "anchor": [0.5, 1.0], "baseScale": 0.72 },
  "layers": [
    { "id": "body", "image": "parts/body.png", "z": 0, "pivot": [0.5, 1.0] }
  ],
  "idle": { "breath": 0.012, "sway": 0.9, "bob": 2.6, "speed": 1.0 }
}
```

- `canvas.width/height` = 立绘像素尺寸；窗口尺寸 = canvas × `baseScale` × 用户缩放。
- `layers` 的每张图都是**画布同尺寸的透明 PNG**，`pivot` 为画布归一化坐标；
  这样任何层都能绕自己的枢轴做变换，命中测试也统一。
- `idle` 是程序化待机参数；L1 会在此基础上叠加弹簧与动作片段。

### 3.4 L1 扩展（M2 实现）

```jsonc
{
  "rig": "rig.json",
  "animations": ["anim/idle.json", "anim/react.json"],
  "behaviors": "behaviors.json"
}
```

`rig.json` 关键字段：`bones[]`（父子与 pivot）、`parts[]`（image/bone/z/pivot/spring）、
`springs[]`（stiffness/damping/gravity/maxAngle）、`blink`、`lookAt`。
动画轨道只允许 `x/y/rotate/scaleX/scaleY/opacity/variant` 与白名单特效名，**不允许脚本**。

运行时（PetHost）实际支持的 `pet.json` 扩展字段（whale-maid-l1 v0.9.0 全量使用）：

| 字段 | 位置 | 含义 |
|---|---|---|
| `spring` | layer | 弹簧组：`tail` / `hair` / `arm` / `ear` / `skirt` / `ahoge` |
| `variants` | layer | 同画布替换图（表情眼态、嘴型、步态帧） |
| `hidden` | layer | 默认不显示；**首次显示时才解码**（步态/举手/悬空腿等动作层） |
| `retired` | layer | 占位层，永不加载、永不显示（旧眼层） |
| `parent` | layer | 父子链：子层先绕自身 `pivot` 旋转，再叠加父层累积矩阵，可做尾巴/头发/手臂的波浪跟随 |
| `eyesRig` | pet | 分层眼组：固定眼白/眼线 + 移动组（虹膜/瞳孔/高光）+ 眼态/瞳孔变体 + `limit` |
| `poses` | pet | 整图姿态（`image` + `ledgeY`）；显示时隐藏全部角色层，`ledgeY` 用于把坐/趴的边沿线对齐到窗台或屏幕边缘 |

### 3.5 校验规则（M3 做进校验器）

| 项 | 规则 |
|---|---|
| 体积 | ≤ 8MB / 包 |
| 图片 | PNG（L0/L1），≤ 4096×4096，透明底 |
| 引用 | 所有 image / anim / bone / part 必须存在 |
| 路径 | 包内相对路径，禁止 `..`、绝对路径、符号链接逃逸 |
| 版本 | `spec` 必须 `dsh-pet/1`；未知字段忽略 |
| 安全 | v1 无 JS/脚本字段；资源解码有上限 |

### 3.6 作者流程

1. **画**：透明底立绘（正面、脚底贴底边、建议 1000px 高）。
2. **配**：复制模板包，改 `pet.json` 的尺寸与身份字段。
3. **验**：重启插件即可看到；M3 提供 `validate` 与 Pet Studio。
4. **发**：打包目录 → M3 支持 `.dshpet` 导入。

---

## 4. 「灵活」怎么实现（M2 动画体系）

灵活感 90% 来自每帧都在变的次要动作，而不是动作数量。

| 层 | 内容 | 状态 |
|---|---|---|
| **程序层** | 呼吸缩放、重心 sway、悬浮、视线跟随、拖拽倾斜、落地缓冲 | M1 已有 |
| **L1 弹簧** | 头发 2–3 段链、鲸尾摆动、耳朵轻颤、呆毛抖动（临界阻尼 + 子步长） | M2 |
| **状态机** | idle / think / work / celebrate / error / sleep / drag；优先级打断 + 120–250ms crossfade | M2 |
| **短动作** | 摸头害羞、戳尾巴、双击转圈、喂白饭彩蛋、喷水 | M2 |
| **特效** | 气泡、水滴、音符、爱心、zzz（WPF 绘制，零依赖） | M2/M4 |

帧率：`CompositionTarget.Rendering` 跟随 vsync；只更新矩阵，不做像素回读。
M2 加入弹簧时用固定子步长积分（120Hz）+ 渲染插值，保证高刷屏稳定。

---

## 5. 与 DSH 的联动（M2）

| DSH 信号 | 宠物反应 |
|---|---|
| `agent/status → running` | 工作循环（敲键盘/尾巴摇快） |
| `agent/assistant-stream` | 抬头 + 说话气泡 |
| `tools/result` | 成功比耶 / 失败挠头 |
| `turn/end`（`agent/turn-stopping`） | 庆祝 + 星星特效 |
| `agent/error` | 委屈垂耳 + 泪滴 |
| `user-questions/request` | 举手提示 |
| `goal/changed` | 目标进度庆祝 |
| `task.start` | 开工台词 + 星星（每个 Agent 回合） |
| `task.progress` | 按工具调用步数报进度（「第 N 步了…」，有冷却） |
| `task.done` | 按「N 步 · N 秒」总结并庆祝 |
| `task.failed` | 失败安慰台词 |
| 空闲 > 5min | 打瞌睡 + zzz |

实现方式（M2 已落地）：Host 订阅事件 → 归一化为一行 JSON → 追加到 `%LOCALAPPDATA%\dsh-pet\signals.jsonl`；
PetHost 每 150ms 增量读取该文件。不走网络监听，避免防火墙弹窗；断连时宠物退回 idle。
`user-questions/request` 是 waterfall 事件，为避免影响问答流程，M2 暂未接入。

---

## 6. M1 / M2 实现与验收记录

### 6.1 交付物

| 文件 | 说明 |
|---|---|
| `index.js` | Host 插件：自动构建、启动、守护、清理桌宠进程；配置项 enabled/petId/petsDir/scale/clickThrough |
| `cordis.patch.yml` / `package.json` / `locale/*` / `icon.svg` | bundle 与显示元数据（已安装为 `@local/dsh-pet`） |
| `companion/src/PetHost.cs` | 约 3.5k 行 C# WPF 宿主：窗口、渲染、分层骨骼/弹簧、姿态与表情、前台窗口感知、动画、交互、命中测试、热键、状态 |
| `companion/bin/PetHost.exe` | 构建产物（缺失时插件自动用系统 csc 编译） |
| `pets/whale-maid/` | 参考宠物包：设定图抠图 → 透明立绘 + pet.json + LICENSE/README |
| `tools/` | build-companion / build-pet-art / screenshot / nchittest / capture-window / extract-asar |

### 6.2 已验证

- 插件激活后自动拉起 `PetHost.exe`，10s 看门狗在崩溃后能重启（spawn.log 可查）。
- 桌宠透明、置顶显示在其他应用之上（DPI 感知截图）。
- 逐像素命中：透明角 `HTTRANSPARENT(-1)`，身体 `HTCLIENT(1)`。
- 插件禁用 → 子进程被清理；应用退出 → 伙伴进程父进程看门狗 2s 内退出。
- 位置/大小/穿透状态持久化；宠物菜单「退出」写 `pet.closed` 标记，本次会话不再拉起。

### 6.3 尚未验证（需人工确认）

- 拖拽/双击手感、长时间运行内存与 CPU（建议挂 30 分钟观察）。
- 真正关闭 DSH 主窗口后桌宠仍在（源码逻辑成立，但需要用户实际点一次关闭窗体验证）。
- 多显示器 / 不同缩放比例下的位置与命中偏差。
- 部分远程桌面/显卡环境下透明窗口的边缘表现。

### 6.4 复现

```powershell
pwsh tools/build-companion.ps1          # 编译宿主（可选，插件会自动做）
# 插件页禁用/启用 @local/dsh-pet 即可重启桌宠
tools/screenshot.exe shot.png           # DPI 感知截屏
tools/nchittest.exe <hwnd> <x> <y> ...  # 命中测试
tools/mouse-drag.exe 2890 1720 1500 820 # 真实鼠标拖拽（验证拖出趴姿/保持期）
tools/mouse-drag.exe click 2890 1720     # 单击
tools/setcursor.exe <x> <y>              # 移动光标 / 读当前光标（物理像素）
python tools/qa-pack.py <pack> <out>    # 合成/白边/枢轴旋转露洞检查
python tools/fix-face-split.py          # 修母版拆层残留：头发层里的皮肤块 + 刘海下静态底图
# 宿主自带 QA 位（不影响正常使用）：
#   PetHost.exe --dump-frame out.png   离屏渲染当前合成帧（不显示窗口，可配合截屏定位接缝）
#               --hide-layers tail,head 隐藏指定层做逐层隔离
#               --reload-decode 1024    强制按指定宽度重解码，验证缩放重解码路径
#               --qa-trace trace.txt    每 0.5s 追加一行「前台窗口/目标/姿态/位置」轨迹
#               --qa-fg-file fg.txt     用文件模拟前台窗口「left,top,width,height[,fullscreen]」（物理像素）
#               --qa-cursor 200,1200    钉住光标位置，验证目光跟随/待机回中
#               --qa-mode corner        强制全屏前台状态，验证右下角趴姿
#               --qa-mode perch-sit     走真实趴窗台控制器，验证等待→移动→坐下
```

### 6.5 M2 交付与验收

**运行时（PetHost.cs）**
- 分层骨骼分组：`body`（根运动）→ `head`（颈部枢轴）→ 头发/耳鳍/呆毛/发饰；手臂、尾巴、裙摆各自绕枢轴；Phase 2 起身体无腿 + `leg_l/leg_r/shoe_l/shoe_r` 独立腿鞋、`gait`（4 帧步态）、`legs_air`、`arm_*_up/shy`、`hair_back_action` 为按需解码的动作层
- 弹簧：tail / ear / hair / ahoge / skirt / arm 六组参数（刚度/阻尼/角度限幅）
- 眨眼调度：随机 2.2–6.2s，四段（半闭→闭→半闭→睁）
- 眼/嘴变体：`eyes_open/half/closed`、`mouth_smile/open/surprised` 运行时换 `Image.Source`
- 状态机：Idle / Think / Work / Celebrate / Sad / Sleep / Drag / Poke / Alert，超时回 Idle
- 互动：拖拽倾斜、单击害羞（腮红+爱心+气泡）、双击转身（水平翻转 twirl，0→翻→0 完整归位）、右键菜单、热键、穿透
- 特效：星星/爱心粒子、zzz、白底对话气泡（WPF 绘制，零依赖）
- 优化：贴图按**实际绘制尺寸**解码（512–1024，随缩放/DPI 重解码，按需解码表情变体；旧的隐藏眼部层不解码）、缓存 `MatrixTransform`、`LowQuality` 采样 → CPU 40%→17–26% 单核，内存 597→390MB

**信号桥（index.js）**
- 订阅：`agent/status`、`agent/assistant-stream`（250ms 节流）、`tools/result`、`agent/turn-stopping`、`agent/error`、`goal/changed`
- 写入 `signals.jsonl`（超过 128KB 自动裁剪），PetHost 增量读取
- 已验证：插件拉起 L1 宠物；`assistant.stream`/`tool.result` 真实事件写入；手动 `turn.end` 触发庆祝（气泡+星星+跳跃）；透明角穿透、身体命中正常

**性能**
- 5 秒采样：约 27% 单核、390–480MB 工作集（31 层 / 1024×1328 素材 / 按显示尺寸解码 512–1024，隐藏动作层按需解码）
- 已知可继续优化：按 bbox 裁剪图层、静态层合成缓存、Direct2D（列入 M3）

**尚未验证**
- `agent/turn-stopping` 的真实回合结束（需要跨一次完整对话观察；当前用手动信号验证了同一路径）
- `user-questions/request` 未接入
- 设置面板（M3.5）

### 6.6 M3 行为层交付

| 能力 | 实现 |
|---|---|
| 任务绑定 | Host 把每个 Agent 回合记为一个 task：`agent/status running` 或首个事件 → `task.start`；`tools/result` → 累计步数 `task.progress`；`turn-stopping` / `status idle` → `task.done`（步数 + 耗时）；`agent/error` → `task.failed`。PetHost 对应台词与动作（开工/第 N 步/总结/安慰） |
| 目光跟随 | 独立眼层 rig：固定眼白 + 眼线 + 遮罩；虹膜/瞳孔/高光作为移动组按 `gaze.limit`（±12/±6 源 px）平移，镜像时乘 `_facing`；支持 open/half/sleepy/sad/angry/closed/happy 与 normal/heart/star 瞳孔；眨眼/闭眼隐藏移动组，旧整眼像素变形已移除。**只在光标真的在动时跟随（1.5s 内有过位移）；光标停住后目光平滑回到正视前方**（虹膜回中约 0.5–0.8s），不会一直盯着静止的鼠标 |
| 拖拽被领着走 | 鼠标移动时平滑计算 `_dragVelX`；Drag 态身体反向倾斜（±18°）、尾巴/裙摆/呆毛甩动、头部滞后；**Phase 2 起换成 v2 动作层**：隐藏原手臂/腿鞋/后发，显示 `arm_l_up/arm_r_up`（举手，z=65）+ `legs_air`（悬空腿）+ `hair_back_action`（修掉后发里旧袖口残片），拖起时随机台词，松手落地。**从整图姿态（趴/坐）开始拖时先 `ExitPose` 换回完全体**（并重新锚定拖拽起点，避免跳一下），所以被拎着走的一定是完整角色。**松手后进入 60s「放哪儿待哪儿」保持期**（`DragHoldActive`）：保持期内不自动回窗台/右下角、不散步、不触发探头；一旦前台窗口句柄/边界/全屏标记变化，保持期立即解除 |
| 空闲散步 | 条件：`GetLastInputInfo` 系统键鼠空闲 > `walkIdleSeconds`（默认 60s）、60s 无前台窗口切换、60s 无宠物交互、无 DSH 任务（`_agentBusy`）、且当前无前台窗口可趴。满足后 `StartWalk()`：工作区内随机选目标、62 DIP/s、面向翻转、步频颠簸、手臂/尾巴摆动；**Phase 2 起腿部换成 4 帧步态 `walk_1..4`**（隐藏独立腿鞋，按 8 rad/s 与颠簸同频切帧）；到达或 9s 回 Idle，间隔 9–25s 再走；任何输入/信号立即取消 |
| 姿态组（Phase 2） | 身体拆成无腿 `body` + `leg_l/leg_r/shoe_l/shoe_r`；`gait`/`legs_air`/`arm_*_up`/`arm_*_shy`/`hair_back_action` 都是 `hidden` 层，**按需解码**（第一次显示才 decode）；被戳时双手合十（shy 臂），走路/拖拽/害羞之间原子切换，避免两套腿臂同时出现 |
| 分段骨骼（Phase 3） | 尾巴 3 段（tail_base→mid→tip）、后发 2 段、侧发各 2 段、手臂 3 段（upper_arm→forearm→hand）用 `parent` 父子链：子层先绕自身 pivot 旋转再叠加父层累积矩阵，弹簧目标逐段滞后，摆动像波浪一样传下去；默认合成与旧单层逐像素一致 |
| 坐窗台 / 探头 / 右下角趴着（Phase 3 + 修正） | 整图姿态 `poses.sit` / `poses.peek`：坐姿在**深度空闲 + 已趴稳**时自动切换（`ledgeY=960` 对齐前台窗口上沿，腿垂在窗口前，1.2s 坐稳过程平滑下移），任何交互/任务立刻起身；探头是长时间没人且没有可趴窗口时的彩蛋（`ledgeY=975` 对齐工作区底边，从任务栏后探出 4.5s）。**全屏 / 最大化前台窗口**时不趴窗口，而是滑到工作区**右下角**进入 `peek` 姿态趴着（`ledgeY=975` 对齐底边、只露出上半身），全屏期间一直保持；全屏结束后站起来，按正常趴窗台规则重新判定。坐在原地时窗口一动她先起身，等窗口稳了再重新过去 |

| 余额徽标 | Host 通过 ctx.deepseekAccount.getBalance() 每 5 分钟拉取平台余额，写入 balance.json（充值/赠送钱包、状态、时间）；PetHost 每 15 秒读取，但**徽标不常驻**：只在鼠标悬浮 / 单击她 / 右键「查看余额」时短暂出现约 10 秒，显示 ¥总额（充值+赠送）；右键「查看余额」同时用气泡显示明细；未登录/失败自动隐藏。showBalance: false 可关 |
| 文字框策略 | 气泡只在有话说时出现（交互 / 自主发言 / 任务进展），只保留最新一句；到时间真正淡出（旧实现的淡出标志被提前清掉，气泡和徽标会一直挂在屏幕上，已修）。时长：任务总结约 60s、开工台词约 10s、点击/拖拽约 8s、自主闲聊约 20s 且两句间隔 ≥60s。同一时间最多 1 个文字框：气泡在时余额徽标让位，悬浮 peek 被跳过。气泡宽度上限 = 立绘宽度、自动换行并居中，贴屏幕边缘也不会被切 |
| 窗口留白（防截断） | 窗口比立绘大一圈：左右各 `PadXRatio=0.16`、上 `0.13`、下 `0.08`。立绘图层整体偏移 `_padX/_padTop`，变换仍在立绘坐标系里算；命中测试减去偏移；位置钳制改用**立绘边界**（透明留白可以伸到屏幕外），所以仍然能贴边角站。拖拽倾斜、甩尾、跳跃和气泡全部在留白内完成，不会再被窗口边缘切出一截 |
| 不睡觉 | sleep: false → 启动参数 --no-sleep，自动睡觉阈值设为无穷；pet.sleep 仅保留为测试信号 |
| 表情预设 | v2 母版锁定素材：眼 6 态 / 眉 5 态 / 口型 8 态 / 腮红 3 态 + head_brow_ready；运行时按状态映射 neutral/happy/sad/angry/shy/celebrate/focused/sleepy/alert，互斥切换；眨眼结束恢复当前眼态；不用眉毛时 brow 层隐藏 |
| PNG 粒子 | parts/fx_*.png（128px）替换文字符号，爱心/星星/zzz 按需加载，缺失时回退文字 |

| 感知前台窗口 | 每 200ms 调 GetForegroundWindow，用 DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) 取真实可见边界，排除自身/桌面/最小化/被 cloaked 的窗口；窗口（hwnd + 边界 + 全屏标记）**必须连续 5 秒不变**才成为趴台目标，被拖动/缩放/来回切换的窗口不追；成为目标后她把宠物移动到该窗口右上角窗台（脚踩上沿、面向窗口中心，位置限制在工作区内），窗口移动/缩放时先起身等它停稳再跟。右键「查看前台窗口」显示标题与尺寸，「跟随前台窗口」开关持久化。只读几何与标题，不读窗口内容；**全屏窗口（覆盖整个显示器，含 F11/游戏/视频）和最大化窗口都不当作趴台目标**，改为去右下角趴着。|验证：`pet.walk` 信号后 1.6s 窗口 x 位移 80px；`pet.drag` 抓拍显示倾斜+臂摆；光标移到宠物左/右两侧，瞳孔与头部跟随；`task.start/done` 显示「包在我身上…大概！」与「搞定！7 步 · 83 秒」。新增 `pet.walk / pet.drag / pet.twirl / pet.poke / pet.sleep` 测试信号。

---

## 7. 里程碑

| 阶段 | 内容 | 状态 |
|---|---|---|
| **M0** | 方案 + 规范草案 | ✅ |
| **M1** | 独立宿主 + 透明置顶 + 拖拽/交互 + 点击穿透 + 自动启动/守护 + L0 参考宠物 | ✅ 本机可跑 |
| **M2** | 状态机、L1 分层弹簧、眨眼/表情、DSH 信号联动 | ✅ 本机可跑（设置面板并入 M3） |
| **M3** | 任务绑定台词、目光跟随、拖拽被领着走、空闲散步、4 帧步态、动作姿态、坐窗台/探头、分段父子骨骼 | ✅ 本机可跑 |
| **M3.5** | 图形设置页、DPF 校验器、`.dshpet` 导入、Pet Studio、性能二轮优化 | 计划 |
| **M4** | 网格变形、3/4 转身、音效粒子、社区页、可选 Live2D/Spine 导入 | 计划 |

---

## 8. 风险与对策

| 风险 | 对策 |
|---|---|
| 美术分层成本（整图遮挡区无信息） | L0 先用整图程序化跑通；M2 做「只拆眼/嘴/前发/呆毛/尾/耳鳍」的伪 2.5D；可选见 §9 工具链 |
| 透明窗口兼容性（远程桌面/显卡） | 已做纯透明无遮罩；可加「不透明底/圆角底」降级选项 |
| 关闭窗口语义变化（未来版本改成真退出） | 桌宠有父进程看门狗：宿主退出 2s 内自动退出，不会残留 |
| 单实例竞争 | 命名互斥量 + pid 文件 + 看门狗 |
| 插件升级/HMR 重复启动 | pid 校验 + 同父进程判断；新宿主会清理旧实例 |
| 用户主动退出又被拉起 | `pet.closed` 标记，本次应用会话不再启动 |
| 安全（宠物包） | 纯数据规范、路径穿越校验、大小/解码上限 |
| 透明窗口软件合成开销（31 层 × 60fps） | 贴图降到 512px 解码、缓存变换、LowQuality 采样；M3 用 bbox 裁剪 + 静态层缓存 + Direct2D |

---

## 9. 技术选型与授权结论（调研）

结论：**不内置 Live2D，不内置 Spine**，自研纯数据格式 + 自绘运行时；作者工具链全免费。

| 方案 | 授权 | 作者成本 | 结论 |
|---|---|---|---|
| Live2D Cubism | Core 不可随非 Live2D 许可再分发；加载任意用户模型属 "Expandable Application"，需审批 + 分成 | 编辑器 FREE 有硬上限 | ❌ |
| Spine | 运行时可按通知分发，但每个模型作者需自购编辑器 | $69 / $379 | ❌（UGC 不友好） |
| Rive | 运行时 MIT | Free 带 splash；去 splash $108/年 | ⚠️ 备选 |
| 自研 + WPF/Canvas | 自研，零依赖 | 免费 | ✅ 采用 |

作者拆层（后续 L1 用）：Krita/Photoshop 手工分层（1–4h/角色），或
[see-through](https://github.com/shitagaki-lab/see-through)（Apache-2.0，单图 → 最多 23 层 + 遮挡区 inpaint）
再人工修边；命名与弹簧参数可参考
[Anime2.5DRig](https://github.com/852wa/Anime2.5DRig)、[StretchyStudio](https://github.com/MangoLion/stretchystudio)。

出处：[Live2D 授权](https://www.live2d.com/en/sdk/license/)、[Expandable Applications](https://www.live2d.com/en/sdk/license/expandable/)、
[Spine 购买](https://esotericsoftware.com/spine-purchase)、[Rive 定价](https://rive.app/docs/account-admin/pricing)。

---

## 10. 待确认

1. L1 分层的美术由谁完成（人工 / 我按图分割 + 你修边）——决定 M2 观感上限。
2. DSH 事件联动是否需要开关（避免工作时被打扰）。
3. 是否要「桌面宠物商店」式的打包分发（M3 的 `.dshpet`）。





