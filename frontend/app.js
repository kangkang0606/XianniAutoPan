const wsStatusEl = document.getElementById("wsStatus");
const aiStatusEl = document.getElementById("aiStatus");
const userIdEl = document.getElementById("userId");
const playerNameEl = document.getElementById("playerName");
const bindingTextEl = document.getElementById("bindingText");
const listenAddressesEl = document.getElementById("listenAddresses");
const commandTextEl = document.getElementById("commandText");
const replyBoxEl = document.getElementById("replyBox");
const logBoxEl = document.getElementById("logBox");
const commandBookEl = document.getElementById("commandBook");
const kingdomTableBodyEl = document.getElementById("kingdomTableBody");
const sendButtonEl = document.getElementById("sendButton");
const refreshButtonEl = document.getElementById("refreshButton");
const viewBindingButtonEl = document.getElementById("viewBindingButton");

let socket = null;
let dashboardTimer = null;
let currentBinding = null;
let currentKingdoms = [];

function pick(obj, camelKey, pascalKey) {
  if (!obj) {
    return undefined;
  }
  if (Object.prototype.hasOwnProperty.call(obj, camelKey)) {
    return obj[camelKey];
  }
  return obj[pascalKey];
}

function appendReply(text, ok = true) {
  const item = document.createElement("div");
  item.className = `reply-item ${ok ? "reply-ok" : "reply-fail"}`;
  item.textContent = `[${new Date().toLocaleTimeString()}] ${text}`;
  replyBoxEl.prepend(item);
}

function updateBinding(binding) {
  currentBinding = binding || null;
  if (!binding) {
    bindingTextEl.textContent = "未绑定";
    renderKingdoms(currentKingdoms);
    return;
  }
  const kingdomName = pick(binding, "kingdomName", "KingdomName");
  const kingdomId = pick(binding, "kingdomId", "KingdomId");
  bindingTextEl.textContent = `${kingdomName} (kingdomId=${kingdomId})`;
  renderKingdoms(currentKingdoms);
}

function renderLogs(logs) {
  logBoxEl.innerHTML = "";
  (logs || []).slice().reverse().forEach((entry) => {
    const item = document.createElement("div");
    item.className = "log-item";
    item.textContent = `${pick(entry, "timeText", "TimeText")} ${pick(entry, "message", "Message")}`;
    logBoxEl.appendChild(item);
  });
}

function renderAddresses(addresses) {
  listenAddressesEl.innerHTML = "";
  (addresses || []).forEach((address) => {
    const li = document.createElement("li");
    li.textContent = address;
    listenAddressesEl.appendChild(li);
  });
}

function createMiniButton(text, kind, onClick, disabled = false) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = `mini-btn ${kind || ""}`.trim();
  button.textContent = text;
  button.disabled = disabled;
  if (!disabled) {
    button.addEventListener("click", onClick);
  }
  return button;
}

function buildOwnerText(kingdom) {
  const ownerName = pick(kingdom, "ownerName", "OwnerName");
  const ownerUserId = pick(kingdom, "ownerUserId", "OwnerUserId");
  if (ownerName && ownerUserId) {
    return `${ownerName}\n${ownerUserId}`;
  }
  if (ownerName) {
    return ownerName;
  }
  return "AI/无人绑定";
}

function buildKingdomLabel(name, id) {
  const safeName = name || "未知国家";
  return `${safeName} [${id}]`;
}

function renderKingdoms(kingdoms) {
  currentKingdoms = Array.isArray(kingdoms) ? kingdoms : [];
  kingdomTableBodyEl.innerHTML = "";

  if (currentKingdoms.length === 0) {
    const emptyRow = document.createElement("tr");
    const cell = document.createElement("td");
    cell.colSpan = 12;
    cell.className = "table-empty";
    cell.textContent = "当前没有可展示的存活文明国家。";
    emptyRow.appendChild(cell);
    kingdomTableBodyEl.appendChild(emptyRow);
    return;
  }

  const currentKingdomId = currentBinding ? Number(pick(currentBinding, "kingdomId", "KingdomId")) : 0;
  currentKingdoms.forEach((kingdom) => {
    const kingdomId = Number(pick(kingdom, "kingdomId", "KingdomId"));
    const kingdomName = pick(kingdom, "kingdomName", "KingdomName") || "未知国家";
    const kingdomLabel = buildKingdomLabel(kingdomName, kingdomId);
    const isCurrent = currentKingdomId > 0 && currentKingdomId === kingdomId;
    const row = document.createElement("tr");
    if (isCurrent) {
      row.classList.add("kingdom-row-current");
    }

    const cells = [
      isCurrent ? `${kingdomLabel}\n当前绑定` : kingdomLabel,
      buildOwnerText(kingdom),
      String(pick(kingdom, "treasury", "Treasury") ?? 0),
      String(pick(kingdom, "nationLevel", "NationLevel") ?? 0),
      String(pick(kingdom, "xiuzhenguoLevel", "XiuzhenguoLevel") ?? 0),
      String(pick(kingdom, "cityCount", "CityCount") ?? 0),
      String(pick(kingdom, "population", "Population") ?? 0),
      String(pick(kingdom, "totalAura", "TotalAura") ?? 0),
      String(pick(kingdom, "annualIncome", "AnnualIncome") ?? 0),
      pick(kingdom, "allianceName", "AllianceName") || "无",
      pick(kingdom, "atWar", "AtWar") ? "交战中" : "和平"
    ];

    cells.forEach((text, index) => {
      const cell = document.createElement("td");
      cell.textContent = text;
      if (index === 0 || index === 1) {
        cell.className = "wide-cell";
      }
      row.appendChild(cell);
    });

    const actionCell = document.createElement("td");
    actionCell.className = "action-cell";
    const actions = document.createElement("div");
    actions.className = "row-actions";

    actions.appendChild(createMiniButton("查看金币", "mini-btn-admin", () => sendCommand(`#查看国家金币 ${kingdomLabel}`)));
    actions.appendChild(createMiniButton("+500", "mini-btn-admin", () => sendCommand(`#增加国家金币 ${kingdomLabel} 500`)));
    actions.appendChild(createMiniButton("+1000", "mini-btn-admin", () => sendCommand(`#增加国家金币 ${kingdomLabel} 1000`)));
    actions.appendChild(createMiniButton("转账1000", "mini-btn-admin", () => sendCommand(`转账 ${kingdomLabel} 1000`), isCurrent));
    actions.appendChild(createMiniButton("宣战", "mini-btn-danger", () => sendCommand(`宣战 ${kingdomLabel}`), isCurrent));
    actions.appendChild(createMiniButton("求和", "mini-btn-neutral", () => sendCommand(`求和 ${kingdomLabel}`), isCurrent));
    actions.appendChild(createMiniButton("结盟", "mini-btn-ok", () => sendCommand(`结盟 ${kingdomLabel}`), isCurrent));
    actions.appendChild(createMiniButton("解盟", "mini-btn-neutral", () => sendCommand(`解盟 ${kingdomLabel}`), isCurrent));
    actions.appendChild(createMiniButton("削灵500", "mini-btn-danger", () => sendCommand(`削灵 ${kingdomLabel} 500`), isCurrent));
    actions.appendChild(createMiniButton("斩首", "mini-btn-danger", () => sendCommand(`斩首 ${kingdomLabel}`), isCurrent));
    actions.appendChild(createMiniButton("诅咒3人", "mini-btn-danger", () => sendCommand(`诅咒 ${kingdomLabel} 3`), isCurrent));
    actions.appendChild(createMiniButton("修士-1境", "mini-btn-neutral", () => sendCommand(`修士降境 ${kingdomLabel} 2 1`), isCurrent));

    actionCell.appendChild(actions);
    row.appendChild(actionCell);
    kingdomTableBodyEl.appendChild(row);
  });
}

async function refreshDashboard() {
  const userId = encodeURIComponent(userIdEl.value.trim());
  const response = await fetch(`/api/dashboard?userId=${userId}`, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`状态刷新失败：${response.status}`);
  }

  const snapshot = await response.json();
  updateBinding(pick(snapshot, "binding", "Binding"));
  renderLogs(pick(snapshot, "recentLogs", "RecentLogs"));
  renderAddresses(pick(snapshot, "listenAddresses", "ListenAddresses"));
  renderKingdoms(pick(snapshot, "kingdoms", "Kingdoms"));

  const commandBookText = pick(snapshot, "commandBookText", "CommandBookText");
  if (commandBookText) {
    commandBookEl.textContent = commandBookText;
  } else {
    const bookResponse = await fetch("/指令书.txt", { cache: "no-store" });
    commandBookEl.textContent = bookResponse.ok ? await bookResponse.text() : "";
  }

  aiStatusEl.textContent = `AI：${pick(snapshot, "aiEnabled", "AiEnabled") ? "已开启" : "已关闭"}`;
}

function connectWebSocket() {
  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
    return;
  }

  const protocol = location.protocol === "https:" ? "wss" : "ws";
  socket = new WebSocket(`${protocol}://${location.host}/ws`);
  wsStatusEl.textContent = "WebSocket：连接中";

  socket.addEventListener("open", () => {
    wsStatusEl.textContent = "WebSocket：已连接";
  });

  socket.addEventListener("error", () => {
    appendReply("WebSocket 连接发生错误。", false);
  });

  socket.addEventListener("close", (e) => {
    wsStatusEl.textContent = "WebSocket：已断开，2 秒后重连";
    appendReply(`WebSocket 已断开：code=${e.code}`, false);
    setTimeout(connectWebSocket, 2000);
  });

  socket.addEventListener("message", async (event) => {
    const payload = JSON.parse(event.data);
    if (payload.type === "reply") {
      appendReply(payload.text, !!payload.ok);
      updateBinding(payload.binding);
      aiStatusEl.textContent = `AI：${payload.aiEnabled ? "已开启" : "已关闭"}`;
      await refreshDashboard().catch((err) => appendReply(err.message, false));
    }
  });
}

function sendCommand(command) {
  const text = (command || commandTextEl.value).trim();
  if (!text) {
    appendReply("请输入要发送的命令。", false);
    return;
  }
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    appendReply("WebSocket 尚未连接成功。", false);
    return;
  }

  socket.send(JSON.stringify({
    userId: userIdEl.value.trim(),
    playerName: playerNameEl.value.trim(),
    text
  }));

  if (!command) {
    commandTextEl.value = "";
  }
}

document.querySelectorAll("[data-command]").forEach((button) => {
  button.addEventListener("click", () => sendCommand(button.dataset.command));
});

viewBindingButtonEl.addEventListener("click", () => {
  const userId = userIdEl.value.trim();
  if (!userId) {
    appendReply("请先填写 userId，再查看绑定。", false);
    return;
  }
  sendCommand(`#查看绑定 ${userId}`);
});

sendButtonEl.addEventListener("click", () => sendCommand());
refreshButtonEl.addEventListener("click", () => {
  refreshDashboard().catch((err) => appendReply(err.message, false));
});

commandTextEl.addEventListener("keydown", (event) => {
  if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
    sendCommand();
  }
});

function startDashboardPolling() {
  if (dashboardTimer) {
    clearInterval(dashboardTimer);
  }
  dashboardTimer = setInterval(() => {
    refreshDashboard().catch(() => {});
  }, 3000);
}

connectWebSocket();
refreshDashboard().catch((err) => appendReply(err.message, false));
startDashboardPolling();
