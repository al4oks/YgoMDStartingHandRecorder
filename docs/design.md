# 设计文档（YGO 起手记录器）

> 配套文档：[使用说明](usage.md)
> 本文描述软件架构与开发方案。所有 line number 以当前 main 分支为准。

## 1. 目标与范围

个人用的《游戏王 Master Duel》（简体中文、窗口化）**起手牌记录器**：每局手动点一次按钮，截取客户端窗口像素 → 识别起手 5 张卡 → 存证截图 + 入库 → 供事后复盘/统计上手率。

明确不做（用户拍板的范围红线）：

- 不做事件级实时跟踪、不录视频、不读游戏内存/不改包/不注入；**只读屏幕像素**。
- **对 MD 窗口不发任何输入**（鼠标/键盘/UIA 均禁）；自动化只作用于本软件自有窗口与测试 viewer。
- 输入是手动截图时机（含从网页视频暂停截取），识别失败靠重识别/人工纠正闭环，不做自动重试循环。
- 卡表保持 JSON 内存索引（不上 SQLite）；SQLite 只存对局记录。
- 不引 WPF/MAUI/ffmpeg/node；Python 工具仅标准库。

## 2. 总体架构

```
┌────────────────────────── UI 头（共用下层逻辑） ──────────────────────────┐
│  YgoMDStartingHandRecorder（唯一现役头，P12 起 WinForms 已删除）                │
│    捕获 / 记录 / 卡组 / 存储 四页（MD 风格视觉）                            │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │
┌──────────────────── Ygo.Records（共享逻辑层，P0 抽出） ────────────────────┐
│  RecordStore (SQLite) · HandStats · EmbeddedData (仅内嵌配置)             │
│  AppSettings (settings.json 双路径) · CardDataUpdater (卡表下载/MD5/搬迁)   │
│  NativeMethods (PrintWindow) · WgcCapture (WGC 回退)                       │
└───────────────┬──────────────────────────────┬────────────────────────────┘
                │                              │
        Ygo.Recognition                 Ygo.CardLib（共享契约/索引）
        OcrEngine(PaddleOCR)            CardDatabase / HandConfig / LabelCodec
        HandRecognizer(槽位+匹配)        LabelStore / DeckTextParser / DataRoot
                │                              │
                └────────── data/config.json（内嵌+磁盘） ──────────┘
                           卡表 cards.json：repo 开发数据 / 便携版由 CardDataUpdater 下载
                           （tools/fetch_cards.py 同源 ygocdb cards.zip，C#/Python 共读）
旁路工具：Ygo.Labeler（样本标注）· tests/*（headless smoke + UIA ps1 + eval）
```

五个 src 工程 + 三个 test 工程（P12 删除 WinForms 头后），解决方案 `YgoMDStartingHandRecorder.slnx`：

| 工程 | 职责 | TFM |
|---|---|---|
| `Ygo.CardLib` | 卡表内存索引、别名/模糊匹配、文件名标签 codec、ROI 配置、卡组文本解析、数据根解析 | net11.0 |
| `Ygo.Recognition` | PaddleOCR 封装 + 起手识别流水线（槽位聚类、匹配、重试、超框约束） | net11.0-windows |
| `Ygo.Records` | 共享逻辑层：SQLite 记录库、上手率统计、PrintWindow/WGC 捕获、内嵌配置、存储设置、卡表下载（UI 框架无关） | net11.0-windows10.0.19041.0 |
| `YgoMDStartingHandRecorder` | 唯一 UI 头：捕获/记录/卡组/存储四页、单文件发布（卡表不内嵌） | net11.0-windows10.0.19041.0 |
| `Ygo.Labeler` | 样本标注工具（改名写标签 + labels.json 索引） | net11.0-windows |

依赖方向单向：Recorder → Ygo.Records → Recognition → CardLib；Labeler → CardLib。Recognition 不依赖记录库（`IAliasLookup` 接口反向注入，避免循环）。Avalonia 迁移见 [avalonia-plan.md](avalonia-plan.md)：新 UI 头只换框架与视觉，Ygo.Records 及以下零逻辑改动。

## 3. 数据契约

- `data/cards.json`：Konami 官方卡库抓取（`tools/fetch_cards.py`），键为 cid，字段含 `md_name / sc_name / cn_name / nwbbs_n / cnocg_n / jp_name / en_name` 等多语言别名。识别以 `md_name` 为主、其余为别名匹配，**只匹配卡名文本**（异画安全，不做卡图指纹）。
- `data/config.json`：`handRoi {x:0.26,y:0.83,w:0.48,h:0.17}`（比例坐标）、`upscale:3`、`slots:5`、标签分隔符 `__` / `+`、`skipPrefix:"SKIP_"`、`unknownSlot:"?"`。C# 与 Python 共读，保证跨语言一致。
- 样本标签 = 文件名：`{stem}__{name1}+…+{name5}.ext`（非法字符转全角、`?` 未知槽、`SKIP_` 前缀跳过、`_2` 去重）；`labels.json` 为备份索引。`LabelCodec` 是唯一编解码入口。
- 记录库 `records/ygo.db`（SQLite，运行时产物，gitignore）：见 §6。

## 4. 捕获子系统（只读像素）

两级回退，均不向 MD 发输入：

1. **PrintWindow**（`PW_CLIENTONLY | PW_RENDERFULLCONTENT`）：离屏渲染客户区，窗口被遮挡/锁屏仍可截、不抢焦点。对 GDI/WinForms 窗口（含测试 viewer）有效。
   - 关键坑：兼容 DC 必须 `CreateCompatibleDC(GetDC(NULL))` + 屏幕 DC 建 bitmap，否则锁屏下全黑（`NativeMethods.cs:60`）。
2. **WGC（Windows.Graphics.Capture）**：真实 MD 是 Unity/DX，PrintWindow 返回 err=5/全黑 → 回退 WGC（OBS 式读 DWM 合成表面，被遮挡亦可）。
   - WinRT 投影类不能从 `Marshal.GetObjectForIUnknown` cast，须 `GraphicsCaptureItem.FromAbi` / `WinRT.MarshalInterface<T>.FromAbi`（`WgcCapture.cs:121`）。
   - WGC 表面含标题栏/边框 → 用 `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS=9)` 减客户区裁剪（`WgcCapture.cs:58`）；`GetWindowRect` 含 Win11 不可见_resize 边框会越界。
   - 截图时窗口边框**闪黄框 = OS 的 WGC 隐私指示**，属系统行为、只读不注入，安全；去除需 SDK 22621 `IsBorderRequired`（推迟）。
- 环境变量 `YGO_CAPTURE=wgc|pw` 可强制路径；`YGO_DEBUGSHOT` 落盘 last_shot.png + capture.log 供排查。
- 目标窗口最小化时直接报错提示还原（不静默截黑图）。

## 5. 识别子系统（流水线）

`HandRecognizer.Recognize(Bitmap)`（`HandRecognizer.cs:58`）：

1. 比例 ROI → 取**卡名条带**（ROI 上 55%）作为检测输入（`mat1`）。
2. **两段 OCR**：Detector 在 2x 缩放跑（`YGO_DETUP`），逐 box 用静态 `GetRotateCropImage` 裁旋转框，再在 4x 缩放（`YGO_UP`）批量 Recognizer。两段比整图单段更快且修好闪卡漏检。
3. 行过滤：score≥0.5、含汉字或长度≥4、排除「【/法卡/陷阱」、尺寸 max≥35 && min≥10（**防后缀碎片污染 pitch**）、y 落在条带内。
4. 槽位聚类：pitch = 相邻 cx 间隙中位数；枚举中心假设（`Assign`，`:244`）选代价最小者 → 5 槽；每槽取最上行 + 右侧小后缀拼接（「－"剑"」等常被切成独立小 region）。
5. 匹配 `MatchName`（`:276`）= `GlobalMatch`（精确 key → 精确别名 → alias 表 → 前缀 0.9 → 模糊≥0.6）+ **卡组约束 fallback**（见 §5.1）。
6. 仅对 `Card==null` 的槽做 **retry**（`RetryWeakSlots`，`:104`）：5x 裁片 + sharpen（`YGO_RETRYUP`/`YGO_RETRYPRE` 可换形态学变体，实测 dilate/erode/median/clahe 均更差，默认只 sharpen）；接受阈 sim≥0.99 或 (≥0.8 且 score≥0.75) 或 **deck-hit(sim<0.6)**。
7. 输出 `HandResult(Slots, Lines, Elapsed, Trace)`；弱槽附 `Candidates` top-5 供人工点选。

### 5.1 超框方案（M3-13，收敛设计）

超框 = 卡图压过卡名条底色，OCR 字形坏。可行性结论（scratch `build/feasibility`）：

- **色板纯度可作 flag**：banner HSV 18 bin（S/V>60 去火花/暗字）纯度，超框 0.33–0.49 vs 闪卡 0.69–0.80 vs 普通均值 0.72 → 能区分"超框 vs 闪卡"，但不作唯一判据（`BannerPurity`，`:324`；`SlotResult.BannerPurity`）。
- **粗卡图均值哈希不可用**：intra-SUPER Hamming 23–31/64 ≈ inter 22–33/64，无区分度 → 放弃全局自积累指纹。
- **卡组内容约束可行且已落地**：把模糊候选限制为当前选中卡组名单（`DeckCounts` cid→count，`:49`）。仅当 global 返回 null，或（纯度<0.55 超框嫌疑 且 global 卡不在卡组）时，取卡组内 `FuzzyTop` top1，要求 sim≥0.25（`YGO_DECKSIM`）且 >global sim 且**不超过卡组数量**（`_used` 每手计数）才采纳。
- 效果：选对卡组时 samples/own 23/25·123/125 → **25/25·125/125**（两个超框漏槽救回，含一个 OCR 全空靠 retry+约束）；**未选卡组行为与旧版完全一致**（基线 92.0/98.4 不变）。
- 首次每张超框卡仍可人工点选一次写 alias（闭环见 §6），之后 alias/卡组约束自动命中。

### 5.2 alias 闭环

记录页/捕获页人工纠正 → `RecordStore.Correct` 写 `SnapshotCard.CorrectedCid` + `UpsertAlias(raw→cid)`；下次识别 `MatchName` 在精确后/模糊前查 alias（`IAliasLookup.TryLookup`，sim=1）→ 同一段坏 OCR 文本自动命中。`TryPeek` 只读不计数，用于候选预填。

## 6. 记录子系统（SQLite）

`RecordStore`（`Ygo.Records/RecordStore.cs`）表结构（`:36-68`）：

| 表 | 用途 |
|---|---|
| `DuelSession` | 会话＝一次运行×一个卡组批次（StartedAt 全时间戳;Note=卡组名_时间戳，自动创建、暂不改名） |
| `HandSnapshot` | 一次起手：会话、证据图路径、原始截图路径、耗时、先后手、DeckId |
| `SnapshotCard` | 5 槽：CardCid、RawText、OcrScore、MatchSim、CorrectedCid/CorrectedAt |
| `OcrAlias` | 人工确认的 OCR 原文→cid + Hits 计数 |
| `Deck` / `DeckCard` | 导入卡组（Main/Extra/Side + Count + RawName + Cid） |

关键方法：`CreateSession:141`（每次调用新建一会话）、`ListSessions:149`（可按 deckId 过滤出含该卡组记录的会话）、`AddSnapshot:168`、`Correct:228`、`UpdateRecognition:296`（重识别保留纠正）、`UpsertAlias:314`、`AddDeck:248`、`MarkImagesCleanedUnder:381`（清理标记）、`ExportCsv:397`、`ImportLabeledDir:416`（把已标注样本目录导入为一条会话）。

打开时幂等迁移：① 旧库中文 Section 显示名 → Main/Extra/Side;② **旧按天会话 → 按卡组各合并为一个会话**（`MigrateDaySessions:90`：以 `length(StartedAt)=10` 识别旧行，按 DeckId 分组、取组内最早 CapturedAt 作时间戳命名，未分类单独一组;迁移后删旧行，重开不再触发）。

### 6.1 截图缓存统计与清理（M3-15）

- **缓存目录**＝`YGO_AUTOSAVE ?? root/captures`（与捕获存盘同目录）。记录页状态栏常显 `共 N 条记录 · 截图缓存 X 张 / Y MB`（目录文件数/字节，含未入库的孤儿截图）。
- **清理**＝记录页「清理截图」按钮，**两段内联确认**（首点显示将删张数/大小并变「确认清理截图?」，再点执行;「刷新」取消）——不弹模态框，既防误删又可 UIA 自动化。只删缓存目录内文件;`ImportLabeledDir` 导入的验收集记录指向 samples，**不在缓存目录、绝不被清理**。
- **已清理标识**＝删文件后 `MarkImagesCleanedUnder:381` 把 ImagePath 位于缓存目录下的记录置 `ImagePath=''`（幂等）。详情 pane 对 `''` 显示占位「证据截图已被清理」，对路径非空但文件缺失显示「证据截图缺失：path」;已清理记录**禁止重识别**（像素已不存在），记录/卡名/统计保留。

## 7. 统计（上手率）

`HandStats`（`Ygo.Records/HandStats.cs`）：

- **理论**：超几何分布 `Theoretical(mainTotal, cardCount, handSize=5)`（`:24`）——主卡组 N 张含 k 张某卡，起手 5 张至少 1 张的概率；先/后手相同（都抽 5）。
- **实际**：`Compute(snaps)`（`:34`）以**会话**为集合（不再按天），分 合计/先手/后手 三桶;**按"是否出现"计**（一手抽到 1 张或 2 张都记 1 次命中），率 = 命中手数/桶手数。调用方决定口径：捕获页传当前会话+选中卡组（`ListSnapshots(_sessionId, deckId)`）;卡组页传「全部会话合并」或单个会话（`ListSnapshots(sessionId|null, deckId)`）。
- **标注**：`Mark(observed, theoretical)`（`:64`）差 ±1pp 内标 `-`，否则 ↑/↓;捕获页卡名行（含本次识别）与卡组页「上手率」列实时计算。
- **会话生命周期**：启动选中卡组即 `CreateSession("卡组名_yyyyMMdd_HHmmss")`，切换卡组再建一个;本次运行的截图都归当前会话。空会话（没截图）不出现在卡组页口径下拉（该下拉只列含该卡组记录的会话）。

## 8. 卡组导入（零 OCR）

自有卡组不走 OCR：用户在 Edge 登录 Konami 卡库「我的卡组」详情页手动 Ctrl+A/Ctrl+C（**登录与复制由用户手动**，内置浏览器被墙、不复制 cookies），`DeckImportDialog` 读剪贴板 → `DeckTextParser.Parse`（gallery 重复名行 / list 索引 triples 双格式）→ 预览命中/未命中（红）→ 填名入库。捕获页选卡组后：记录归类 + **新建会话** + **超框候选约束**（§5.1）。"抄别人卡组"的缩略图 OCR/指纹为低优先遗留（名字条 ~6px 不可 OCR）。

## 9. 标注工具（Ygo.Labeler）

浏览样本目录 → 5 槽 TextBox + **共享向上弹出 ListBox** 自动完成（焦点留在 textbox，鼠标点击/Enter 选中才提交；候选只显示规范名，重名才加 [id]）→ Enter=保存+下一张（改名写标签 + 更新 labels.json）→ Skip=`SKIP_` 前缀。键盘流为主；缩放/手牌区放大辅助目视。

## 10. 打包（M4 单 exe）

`dotnet publish src/YgoMDStartingHandRecorder -c Release -p:PublishProfile=SingleFile`：win-x64 **自包含** + `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` + 压缩，不 trim/R2R（反射/Paddle 不耐 trim）→ 单文件 **约 219MB**（.NET 运行时 + Paddle mkl + OpenCV + 内嵌模型 + 内嵌配置;**卡表不内嵌**，P12）。

- OCR 模型本就内嵌于 `Sdcb.PaddleOCR.Models.Local` dll（包仅 lib 无 content）。
- **内嵌数据（P12 起仅配置）**：`data/config.json` 以 EmbeddedResource 打进 **Ygo.Records**（`EmbeddedData.Config()`）；**卡表不再内嵌**——启动缺 `cards.json` 时弹提示→存储页从 ygocdb 下载（zip+MD5 校验+进度+更新时间 meta）；`CardDatabase.Load(Stream)` 保留；repo 内带 data/ 或 `YGO_CARD_DATA` 指向已有卡表则免下载。存储路径（截图/卡片数据）由 `settings.json` 管理、存储页可改并整体搬迁。
- 运行时在 exe 旁创建 `records/`、`captures/`（可写产物）。

## 11. 开发方案与验收

- **plan-first**：每阶段先收敛计划等用户「开工」；每里程碑 commit（已授权），**push 未授权**。
- 里程碑：M0 卡表+CardLib+Labeler+check_labels → M1 窗口选择+捕获 → M2 识别达标 → M3 记录/卡组/统计/UI → M3-13 超框 → M4 单 exe → M3-14 会话化统计（会话=运行×卡组、卡组页合并/单会话上手率、旧按天会话自动迁移）→ M3-15 截图缓存统计+清理+已清理标识。
- **Avalonia 迁移（2026-11-15 起，[avalonia-plan.md](avalonia-plan.md) P0–P6）**：P0 已完成——共享逻辑层抽为 `Ygo.Records`（RecordStore/HandStats/NativeMethods/WgcCapture/EmbeddedData 平移，namespace `Ygo.Records`，零逻辑改动），WinForms 头与测试改引用；回归 build 0 错误 + Records.Smoke 67 + CardLib.Smoke 19 + recorder-smoke + deck-constraint-review 全绿、eval own 92.0/98.4 不变。P1 起新增 Avalonia 12 UI 头，每阶段以「截图对照原型 + 回归结果」过用户审核门。
- **M2 验收阈值**：samples/own 整手≥90%、单卡≥95%、<2s（ idle 基线 92.0/98.4、avg1087ms）；samples/web 仅鲁棒性不计阈值（低分辨率视频帧 8%/16.8%）。
- 验证三层：headless smoke（CardLib.Smoke / Records.Smoke 63 检查）+ **UIA ps1 端到端**（recorder-smoke / deck-constraint-review / ui-opt-review，ps1 须 UTF-8 **BOM**；模态对话框不自动化）+ eval 准确率/计时。UI 改动另截图目视。
- 计时口径：整手 `Recognize` 耗时；MD 运行时约 2 倍（环境干扰非回归），阈值未改。

## 12. 已知限制

- 超框未选卡组时仍可能漏（靠 alias/人工闭环）；闪卡火花动画致实机置信度逐帧波动（名字仍对）。
- 截图时机被游戏内_overlay 遮挡 → 像素本身被盖，重识别无效，需重截（用户把控时机）。
- web 低分辨率压缩帧准确率低（无阈值）；自适应 upscale 实测更差已回退。
- WGC 黄框为 OS 隐私指示（安全）；去除需更新 SDK（推迟）。

## 13. 环境 knobs

| 变量 | 作用 |
|---|---|
| `YGO_DB` | 记录库路径（隔离测试） |
| `YGO_DECK` | 捕获页卡组预选名 |
| `YGO_AUTOSAVE` | 跳过存盘对话框的目录（自动化）；亦即截图缓存统计/清理目录 |
| `YGO_DEBUGSHOT` | 落盘 last_shot.png + capture.log |
| `YGO_CAPTURE` | `wgc`/`pw` 强制捕获路径 |
| `YGO_DUMP` / `YGO_CROP` | eval 打印 trace+lines / 存 ROI 裁片 |
| `YGO_UP`/`YGO_DETUP`/`YGO_RETRYUP` | rec/det/retry 缩放倍数 |
| `YGO_RETRYPRE`/`YGO_RETRYCROP` | retry 预处理变体 / 存 retry 裁片 |
| `YGO_DECKSIM`/`YGO_SUPERPUR` | 卡组约束接受阈(0.25) / 超框纯度阈(0.55) |