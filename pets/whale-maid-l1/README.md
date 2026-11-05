# 蓝鲸女仆·小蓝（L1 新母版 · 已确认母版分层）

来源：`art-source/face-remake-v1/whale-maid-approved-master-layers-v1.zip`（基于用户确认母版的完整分层交付）。
画布 1024×1328，分层 RGBA 透明图层；眼睛/嘴型共用一个画布原点，切换时只替换对应层。目光跟随由运行时的**虹膜/瞳孔/高光分层眼组**（`pet.json` 的 `eyesRig`）完成：眼白与眼线固定，移动组按 `gaze.limit` 平移后再按眼眶遮罩裁切，`whale-maid-eye-layers-v1` 提供 open/half/sleepy/sad/angry/closed/happy 与 normal/heart/star 瞳孔。

## 2026-11-05 接缝修复（v0.7.1）

母版拆层残留导致刘海区域出现发白方块/台阶接缝：`hair_front.png` / `headdress.png` 里烘焙了刘海缝隙处的皮肤，`head.png` 在刘海下面又还是脸皮，弹簧摆动时两者错位。已用 `tools/fix-face-split.py` 修复：

- 剔除头发/头饰层里的皮肤像素，并把母版底色回填到下层
- `head.png` / `head_brow_ready.png` 在刘海覆盖区改为母版同款静态底图（晃开时露出头发/皮肤楔形而不是色条）
- 原始图层备份：`art-source/whale-maid-l1/face-split-fix/parts-before/`

## 2026-11-05 Phase 2（v0.8.0）

- `body.png` 换成 v2 无腿身体；新增 `leg_l / leg_r / shoe_l / shoe_r` 独立腿鞋层（z=35–38）。默认站姿与旧版逐像素一致
- 新增 `walk_1..4.png`（4 帧步态）、`legs_air.png`（悬空腿）、`arm_l_up / arm_r_up`（举手）、`arm_l_shy / arm_r_shy`（害羞合十）、`hair_back_action.png`（动作后发，修掉旧袖口残片）
- 这些动作层在 `pet.json` 里是 `hidden`（默认不显示，首次使用时才解码）；旧 `eyes` 层标为 `retired`，永不加载
- 运行时：走路切 4 帧步态、拖拽换举手+悬空腿+动作后发、被戳换害羞手臂；预览见 `docs/images/phase2-poses.png`

## 2026-11-05 Phase 3（v0.9.0）

- **分段骨骼**：尾巴 3 段（`tail_base/mid/tip`）、后发 2 段（`hair_back_upper/lower`）、侧发各 2 段、手臂 3 段（`upper_arm/forearm/hand`），`pet.json` 用 `parent` 组父子链；子层先绕自身 pivot 旋转再叠加父层矩阵，弹簧目标逐段错后，摆动呈波浪
- **整图姿态**：`poses.sit`（坐窗台）/ `poses.peek`（探头），带 `ledgeY`；运行时显示姿态时隐藏全部角色层。`sit` 会自动对齐前台窗口上沿，`peek` 对齐工作区底边
- **包体瘦身**：移除运行时不再使用的眼部源分层（`eye_white_* / eyelid_* / iris_* / highlight_* / pupil_*`，源在 `assets/whale-maid-eye-layers-v1` zip）、`retired` 旧眼层、旧单层 `tail/hair_back/hair_side/arm`；`preview.png` 移到 `docs/images/pack-preview.png`；包体约 **7.1MB**（DPF 8MB 内）
- `pose_sleep` 未接入（睡眠功能默认关闭）；侧背/配色为 v2 rejected 候选，不接入

## 与上一版（v2 修正版）的区别

- 五官全部按「确认母版」重做：默认睁眼/微笑/眉毛来自母版，半闭/闭眼/说话/惊讶由母版参照生成
- 新母版**没有宝石胸针**，胸前是深蓝领结（属于 body），因此本包没有 brooch 层
- 发箍更窄、裙摆刺绣改为藤蔓纹、腰扣两颗、鞋型简化
- 保留右侧小蝴蝶结、白色蕾丝、围裙小鲸鱼刺绣

## 图层顺序（来自 layer-order.json，禁止同时叠多个眼态/嘴型/腿鞋）

tail_base → tail_mid → tail_tip → hair_back_upper → hair_back_lower → hair_back_action →
hand_l → forearm_l → upper_arm_l → hand_r → forearm_r → upper_arm_r →
leg_l → leg_r → shoe_l → gait → legs_air → shoe_r → body → skirt → apron →
arm_l_up → arm_l_shy → arm_r_up → arm_r_shy → ear_l → ear_r → head → brow → blush → mouth →
hair_side_l_upper → hair_side_l_lower → hair_side_r_upper → hair_side_r_lower →
hair_front → ahoge → headdress → bow_r

> 注意：手臂在 body 之前（手臂在下层）；动作臂 `arm_*_up/shy` 在围裙之后、头发之前（z=65），用它们时要隐藏原 `arm_l/arm_r`。`gait / legs_air` 与 `leg_l/leg_r/shoe_*` 互斥，不能同时显示。

## 验证

- ✅ 全部图层 1024×1328 RGBA（39 层），文件清单/尺寸/非空/透明区检查通过（原始 `audit.json` 一并保留）
- ✅ 项目 `tools/check-pet-pack.py` 返回 ok（对方也跑过，日志在 art-source）
- ✅ 深色底无白边；五种表情合成位置一致
- ✅ Phase 2：默认合成与旧 body 逐像素一致；rest / walk / drag / shy 实机截图通过
- ⚠️ 袖口、侧发等交界在独立旋转时仍可能有轻微接缝，需在绑定层级/枢轴/动作幅度里收
- ℹ️ 未制作 `shadow.png`（运行时程序化生成脚下阴影）
- ✅ 骨骼弹簧、表情切换、DSH 联动、步态/动作姿态均已接入运行时

## 授权

角色来自用户提供的设定图与 GPT 分层精修；沿用 `CC-BY-NC-4.0`（非商业同人）。


