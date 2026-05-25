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
using System.Threading;

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
// API SCAN FINGERPRINT (VERSI RADAR POLLING 1 DETIK)
// ==============================================================
app.MapGet("/api/scan_fingerprint", async (HttpResponse response) =>
{
    try 
    {
        string dllPath = @"C:\Program Files\DigitalPersona\U.are.U RTE\Windows\Lib\.NET\DPUruNet.dll";
        if (!System.IO.File.Exists(dllPath)) dllPath = Path.Combine(Directory.GetCurrentDirectory(), "DPUruNet.dll");
        if (!System.IO.File.Exists(dllPath)) throw new Exception("File DLL DPUruNet tidak ditemukan!");

        var dpAssembly = Assembly.LoadFile(dllPath);
        Type readerColType = dpAssembly.GetType("DPUruNet.ReaderCollection");
        var readersObj = readerColType.GetMethod("GetReaders").Invoke(null, null);

        int count = Convert.ToInt32(readersObj.GetType().GetProperty("Count").GetValue(readersObj));
        if (count == 0) throw new Exception("Alat U.are.U 4500 tidak terdeteksi! Pastikan USB tertancap kuat.");

        var reader = readersObj.GetType().GetProperty("Item").GetValue(readersObj, new object[] { 0 });
        
        // --- 1. BUKA KONEKSI ---
        var openMethod = reader.GetType().GetMethod("Open");
        Type prioType = openMethod.GetParameters()[0].ParameterType;
        
        // Mode Cooperative (1) lebih stabil untuk API Background
        object priority = Enum.ToObject(prioType, 1); 
        int openCode = Convert.ToInt32(openMethod.Invoke(reader, new object[] { priority }));
        if (openCode != 0) 
        {
            priority = Enum.ToObject(prioType, 2); 
            openCode = Convert.ToInt32(openMethod.Invoke(reader, new object[] { priority }));
            if (openCode != 0) throw new Exception($"Alat Terkunci oleh sistem! (Kode: {openCode}). CABUT & COLOK ULANG USB.");
        }

        string finalBase64 = "";

        try
        {
            object caps = reader.GetType().GetProperty("Capabilities").GetValue(reader);
            int[] resolutions = (int[])caps.GetType().GetProperty("Resolutions").GetValue(caps);
            int res = resolutions.Length > 0 ? resolutions[0] : 500;

            MethodInfo captureMethod = null;
            foreach (var method in reader.GetType().GetMethods())
            {
                if (method.Name == "Capture" && method.GetParameters().Length == 4) { captureMethod = method; break; }
            }
            if (captureMethod == null) throw new Exception("Fungsi Capture tidak ditemukan.");

            var pInfos = captureMethod.GetParameters();
            
            // PAKSA format 0 (Raw Image) agar sensor tidak menolak jari yang sedikit cacat
            object fidFormat = Enum.ToObject(pInfos[0].ParameterType, 0); 
            object captureProc = Enum.ToObject(pInfos[1].ParameterType, 0); 
            
            // --- 2. TEKNIK RADAR POLLING (1 Detik x 20 Kali) ---
            object finalCaptureResult = null;
            bool isCaptured = false;
            int lastQuality = -1;

            // Kita tembak sensor 20 kali, masing-masing selama 1 detik (Total 20 detik maksimal)
            for (int i = 0; i < 20; i++)
            {
                // Timeout 1000ms (1 Detik)
                object captureResult = captureMethod.Invoke(reader, new object[] { fidFormat, captureProc, 1000, res });
                
                int resultCode = Convert.ToInt32(captureResult.GetType().GetProperty("ResultCode").GetValue(captureResult));
                
                if (resultCode == 0) // Jika eksekusi berhasil
                {
                    object qualityObj = captureResult.GetType().GetProperty("Quality").GetValue(captureResult);
                    int qualityCode = Convert.ToInt32(qualityObj);
                    
                    if (qualityCode == 0) // 0 = GOOD (Gambar Berhasil Diambil!)
                    {
                        finalCaptureResult = captureResult;
                        isCaptured = true;
                        break; // Langsung KELUAR dari putaran 20 detik!
                    }
                    else if (qualityCode != 1) // 1 = Timeout. Jika selain 1 & 0, artinya ada jari tapi miring/basah
                    {
                        lastQuality = qualityCode; // Simpan errornya, lalu terus ulangi putaran
                    }
                }
                
                // Beri nafas ke USB bus selama 50ms sebelum nembak lagi
                await Task.Delay(50);
            }

            if (!isCaptured)
            {
                if (lastQuality != -1) throw new Exception($"Jari terdeteksi tapi kualitas gambar sangat buruk/miring. Lap kaca scanner dengan tisu. (Kode: {lastQuality})");
                throw new Exception("Waktu Habis (20 Detik)! Anda belum menempelkan jari ke scanner.");
            }

            // --- 3. EKSTRAK GAMBAR MENTAH ---
            object dataObj = finalCaptureResult.GetType().GetProperty("Data").GetValue(finalCaptureResult);
            if (dataObj == null) throw new Exception("Data sidik jari kosong!");

            var viewsObj = (System.Collections.IList)dataObj.GetType().GetProperty("Views").GetValue(dataObj);
            if (viewsObj.Count == 0) throw new Exception("Tidak ada frame gambar tertangkap!");

            var fivObj = viewsObj[0]; 
            int width = Convert.ToInt32(fivObj.GetType().GetProperty("Width").GetValue(fivObj));
            int height = Convert.ToInt32(fivObj.GetType().GetProperty("Height").GetValue(fivObj));
            
            var rawProp = fivObj.GetType().GetProperty("RawImage") ?? fivObj.GetType().GetProperty("Bytes") ?? fivObj.GetType().GetProperty("Data");
            if (rawProp == null) throw new Exception("Properti byte gambar gagal dibaca.");

            byte[] rawBytes = (byte[])rawProp.GetValue(fivObj);

            // --- 4. RAKIT JADI BMP ---
            byte[] bmpBytes = CreateGrayscaleBmp(rawBytes, width, height);
            finalBase64 = Convert.ToBase64String(bmpBytes);
        }
        finally
        {
            reader.GetType().GetMethod("Dispose").Invoke(reader, null);
        }
        
        await response.WriteAsJsonAsync(new { status = "success", base64 = finalBase64 });
    }
    catch (Exception ex)
    {
        response.StatusCode = 500;
        await response.WriteAsJsonAsync(new { status = "error", message = ex.InnerException?.Message ?? ex.Message });
    }
});

// ==============================================================
// AUTO-LAUNCHER WEBVIEW
// ==============================================================
app.Lifetime.ApplicationStarted.Register(() =>
{
    Task.Run(async () =>
    {
        await Task.Delay(1000); 
        try { Process.Start(new ProcessStartInfo("msedge", $"--app=http://localhost:5000") { UseShellExecute = true }); }
        catch { try { Process.Start(new ProcessStartInfo("http://localhost:5000") { UseShellExecute = true }); } catch { } }
    });
});

app.Run();

// ==============================================================
// FUNGSI BANTUAN DATABASE & PERAKIT GAMBAR BMP
// ==============================================================
static void InitDatabase(string dbPath)
{
    using var conn = new SqliteConnection($"Data Source={dbPath}");
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "CREATE TABLE IF NOT EXISTS siswa (nis TEXT PRIMARY KEY, nama TEXT, timestamp TEXT, folder_path TEXT, json_kamera TEXT, json_jari TEXT, json_biodata TEXT); CREATE TABLE IF NOT EXISTS settings (key_name TEXT PRIMARY KEY, value_data TEXT);";
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

// JURUS MERAKIT GAMBAR RAW MENJADI BMP
static byte[] CreateGrayscaleBmp(byte[] rawData, int width, int height)
{
    int stride = width;
    if (width % 4 != 0) stride += 4 - (width % 4); 

    int paletteSize = 256 * 4;
    int headerSize = 14 + 40 + paletteSize; 
    int fileSize = headerSize + (stride * height);

    byte[] bmp = new byte[fileSize];

    bmp[0] = 0x42; bmp[1] = 0x4D; 
    BitConverter.GetBytes(fileSize).CopyTo(bmp, 2); 
    bmp[10] = (byte)headerSize; 

    bmp[14] = 40; 
    BitConverter.GetBytes(width).CopyTo(bmp, 18);
    BitConverter.GetBytes(height).CopyTo(bmp, 22); 
    bmp[26] = 1; 
    bmp[28] = 8; 
    BitConverter.GetBytes(stride * height).CopyTo(bmp, 34); 

    for (int i = 0; i < 256; i++)
    {
        int offset = headerSize - paletteSize + (i * 4);
        bmp[offset] = (byte)i;     
        bmp[offset + 1] = (byte)i; 
        bmp[offset + 2] = (byte)i; 
        bmp[offset + 3] = 0;       
    }

    for (int y = 0; y < height; y++)
    {
        Buffer.BlockCopy(rawData, y * width, bmp, headerSize + ((height - 1 - y) * stride), width);
    }

    return bmp;
}
