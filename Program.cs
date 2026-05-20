using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System;

var builder = WebApplication.CreateBuilder(args);

// Set port lokal murni offline
builder.WebHost.UseUrls("http://localhost:5000");

// Tambahkan CORS (berjaga-jaga saat testing)
builder.Services.AddCors();

var app = builder.Build();
app.UseCors(builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

// 1. Setup agar C# membaca index.html dari folder "public"
var publicPath = Path.Combine(Directory.GetCurrentDirectory(), "public");
if (!Directory.Exists(publicPath)) Directory.CreateDirectory(publicPath);

app.UseDefaultFiles(new DefaultFilesOptions { DefaultFileNames = new[] { "index.html" }, FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicPath) });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicPath) });

// 2. Inisialisasi Database SQLite
string dbPath = "database.sqlite";
InitDatabase(dbPath);

// ========================================================
// API 1: GET DATA SISWA (Untuk Tabel Data Tersimpan)
// ========================================================
app.MapGet("/api/siswa", async (HttpResponse response) =>
{
    var listSiswa = new System.Collections.Generic.List<object>();
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT nis, nama, timestamp, folder_path, json_kamera, json_jari FROM siswa ORDER BY timestamp DESC";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                listSiswa.Add(new {
                    nis = reader.GetString(0),
                    nama = reader.GetString(1),
                    timestamp = reader.GetString(2),
                    folder = reader.GetString(3),
                    // Hanya mengirim data teks agar tabel cepat, gambar dipanggil terpisah jika perlu
                    tgl_lahir = "-", jk = "-", parents = "-", no_wa = "-", alamat = "-" 
                });
            }
        }
    }
    await response.WriteAsJsonAsync(listSiswa);
});

// ========================================================
// API 2: POST SIMPAN DATA & GENERATE FOLDER 
// ========================================================
app.MapPost("/api/save", async (HttpContext context) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        string nis = root.GetProperty("nis").GetString() ?? "000";
        string nama = root.GetProperty("nama").GetString() ?? "Fulan";
        string timestamp = root.GetProperty("timestamp").GetString() ?? DateTime.Now.ToString();
        
        string rootOutputName = "Output_Biometrik";
        string folderName = $"{nis}_{nama.Replace(" ", "_")}";
        string outputDir = Path.Combine(Directory.GetCurrentDirectory(), rootOutputName, folderName);
        
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        // --- SIMPAN FILE GAMBAR WAJAH & APD ---
        var kamera = root.GetProperty("kamera");
        SaveBase64ToFile(kamera.GetProperty("face_front").GetString(), Path.Combine(outputDir, "face_front.jpg"));
        SaveBase64ToFile(kamera.GetProperty("apd_left").GetString(), Path.Combine(outputDir, "apd_left.jpg"));
        SaveBase64ToFile(kamera.GetProperty("apd_right").GetString(), Path.Combine(outputDir, "apd_right.jpg"));

        // --- SIMPAN FILE SIDIK JARI (.jpg) ---
        var jari = root.GetProperty("sidik_jari");
        foreach (var jariProps in jari.EnumerateObject())
        {
            foreach (var angleProps in jariProps.Value.EnumerateObject())
            {
                string kodeJari = jariProps.Name.Split('_')[0]; 
                string kodeAngle = angleProps.Name.Substring(0, 1).ToLower(); 
                
                // EKSTENSI DIUBAH MENJADI .jpg
                string fileName = $"{kodeJari}{kodeAngle}.jpg"; 
                
                SaveBase64ToFile(angleProps.Value.GetString(), Path.Combine(outputDir, fileName));
            }
        }

        // --- SIMPAN KE SQLITE ---
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO siswa (nis, nama, timestamp, folder_path, json_kamera, json_jari) 
                VALUES ($nis, $nama, $timestamp, $folder, $jsonKamera, $jsonJari)";
            cmd.Parameters.AddWithValue("$nis", nis);
            cmd.Parameters.AddWithValue("$nama", nama);
            cmd.Parameters.AddWithValue("$timestamp", timestamp);
            cmd.Parameters.AddWithValue("$folder", Path.Combine(rootOutputName, folderName));
            cmd.Parameters.AddWithValue("$jsonKamera", kamera.ToString());
            cmd.Parameters.AddWithValue("$jsonJari", jari.ToString());
            cmd.ExecuteNonQuery();
        }

        await context.Response.WriteAsJsonAsync(new { status = "success", message = "Data dan Folder berhasil dibuat" });
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { status = "error", message = ex.Message });
    }
});


app.Run();

// ========================================================
// FUNGSI PEMBANTU (HELPER)
// ========================================================
static void InitDatabase(string dbPath)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS siswa (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            nis TEXT,
            nama TEXT,
            timestamp TEXT,
            folder_path TEXT,
            json_kamera TEXT,
            json_jari TEXT
        );
    ";
    command.ExecuteNonQuery();
}

static void SaveBase64ToFile(string? base64, string filePath)
{
    if (string.IsNullOrEmpty(base64)) return;
    try
    {
        // Bersihkan header jika masih terbawa dari HTML
        if (base64.Contains(",")) base64 = base64.Split(',')[1];
        byte[] bytes = Convert.FromBase64String(base64);
        File.WriteAllBytes(filePath, bytes);
    }
    catch { /* Abaikan jika base64 kosong/rusak */ }
}
