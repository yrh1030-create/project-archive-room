# 项目档案室 · Project Archive Room

> 面向乡镇（街道）工程项目的 Windows 档案管理软件：一项目一档、全文查资料、合同与财务台账、发票拆分，以及接入 DeepSeek 等大模型的 AI 填表和 AI 助手。
>
> A Windows desktop app for managing the archives of township-level public works projects (e.g. rural roads): one folder per project, full-text search, contract & finance ledgers, invoice splitting, plus AI form-filling and an AI assistant powered by DeepSeek or any OpenAI-compatible API.

**版本 Version 1.1 · 开发者 Developer: misslsf**

[中文](#中文说明) | [English](#english)

---

## 中文说明

### 能做什么

| 功能 | 说明 |
|---|---|
| 项目总览 | 自动把项目文件夹识别成项目卡片，显示办理进展、合同金额、资料份数、缺少的资料 |
| 一页看完一个项目 | 概况、办理流程、关键信息、财务、合同台账、全部资料、备注都在同一页 |
| 自动归类 | 资料按“立项、设计、招标、合同、施工、验收、结算、决算、资金支付、人员资质……”自动分类；拖进窗口即可归档，压缩包自动解压 |
| 全文查资料 | Word、Excel、PDF 扫描件、照片都能搜（扫描件用 Windows 自带中文 OCR 识别） |
| 合同台账、财务 | 合同金额、已付、未付；收支明细可挂记账凭证和发票原件；支付进度一目了然 |
| 发票 | 从合同、凭证混合 PDF 里自动拆出单张发票；添加电子发票自动识别金额、日期、销售方；重开的新票可一键替换旧票 |
| 导出 | 项目总表、资料目录、合同台账导出 Excel；一键导出“经济合同 PDF 包”（含清单和网页预览） |
| 一键导入 / 取消关联 | 识别带项目名称的文件夹批量导入；误把分类文件夹当成项目时一键合并 |
| **AI 填上级报表** | 选一张上级发来的 Excel，AI 自动认出哪几行是本单位项目，查档案原文后只填空白格，找不到依据的留空；填过的格子标黄，批注写明依据 |
| **AI 整理合同收支** | AI 读合同、成交通知书、凭证、回单、发票，列出建议登记的合同和收支，人工勾选后写入台账（只有发票没有付款凭证的不算已付） |
| **AI 助手** | 像聊天一样提问或交代任务：查档案、用档案填表、修改 Word / Excel、写情况说明（生成 Word）、做汇总（生成 Excel）。生成的都是新文件，原文件不动 |

### 运行环境

- Windows 10 / 11（64 位）
- .NET Framework 4.x（Windows 自带，无需另装）
- 可选：Microsoft Office 或 WPS —— 读写老版 `.doc` / `.xls`、AI 填表、Word 转 PDF 时需要
- 可选：AI 接口密钥 —— 只有 AI 功能需要

### 安装

到 [Releases](../../releases) 下载 `项目档案室安装程序.exe`，双击安装。已安装旧版本时会自动覆盖升级，档案库和设置都保留。

首次打开时选择“项目总文件夹”（放各项目资料的那个文件夹），软件会自动扫描、分类、建立档案库。

### 设置 AI 接口

“设置与备份 → AI 接口设置”，选服务商并填入 API 密钥，点“测试连接”：

| 服务商 | 接口地址 | 默认模型 |
|---|---|---|
| DeepSeek | `https://api.deepseek.com` | `deepseek-chat` |
| 通义千问 | `https://dashscope.aliyuncs.com/compatible-mode/v1` | `qwen-plus` |
| Kimi | `https://api.moonshot.cn/v1` | `moonshot-v1-128k` |
| 智谱 GLM | `https://open.bigmodel.cn/api/paas/v4` | `glm-4-plus` |
| 自定义 | 任何兼容 OpenAI `/chat/completions` 的接口 | — |

- 密钥用 Windows 账户加密保存在本机（`%APPDATA%\项目档案室\ai.json`），**不写进档案库，也不在安装包里**，分享档案库或安装包都不会带出去。
- 资料只保存在本机。只有使用 AI 功能时，才会把相关项目的文字资料发给你设置的 AI 接口。
- AI 的结果都要人工核对：AI 填表和 AI 整理都会先列出结果和原文依据，确认后才写入。

### 从源码编译

不需要 Visual Studio，只用 Windows 自带的 C# 编译器：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

- 主程序输出到 `build\out\项目档案室.exe`
- 安装包输出到 `安装包\项目档案室安装程序.exe`（内嵌主程序）

### 目录结构

```
src/        主程序源码（WinForms，C# 5）
  Store.cs        档案库：扫描、分类、全文索引、导入导出
  ProjectView.cs  项目页（概况、财务、合同台账、全部资料）
  Invoices.cs     发票识别、拆分、关联
  Ocr.cs          Windows 中文 OCR、PDF 渲染
  AiFill.cs       AI 接口、Excel 读写、AI 填表
  AiFinance.cs    AI 整理合同和收支
  AiChat.cs       AI 助手的工具（查档案、读写 Word/Excel、生成文档）
  ChatPage.cs     AI 助手界面
setup/      安装程序源码
build/      编译用的清单、图标生成器、合同预览网页模板
build.ps1   一键编译脚本
```

### 更新记录

- **1.1**：AI 填上级报表、AI 整理合同收支、AI 助手（左侧栏）；添加电子发票、重开发票一键替换；移除的资料可恢复、每类资料可直接添加；AI 以原件为准（金额以大写为准）、数字计算用计算器工具。
- **1.0**：项目总览、项目页、自动分类、全文搜索、合同台账、财务与发票拆分、导出合同 PDF 包、一键导入、一键取消关联、安装包覆盖升级。

---

## English

### Features

| Feature | Description |
|---|---|
| Project overview | Each project folder becomes a project card showing progress, contract value, file count and missing documents |
| One page per project | Summary, workflow stages, key facts, finance, contract ledger, all files and notes on a single scrollable page |
| Auto-classification | Files are sorted into categories (approval, design, tendering, contracts, construction, acceptance, settlement, final accounts, payments, personnel…); drag files into the window to archive them, archives are unpacked automatically |
| Full-text search | Searches Word, Excel, scanned PDFs and photos (scans are recognised with the built-in Windows Chinese OCR) |
| Contracts & finance | Contract amount / paid / unpaid; payment records linked to accounting vouchers and invoice originals; payment progress at a glance |
| Invoices | Splits individual invoices out of mixed contract/voucher PDFs; recognises amount, date and seller of e-invoices; a re-issued invoice can replace the old one in one click |
| Export | Project summary, file catalogue and contract ledger to Excel; one-click "contract PDF pack" with an index sheet and a web preview |
| Batch import / unlink | Imports folders named after projects in bulk; merges mistakenly created "category folder" projects in one click |
| **AI form filling** | Pick an Excel sheet sent by a higher authority; the AI finds the rows that belong to your projects, reads the original documents and fills only the blank cells, leaving cells blank when no evidence is found. Filled cells are highlighted and annotated with the source |
| **AI contract & payment extraction** | The AI reads contracts, award notices, vouchers, bank slips and invoices and proposes ledger entries for review (an invoice alone is not treated as a payment) |
| **AI assistant** | Chat-style: ask questions about the archive, fill sheets from the archive, edit Word/Excel files, draft reports (Word) or summaries (Excel). Results are always saved as new files; originals are never modified |

### Requirements

- Windows 10 / 11 (64-bit)
- .NET Framework 4.x (built into Windows)
- Optional: Microsoft Office or WPS — needed for legacy `.doc` / `.xls`, AI form filling and Word-to-PDF conversion
- Optional: an AI API key — only for the AI features

### Installation

Download `项目档案室安装程序.exe` from [Releases](../../releases) and run it. An existing installation is upgraded in place; the archive database and settings are kept.

On first launch, choose the root folder that contains all project folders. The app scans, classifies and builds the archive database automatically.

### AI configuration

Settings → *AI 接口设置*: choose a provider, enter the API key and click *Test connection*. Any OpenAI-compatible `/chat/completions` endpoint works (DeepSeek, Qwen, Kimi, GLM, or custom).

- The key is encrypted with the current Windows account (DPAPI) and stored locally in `%APPDATA%\项目档案室\ai.json`. It is **never written to the archive database or the installer**.
- All data stays on your computer. Only when you use an AI feature is the relevant project text sent to the API you configured.
- AI output is always shown with its evidence for human review before anything is written.

### Build from source

No Visual Studio needed — the script uses the C# compiler shipped with Windows:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Outputs `build\out\项目档案室.exe` and the installer `安装包\项目档案室安装程序.exe`.

### Changelog

- **1.1** — AI form filling, AI contract & payment extraction, AI assistant; e-invoice import and re-issued invoice replacement; restore removed files and add files per category; AI trusts original documents over manual records (amounts in Chinese capital numerals win) and uses a calculator tool for arithmetic.
- **1.0** — Project overview and project page, auto-classification, full-text search, contract ledger, finance and invoice splitting, contract PDF pack export, batch import, one-click unlink, in-place upgrade installer.
