# 第三方组件声明

单文件发布（self-contained + PublishSingleFile）会将下列组件的运行时/原生库/模型权重打包进 exe。再分发二进制时请保留本声明。源码仓库本身不含这些组件的二进制（经 NuGet 还原）。

Apache-2.0 许可证全文见 [licenses/APACHE-2.0.txt](licenses/APACHE-2.0.txt)。下表所有 Apache-2.0 组件均以**未修改**的 NuGet 包形式引用（无源码改动）；再分发二进制时请随附该全文与本声明。

| 组件 | 版本 | 许可证 | 说明 |
|---|---|---|---|
| .NET Runtime / Windows Desktop 参考 assemblies | 11.0 | MIT | Microsoft；含 System.Drawing 等 |
| Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent | 12.1.6 | MIT | UI 框架 |
| SkiaSharp / libSkiaSharp（Avalonia 传递依赖） | 随 Avalonia | MIT | 图形原生库 |
| HarfBuzzSharp / libHarfBuzzSharp（Avalonia 传递依赖） | 随 Avalonia | MIT | 文本整形原生库（HarfBuzz 本体 Old MIT） |
| Sdcb.PaddleOCR | 3.3.1 | Apache-2.0 | PaddleOCR 的 .NET 绑定 |
| Sdcb.PaddleOCR.Models.Local | 3.3.1 | Apache-2.0 | 内嵌 PP-OCRv5 中文模型权重；权重 © PaddlePaddle Authors，Apache-2.0 |
| Sdcb.PaddleInference.runtime.win64.mkl | 3.3.1.70 | Apache-2.0 | Paddle Inference 原生库；其中 Intel MKL 运行时组件遵循 Intel Simplified Software License 再分发条款 |
| OpenCvSharp4 / OpenCvSharp4.runtime.win | 4.11.0.20250507 | Apache-2.0 | OpenCV 绑定与原生库（OpenCV ≥4.5 为 Apache-2.0） |
| Microsoft.Data.Sqlite | 11.0.0 | MIT | SQLite 访问层；SQLite 本体 public domain |

## 数据源（非代码依赖）

- 卡表 cards.zip / MD5 与卡图缩略图 CDN：ygocdb（百鸽）<https://ygocdb.com>，运行时按需下载，不随仓库/二进制分发；卡表内容著作权归 Konami。
- 卡组文本导入：用户自官方卡组站 <https://www.db.yugioh-card.com/yugiohdb> 手动复制，程序只读剪贴板。

## 商标

「游戏王 / Yu-Gi-Oh! / MASTER DUEL」为 Konami 商标。本项目为非官方粉丝工具，详见 README「版权与声明」。