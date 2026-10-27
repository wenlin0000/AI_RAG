using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
class FileGenerator
{
    public static void GenerateReadme()
    {
        string readmeContent = @"# 🏢 AdventureWorksDW2025 雲地整合 AI 智慧問答系統 (RAG)

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
4. **雲端生成**：將地端上下文結合手工組裝的全小寫標準 JSON `contents` 結構（帶有 `role: ""user""` 標籤）送至 `gemini-1.5-flash`，由 AI 彙整輸出流暢且無幻覺的繁體中文專業回覆。

---

## 💡 技術盲點與核心避坑指南

* **401 Unauthorized 錯誤**：Gemini API 拒絕帶有空格或加號拼接異常的變數。本專案將 URL 參數與金鑰完全手工寫死在引號內，並以 `.Trim()` 去除任何潛在空格污染。
* **404 Not Found 錯誤**：免費版金鑰（Free Tier）已被 Google 強制收攏，本專案全面棄用已淘汰或限制付費專案的測試模型，全面校準為長青穩定的全球通用端點：
  * **Embedding**: `https://googleapis.com`
  * **Chat**: `https://googleapis.com`
  * *註：網址前綴必須完整包含子網域 `generativelanguage.` 才能被路由辨識。*
* **400 Bad Request 錯誤**：新世代 Gemini 嚴格限制外層 JSON 鍵值必須為**全小寫**（如 `contents`、`role`、`parts`），且必須聲明 `role: ""user""`。本專案透過純手工字串替換法封裝 JSON，完美規避 C# 匿名物件轉型大寫所引發的 Gateway 驗證衝突。
* **維度不相符錯誤**：SQL Server 2025 原生向量上限為 1998 維。本專案透過在 Body 內附加大寫 D 的駝峰參數 ""outputDimensionality: 768"" 進行精準降維，完美對齊地端 `VECTOR(768)` 限制。
";

        string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
        File.WriteAllText(outputPath, readmeContent, Encoding.UTF8);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[成功] README.md 檔案已成功匯出至目錄：\n👉 {outputPath}");
        Console.ResetColor();
    }
}

class Program
{
    // ==================== 🛠️ 系統基礎配置設定 ====================
    // 💡 1. 請填入您地端 SQL Server 2025 的伺服器名稱
    private static readonly string DbConnectionString = "Server=localhost;Database=AdventureWorksDW2025;Trusted_Connection=True;Encrypt=False;";

    // 核心記憶庫：儲存多輪對話歷史紀錄
    private static System.Collections.Generic.List<ChatTurnDto> _chatHistory = new System.Collections.Generic.List<ChatTurnDto>();
    // ==============================================================

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=========================================================");
        Console.WriteLine(" 🏢 AdventureWorksDW2025 AI 智慧系統 (記憶+自動儲存日誌)  ");
        Console.WriteLine("=========================================================");
        Console.ResetColor();
        Console.WriteLine(" 💾 系統提示：每一次對話紀錄都將自動異步寫入 [dbo].[AiChatLogs] 表存檔");
        Console.WriteLine(" 🔄 提示：輸入 [clear] 清除記憶 / 輸入 [exit] 結束程式\n");

        while (true)
        {
            Console.WriteLine("\n---------------------------------------------------------");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("👤 使用者提問：");
            Console.ResetColor();

            string userInput = Console.ReadLine()?.Trim() ?? "";
            if (userInput.Length == 0) continue;
            if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            if (userInput.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                _chatHistory.Clear();
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("[系統] 對話歷史記憶已成功清除！");
                Console.ResetColor();
                continue;
            }

            try
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" ☁️  [雲端] 正在轉換語義向量... ");
                float[] queryVector = await GetGeminiEmbeddingAsync(userInput);
                Console.WriteLine("完成。");

                Console.Write(" 🖥️  [地端] 正在執行 SQL Server 2025 向量比對... ");
                string localContext = await SearchLocalDatabaseAsync(queryVector);
                Console.WriteLine("完成。");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" ☁️  [雲端] Gemini 正在組織回答內容...");
                Console.ResetColor();

                string aiResponse = await GenerateGeminiResponseWithHistoryAsync(userInput, localContext);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("🤖 AI 數位助理：");
                Console.ResetColor();
                Console.WriteLine(aiResponse);

                // 在背景異步執行 Log 儲存，完全不卡住使用者的對話流暢度
                _ = SaveChatLogToDatabaseAsync(userInput, localContext, aiResponse);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ [系統發生致命錯誤]");
                Console.WriteLine($"錯誤訊息: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    // 💡 位置一：轉向量方法 (完美寫死完整包含 generativelanguage 的網址)
    private static async Task<float[]> GetGeminiEmbeddingAsync(string text)
    {
        using var client = new HttpClient();

        // ⚠️ 100% 包含 generativelanguage 的真・寫死網址！請直接把 key= 後面的 AQ... 換成您真正的金鑰
        string requestUrl = $"https://generativelanguage.googleapis.com/v1/models/gemini-embedding-001:embedContent?key=YOUR_API_KEY";

        var requestBody = new
        {
            model = "models/gemini-embedding-001",
            content = new { parts = new[] { new { text = text } } },
            outputDimensionality = 768
        };

        var response = await client.PostAsync(requestUrl, new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        string jsonResult = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonResult);
        var values = doc.RootElement.GetProperty("embedding").GetProperty("values");

        float[] vector = new float[values.GetArrayLength()];
        int i = 0;
        foreach (var val in values.EnumerateArray()) { vector[i++] = val.GetSingle(); }
        return vector;
    }

    // 地端資料庫 768 維度預存程序搜尋
    private static async Task<string> SearchLocalDatabaseAsync(float[] queryVector)
    {
        StringBuilder contextBuilder = new StringBuilder();
        using var conn = new SqlConnection(DbConnectionString);
        using var cmd = new SqlCommand("[dbo].[SearchSimilarProducts]", conn);
        cmd.CommandType = CommandType.StoredProcedure;

        string vectorJson = "[" + string.Join(",", queryVector) + "]";
        cmd.Parameters.Add(new SqlParameter("@QueryVector", SqlDbType.VarChar, -1) { Value = vectorJson });
        cmd.Parameters.Add(new SqlParameter("@TopK", SqlDbType.Int) { Value = 3 });

        await conn.OpenAsync();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string prodName = reader["EnglishProductName"]?.ToString() ?? "未命名產品";
            string prodDesc = reader["EnglishDescription"]?.ToString() ?? "";

            prodName = prodName.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ").Trim();
            prodDesc = prodDesc.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ").Trim();

            contextBuilder.AppendLine($"產品名稱: {prodName}");
            contextBuilder.AppendLine($"產品描述: {prodDesc}");
            contextBuilder.AppendLine("-----------------------------------");
        }

        string finalContext = contextBuilder.ToString().Trim();
        return string.IsNullOrEmpty(finalContext) ? "目前地端資料庫查無相關產品參考資訊。" : finalContext;
    }

    // 💡 位置二：智慧問答方法 (同樣完美寫死完整包含 generativelanguage 的網址)
    private static async Task<string> GenerateGeminiResponseWithHistoryAsync(string question, string context)
    {
        using var client = new HttpClient();

        // ⚠️ 100% 包含 generativelanguage 的真・寫死網址！請直接把 key= 後面的 AQ... 換成您真正的金鑰
        string requestUrl = $"https://generativelanguage.googleapis.com/v1/models/gemini-3.5-flash-lite:generateContent?key=YOUR_API_KEY";

        string prompt = $"你是一個專業的 AdventureWorks 企業數位助理。\n" +
                        $"請根據以下由地端資料庫檢索出的真實產品資訊，來回答使用者的提問。\n" +
                        $"【地端參考產品資料】:\n{context}\n" +
                        $"【使用者當前問題】: {question}\n\n" +
                        $"請結合上述真實資料與過去的對話歷史，使用台灣習慣的「繁體中文」進行專業親切的回答：";

        string safePrompt = JsonSerializer.Serialize(prompt);

        // 全小寫手工字串打包，完美包含對話記憶與當前 Prompt
        StringBuilder historyBuilder = new StringBuilder();
        foreach (var historyTurn in _chatHistory)
        {
            string safeHistoryText = JsonSerializer.Serialize(historyTurn.Text);
            historyBuilder.Append("{\"role\": \"" + historyTurn.Role + "\", \"parts\": [{\"text\": " + safeHistoryText + "}]},");
        }

        string jsonPayload = "{" +
                                "\"contents\": [" +
                                    historyBuilder.ToString() + // 灌入過往歷史
                                    "{\"role\": \"user\", \"parts\": [{\"text\": " + safePrompt + "}]}" + // 當前最新 Prompt
                                "]" +
                             "}";

        var response = await client.PostAsync(requestUrl, new StringContent(jsonPayload, Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode)
        {
            string errorDetails = await response.Content.ReadAsStringAsync();
            throw new Exception($"[Gemini 記憶生成失敗]: {errorDetails}");
        }

        string jsonResult = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonResult);
        string aiAnswer = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "AI 未能生成有效的文字回答。";

        // 對話成功後，才將原始問題與回答加入記憶清單
        _chatHistory.Add(new ChatTurnDto { Role = "user", Text = question });
        _chatHistory.Add(new ChatTurnDto { Role = "model", Text = aiAnswer });

        if (_chatHistory.Count > 20) { _chatHistory.RemoveRange(0, 2); }
        return aiAnswer;
    }

    // 自動將問答數據非同步存入地端 SQL Server Log 表
    private static async Task SaveChatLogToDatabaseAsync(string question, string context, string aiResponse)
    {
        try
        {
            using var conn = new SqlConnection(DbConnectionString);
            string insertSql = @"
                INSERT INTO [dbo].[AiChatLogs] ([UserQuestion], [RetrievedContext], [AiResponse])
                VALUES (@UserQuestion, @RetrievedContext, @AiResponse);";

            using var cmd = new SqlCommand(insertSql, conn);
            cmd.Parameters.Add(new SqlParameter("@UserQuestion", SqlDbType.NVarChar, -1) { Value = question });
            cmd.Parameters.Add(new SqlParameter("@RetrievedContext", SqlDbType.NVarChar, -1) { Value = context });
            cmd.Parameters.Add(new SqlParameter("@AiResponse", SqlDbType.NVarChar, -1) { Value = aiResponse });

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[日誌儲存失敗]: {ex.Message}");
        }
    }

    public class ChatTurnDto
    {
        public string Role { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}