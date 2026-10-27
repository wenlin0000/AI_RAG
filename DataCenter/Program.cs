using System;
using System.Data;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

class DataCleaner
{
    // 地端資料庫連線字串 (請修正您的伺服器名稱)
    private static readonly string DbConnectionString = "Server=localhost;Database=AdventureWorksDW2025;Trusted_Connection=True;Encrypt=False;";

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== 開始執行 AdventureWorksDW2025 資料清洗與向量化 ===");

        try
        {
            DataTable dt = new DataTable();

            // 1. 【地端】安全讀取中轉表
            using (SqlConnection conn = new SqlConnection(DbConnectionString))
            {
                string selectQuery = "SELECT [ProductKey], [ProdName], [ProdDesc] FROM [dbo].[TempCleanProducts]";
                using (SqlDataAdapter adapter = new SqlDataAdapter(selectQuery, conn))
                {
                    await conn.OpenAsync();
                    adapter.Fill(dt);
                }
            }

            Console.WriteLine($"[地端] 成功讀取中轉表！共快取了 {dt.Rows.Count} 筆產品資料開始處理。");

            int successCount = 0;

            // 2. 【地/雲整合】逐筆更新向量
            using (SqlConnection conn = new SqlConnection(DbConnectionString))
            {
                await conn.OpenAsync();

                foreach (DataRow row in dt.Rows)
                {
                    int productKey = Convert.ToInt32(row["ProductKey"]);
                    string prodName = row["ProdName"]?.ToString() ?? "未命名";
                    string prodDesc = row["ProdDesc"]?.ToString() ?? "";

                    if (string.IsNullOrEmpty(prodDesc.Trim())) continue;

                    string cleanedText = $"Product: {prodName}. Description: {prodDesc.Replace("\r", " ").Replace("\n", " ").Trim()}";
                    Console.Write($"[雲端] 正在轉換 ProductKey {productKey} ({prodName})... ");

                    try
                    {
                        // 呼叫下方完全寫死網址的方法
                        float[] vector = await GetGeminiEmbeddingAsync(cleanedText);

                        // 寫回原始 DimProduct 向量欄位
                        string updateQuery = "UPDATE [dbo].[DimProduct] SET [ProductDescription_Vector] = @Vector WHERE [ProductKey] = @ProductKey";
                        using (SqlCommand updateCmd = new SqlCommand(updateQuery, conn))
                        {
                            // 💡 關鍵修正 1：每次執行前，強制清空上一次留下來的舊參數快取
                            updateCmd.Parameters.Clear();
                            string vectorJson = "[" + string.Join(",", vector) + "]";
                            // 💡 關鍵修正 2：明確限制規格，不讓 SqlClient 自動亂抓長度
                            updateCmd.Parameters.Add(new SqlParameter("@Vector", SqlDbType.VarChar, -1) { Value = vectorJson });
                            updateCmd.Parameters.Add(new SqlParameter("@ProductKey", SqlDbType.Int) { Value = productKey });
                            await updateCmd.ExecuteNonQueryAsync();
                        }

                        Console.WriteLine("【成功】");
                        successCount++;

                        //await Task.Delay(250); // 免費額度頻率限制保護
                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"【失敗: {ex.Message}】");
                    }
                }
            }

            Console.WriteLine($"\n=== 清洗完成！成功將 {successCount} / {dt.Rows.Count} 筆向量塞入 DimProduct 表 ===");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ 發生致命錯誤！");
            Console.WriteLine($"訊息: {ex.Message}");
            Console.ResetColor();
        }
    }

    // 💡 2026 最新 1536 維度 Embedding 方法 (網址完全手動寫死)
    private static async Task<float[]> GetGeminiEmbeddingAsync(string text)
    {
        using var client = new HttpClient();

        // ⚠️ 核心修正：整串網址直接在引號內寫死！請直接把 key= 後面的 AQ... 換成您真正的金鑰
        var requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key=YOUR_API_KEY";
        // 💡 修正核心：依據 Google REST 規範，outputDimensionality 必須與 content 同級，放在最外層！
        var requestBody = new
        {
            model = "models/gemini-embedding-001",
            content = new { parts = new[] { new { text = text } } },
            outputDimensionality = 768 // 👈 直接拉到最外層
        };
        var response = await client.PostAsync(requestUrl, new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        string jsonResult = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonResult);
        var values = doc.RootElement.GetProperty("embedding").GetProperty("values");

        float[] vector = new float[values.GetArrayLength()];
        int i = 0;
        foreach (var val in values.EnumerateArray())
        {
            vector[i++] = val.GetSingle();
        }
        return vector;
    }
}
