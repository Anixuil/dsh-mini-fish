# 行为补充素材 v1 · 验收与接入评估

来源：`pets/whale-maid-l1/art-source/behavior-supplement-v1/whale-maid-behavior-supplement-v1.zip`
验收日期：2026-11-05 · 未改动现有 `pets/whale-maid-l1/parts` 与运行时

## 1. 交付内容概览

| 类别 | 数量 | 说明 |
|---|---:|---|
| 新增整图分层 | 82 | 全部 1024×1328 RGBA sRGB，统一原点 |
| 粒子 | 12 | 128×128 |
| 道具/图标 | 7 | 128×128（键盘/便签/勾选/咖啡/白饭/钱包/人民币） |
| 参考原图 | 24 | 与现有 `parts` **逐字节一致**（SHA256 对比 0 差异） |
| PSD | 1 | 53.3MB，签名 `8BPS` 有效，82 新层 + 20 原层，图层名带 `@pivot` |
| 元数据 | 5 | `manifest.json`（bbox/pivot/z/replaces）、`integration-map.json`、分类元数据、prompt |
| 预览 | 20+ | 表情/姿态/步态/结构/粒子/参考合成 |

覆盖了清单里的 P0/P1 全部命名项，并额外给了 P2（睡姿/探头/3/4 侧身/背面/蓝色服装差分/余额图标）。

## 2. 校验结果

- **尺寸/格式**：82 张全部 1024×1328 RGBA；12 粒子 + 7 图标全部 128×128 ✅
- **参考一致性**：`reference-parts/` 与当前 pack 的 24 张逐字节一致 ✅（说明没有偷偷改旧素材）
- **PSD**：`8BPS` 头有效，53.3MB ✅（未在 Photoshop/Krita 打开验证，仅结构检查）
- **manifest**：bbox 与实际 alpha 包围盒一致（抽查 20+ 张） ✅
- **表情对齐**：用 `head_clean + hair_front_clean + 新眼/眉/嘴/腮红` 自行合成 happy/sad/angry/shy/celebrate/focused，与作者预览一致，无错位 ✅
- **步态**：`walk_1..4` 是**下半身（腿+鞋）帧**，与 `body`、`skirt_front/back`、旧 `apron` 组合后步态自然 ✅
- **坐姿**：`sit_pose` 为整图姿态，腿自然下垂，`ledgeY=974` 供趴窗台对齐 ✅
- **体积**：`parts/` 合计 **13.97MB**；含姿态整图会更大（单 `sit_pose`/`peek_pose`/`pose_sleep` 各 ~1.4–1.7MB）。当前 DPF v1 的 8MB 上限需要调整或拆分可选包 ⚠️

## 3. 已确认的组装关系（关键）

- **必须保留旧 `apron`、`ahoge`、`headdress`、`bow_r`、`ear_l/r`**：新包没有这些，但和新身体/裙片组合后完全对得上（本仓库未附该对比图，合成结果以 `pets/whale-maid-l1` 的实际预览为准）
- **必须用 `head_clean` / `hair_front_clean`** 替换旧 `head` / `hair_front`：旧 head 有烘焙的眉线/腮红残片，旧前发有眉区方块
- 新 `body` 只有上身+短裤（不含腿/鞋）；站立用 `leg_l/r + shoe_l/r`，走路用 `walk_1..4` 并隐藏腿鞋
- 尾巴三段、后发两段、侧发两段、手臂三段、裙前后片：父级 pivot 已在 manifest，运行时需要父子变换
- 整图姿态（sit/peek/sleep/three_quarter/back/outfit_blue）显示时隐藏所有站立层
- 表情互斥：眼/眉/嘴/腮红各一次只显示一层

## 4. 发现的问题 / 风险

1. **体积超 DPF v1 上限**：全量 82 层 14MB，加姿态 ~20MB。方案：
   - 把姿态图（sit/peek/sleep/three_quarter/back/outfit）做「可选包」按需下载/导入；
   - 或把 DPF 上限从 8MB 调到 24MB（规范由我们维护）；
   - PNG 可再压缩（量化/optimize）约省 20–35%。
2. **接缝需实机验证**：作者自己也标注「旋转接缝/走路重心/挂点/趴窗台贴边需运行时确认」。我们会用 ±10–20° 旋转测试逐段检查。
3. **旧 apron 未重绘**：它和新裙片的接缝、走路时裙摆摆动是否自然，需要运行时验证；必要时让作者补一张新 apron。
4. **表情层只覆盖脸**，需要 `head_clean` 才能干净；如果未来换头/换角度，需要同步新 clean 底。
5. **PSD 未在图形软件里打开验收**（仅结构检查），图层可见性/混合模式建议作者再自查一次。

## 5. 接入计划（建议分三阶段）

### Phase 1（低风险，立刻提升观感）
- 接入 22 张表情（眼 6 / 眉 5 / 嘴 8 / 腮红 3）
- 替换 `head_clean`、`hair_front_clean`
- 粒子 PNG 替换文字字符（12 个）
- 新增表情预设映射（happy/sad/angry/shy/celebrate/focused/speaking/sleepy）

### Phase 2（行走与拖拽）
- 替换 body + 接入 `leg_l/r`、`shoe_l/r`
- 走路切 `walk_1..4`（140ms/帧，按移动速度微调）
- 拖拽切 `arm_*_up + legs_air`，隐藏原手臂/腿
- 保留旧 apron / 发饰 / 耳朵

### Phase 3（姿态与骨骼升级）
- `sit_pose` 用于窗口趴台（ledgeY 对齐），`peek_pose` 做探头
- 尾巴 3 段、后发 2 段、侧发 2 段、手臂 3 段、裙前后片接入父子 pivot + 分段弹簧
- 可选：3/4 侧身、背面、蓝色服装差分
- 余额徽标换 `prop_wallet` / `prop_yuan` 图标

## 6. 需要你确认

1. 是否按上面 Phase 1→2→3 推进（我可以先做 Phase 1，改动小、当天可看效果）？
2. 姿态整图（sit/peek/sleep/3/4/背面/服装）是全部随包发布，还是做成可选包？
3. 旧 `apron` 先沿用，还是让作者补一张适配新裙片的 apron？
4. 蓝色服装差分（`pose_outfit_blue`）要不要做一套可切换皮肤？
