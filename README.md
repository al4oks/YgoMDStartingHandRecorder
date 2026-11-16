# YgoMDStartingHandRecorder（YGO 起手记录）

[![CI](https://github.com/al4oks/YgoMDStartingHandRecorder/actions/workflows/ci.yml/badge.svg)](https://github.com/al4oks/YgoMDStartingHandRecorder/actions/workflows/ci.yml)

被动记录《游戏王 MASTER DUEL》（简中、窗口化客户端）每局**起手 5 张**（先手/后手均 5 张）的本地工具：手动点一次「先手截图 / 后手截图」即完成 截图 → OCR 识别 → 存证 → 入库，并按卡组统计上手率。无实时循环、不录视频、不抽帧。

## 功能特性

- 一步捕获：PrintWindow 主路径（遮挡/锁屏可用、不抢焦点），WGC 回退（Unity/DX 窗口）
- 识别：PaddleOCR 两段检测/识别 + 尺寸过滤 + 空槽重试；别名闭环（人工纠正 → 下次精确命中）
- 记录页：会话/卡组过滤、证据图核对、逐槽纠正与候选、错误识别映射、导出/导入记录集、清理
- 卡组页：官方卡组站剪贴板零 OCR 导入；类型/组合统计（超几何理论上手率 vs 实际上手率，合/先/后三桶）
- 捕获页悬浮窗：置顶小窗只放截图按钮 + 五卡名 + 合计上手率，摆游戏旁边
- 存储页：卡表下载/更新（官方 MD5 校验）、截图与卡图缓存目录可改路径（整树搬迁）、占用统计与清理
- 单文件便携发布：self-contained 单 exe，拷到任意目录双击运行；**卡表不内嵌**，首启提示下载

## 截图

> 以下为作者实机 / 种子数据示例；个人路径与玩家名已遮罩。画面中的卡图缩略图为 Konami 素材（见「版权与声明」）。

**捕获页**（空状态 → 一键截图识别入库）：

![捕获页空状态](docs/images/capture-empty.png)
![捕获页识别结果](docs/images/capture-result.png)

**记录页**（会话/卡组过滤 + 证据图核对 + 逐槽纠正）：

![记录页](docs/images/records.png)

**卡组页**（卡组一览 + 官方卡组站剪贴板导入）：

![卡组页](docs/images/decks.png)

**卡组详情**（类型/组合统计 + 理论 vs 实际上手率）：

![卡组详情](docs/images/deck-detail.png)

**存储页**（卡表更新 / 缓存目录改路径与清理）：

![存储页](docs/images/storage.png)

## 环境要求

- Windows 10 19041+ / Windows 11，x64
- 开发构建：.NET 11 SDK（`net11.0-windows10.0.19041.0`）
- 运行单文件 exe：无需安装 .NET
- 捕获对象：用户自行合法安装并运行的《游戏王 MASTER DUEL》简中窗口化客户端

## 快速开始

```bash
dotnet build YgoMDStartingHandRecorder.slnx
dotnet run --project src/YgoMDStartingHandRecorder          # 开发运行（repo 内自带 data/config.json）
dotnet publish src/YgoMDStartingHandRecorder -c Release -p:PublishProfile=SingleFile
# 产物: src/YgoMDStartingHandRecorder/bin/Release/net11.0-windows10.0.19041.0/win-x64/publish/YgoMDStartingHandRecorder.exe
```

首次启动若无卡表会弹「缺少卡片数据」提示，点「下载」从百鸽数据源拉取（zip 约 2.4MB，带官方 MD5 校验与进度条）；也可稍后到「存储」页下载。运行时在 root 旁自动建 `records/`、`captures/`、卡片数据目录（默认系统文档 `Documents\YgoHand\`，可改）。

卡组导入：MD 内「导出」到官方卡组站 → 浏览器打开「我的卡组」（可切 List 视图）→ Ctrl+A/Ctrl+C → 程序「导入卡组」读剪贴板 → 预览确认入库。

更多用法见 [docs/usage.md](docs/usage.md)，架构见 [docs/design.md](docs/design.md)。

## 测试

仓库**不含游戏截图样张**（版权与隐私原因，见下）。headless 测试中 CardLib.Smoke 开箱即跑；Records.Smoke 依赖卡表，先 `python tools/fetch_cards.py`（或程序内下载）生成 `data/cards.json`：

```bash
dotnet run --project tests/Ygo.CardLib.Smoke
dotnet run --project tests/Ygo.Records.Smoke     # 缺样张时自动跳过别名识别块并打印 SKIP
```

识别评测与 UIA 端到端冒烟需自备样张：用 `src/Ygo.Labeler` 标注自己的截图，放到 `samples/own/`（文件名格式 `{stem}__{卡1}+…+{卡5}.ext`，见 usage.md），再跑：

```bash
dotnet run --project tests/Ygo.Recog.Eval --no-build -- <root> samples/own
powershell -NoProfile -File tests/av-portable-smoke.ps1   # 需 samples/own 内含 *005109* 样张
```

`tests/av-portable-smoke.ps1` 需要交互桌面（UIA），不能在无头 CI 中运行。

## 目录结构

```
src/YgoMDStartingHandRecorder/   Avalonia 12 UI 头（捕获/记录/卡组/存储四页 + 悬浮窗）
src/Ygo.Records/                 共享层：SQLite 记录库/上手率统计/捕获 interop/卡表更新/设置
src/Ygo.Recognition/             PaddleOCR 识别管线
src/Ygo.CardLib/                 卡表索引/别名/模糊匹配/文件名标签 codec/ROI 契约
src/Ygo.Labeler/                 样张标注工具（WinForms）
tests/                           headless smoke ×3 + 识别评测 + 便携 UIA 冒烟
tools/                           fetch_cards.py / check_labels.py（纯标准库）
data/config.json                 识别契约（handRoi/upscale/slots/分隔符）
docs/                            usage / design
```

## 开发者碎碎念

Qcoder新人首月套餐5折+qwen 4.0新发布打折，弄个最低档的套餐试一下，弄一下之前的一个有想法的小工具，社区很多人骂MD费件上手率过高，弄个小工具觉得概率不正常的时候可以方便点记录手牌和分析数据。

在前司做开发的时候，22年刚进去是古法编程了一年，后来AI大模型开始爆发，但由于内部信息安全限制，也直到近一年才开始大规模推广AI辅助开发，前面顶多是用注释补齐的AI辅助开发，等现在出来看AI编程的发展自己有种“烂柯人”的感觉了。

在Qcoder harness开发，整个过程确实是纯自然语言开发得到了最终的结果，深感基础程序员几乎已经淘汰了。
不过，即使AI编程现在很强大了，功能的设计，流程的组织，交互的打磨还是需要人工确认和约束，不然成果可能还是没那么好用。

对Qcoder+qwen 4.0的印象的话，理解能力不错对话中的任务目的基本都识别到了，特别是本项目是客户端程序对图形界面的理解还是不错的；但是打磨细节时就还不太行，在软件规模稍微大了一点后，提出的一些细节修改有的处理任务处理时间很长，所以打磨细节就有点烦躁了。

## 已知不足

- 超框卡的出现极大的影响了卡名识别准确率，让AI想了想解决方案，当前模型暂且没什么好办法
- 导入卡组的方式有点抽象，尝试过截图导入，OCR识别准确率有点垃圾
- 当前只支持简中，缺少日文、英文的本地化
- 手动触发截图，时机不对可能被UI切换阶段的动画挡住手牌
- 打包.Net环境和图像识别依赖，软件体积有点大
- 表单输入缺少约束，可能存在一些BUG
- 软件交互缺少打磨，可能有些功能交互得比较奇怪


## 卡表数据来源与鸣谢（ygocdb / 百鸽）

- 卡表 `cards.zip`（含官方 MD5 校验文件）与卡图缩略图 CDN 由 **ygocdb（百鸽）** <https://ygocdb.com> 提供（API：`ygocdb.com/api/v0`）。**感谢百鸽提供的开放卡表与卡图数据服务。**
- 本仓库**不再分发卡表数据**：程序内置下载器与 `tools/fetch_cards.py` 在运行时按需拉取；卡图按需从 CDN 缓存缩略图到本地。
- 卡表内容（卡名/文案/密码等）的著作权属于 Konami；百鸽为社区数据整理方。使用其数据请遵守其服务条款。

## 版权与声明（Konami）

- 本项目为**非官方粉丝工具**，与 Konami（株式会社科乐美数码娱乐）无任何关联，亦未获其认可或赞助。
- 「游戏王」「遊☆戯☆王」「Yu-Gi-Oh!」「MASTER DUEL」等商标，以及卡片名称、卡图、卡片文案的著作权与商标权均归 **Konami** 所有。
- 本仓库不分发任何游戏客户端文件、卡图或卡表数据；运行时卡表/卡图来自上述第三方数据源，由用户自行拉取。
- 使用前提：用户自行合法持有并安装《游戏王 MASTER DUEL》客户端。本工具仅读取该窗口的屏幕像素，详见下节。
- 用户应自行确认其使用方式符合游戏服务条款与当地法律；作者不对使用本工具产生的任何后果负责。

## 合规红线（设计约束）

- **只读屏幕像素**：不读游戏内存、不改包、不注入、不 hook。
- **对 MD 窗口零输入**：不发送任何键鼠消息；捕获用 PrintWindow/WGC 被动读取。
- 无实时循环、无视频录制；每次捕获均由用户手动点击触发。

## 许可证

本项目源码以 [MIT](LICENSE) 许可证发布。

随单文件 exe 分发的第三方组件（.NET 运行时、Avalonia、SkiaSharp、PaddleOCR 及模型权重、OpenCV 等）各自遵循其许可证，清单见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)（Apache-2.0 全文见 [licenses/APACHE-2.0.txt](licenses/APACHE-2.0.txt)）；再分发二进制时请保留该声明与全文。

## 鸣谢

- [ygocdb（百鸽）](https://ygocdb.com)：卡表与卡图数据服务
- Konami：《游戏王 MASTER DUEL》原作（见版权声明）
- 各开源依赖作者（见 THIRD-PARTY-NOTICES.md）
<!-- TODO: 贡献者名单 -->