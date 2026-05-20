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
    
    // Tampilkan preview (menimpa gambar lama jika diklik ulang)
    const previewEl = document.getElementById(`preview_${type}`);
    if(previewEl) {
        previewEl.src = base64Data;
        previewEl.style.display = 'block';
    }
    
    // Simpan/Timpa Base64 murni ke input hidden
    const cleanBase64 = base64Data.replace(/^data:image\/[a-z]+;base64,/, "");
    const inputEl = document.getElementById(`b64_${type}`);
    if(inputEl) inputEl.value = cleanBase64;
}

// =======================================================
// MODUL 2: WIZARD U.ARE.U 4500 (30x QUEUE)
// =======================================================
const fp_fingers = [
    'L1_kelingking', 'L2_jari_manis', 'L3_tengah', 'L4_telunjuk', 'L5_jempol',
    'R1_jempol', 'R2_telunjuk', 'R3_tengah', 'R4_jari_manis', 'R5_kelingking'
];
const fp_angles = ['front', 'left', 'right'];

let captureQueue = [];
let currentStep = 0;

// Build antrian 30 kombinasi
fp_fingers.forEach(finger => {
    fp_angles.forEach(angle => {
        captureQueue.push({ finger: finger, angle: angle });
    });
});

function startFingerprintWizard() {
    if (currentStep >= captureQueue.length) return;

    const task = captureQueue[currentStep];
    const instructionEl = document.getElementById('fp_instruction');
    const counterEl = document.getElementById('fp_counter');
    const btnAction = document.getElementById('btn_fp_action');

    const formattedFinger = task.finger.replace(/_/g, ' ').toUpperCase();
    const formattedAngle = task.angle.toUpperCase();

    instructionEl.innerText = `Tempelkan Jari: ${formattedFinger} - Bagian ${formattedAngle}`;
    counterEl.innerText = `Progress: ${currentStep + 1} / 30`;
    
    btnAction.innerText = "Scan Scanner Sekarang";
    btnAction.onclick = () => triggerLocalScanner(task.finger, task.angle);
}

function triggerLocalScanner(finger, angle) {
    const instructionEl = document.getElementById('fp_instruction');
    instructionEl.innerText = "Membaca Scanner...";
    
    // MOCKING: Delay 1 detik untuk simulasi hardware scanner
    setTimeout(() => {
        const inputId = `fp_${finger}_${angle}`;
        let hiddenInput = document.getElementById(inputId);
        
        if (!hiddenInput) {
            hiddenInput = document.createElement('input');
            hiddenInput.type = 'hidden';
            hiddenInput.id = inputId;
            document.getElementById('fp_data_container').appendChild(hiddenInput);
        }
        
        hiddenInput.value = `BASE64_DATA_FOR_${finger}_${angle}`;
        
        currentStep++;
        
        if (currentStep < captureQueue.length) {
            startFingerprintWizard(); // Loop lanjut
        } else {
            finalizeWizard(); // 30 selesai, munculkan menu save & retake
        }
    }, 1000);
}

function finalizeWizard() {
    document.getElementById('fp_instruction').innerText = "✅ SEMUA 30 JARI TEREKAM";
    document.getElementById('fp_instruction').style.color = "green";
    document.getElementById('btn_fp_action').style.display = "none";
    document.getElementById('fp_counter').innerText = "Progress: 30 / 30";
    
    // Munculkan Tombol Simpan
    document.getElementById('save_section').style.display = "block";
    
    // Setup Dropdown Retake Menu
    const retakeSelect = document.getElementById('retake_select');
    retakeSelect.innerHTML = '';
    
    captureQueue.forEach(task => {
        const option = document.createElement('option');
        option.value = `${task.finger}|${task.angle}`; // Payload separator
        const textFinger = task.finger.replace(/_/g, ' ').toUpperCase();
        option.text = `${textFinger} - ${task.angle.toUpperCase()}`;
        retakeSelect.appendChild(option);
    });
    
    document.getElementById('retake_section').style.display = "block";
}

// =======================================================
// MODUL 3: RETAKE SPECIFIC FINGERPRINT
// =======================================================
function retakeFingerprint() {
    const selected = document.getElementById('retake_select').value.split('|');
    const finger = selected[0];
    const angle = selected[1];
    const statusEl = document.getElementById('retake_status');
    const btnRetake = document.querySelector('#retake_section button');

    btnRetake.innerText = "Membaca Scanner...";
    statusEl.style.display = "none";

    // MOCKING: Simulasi ulang scan untuk 1 jari spesifik
    setTimeout(() => {
        const inputId = `fp_${finger}_${angle}`;
        let hiddenInput = document.getElementById(inputId);
        
        // Timpa value lama
        if(hiddenInput) {
            hiddenInput.value = `DUMMY_RETAKE_BASE64_FOR_${finger}_${angle}`;
        }
        
        btnRetake.innerText = "🔄 Ulangi Scan Jari Terpilih";
        statusEl.innerText = `Update: Jari ${finger.replace(/_/g, ' ')} (${angle}) berhasil ditimpa!`;
        statusEl.style.display = "block";
    }, 1000);
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

    // Kompilasi JSON Payload
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

    console.log("PAYLOAD JSON FINAL UNTUK DIKIRIM KE C# ENGINE:", payload);
    
    // TAHAP 3: Integrasi API ke Engine Desktop
    // fetch('http://localhost:5000/api/save', { ... })
    
    setTimeout(() => {
        alert(`Berhasil! Data siswa ${nama} (NIS: ${nis}) telah disimpan.\n\nFolder Dinas sudah digenerate.`);
        window.location.reload(); 
    }, 1500);
}

// =======================================================
// MODUL 5: DATABASE VIEWER & MODAL IMAGE
// =======================================================
function loadDataTersimpan() {
    const tbody = document.getElementById('table_body_siswa');
    tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;">⏳ Memuat data dari SQLite...</td></tr>`;

    // TODO TAHAP 3: Fetch dari C# Engine
    // fetch('http://localhost:5000/api/siswa')
    //    .then(res => res.json())
    //    .then(data => renderTable(data))
    
    // MOCKING DATA (Simulasi balikan dari database C#):
    setTimeout(() => {
        const mockDbData = [
            {
                timestamp: "2026-05-20 14:30",
                nis: "992341",
                nama: "Ahmad Subarjo",
                tgl_lahir: "2008-01-15",
                jk: "L",
                parents: "Budi Santoso",
                no_wa: "08123456789",
                alamat: "Jl. Merdeka No 10, Bali",
                folder: "992341_Ahmad",
                media_wajah: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=",
                media_jari_L1: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" 
            },
            {
                timestamp: "2026-05-20 15:10",
                nis: "992342",
                nama: "Siti Aminah",
                tgl_lahir: "2009-03-22",
                jk: "P",
                parents: "Joko Anwar",
                no_wa: "085711223344",
                alamat: "Jl. Diponegoro, Denpasar",
                folder: "992342_Siti",
                media_wajah: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=",
                media_jari_L1: ""
            }
        ];
        
        renderTable(mockDbData);
    }, 800);
}

function renderTable(dataList) {
    const tbody = document.getElementById('table_body_siswa');
    tbody.innerHTML = ''; 
    
    if (dataList.length === 0) {
        tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;">Tidak ada data tersimpan.</td></tr>`;
        return;
    }

    dataList.forEach(row => {
        const tr = document.createElement('tr');
        
        tr.innerHTML = `
            <td style="font-size:12px; color:#64748b;">${row.timestamp}</td>
            <td style="font-weight:bold;">${row.nis}</td>
            <td>${row.nama}</td>
            <td style="font-size:13px;">${row.tgl_lahir} <br> (${row.jk})</td>
            <td style="font-size:13px;">${row.parents}</td>
            <td style="font-size:13px;">${row.no_wa} <br> <span style="color:#64748b;">${row.alamat}</span></td>
            <td><code style="background:#f1f5f9; padding:2px 5px; border-radius:4px; font-size:12px;">${row.folder}</code></td>
            <td style="text-align:center;">
                <button onclick="viewMedia('${row.nama}', 'Wajah', '${row.media_wajah}')" style="background:#3b82f6; color:white; padding:5px 10px; font-size:12px; border:none; border-radius:4px; cursor:pointer; margin-bottom:4px;">🖼️ Wajah</button>
                <br>
                <button onclick="viewMedia('${row.nama}', 'Jari L1 (Front)', '${row.media_jari_L1}')" style="background:#64748b; color:white; padding:5px 10px; font-size:12px; border:none; border-radius:4px; cursor:pointer;">👆 Jari (L1)</button>
            </td>
        `;
        tbody.appendChild(tr);
    });
}

function viewMedia(nama, jenisMedia, base64String) {
    if (!base64String) {
        alert("Data media belum di-sync ke UI atau kosong.");
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
    // Mulai kamera otomatis
    startWebcam();
    // Jika ada elemen tab, pastikan Enrollment yg aktif di awal
    if(typeof switchTab === 'function') switchTab('enrollment');
};
