// ── Drag & Drop: gestió síncrona del dragover sense round-trip a Blazor ──────
// En Blazor Server, preventDefault() en ondragover arriba tard (xarxa).
// Afegim un listener global que crida preventDefault de forma síncrona
// sempre que el cursor estigui sobre un element .dnd-zone o fill seu.
document.addEventListener('dragover', function (e) {
    if (e.target.closest && e.target.closest('.dnd-zone')) {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
    }
});

// ── Canvi de cultura (i18n) ───────────────────────────────────────────────────
// Escriu la cookie .AspNetCore.Culture i recarrega la pàgina per aplicar-la.
window.setCulture = function (culture) {
    const expiry = new Date();
    expiry.setFullYear(expiry.getFullYear() + 1);
    document.cookie = `.AspNetCore.Culture=c=${culture}|uic=${culture}; expires=${expiry.toUTCString()}; path=/; SameSite=Lax`;
    location.reload();
};

window.downloadBase64File = function (base64, fileName, mimeType) {
    const bytes  = atob(base64);
    const buffer = new Uint8Array(bytes.length);
    for (let i = 0; i < bytes.length; i++) buffer[i] = bytes.charCodeAt(i);
    const blob = new Blob([buffer], { type: mimeType });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// Alias per compatibilitat amb el plafó d'examen
window.downloadFileFromBase64 = window.downloadBase64File;

// ── Entorn Examen: IP del client injectada pel servidor ──────────────────────
// App.razor escriu window.__entornClientIp durant el render HTTP inicial.
// Portal.razor el llegeix via JS interop un cop establert el circuit Blazor.
window.getClientIp = function () { return window.__entornClientIp || ''; };

// ── Entorn Examen: so de desconnexió (Web Audio API, sense fitxers externs) ───
window.examenSoDesconnexio = function () {
    try {
        const ctx  = new (window.AudioContext || window.webkitAudioContext)();
        const osc  = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.type = 'square';
        osc.frequency.setValueAtTime(880, ctx.currentTime);
        osc.frequency.exponentialRampToValueAtTime(220, ctx.currentTime + 0.4);
        gain.gain.setValueAtTime(0.3, ctx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.5);
        osc.start(ctx.currentTime);
        osc.stop(ctx.currentTime + 0.5);
    } catch (e) { /* AudioContext no disponible */ }
};

// ── Entorn Examen: pulsació verda per a "CONNECTAT" ──────────────────────────
(function injectExamenStyles() {
    if (document.getElementById('examen-styles')) return;
    const style = document.createElement('style');
    style.id = 'examen-styles';
    style.textContent = `
        @keyframes pulse-green {
            0%, 100% { box-shadow: 0 0 0 0 rgba(34,197,94,.7); }
            50%       { box-shadow: 0 0 0 8px rgba(34,197,94,0); }
        }
        @keyframes pulse-warning {
            0%, 100% { opacity: 1; }
            50%       { opacity: 0.4; }
        }
    `;
    document.head.appendChild(style);
})();
