// =======================================================
// CORE DOM ELEMENTS
// =======================================================
const video = document.getElementById('webcam');
const canvas = document.getElementById('canvas');
const ctx = canvas.getContext('2d');

// =======================================================
// MODUL 1: WEBCAM (WAJAH & APD) - SUPPORT RETAKE OTOMATIS
// =======================================================
async function startWebcam() {
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ 
            video: { width: 640, height: 480 } 
        });
        if(video) video.srcObject = stream;
    } catch (err) {
        console.error("Gagal akses kamera: ", err);
        alert("Izinkan akses kamera di browser Anda!");
    }
}

function captureImage(type) {
    if(!video || !canvas) return;
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
    
    const base64Data = canvas.toDataURL('image/jpeg', 0.8);
    
    const previewEl = document.getElementById(`preview_${type}`);
    if(previewEl) {
        previewEl.src = base64Data;
        previewEl.style.display = 'block';
    }
    
    const cleanBase64 = base64Data.replace(/^data:image\/[a-z]+;base64,/, "");
    const inputEl = document.getElementById(`b64_${type}`);
    if(inputEl) inputEl.value = cleanBase64;
}

// =======================================================
// MODUL 2: WIZARD U.ARE.U 4500 (30x QUEUE) - WITH PREVIEW
// =======================================================
const fingers = ['L1','L2','L3','L4','L5','R1','R2','R3','R4','R5'];
const angles = ['f', 'l', 'r']; // front, left, right

let captureQueue = [];
let currentStep = 0;
let tempBase64 = ""; // Menampung hasil scan sementara sebelum dikonfirmasi

// Generate 30 Antrean (L1f, L1l, L1r ... R5r)
fingers.forEach(f => {
    angles.forEach(a => {
        captureQueue.push({ id: f + a, label: getFingerLabel(f, a) });
    });
});

function getFingerLabel(f, a) {
    const names = {
        'L1': 'Kelingking Kiri', 'L2': 'Jari Manis Kiri', 'L3': 'Jari Tengah Kiri', 'L4': 'Telunjuk Kiri', 'L5': 'Jempol Kiri',
        'R1': 'Jempol Kanan', 'R2': 'Telunjuk Kanan', 'R3': 'Jari Tengah Kanan', 'R4': 'Jari Manis Kanan', 'R5': 'Kelingking Kanan'
    };
    const pos = { 'f': 'Depan (Front)', 'l': 'Samping Kiri (Left)', 'r': 'Samping Kanan (Right)' };
    return `${names[f]} - Bagian ${pos[a]}`;
}

function startFingerprintWizard() {
    currentStep = 0;
    document.getElementById('btn_fp_action').style.display = 'none';
    showStepInstruction();
    triggerLocalScanner(); 
}

function showStepInstruction() {
    const task = captureQueue[currentStep];
    document.getElementById('fp_label').innerText = `Perekaman: ${task.id}`;
    document.getElementById('fp_instruction').innerText = `Tempelkan ${task.label}`;
    document.getElementById('fp_counter').innerText = `Progress: ${currentStep + 1} / 30`;
}

// Fungsi memanggil Hardware (C# bridge)
function triggerLocalScanner() {
    const instructionEl = document.getElementById('fp_instruction');
    instructionEl.innerText = "⏳ Menunggu Jari di Sensor...";
    instructionEl.style.color = "#3b82f6";

    // Simulasi atau Panggil API Engine C#
    // Ganti URL ini dengan endpoint scan fingerprint di engine kakak jika ada
    fetch('http://localhost:5000/api/scan_fingerprint') 
    .then(res => res.json())
    .then(data => {
        // Asumsi data.base64 adalah hasil dari mesin U.are.U
        tempBase64 = data.base64 || "DUMMY_BASE64_RESULT"; 
        
        // Tampilkan ke Preview
        const previewImg = document.getElementById('fp_preview_img');
        previewImg.src = "data:image/png;base64," + tempBase64;
        document.getElementById('fp_preview_container').style.display = 'block';
        
        instructionEl.innerText = "Selesai Membaca! Silakan Review Gambar.";
        instructionEl.style.color = "#16a34a";
    })
    .catch(err => {
        // Fallback jika API belum siap (Dummy untuk Testing)
        tempBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
        document.getElementById('fp_preview_img').src = "data:image/png;base64," + tempBase64;
        document.getElementById('fp_preview_container').style.display = 'block';
        instructionEl.innerText = "Review Hasil (Mode Simulasi)";
    });
}

function confirmFingerprint() {
    const task = captureQueue[currentStep];
    const inputId = `fp_${task.id}`;
    
    // Simpan ke Hidden Input
    let hiddenInput = document.getElementById(inputId);
    if (!hiddenInput) {
        hiddenInput = document.createElement('input');
        hiddenInput.type = 'hidden';
        hiddenInput.id = inputId;
        document.getElementById('fp_data_container').appendChild(hiddenInput);
    }
    hiddenInput.value = tempBase64;

    // Sembunyikan preview dan lanjut
    document.getElementById('fp_preview_container').style.display = 'none';
    currentStep++;

    if (currentStep < captureQueue.length) {
        showStepInstruction();
        triggerLocalScanner(); // Otomatis trigger jari berikutnya
    } else {
        finalizeWizard();
    }
}

function finalizeWizard() {
    document.getElementById('fp_label').innerText = "✅ PROSES SCAN SELESAI";
    document.getElementById('fp_instruction').innerText = "Silakan cek ulang atau langsung simpan data.";
    document.getElementById('fp_instruction').style.color = "green";
    document.getElementById('fp_counter').innerText = "Progress: 30 / 30";
    
    // Tampilkan tombol simpan & section retake
    document.getElementById('save_section').style.display = "block";
    document.getElementById('retake_section').style.display = "block";
    
    // Isi Dropdown Retake
    const retakeSelect = document.getElementById('retake_select');
    retakeSelect.innerHTML = '<option value="">-- Pilih Jari untuk Review/Retake --</option>';
    captureQueue.forEach(task => {
        const option = document.createElement('option');
        option.value = task.id; 
        option.text = `${task.id} - ${task.label}`;
        retakeSelect.appendChild(option);
    });

    // Tambahkan event listener agar saat pilih dropdown, gambar yang sudah tersimpan muncul
    retakeSelect.onchange = previewSelectedRetake;
}

function previewSelectedRetake() {
    const selectedId = document.getElementById('retake_select').value;
    const statusEl = document.getElementById('retake_status');
    const previewImg = document.getElementById('fp_preview_img');
    const container = document.getElementById('fp_preview_container');

    if(!selectedId) {
        container.style.display = 'none';
        return;
    }

    // Ambil data base64 yang sudah tersimpan di hidden input
    const savedData = document.getElementById(`fp_${selectedId}`).value;
    
    if(savedData) {
        previewImg.src = "data:image/png;base64," + savedData;
        container.style.display = 'block';
        statusEl.innerText = `Menampilkan hasil scan: ${selectedId}`;
        statusEl.style.color = "#64748b";
        statusEl.style.display = "block";
    }
}

// =======================================================
// MODUL 3: RETAKE SPECIFIC FINGERPRINT
// =======================================================
function retakeFingerprint() {
    const selectedId = document.getElementById('retake_select').value;
    const statusEl = document.getElementById('retake_status');
    
    statusEl.innerText = "⏳ Hubungkan sensor untuk " + selectedId + "...";
    statusEl.style.display = "block";
    statusEl.style.color = "#3b82f6";

    fetch('http://localhost:5000/api/scan_fingerprint')
    .then(res => res.json())
    .then(data => {
        const inputId = `fp_${selectedId}`;
        let hiddenInput = document.getElementById(inputId);
        if(hiddenInput) {
            hiddenInput.value = data.base64;
            statusEl.innerText = `✅ Berhasil! Data ${selectedId} telah diperbarui.`;
            statusEl.style.color = "#16a34a";
        }
    })
    .catch(() => {
        statusEl.innerText = "❌ Gagal koneksi ke scanner.";
        statusEl.style.color = "red";
    });
}

// =======================================================
// MODUL 4: KOMPILASI & SUBMIT DATA KE C#
// =======================================================
function simpanDataKeDatabase() {
    const getVal = (id) => document.getElementById(id) ? document.getElementById(id).value : '';

    const nis = getVal('nis');
    const nama = getVal('nama');
    
    if (!nis || !nama) {
        alert("Peringatan: NIS dan Nama Lengkap wajib diisi sebelum menyimpan!");
        return;
    }

    const btnSave = document.querySelector('#save_section button');
    if(btnSave) {
        btnSave.innerText = "⏳ Menyimpan Data...";
        btnSave.disabled = true;
        btnSave.style.background = "#94a3b8";
    }

    const payload = {
        nis: nis,
        nama: nama,
        tgl_lahir: getVal('tgl_lahir'),
        jk: getVal('jk'),
        parents: getVal('parents'),
        no_wa: getVal('no_wa'),
        email: getVal('email'),
        alamat: getVal('alamat'),
        timestamp: new Date().toISOString(),
        kamera: {
            face_front: getVal('b64_face_front'),
            apd_left: getVal('b64_apd_left'),
            apd_right: getVal('b64_apd_right')
        },
        sidik_jari: {}
    };

    captureQueue.forEach(task => {
        const val = getVal(`fp_${task.finger}_${task.angle}`);
        if (!payload.sidik_jari[task.finger]) payload.sidik_jari[task.finger] = {};
        payload.sidik_jari[task.finger][task.angle] = val;
    });

    // KIRIM KE C# REAL ENGINE
    fetch('http://localhost:5000/api/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
    .then(res => res.json())
    .then(data => {
        if(data.status === "success") {
            alert(`Berhasil! Data siswa ${nama} (NIS: ${nis}) telah disimpan.\n\n${data.message}`);
            window.location.reload(); 
        } else {
            alert(`Gagal Menyimpan: ${data.message}`);
            if(btnSave) {
                btnSave.innerText = "💾 SIMPAN DATA REGISTRASI";
                btnSave.disabled = false;
                btnSave.style.background = "#16a34a";
            }
        }
    })
    .catch(err => {
        console.error("Gagal koneksi engine backend", err);
        alert("Koneksi gagal! Pastikan MRD_Engine.exe sudah dijalankan di laptop Windows.");
        if(btnSave) {
            btnSave.innerText = "💾 SIMPAN DATA REGISTRASI";
            btnSave.disabled = false;
            btnSave.style.background = "#16a34a";
        }
    });
}

// =======================================================
// MODUL 5: DATABASE VIEWER, LISENSI & MODAL IMAGE
// =======================================================

// REVISI: Fungsi Cek Status Aktivasi dari SQLite Backend C#
function cekStatusAktivasiBackend() {
    fetch('http://localhost:5000/api/status')
    .then(res => res.json())
    .then(data => {
        const statusEl = document.getElementById('activation_status');
        const formGroup = document.getElementById('activation_form_group');
        
        if(data.is_activated) {
            if(statusEl) {
                statusEl.innerText = "✅ STATUS LISENSI: FULL VERSION AKTIF";
                statusEl.style.color = "#16a34a";
            }
            if(formGroup) formGroup.style.display = "none"; // Sembunyikan input token jika sukses
        } else {
            if(statusEl) {
                statusEl.innerText = "⚠️ STATUS LISENSI: DEMO VERSION (Maksimal 10 Registrasi Siswa)";
                statusEl.style.color = "#d97706";
            }
            if(formGroup) formGroup.style.display = "flex";
        }
    })
    .catch(() => {
        // Fallback offline UI di HP sebelum C# terkoneksi
        const statusEl = document.getElementById('activation_status');
        if(statusEl) statusEl.innerText = "⚠️ Versi Demo (Belum Terkoneksi ke MRD_Engine.exe)";
    });
}

// REVISI: Fungsi kirim Token Aktivasi Ke C# Backend
function eksekusiAktivasiToken() {
    const inputCode = document.getElementById('license_code').value;
    if(!inputCode) {
        alert("Masukkan kode aktivasi terlebih dahulu!");
        return;
    }

    fetch('http://localhost:5000/api/activate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code: inputCode })
    })
    .then(res => res.json())
    .then(data => {
        if(data.status === "success") {
            alert(data.message);
            window.location.reload(); // Refresh halaman untuk aktifkan mode penuh
        } else {
            alert(`Gagal Aktivasi: ${data.message}`);
        }
    })
    .catch(err => {
        alert("Koneksi gagal! Pastikan engine C# menyala untuk memverifikasi lisensi.");
    });
}

function loadDataTersimpan() {
    const tbody = document.getElementById('table_body_siswa');
    tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;">⏳ Memuat data dari SQLite...</td></tr>`;

    fetch('http://localhost:5000/api/siswa')
    .then(res => res.json())
    .then(data => renderTable(data))
    .catch(err => {
        tbody.innerHTML = `<tr><td colspan="8" style="text-align:center; color:red;">Gagal memuat data. Hubungkan ke MRD_Engine.exe!</td></tr>`;
    });
}

function renderTable(dataList) {
    const tbody = document.getElementById('table_body_siswa');
    tbody.innerHTML = ''; 
    
    if (dataList.length === 0) {
        tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;">Tidak ada data tersimpan.</td></tr>`;
        return;
    }

    dataList.forEach(row => {
        // Parse JSON biodata & kamera agar mudah diekstrak
        let bio = {}, cam = {};
        try { bio = JSON.parse(row.json_biodata); } catch(e){}
        try { cam = JSON.parse(row.json_kamera); } catch(e){}

        // Encode seluruh baris agar bisa dikirim ke fungsi Edit
        const encodedRow = encodeURIComponent(JSON.stringify(row));

        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td style="font-size:12px; color:#64748b;">${row.timestamp}</td>
            <td style="font-weight:bold;">${row.nis}</td>
            <td>${row.nama}</td>
            <td style="font-size:13px;">${bio.tgl_lahir || '-'} <br> (${bio.jk || '-'})</td>
            <td style="font-size:13px;">${bio.parents || '-'}</td>
            <td style="font-size:13px;">${bio.no_wa || '-'} <br> <span style="color:#64748b;">${bio.alamat || '-'}</span></td>
            <td><code style="background:#f1f5f9; padding:2px 5px; border-radius:4px; font-size:12px;">${row.folder}</code></td>
            <td style="text-align:center;">
                <button onclick="viewMedia('${row.nama}', 'Wajah', '${cam.face_front || ''}')" style="background:#3b82f6; color:white; padding:5px; font-size:12px; border:none; border-radius:4px; cursor:pointer; width:100%; margin-bottom:4px;">🖼️ Preview</button>
                <div style="display:flex; gap:4px;">
                    <button onclick="editDataSiswa('${encodedRow}')" style="background:#f59e0b; color:white; padding:5px; font-size:12px; border:none; border-radius:4px; cursor:pointer; width:50%;">✏️ Edit</button>
                    <button onclick="hapusDataSiswa('${row.nis}', '${row.nama}')" style="background:#ef4444; color:white; padding:5px; font-size:12px; border:none; border-radius:4px; cursor:pointer; width:50%;">🗑️ Hapus</button>
                </div>
            </td>
        `;
        tbody.appendChild(tr);
    });
}


function viewMedia(nama, jenisMedia, base64String) {
    if (!base64String) {
        alert("Data gambar kosong atau belum di-sync.");
        return;
    }
    document.getElementById('modalTitle').innerText = `Preview ${jenisMedia} - ${nama}`;
    const imgSrc = base64String.startsWith('data:image') ? base64String : `data:image/jpeg;base64,${base64String}`;
    document.getElementById('modalImage').src = imgSrc;
    document.getElementById('imageModal').style.display = "block";
}

function closeImageModal() {
    document.getElementById('imageModal').style.display = "none";
    document.getElementById('modalImage').src = "";
}

// =======================================================
// INISIALISASI SAAT HALAMAN DIMUAT
// =======================================================
window.onload = () => {
    startWebcam();
    cekStatusAktivasiBackend(); // Validasi lisensi otomatis saat startup
    if(typeof switchTab === 'function') switchTab('enrollment');
};
// =======================================================
// FUNGSI EDIT DATA & KEMBALI KE FORM
// =======================================================
function editDataSiswa(encodedData) {
    const data = JSON.parse(decodeURIComponent(encodedData));
    const bio = JSON.parse(data.json_biodata);
    const cam = JSON.parse(data.json_kamera);
    const fp = JSON.parse(data.json_jari);

    // 1. Pindah ke Tab Registrasi
    switchTab('enrollment');

    // 2. Isi Form Teks
    document.getElementById('nis').value = data.nis;
    document.getElementById('nis').readOnly = true; // KUNCI NIS agar folder tidak pecah
    document.getElementById('nis').style.backgroundColor = "#e2e8f0";
    document.getElementById('nama').value = data.nama;
    document.getElementById('tgl_lahir').value = bio.tgl_lahir || '';
    document.getElementById('jk').value = bio.jk || 'L';
    document.getElementById('parents').value = bio.parents || '';
    document.getElementById('no_wa').value = bio.no_wa || '';
    document.getElementById('email').value = bio.email || '';
    document.getElementById('alamat').value = bio.alamat || '';

    // 3. Tampilkan Preview Gambar Wajah/APD
    if (cam.face_front) {
        document.getElementById('preview_face_front').src = `data:image/jpeg;base64,${cam.face_front}`;
        document.getElementById('preview_face_front').style.display = 'block';
        document.getElementById('b64_face_front').value = cam.face_front;
    }
    // Lakukan hal serupa untuk APD jika diperlukan

    // 4. Ubah Tombol Simpan
    document.getElementById('save_section').style.display = "block";
    const btnSave = document.querySelector('#save_section button');
    btnSave.innerHTML = "💾 UPDATE DATA (RE-SAVE)";
    btnSave.style.background = "#f59e0b"; // Warna orange tanda edit
    
    // Auto Scroll ke Atas
    window.scrollTo(0, 0);
    alert(`Mode Edit Aktif untuk ${data.nama}.\nSilakan ubah data atau rekam ulang foto/jari, lalu klik Update Data di paling bawah.`);
}

// =======================================================
// FUNGSI HAPUS DATA
// =======================================================
function hapusDataSiswa(nis, nama) {
    if(!confirm(`Yakin ingin HAPUS PERMANEN data ${nama} (NIS: ${nis}) beserta foldernya?`)) return;

    fetch(`http://localhost:5000/api/siswa/${nis}`, { method: 'DELETE' })
    .then(res => res.json())
    .then(data => {
        if(data.status === "success") {
            alert(`Terhapus! ${data.message}`);
            loadDataTersimpan();
        } else alert(`Gagal: ${data.message}`);
    });
}
