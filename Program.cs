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
builder.Services.AddCors();

var app = builder.Build();
app.UseCors(builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

// Setup agar C# membaca index.html dari folder "public"
var publicPath = Path.Combine(Directory.GetCurrentDirectory(), "public");
if (!Directory.Exists(publicPath)) Directory.CreateDirectory(publicPath);

app.UseDefaultFiles(new DefaultFilesOptions { DefaultFileNames = new[] { "index.html" }, FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicPath) });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicPath) });

// Inisialisasi Database SQLite
string dbPath = "database.sqlite";
InitDatabase(dbPath);

// ========================================================
// REVISI: API STATUS AKTIVASI (Cek Apakah Sistem Full / Demo)
// ========================================================
app.MapGet("/api/status", async (HttpResponse response) =>
{
    bool activated = IsSystemActivated(dbPath);
    await response.WriteAsJsonAsync(new { is_activated = activated });
});

// ========================================================
// REVISI: API UNTUK VERIFIKASI KODE AKTIVASI TOKEN (Sukses1234#)
// ========================================================
app.MapPost("/api/activate", async (HttpContext context) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        string inputCode = root.GetProperty("code").GetString() ?? "";

        // Kunci Pas Aktivasi dari Suhu
        if (inputCode == "Sukses1234#")
        {
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO settings (key_name, value_data) VALUES ('activated', 'true')";
                cmd.ExecuteNonQuery();
            }
            await context.Response.WriteAsJsonAsync(new { status = "success", message = "Aktivasi Berhasil! MRD Biometric Engine kini berstatus FULL VERSION permanen." });
        }
        else
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { status = "error", message = "Kode Aktivasi Salah/Tidak Valid!" });
        }
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { status = "error", message = ex.Message });
    }
});

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
                // Ekstraksi data kamera depan & jari L1 untuk preview modal gambar di UI
                var jsonKamera = reader.IsDBNull(4) ? "{}" : reader.GetString(4);
                var jsonJari = reader.IsDBNull(5) ? "{}" : reader.GetString(5);

                string faceFrontB64 = "";
                string jariL1B64 = "";

                try {
                    using var camDoc = JsonDocument.Parse(jsonKamera);
                    faceFrontB64 = camDoc.RootElement.GetProperty("face_front").GetString() ?? "";
                } catch {}

                try {
                    using var jariDoc = JsonDocument.Parse(jsonJari);
                    jariL1B64 = jariDoc.RootElement.GetProperty("L1_kelingking").GetProperty("front").GetString() ?? "";
                } catch {}

                listSiswa.Add(new {
                    timestamp = reader.GetString(2),
                    nis = reader.GetString(0),
                    nama = reader.GetString(1),
                    tgl_lahir = "-", jk = "-", parents = "-", no_wa = "-", alamat = "-", 
                    folder = reader.GetString(3),
                    media_wajah = faceFrontB64,
                    media_jari_L1 = jariL1B64
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
        // --------------------------------------------------------
        // REVISI LOGIKA: PROTEKSI MAKSIMAL 10 DATA JIKA BELUM AKTIF
        // --------------------------------------------------------
        bool isFullVersion = IsSystemActivated(dbPath);
        if (!isFullVersion)
        {
            long currentCount = 0;
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM siswa";
                currentCount = (long)(checkCmd.ExecuteScalar() ?? 0L);
            }

            if (currentCount >= 10)
            {
                context.Response.StatusCode = 403; // Forbidden
                await context.Response.WriteAsJsonAsync(new { 
                    status = "error", 
                    message = "Kuota Registrasi Versi Demo Terbatas (Maksimal 10 Data Siswa). Silakan masukkan Kode Aktivasi di tab Data Tersimpan untuk meningkatkan ke Full Version!" 
                });
                return;
            }
        }
        // --------------------------------------------------------

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        string nis = root.GetProperty("nis").GetString() ?? "000";
        string nama = root.GetProperty("nama").GetString() ?? "Fulan";
        string timestamp = root.GetProperty("timestamp").GetString() ?? DateTime.Now.ToString();
        
        string rootOutputName = "Output_Biometrik";
        
        // REVISI LOGIKA: Tambahkan watermark DEMO_ jika lisensi belum aktif
        string folderName = isFullVersion ? $"{nis}_{nama.Replace(" ", "_")}" : $"DEMO_{nis}_{nama.Replace(" ", "_")}";
        
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

        string successMessage = isFullVersion ? "Folder Dinas standar telah dibuat." : "Folder terbuat dengan tanda air [DEMO].";
        await context.Response.WriteAsJsonAsync(new { status = "success", message = successMessage });
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
        CREATE TABLE IF NOT EXISTS settings (
            key_name TEXT PRIMARY KEY,
            value_data TEXT
        );
    ";
    command.ExecuteNonQuery();
}

// REVISI HELPER: Cek Status Lisensi Terkini di SQLite
static bool IsSystemActivated(string dbPath)
{
    try
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value_data FROM settings WHERE key_name = 'activated'";
        var result = cmd.ExecuteScalar();
        return result != null && result.ToString() == "true";
    }
    catch
    {
        return false;
    }
}

static void SaveBase64ToFile(string? base64, string filePath)
{
    if (string.IsNullOrEmpty(base64)) return;
    try
    {
        if (base64.Contains(",")) base64 = base64.Split(',')[1];
        byte[] bytes = Convert.FromBase64String(base64);
        File.WriteAllBytes(filePath, bytes);
    }
    catch { }
}
