# 🏢 AdventureWorksDW2025 雲地整合 AI 智慧問答系統 (RAG)

本系統是一套結合在地端安全資料庫與雲端先進大語言模型的企業級問答解決方案。利用 **SQL Server 2025 原生向量資料類型** 儲存與檢索在地知識（地），並透過 **C# (.NET 8/9) 呼叫 Google Gemini API** 進行語義理解與對話生成（雲）。

---

## 🏗️ 系統架構設計

| 階層 | 元件 | 負責任務 |
| :--- | :--- | :--- |
| **地（在地資料）** | SQL Server 2025 (`AdventureWorksDW2025`) | 儲存產品/企業文本，並使用原生 `VECTOR(768)` 與 `VECTOR_DISTANCE` 進行高速在地檢索。 |
| **地（中介運算）** | C# Console Application | 處理使用者輸入、排程與執行資料清洗（Data Cleaner）與 RAG 提示詞整合。 |
| **雲（大語言模型）** | Google Gemini API (`gemini-embedding-001` & `gemini-1.5-flash`) | 1. 負責將中文提問轉為 768 維度語義向量。<br>2. 整合地端知識生成無幻覺的繁體中文解答。 |

---

## ⚙️ 環境準備與安裝步驟

### 1. 地端資料庫初始化 (SQL Server 2025)
請於 **SQL Server Management Studio (SSMS)** 中連結至資料庫，並對 `AdventureWorksDW2025` 執行以下指令，以建置向量欄位與專屬預存程序：

```sql
USE [AdventureWorksDW2025];
GO

-- 建立 768 維度向量欄位
ALTER TABLE [dbo].[DimProduct] DROP COLUMN IF EXISTS [ProductDescription_Vector];
ALTER TABLE [dbo].[DimProduct] ADD [ProductDescription_Vector] VECTOR(768) NULL;
GO

-- 建立向量搜尋預存程序
CREATE OR ALTER PROCEDURE [dbo].[SearchSimilarProducts]
    @QueryVector VECTOR(768),
    @TopK INT = 3
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@TopK) 
        [ProductKey], [EnglishProductName], [EnglishDescription],
        VECTOR_DISTANCE('cosine', [dbo].[DimProduct].[ProductDescription_Vector], @QueryVector) AS Distance
    FROM [dbo].[DimProduct]
    WHERE [dbo].[DimProduct].[ProductDescription_Vector] IS NOT NULL
    ORDER BY Distance ASC;
END;
GO
```

### 2. C# 專案配置
請確保您的 C# 專案已安裝最新版微軟官方 SQL 驅動套件：
```bash
dotnet add package Microsoft.Data.SqlClient
```

---

## 🚀 執行流程說明

### 步驟一：執行資料清洗與初始向量化 (`DataCleaner.cs`)
在問答前，必須先將地端資料庫中的舊產品描述進行初始化升維。
1. 開啟 `DataCleaner.cs` 並在 `requestUrl` 中填入寫死的 Gemini API 金鑰。
2. 執行程式，系統將會透過 `SqlDataAdapter` 以全防禦 DataTable 載入 396 筆產品。
3. 呼叫 `gemini-embedding-001` 並限制 `outputDimensionality = 768`，將生成的 JSON 向量陣列安全寫回 `DimProduct` 表中。

### 步驟二：啟動互動式對話主程式 (`Program.cs`)
1. 啟動問答 Console，在畫面提示 `👤 使用者提問：` 後輸入查詢意圖（例如：`我想找避震效果好的山地自行車`）。
2. **雲端轉換**：提問字串傳送至 Gemini 向量端點，產出 768 維的提問向量。
3. **地端運算**：提問向量傳入 SQL Server 2025，利用 `VECTOR_DISTANCE` 高速拉出最相關的前 3 筆真實產品規格。
4. **雲端生成**：將地端上下文結合手工組裝的全小寫標準 JSON `contents` 結構（帶有 `role: "user"` 標籤）送至 `gemini-1.5-flash`，由 AI 彙整輸出流暢且無幻覺的繁體中文專業回覆。

---

## 💡 技術盲點與核心避坑指南

* **401 Unauthorized 錯誤**：Gemini API 拒絕帶有空格或加號拼接異常的變數。本專案將 URL 參數與金鑰完全手工寫死在引號內，並以 `.Trim()` 去除任何潛在空格污染。
* **404 Not Found 錯誤**：免費版金鑰（Free Tier）已被 Google 強制收攏，本專案全面棄用已淘汰或限制付費專案的測試模型，全面校準為長青穩定的全球通用端點：
  * **Embedding**: `https://googleapis.com`
  * **Chat**: `https://googleapis.com`
  * *註：網址前綴必須完整包含子網域 `generativelanguage.` 才能被路由辨識。*
* **400 Bad Request 錯誤**：新世代 Gemini 嚴格限制外層 JSON 鍵值必須為**全小寫**（如 `contents`、`role`、`parts`），且必須聲明 `role: "user"`。本專案透過純手工字串替換法封裝 JSON，完美規避 C# 匿名物件轉型大寫所引發的 Gateway 驗證衝突。
* **維度不相符錯誤**：SQL Server 2025 原生向量上限為 1998 維。本專案透過在 Body 內附加大寫 D 的駝峰參數 "outputDimensionality: 768" 進行精準降維，完美對齊地端 `VECTOR(768)` 限制。
