using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5000");
builder.Services.AddCors();

var app = builder.Build();
app.UseCors(builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

var publicPath = Path.Combine(Directory.GetCurrentDirectory(), "public");
if (!Directory.Exists(publicPath)) Directory.CreateDirectory(publicPath);

app.UseDefaultFiles(new DefaultFilesOptions { DefaultFileNames = new[] { "index.html" }, FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicPath) });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicPath) });

string dbPath = "database.sqlite";
InitDatabase(dbPath);

// API STATUS
app.MapGet("/api/status", async (HttpResponse response) => await response.WriteAsJsonAsync(new { is_activated = IsSystemActivated(dbPath) }));

// API AKTIVASI
app.MapPost("/api/activate", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var code = JsonDocument.Parse(body).RootElement.GetProperty("code").GetString() ?? "";

    if (code == "Sukses1234#")
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO settings (key_name, value_data) VALUES ('activated', 'true')";
        cmd.ExecuteNonQuery();
        await context.Response.WriteAsJsonAsync(new { status = "success", message = "FULL VERSION AKTIF!" });
    }
    else
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { status = "error", message = "Kode Salah!" });
    }
});

// API GET DATA
app.MapGet("/api/siswa", async (HttpResponse response) =>
{
    var list = new List<object>();
    using var conn = new SqliteConnection($"Data Source={dbPath}");
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT nis, nama, timestamp, folder_path, json_kamera, json_jari, json_biodata FROM siswa ORDER BY timestamp DESC";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        list.Add(new {
            nis = reader.GetString(0),
            nama = reader.GetString(1),
            timestamp = reader.GetString(2),
            folder = reader.GetString(3),
            json_kamera = reader.IsDBNull(4) ? "{}" : reader.GetString(4),
            json_jari = reader.IsDBNull(5) ? "{}" : reader.GetString(5),
            json_biodata = reader.IsDBNull(6) ? "{}" : reader.GetString(6)
        });
    }
    await response.WriteAsJsonAsync(list);
});

// API DELETE DATA
app.MapDelete("/api/siswa/{nis}", async (string nis, HttpContext context) =>
{
    using var conn = new SqliteConnection($"Data Source={dbPath}");
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT folder_path FROM siswa WHERE nis = $nis";
    cmd.Parameters.AddWithValue("$nis", nis);
    var folder = cmd.ExecuteScalar()?.ToString();

    var cmdDel = conn.CreateCommand();
    cmdDel.CommandText = "DELETE FROM siswa WHERE nis = $nis";
    cmdDel.Parameters.AddWithValue("$nis", nis);
    cmdDel.ExecuteNonQuery();

    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        Directory.Delete(folder, true);

    await context.Response.WriteAsJsonAsync(new { status = "success", message = "Data terhapus!" });
});

// API POST / UPDATE DATA
app.MapPost("/api/save", async (HttpContext context) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var root = JsonDocument.Parse(await reader.ReadToEndAsync()).RootElement;

        string nis = root.GetProperty("nis").GetString() ?? "";
        string nama = root.GetProperty("nama").GetString() ?? "";
        string timestamp = root.GetProperty("timestamp").GetString() ?? DateTime.Now.ToString();
        
        var biodataObj = new {
            tgl_lahir = root.GetProperty("tgl_lahir").GetString(),
            jk = root.GetProperty("jk").GetString(),
            parents = root.GetProperty("parents").GetString(),
            no_wa = root.GetProperty("no_wa").GetString(),
            email = root.GetProperty("email").GetString(),
            alamat = root.GetProperty("alamat").GetString()
        };
        string jsonBiodata = JsonSerializer.Serialize(biodataObj);

        bool isFull = IsSystemActivated(dbPath);
        bool dataExists = false;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM siswa WHERE nis = $nis";
        checkCmd.Parameters.AddWithValue("$nis", nis);
        dataExists = (long)(checkCmd.ExecuteScalar() ?? 0) > 0;

        if (!isFull && !dataExists)
        {
            var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM siswa";
            if ((long)(countCmd.ExecuteScalar() ?? 0) >= 10)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { status = "error", message = "Limit Demo Tercapai!" });
                return;
            }
        }

        string folderName = isFull ? $"{nis}_{nama.Replace(" ", "_")}" : $"DEMO_{nis}_{nama.Replace(" ", "_")}";
        string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Output_Biometrik", folderName);
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        var kamera = root.GetProperty("kamera");
        var jari = root.GetProperty("sidik_jari");

        SaveBase64ToFile(kamera.GetProperty("face_front").GetString(), Path.Combine(outputDir, "face_front.jpg"));
        SaveBase64ToFile(kamera.GetProperty("apd_left").GetString(), Path.Combine(outputDir, "apd_left.jpg"));
        SaveBase64ToFile(kamera.GetProperty("apd_right").GetString(), Path.Combine(outputDir, "apd_right.jpg"));

        foreach (var jProp in jari.EnumerateObject())
        foreach (var aProp in jProp.Value.EnumerateObject())
            SaveBase64ToFile(aProp.Value.GetString(), Path.Combine(outputDir, $"{jProp.Name.Split('_')[0]}{aProp.Name.Substring(0,1).ToLower()}.jpg"));

        var cmd = conn.CreateCommand();
        if (dataExists)
            cmd.CommandText = "UPDATE siswa SET nama=$nama, timestamp=$timestamp, folder_path=$folder, json_kamera=$cam, json_jari=$fp, json_biodata=$bio WHERE nis=$nis";
        else
            cmd.CommandText = "INSERT INTO siswa (nis, nama, timestamp, folder_path, json_kamera, json_jari, json_biodata) VALUES ($nis, $nama, $timestamp, $folder, $cam, $fp, $bio)";
        
        cmd.Parameters.AddWithValue("$nis", nis);
        cmd.Parameters.AddWithValue("$nama", nama);
        cmd.Parameters.AddWithValue("$timestamp", timestamp);
        cmd.Parameters.AddWithValue("$folder", Path.Combine("Output_Biometrik", folderName));
        cmd.Parameters.AddWithValue("$cam", kamera.ToString());
        cmd.Parameters.AddWithValue("$fp", jari.ToString());
        cmd.Parameters.AddWithValue("$bio", jsonBiodata);
        cmd.ExecuteNonQuery();

        await context.Response.WriteAsJsonAsync(new { status = "success", message = dataExists ? "Data diupdate!" : "Data baru tersimpan!" });
    }
    catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { status = "error", message = ex.Message }); }
});

// ==============================================================
// API SCAN FINGERPRINT (VERSI ANTI-CASTING ERROR & PATH RTE)
// ==============================================================
app.MapGet("/api/scan_fingerprint", async (HttpResponse response) =>
{
    try 
    {
        // 1. Path Absolut RTE
        string dllPath = @"C:\Program Files\DigitalPersona\U.are.U RTE\Windows\Lib\.NET\DPUruNet.dll";
        
        // 2. Fallback Path Portable
        if (!System.IO.File.Exists(dllPath))
        {
            dllPath = Path.Combine(Directory.GetCurrentDirectory(), "DPUruNet.dll");
        }

        // 3. Validasi
        if (!System.IO.File.Exists(dllPath))
        {
            throw new Exception("File 'DPUruNet.dll' tidak ditemukan! Pastikan sudah terinstal di folder RTE atau sudah dicopy ke sebelah MRD_Engine.exe.");
        }

        // 4. Load DLL (Reflection Murni Tanpa Casting)
        var dpAssembly = Assembly.LoadFile(dllPath);
        Type readerCollectionType = dpAssembly.GetType("DPUruNet.ReaderCollection");
        
        // Ambil object Readers
        var readersObj = readerCollectionType.GetMethod("GetReaders").Invoke(null, null);

        // Baca jumlah mesin
        var countProp = readersObj.GetType().GetProperty("Count");
        int count = (int)countProp.GetValue(readersObj);

        if (count == 0)
        {
            throw new Exception("Mesin U.are.U 4500 tidak terdeteksi! Coba cabut-colok kabel USB.");
        }

        // Ambil mesin pertama (index ke-0)
        var itemProp = readersObj.GetType().GetProperty("Item");
        var reader = itemProp.GetValue(readersObj, new object[] { 0 });

        // Ubah prioritas koneksi menggunakan tipe bawaan pabriknya
        Type prioType = dpAssembly.GetType("DPUruNet.Constants+Priorities");
        object priority = Enum.ToObject(prioType, 1);

        // Buka Koneksi ke Scanner (Lampu akan menyala)
        var openMethod = reader.GetType().GetMethod("Open");
        openMethod.Invoke(reader, new object[] { priority });

        // Tunggu 3 Detik (Simulasi menunggu jari / agar lampu nyala terlihat)
        await Task.Delay(3000); 

        // Tutup Koneksi (Lampu mati)
        var disposeMethod = reader.GetType().GetMethod("Dispose");
        disposeMethod.Invoke(reader, null);

        // Kirim dummy image agar tidak error saat dirender ke UI
        string hasilScanBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="; 
        
        await response.WriteAsJsonAsync(new { status = "success", base64 = hasilScanBase64 });
    }
    catch (Exception ex)
    {
        response.StatusCode = 500;
        await response.WriteAsJsonAsync(new { status = "error", message = ex.Message + " | Bantuan: " + ex.InnerException?.Message });
    }
});

// ==============================================================
// AUTO-LAUNCHER WEBVIEW DESKTOP MODE (VERSI ANTI AUTO-CLOSE)
// ==============================================================
app.Lifetime.ApplicationStarted.Register(() =>
{
    Task.Run(async () =>
    {
        await Task.Delay(1000); 
        string url = "http://localhost:5000";
        try
        {
            Process.Start(new ProcessStartInfo("msedge", $"--app={url}") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    });
});

app.Run();

// ==============================================================
// FUNGSI BANTUAN DATABASE & FILE
// ==============================================================
static void InitDatabase(string dbPath)
{
    using var conn = new SqliteConnection($"Data Source={dbPath}");
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS siswa (nis TEXT PRIMARY KEY, nama TEXT, timestamp TEXT, folder_path TEXT, json_kamera TEXT, json_jari TEXT, json_biodata TEXT);
        CREATE TABLE IF NOT EXISTS settings (key_name TEXT PRIMARY KEY, value_data TEXT);
    ";
    cmd.ExecuteNonQuery();
}
static bool IsSystemActivated(string dbPath)
{
    try {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value_data FROM settings WHERE key_name = 'activated'";
        return (cmd.ExecuteScalar()?.ToString() == "true");
    } catch { return false; }
}
static void SaveBase64ToFile(string? b64, string path)
{
    if (string.IsNullOrEmpty(b64)) return;
    try { File.WriteAllBytes(path, Convert.FromBase64String(b64.Contains(",") ? b64.Split(',')[1] : b64)); } catch {}
}
