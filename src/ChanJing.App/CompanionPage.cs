namespace ChanJing_App;

/// <summary>
/// 手机端伴侣页HTML：禅意风格，显示统计+远程控制专注。
/// 枯山水色板：宣纸白#F4F1E8、素绢#ECE8DD、墨#2B2925、雾灰#7A7265、苔青#6E7F63、金缮金#A8842C、赭石#A0563B。
/// </summary>
public static class CompanionPage
{
    public const string Html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
<title>ZenFocus · Companion</title>
<style>
* { margin: 0; padding: 0; box-sizing: border-box; -webkit-tap-highlight-color: transparent; }
body {
  font-family: -apple-system, BlinkMacSystemFont, 'PingFang SC', 'Microsoft YaHei', sans-serif;
  background: #F4F1E8;
  color: #2B2925;
  min-height: 100vh;
  padding: 24px 20px;
}
.header { text-align: center; margin-bottom: 28px; }
.header h1 { font-size: 28px; font-weight: 600; letter-spacing: 4px; }
.header .date { font-size: 13px; color: #7A7265; margin-top: 6px; }
.stats-grid {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 12px;
  margin-bottom: 24px;
}
.stat-card {
  background: #FAF8F2;
  border: 1px solid #D4CFC4;
  border-radius: 14px;
  padding: 16px 12px;
  text-align: center;
}
.stat-card .value { font-size: 28px; font-weight: 600; color: #6E7F63; }
.stat-card .label { font-size: 12px; color: #7A7265; margin-top: 4px; }
.focus-status {
  background: #FAF8F2;
  border: 1px solid #D4CFC4;
  border-radius: 16px;
  padding: 24px;
  text-align: center;
  margin-bottom: 20px;
}
.focus-status.focusing { border-color: #6E7F63; background: linear-gradient(135deg, #F0F4EC, #FAF8F2); }
.focus-status .state-label { font-size: 14px; color: #7A7265; margin-bottom: 8px; }
.focus-status .wish { font-size: 18px; font-weight: 500; margin-bottom: 12px; line-height: 1.5; }
.focus-status .time { font-size: 36px; font-weight: 600; color: #6E7F63; font-variant-numeric: tabular-nums; }
.focus-status .time-label { font-size: 12px; color: #7A7265; margin-top: 4px; }
.control-btn {
  display: block;
  width: 100%;
  padding: 16px;
  border: none;
  border-radius: 28px;
  font-size: 17px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  margin-bottom: 12px;
}
.control-btn.start { background: #6E7F63; color: white; }
.control-btn.start:active { background: #5A6B50; transform: scale(0.98); }
.control-btn.stop { background: #A0563B; color: white; }
.control-btn.stop:active { background: #8A4A32; transform: scale(0.98); }
.control-btn:disabled { background: #D4CFC4; color: #7A7265; cursor: not-allowed; }
.distraction-list {
  background: #FAF8F2;
  border: 1px solid #D4CFC4;
  border-radius: 14px;
  padding: 16px;
  margin-top: 20px;
}
.distraction-list h3 { font-size: 14px; color: #7A7265; margin-bottom: 12px; font-weight: 500; }
.distraction-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 0;
  border-bottom: 1px solid #ECE8DD;
  font-size: 14px;
}
.distraction-item:last-child { border-bottom: none; }
.distraction-item .app { color: #2B2925; }
.distraction-item .count { color: #A0563B; font-weight: 500; }
.empty { text-align: center; color: #7A7265; font-size: 13px; padding: 12px 0; }
.footer { text-align: center; margin-top: 28px; font-size: 11px; color: #7A7265; opacity: 0.6; }
.toast {
  position: fixed;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  background: rgba(43, 41, 37, 0.9);
  color: white;
  padding: 14px 24px;
  border-radius: 12px;
  font-size: 14px;
  z-index: 1000;
  opacity: 0;
  transition: opacity 0.3s;
  pointer-events: none;
}
.toast.show { opacity: 1; }
</style>
</head>
<body>
<div class=""header"">
  <h1>Zen Focus</h1>
  <div class=""date"" id=""date""></div>
</div>

<div class=""stats-grid"">
  <div class=""stat-card"">
    <div class=""value"" id=""focusMinutes"">0</div>
    <div class=""label"">Focus Min</div>
  </div>
  <div class=""stat-card"">
    <div class=""value"" id=""focusSessions"">0</div>
    <div class=""label"">Sessions</div>
  </div>
  <div class=""stat-card"">
    <div class=""value"" id=""distractionCount"">0</div>
    <div class=""label"">Distractions</div>
  </div>
</div>

<div class=""focus-status"" id=""focusStatus"">
  <div class=""state-label"" id=""stateLabel"">Idle</div>
  <div class=""wish"" id=""wish"">—</div>
  <div class=""time"" id=""elapsedTime"">00:00</div>
  <div class=""time-label"">Focused</div>
</div>

<button class=""control-btn start"" id=""startBtn"" onclick=""startFocus()"">Start Focus</button>
<button class=""control-btn stop"" id=""stopBtn"" onclick=""stopFocus()"" style=""display:none;"">End Focus</button>

<div class=""distraction-list"">
  <h3>Today's Distractions</h3>
  <div id=""distractionList""><div class=""empty"">No distractions yet</div></div>
</div>

<div class=""distraction-list"">
  <h3>Peer Focus (optional · non-social)</h3>
  <div>Enter peer's address (e.g. http://192.168.1.5:8765) to see if they're focusing.</div>
  <input id=""peerUrl"" placeholder=""http://IP:8765"" style=""width:100%;padding:8px;margin:6px 0;border:1px solid #ccc;border-radius:6px;"" />
  <button class=""control-btn start"" onclick=""savePeer()"" style=""font-size:12px;padding:6px 10px;"">Save</button>
  <div id=""peerStatus"" class=""peer-status"" style=""margin-top:8px;font-size:13px;""></div>
</div>

<div class=""footer"">ZenFocus · LAN Companion · Phone and PC must be on same WiFi</div>
<div class=""toast"" id=""toast""></div>

<script>
let isFocusing = false;
let startTime = null;
let timerInterval = null;

function formatTime(minutes) {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return h > 0 ? h > 0 ? h + 'h ' + m + 'm' : m + 'min';
}

function formatElapsed(seconds) {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0');
}

function showToast(msg) {
  const toast = document.getElementById('toast');
  toast.textContent = msg;
  toast.classList.add('show');
  setTimeout(() => toast.classList.remove('show'), 2000);
}

async function fetchJSON(url, options) {
  try {
    const res = await fetch(url, options);
    return await res.json();
  } catch (e) {
    showToast('Connection failed. Check WiFi.');
    return null;
  }
}

async function loadStats() {
  const data = await fetchJSON('/api/stats');
  if (!data) return;
  document.getElementById('date').textContent = data.date + ' ' + data.serverTime;
  document.getElementById('focusMinutes').textContent = data.focusMinutes;
  document.getElementById('focusSessions').textContent = data.focusSessions;
  document.getElementById('distractionCount').textContent = data.distractionCount;
  const list = document.getElementById('distractionList');
  if (data.topDistractions && data.topDistractions.length > 0) {
    list.innerHTML = data.topDistractions.map(d =>
      '<div class=""distraction-item""><span class=""app"">' + d.app + '</span><span class=""count"">' + d.count + 'x</span></div>'
    ).join('');
  } else {
    list.innerHTML = '<div class=""empty"">暂无分心记录</div>';
  }
}

async function loadFocusStatus() {
  const data = await fetchJSON('/api/focus/status');
  if (!data) return;
  isFocusing = data.isFocusing;
  const statusEl = document.getElementById('focusStatus');
  const stateLabel = document.getElementById('stateLabel');
  const wishEl = document.getElementById('wish');
  const startBtn = document.getElementById('startBtn');
  const stopBtn = document.getElementById('stopBtn');

  if (isFocusing) {
    statusEl.classList.add('focusing');
    stateLabel.textContent = 'Focusing';
    wishEl.textContent = data.wish || '—';
    startBtn.style.display = 'none';
    stopBtn.style.display = 'block';
    if (data.startedAt) {
      const [h, m, s] = data.startedAt.split(':').map(Number);
      const now = new Date();
      startTime = new Date(now.getFullYear(), now.getMonth(), now.getDate(), h, m, s);
      startTimer();
    }
  } else {
    statusEl.classList.remove('focusing');
    stateLabel.textContent = 'Idle';
    wishEl.textContent = '—';
    document.getElementById('elapsedTime').textContent = '00:00';
    startBtn.style.display = 'block';
    stopBtn.style.display = 'none';
    stopTimer();
  }
}

function startTimer() {
  stopTimer();
  timerInterval = setInterval(() => {
    if (startTime) {
      const elapsed = Math.floor((Date.now() - startTime.getTime()) / 1000);
      document.getElementById('elapsedTime').textContent = formatElapsed(elapsed);
    }
  }, 1000);
}

function stopTimer() {
  if (timerInterval) {
    clearInterval(timerInterval);
    timerInterval = null;
  }
}

async function startFocus() {
  const data = await fetchJSON('/api/focus/start', { method: 'POST' });
  if (data && data.success) {
    showToast('Focus started');
    loadFocusStatus();
    loadStats();
  } else if (data) {
    showToast(data.message || 'Failed to start');
  }
}

async function stopFocus() {
  const data = await fetchJSON('/api/focus/stop', { method: 'POST' });
  if (data && data.success) {
    showToast('Focus ended');
    loadFocusStatus();
    loadStats();
  } else if (data) {
    showToast(data.message || 'Failed to stop');
  }
}

function savePeer() {
  const url = document.getElementById('peerUrl').value.trim();
  if (!url) { showToast('Enter peer address'); return; }
  localStorage.setItem('peerUrl', url);
  showToast('Peer address saved');
  loadPeerStatus();
}

async function loadPeerStatus() {
  const url = localStorage.getItem('peerUrl');
  const el = document.getElementById('peerStatus');
  if (!url) { el.textContent = 'No peer address set'; return; }
  try {
    const res = await fetch(url.replace(/\\/$/, '') + '/api/focus/status');
    const data = await res.json();
    el.textContent = data.isFocusing ? 'Peer is focusing' : 'Peer is idle';
    el.style.color = data.isFocusing ? '#2e7d32' : '#888';
  } catch (e) {
    el.textContent = 'Cannot connect to peer (check network/address)';
    el.style.color = '#c62828';
  }
}

// 初始化
const savedPeer = localStorage.getItem('peerUrl');
if (savedPeer) { document.getElementById('peerUrl').value = savedPeer; loadPeerStatus(); }
loadStats();
loadFocusStatus();
// 每10秒刷新一次
setInterval(() => {
  loadStats();
  if (!isFocusing) loadFocusStatus();
  loadPeerStatus();
}, 10000);
</script>
</body>
</html>";
}
