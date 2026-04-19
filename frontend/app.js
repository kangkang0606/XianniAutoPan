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
const policyModulesEl = document.getElementById("policyModules");
const policyModuleTabsEl = document.getElementById("policyModuleTabs");
const qqBridgePanelEl = document.getElementById("qqBridgePanel");
const pendingRequestsEl = document.getElementById("pendingRequests");
const scoreTableBodyEl = document.getElementById("scoreTableBody");
const scoreUserIdEl = document.getElementById("scoreUserId");
const scorePlayerNameEl = document.getElementById("scorePlayerName");
const scoreWinsEl = document.getElementById("scoreWins");
const scoreSaveButtonEl = document.getElementById("scoreSaveButton");
const sendButtonEl = document.getElementById("sendButton");
const refreshButtonEl = document.getElementById("refreshButton");
const viewBindingButtonEl = document.getElementById("viewBindingButton");
const aiEnableToggleEl = document.getElementById("aiEnableToggle");
const aiQqChatToggleEl = document.getElementById("aiQqChatToggle");
const pageSwitchEl = document.querySelector(".page-switch");
const pageTabEls = Array.from(document.querySelectorAll("[data-page-target]"));
const pageViewEls = Array.from(document.querySelectorAll("[data-page-view]"));
const configTabEls = Array.from(document.querySelectorAll("[data-config-target]"));
const configViewEls = Array.from(document.querySelectorAll("[data-config-view]"));
const saveDirtyConfigButtonEl = document.getElementById("saveDirtyConfigButton");
const discardDirtyConfigButtonEl = document.getElementById("discardDirtyConfigButton");
const configDirtyStatusEl = document.getElementById("configDirtyStatus");

let socket = null;
let dashboardTimer = null;
let currentBinding = null;
let currentKingdoms = [];
let currentPage = "dashboard";
let currentConfigModule = "score";
let currentPolicyModuleKey = "";
let isSavingDirtyConfig = false;
const policyDrafts = Object.create(null);
const policyDirtyKeys = new Set();
const qqDrafts = Object.create(null);
const qqDirtyKeys = new Set();

function pick(obj, camelKey, pascalKey) {
  if (!obj) {
    return undefined;
  }
  if (Object.prototype.hasOwnProperty.call(obj, camelKey)) {
    return obj[camelKey];
  }
  return obj[pascalKey];
}

function switchPage(pageName, force = false) {
  if (!force && currentPage === pageName) {
    return;
  }

  currentPage = pageName;
  if (pageSwitchEl) {
    pageSwitchEl.dataset.activePage = pageName;
  }
  pageTabEls.forEach((button) => {
    const isActive = button.dataset.pageTarget === pageName;
    button.classList.toggle("is-active", isActive);
    button.setAttribute("aria-selected", isActive ? "true" : "false");
  });
  pageViewEls.forEach((view) => {
    view.classList.toggle("is-active", view.dataset.pageView === pageName);
  });
}

function switchConfigModule(moduleName, force = false) {
  if (!force && currentConfigModule === moduleName) {
    return;
  }

  currentConfigModule = moduleName;
  configTabEls.forEach((button) => {
    const isActive = button.dataset.configTarget === moduleName;
    button.classList.toggle("is-active", isActive);
    button.setAttribute("aria-selected", isActive ? "true" : "false");
  });
  configViewEls.forEach((view) => {
    view.classList.toggle("is-active", view.dataset.configView === moduleName);
  });
}

function switchPolicyModule(moduleKey, policy) {
  if (currentPolicyModuleKey === moduleKey) {
    return;
  }

  currentPolicyModuleKey = moduleKey;
  renderPolicy(policy);
}

function appendReply(text, ok = true) {
  const item = document.createElement("div");
  item.className = `reply-item ${ok ? "reply-ok" : "reply-fail"}`;
  item.textContent = `[${new Date().toLocaleTimeString()}] ${text}`;
  replyBoxEl.prepend(item);
}

function escapeHtml(text) {
  return String(text ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function rememberDraft(store, dirtySet, key, value) {
  if (!key) {
    return;
  }
  store[key] = value;
  dirtySet.add(key);
  updateDirtyStatus();
}

function getDraftValue(store, dirtySet, key, fallbackValue) {
  if (key && dirtySet.has(key)) {
    return store[key];
  }
  return fallbackValue;
}

function clearDraft(store, dirtySet, key) {
  if (!key) {
    return;
  }
  delete store[key];
  dirtySet.delete(key);
  updateDirtyStatus();
}

function clearDraftIfSaved(store, dirtySet, key, value) {
  if (!key || String(store[key]) !== String(value)) {
    return;
  }
  clearDraft(store, dirtySet, key);
}

function updateDirtyStatus() {
  const policyCount = policyDirtyKeys.size;
  const qqCount = qqDirtyKeys.size;
  const totalCount = policyCount + qqCount;
  if (configDirtyStatusEl) {
    configDirtyStatusEl.textContent = totalCount > 0
      ? `待保存 ${totalCount} 项（政策 ${policyCount} / QQ ${qqCount}）`
      : "没有未保存修改";
    configDirtyStatusEl.classList.toggle("is-dirty", totalCount > 0);
  }
  if (saveDirtyConfigButtonEl) {
    saveDirtyConfigButtonEl.disabled = totalCount === 0 || isSavingDirtyConfig;
  }
  if (discardDirtyConfigButtonEl) {
    discardDirtyConfigButtonEl.disabled = totalCount === 0 || isSavingDirtyConfig;
  }
}

function shouldSuspendAutoRefresh() {
  if (currentPage === "config") {
    return true;
  }

  const active = document.activeElement;
  if (!active) {
    return false;
  }

  const tagName = (active.tagName || "").toUpperCase();
  if (tagName !== "INPUT" && tagName !== "TEXTAREA" && tagName !== "SELECT") {
    return false;
  }

  return true;
}

function toWebSocketUrl(address, wsPath) {
  const base = String(address || "").trim();
  const path = String(wsPath || "/onebot/ws").trim() || "/onebot/ws";
  if (!base) {
    return "";
  }

  if (base.startsWith("http://")) {
    return `ws://${base.slice("http://".length)}${path}`;
  }
  if (base.startsWith("https://")) {
    return `wss://${base.slice("https://".length)}${path}`;
  }
  return `${base}${path}`;
}

function syncPolicyDrafts(policy) {
  const modules = pick(policy, "modules", "Modules") || [];
  modules.forEach((module) => {
    const items = pick(module, "items", "Items") || [];
    items.forEach((item) => {
      const key = pick(item, "key", "Key");
      const value = String(pick(item, "value", "Value") ?? "");
      if (key && policyDirtyKeys.has(key) && String(policyDrafts[key]) === value) {
        clearDraft(policyDrafts, policyDirtyKeys, key);
      }
    });
  });
}

function syncQqDrafts(qqBridge) {
  const data = qqBridge || {};
  const comparableValues = {
    qqAdapterEnabled: pick(data, "enabled", "Enabled") ? "1" : "0",
    qqOneBotWsPath: pick(data, "wsPath", "WsPath") || "/onebot/ws",
    qqBotSelfId: pick(data, "botSelfId", "BotSelfId") || "",
    qqReplyAtSender: pick(data, "replyAtSender", "ReplyAtSender") ? "1" : "0",
    qqGroupWhitelist: pick(data, "groupWhitelist", "GroupWhitelist") || "",
    qqAdminWhitelist: pick(data, "adminWhitelist", "AdminWhitelist") || ""
  };

  Object.entries(comparableValues).forEach(([key, value]) => {
    if (qqDirtyKeys.has(key) && String(qqDrafts[key]) === String(value)) {
      clearDraft(qqDrafts, qqDirtyKeys, key);
    }
  });
}

function buildKingdomLabel(name, id) {
  const safeName = name || "未知国家";
  return `${safeName} [${id}]`;
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

function formatIsoTime(value) {
  const text = String(value || "").trim();
  if (!text) {
    return "无";
  }

  const date = new Date(text);
  if (Number.isNaN(date.getTime())) {
    return text;
  }

  return date.toLocaleString();
}

function renderPendingRequests(requests) {
  pendingRequestsEl.innerHTML = "";
  const items = Array.isArray(requests) ? requests : [];
  if (items.length === 0) {
    const empty = document.createElement("div");
    empty.className = "pending-empty";
    empty.textContent = "当前没有待处理的结盟或约斗请求。";
    pendingRequestsEl.appendChild(empty);
    return;
  }

  items.forEach((request) => {
    const requestType = pick(request, "requestType", "RequestType") || "请求";
    const sourceLabel = pick(request, "sourceKingdomLabel", "SourceKingdomLabel") || "未知国家";
    const secondsRemaining = pick(request, "secondsRemaining", "SecondsRemaining") ?? 0;
    const detailsText = pick(request, "detailsText", "DetailsText") || "";

    const card = document.createElement("div");
    card.className = "pending-card";

    const title = document.createElement("div");
    title.className = "pending-title";
    title.textContent = `${requestType}请求：${sourceLabel}`;
    card.appendChild(title);

    const meta = document.createElement("div");
    meta.className = "pending-meta";
    meta.textContent = detailsText ? `${detailsText} · 剩余 ${secondsRemaining}s` : `剩余 ${secondsRemaining}s`;
    card.appendChild(meta);

    const actions = document.createElement("div");
    actions.className = "row-actions";
    if (requestType === "结盟") {
      actions.appendChild(createMiniButton("同意", "mini-btn-ok", () => sendCommand(`同意结盟 ${sourceLabel}`)));
      actions.appendChild(createMiniButton("拒绝", "mini-btn-danger", () => sendCommand(`拒绝结盟 ${sourceLabel}`)));
    } else {
      actions.appendChild(createMiniButton("同意", "mini-btn-ok", () => sendCommand(`同意约斗 ${sourceLabel}`)));
      actions.appendChild(createMiniButton("拒绝", "mini-btn-danger", () => sendCommand(`拒绝约斗 ${sourceLabel}`)));
    }
    card.appendChild(actions);
    pendingRequestsEl.appendChild(card);
  });
}

function renderPolicy(policy) {
  policyModulesEl.innerHTML = "";
  if (policyModuleTabsEl) {
    policyModuleTabsEl.innerHTML = "";
  }
  const modules = pick(policy, "modules", "Modules") || [];
  if (!policy || modules.length === 0) {
    const empty = document.createElement("div");
    empty.className = "pending-empty";
    empty.textContent = "当前没有可显示的后端政策。";
    policyModulesEl.appendChild(empty);
    return;
  }

  const moduleKeys = modules.map((module, index) => {
    const displayName = pick(module, "displayName", "DisplayName") || "未命名模块";
    return String(pick(module, "key", "Key") || pick(module, "id", "Id") || `${index}-${displayName}`);
  });
  if (!moduleKeys.includes(currentPolicyModuleKey)) {
    currentPolicyModuleKey = moduleKeys[0];
  }

  modules.forEach((module, moduleIndex) => {
    const moduleKey = moduleKeys[moduleIndex];
    const displayName = pick(module, "displayName", "DisplayName") || "未命名模块";
    const isActive = moduleKey === currentPolicyModuleKey;
    if (policyModuleTabsEl) {
      const tab = document.createElement("button");
      tab.type = "button";
      tab.className = `policy-module-tab ${isActive ? "is-active" : ""}`.trim();
      tab.textContent = displayName;
      tab.addEventListener("click", () => switchPolicyModule(moduleKey, policy));
      policyModuleTabsEl.appendChild(tab);
    }

    const card = document.createElement("section");
    card.className = `policy-module policy-module-page ${isActive ? "is-active" : ""}`.trim();

    const head = document.createElement("div");
    head.className = "policy-module-head";

    const titleWrap = document.createElement("div");
    const title = document.createElement("h3");
    title.textContent = displayName;
    titleWrap.appendChild(title);

    const desc = document.createElement("p");
    desc.className = "policy-module-desc";
    desc.textContent = pick(module, "description", "Description") || "";
    titleWrap.appendChild(desc);
    head.appendChild(titleWrap);
    card.appendChild(head);

    const itemsWrap = document.createElement("div");
    itemsWrap.className = "policy-module-items";
    const items = pick(module, "items", "Items") || [];
    items.forEach((item) => {
      const box = document.createElement("div");
      box.className = "policy-item";

      const labelRow = document.createElement("div");
      labelRow.className = "policy-label-row";

      const label = document.createElement("label");
      label.className = "policy-label";
      label.textContent = pick(item, "displayName", "DisplayName") || "未命名配置";
      labelRow.appendChild(label);

      const help = document.createElement("span");
      help.className = "policy-help";
      help.textContent = "?";
      help.title = `${pick(item, "description", "Description") || "暂无说明"}\n稳定键：${pick(item, "key", "Key") || ""}`;
      labelRow.appendChild(help);
      box.appendChild(labelRow);

      const meta = document.createElement("div");
      meta.className = "policy-item-meta";
      const unitText = pick(item, "unitText", "UnitText") || "";
      const minValue = pick(item, "minValue", "MinValue");
      const maxValue = pick(item, "maxValue", "MaxValue");
      meta.textContent = `键：${pick(item, "key", "Key")}  范围：${minValue} ~ ${maxValue}${unitText ? `  单位：${unitText}` : ""}`;
      box.appendChild(meta);

      const inputRow = document.createElement("div");
      inputRow.className = "policy-input-row";

      const input = document.createElement("input");
      input.type = "number";
      const key = pick(item, "key", "Key");
      input.value = String(getDraftValue(policyDrafts, policyDirtyKeys, key, String(pick(item, "value", "Value") ?? 0)));
      input.addEventListener("input", () => schedulePolicyAutoSave(key, input.value));
      inputRow.appendChild(input);

      const saveButton = createMiniButton("保存", "mini-btn-admin", () => {
        savePolicySetting(key, input.value.trim(), true).catch((error) => appendReply(error.message, false));
      });
      inputRow.appendChild(saveButton);
      box.appendChild(inputRow);
      itemsWrap.appendChild(box);
    });

    card.appendChild(itemsWrap);
    policyModulesEl.appendChild(card);
  });
}

function createConfigActionButton(text, onClick) {
  return createMiniButton(text, "mini-btn-admin", onClick, false);
}

function renderQqBridge(qqBridge, listenAddresses) {
  qqBridgePanelEl.innerHTML = "";
  const data = qqBridge || {};
  const enabled = !!pick(data, "enabled", "Enabled");
  const connected = !!pick(data, "connected", "Connected");
  const wsPath = pick(data, "wsPath", "WsPath") || "/onebot/ws";
  const hasAccessToken = !!pick(data, "hasAccessToken", "HasAccessToken");
  const botSelfId = pick(data, "botSelfId", "BotSelfId") || "";
  const replyAtSender = !!pick(data, "replyAtSender", "ReplyAtSender");
  const groupWhitelist = pick(data, "groupWhitelist", "GroupWhitelist") || "";
  const adminWhitelist = pick(data, "adminWhitelist", "AdminWhitelist") || "";
  const connectedBots = pick(data, "connectedBots", "ConnectedBots") || [];
  const recentGroups = pick(data, "recentGroups", "RecentGroups") || [];
  const recentMessages = pick(data, "recentMessages", "RecentMessages") || [];
  const wsExamples = (Array.isArray(listenAddresses) ? listenAddresses : [])
    .map((address) => toWebSocketUrl(address, wsPath))
    .filter((value) => !!value);

  const statusCard = document.createElement("section");
  statusCard.className = "qq-card";
  statusCard.innerHTML = `
    <div class="qq-card-head">
      <div>
        <p class="panel-tag">运行状态</p>
        <h3>OneBot 连接</h3>
      </div>
      <span class="qq-status ${connected ? "qq-status-on" : "qq-status-off"}">${enabled ? (connected ? "已连接" : "已启用未连接") : "未启用"}</span>
    </div>
    <div class="qq-meta-list">
      <div><strong>WS 路径</strong><span>${escapeHtml(wsPath)}</span></div>
      <div><strong>机器人 QQ</strong><span>${escapeHtml(botSelfId || "未限制 / 待连接上报")}</span></div>
      <div><strong>访问令牌</strong><span>${hasAccessToken ? "已配置" : "未配置"}</span></div>
      <div><strong>已连接实例</strong><span>${escapeHtml(connectedBots.length ? connectedBots.join("，") : "暂无")}</span></div>
      <div><strong>最近群号</strong><span>${escapeHtml(recentGroups.length ? recentGroups.join("，") : "暂无")}</span></div>
      <div><strong>管理员白名单</strong><span>${escapeHtml(adminWhitelist || "未配置")}</span></div>
    </div>
    <div class="qq-status-tip">${connected ? "OneBot 协议端已连接，群消息会进入自动盘。" : "当前还没有 OneBot 协议端连进来，QQ群里发指令不会生效。请把下面的 Reverse WS 地址填到 NapCat / LLOneBot 里。"} </div>
    <div class="qq-example-list">${wsExamples.length ? wsExamples.map((item) => `<div class="qq-example-item">${escapeHtml(item)}</div>`).join("") : '<div class="pending-empty">暂时没有可显示的本地监听地址。</div>'}</div>
    <div class="qq-log-box">${recentMessages.length ? recentMessages.map((item) => `<div class="qq-log-item">${escapeHtml(item)}</div>`).join("") : '<div class="pending-empty">当前还没有收到 QQ 群消息。</div>'}</div>
  `;
  qqBridgePanelEl.appendChild(statusCard);

  const baseCard = document.createElement("section");
  baseCard.className = "qq-card";
  baseCard.innerHTML = `
    <div class="qq-card-head">
      <div>
        <p class="panel-tag">基础开关</p>
        <h3>启用与回包</h3>
      </div>
    </div>
  `;
  const baseBody = document.createElement("div");
  baseBody.className = "qq-setting-list";

  const enabledRow = document.createElement("div");
  enabledRow.className = "qq-setting-row";
  const enabledLabel = document.createElement("label");
  enabledLabel.className = "qq-toggle";
  const enabledInput = document.createElement("input");
  enabledInput.type = "checkbox";
  enabledInput.checked = String(getDraftValue(qqDrafts, qqDirtyKeys, "qqAdapterEnabled", enabled ? "1" : "0")) === "1";
  enabledInput.addEventListener("change", () => scheduleQqAutoSave("qqAdapterEnabled", enabledInput.checked ? "1" : "0"));
  enabledLabel.appendChild(enabledInput);
  enabledLabel.appendChild(document.createTextNode("启用 QQ 群接入"));
  enabledRow.appendChild(enabledLabel);
  enabledRow.appendChild(createConfigActionButton("保存", () => saveQqSetting("qqAdapterEnabled", enabledInput.checked ? "1" : "0", true)));
  baseBody.appendChild(enabledRow);

  const replyRow = document.createElement("div");
  replyRow.className = "qq-setting-row";
  const replyLabel = document.createElement("label");
  replyLabel.className = "qq-toggle";
  const replyInput = document.createElement("input");
  replyInput.type = "checkbox";
  replyInput.checked = String(getDraftValue(qqDrafts, qqDirtyKeys, "qqReplyAtSender", replyAtSender ? "1" : "0")) === "1";
  replyInput.addEventListener("change", () => scheduleQqAutoSave("qqReplyAtSender", replyInput.checked ? "1" : "0"));
  replyLabel.appendChild(replyInput);
  replyLabel.appendChild(document.createTextNode("群回包时自动 @ 发送者"));
  replyRow.appendChild(replyLabel);
  replyRow.appendChild(createConfigActionButton("保存", () => saveQqSetting("qqReplyAtSender", replyInput.checked ? "1" : "0", true)));
  baseBody.appendChild(replyRow);
  baseCard.appendChild(baseBody);
  qqBridgePanelEl.appendChild(baseCard);

  const socketCard = document.createElement("section");
  socketCard.className = "qq-card";
  socketCard.innerHTML = `
    <div class="qq-card-head">
      <div>
        <p class="panel-tag">协议端配置</p>
        <h3>OneBot 路径与鉴权</h3>
      </div>
    </div>
  `;
  const socketBody = document.createElement("div");
  socketBody.className = "qq-setting-list";
  [
    { label: "WS 路径", key: "qqOneBotWsPath", value: wsPath, type: "text", placeholder: "/onebot/ws", help: "协议端 Reverse WS 指向本模组时使用的路径。" },
    { label: "访问令牌", key: "qqOneBotAccessToken", value: "", type: "password", placeholder: hasAccessToken ? "当前已保存令牌；留空保存会清空" : "可留空", help: "对应 OneBot Access Token；留空则不校验。页面不会回显已保存的令牌明文。" },
    { label: "机器人 QQ", key: "qqBotSelfId", value: botSelfId, type: "text", placeholder: "可留空", help: "留空表示接受任意 self_id；填写后只允许该机器人连接。" }
  ].forEach((item) => {
    const row = document.createElement("div");
    row.className = "qq-setting-row";
    const field = document.createElement("div");
    field.className = "qq-setting-field";
    const label = document.createElement("label");
    label.textContent = item.label;
    const input = document.createElement("input");
    input.type = item.type;
    input.value = String(getDraftValue(qqDrafts, qqDirtyKeys, item.key, item.value));
    input.placeholder = item.placeholder;
    input.addEventListener("input", () => scheduleQqAutoSave(item.key, input.value));
    field.appendChild(label);
    field.appendChild(input);
    const tip = document.createElement("div");
    tip.className = "qq-setting-help";
    tip.textContent = item.help;
    field.appendChild(tip);
    row.appendChild(field);
    row.appendChild(createConfigActionButton("保存", () => saveQqSetting(item.key, input.value.trim(), true)));
    socketBody.appendChild(row);
  });
  socketCard.appendChild(socketBody);
  qqBridgePanelEl.appendChild(socketCard);

  const scopeCard = document.createElement("section");
  scopeCard.className = "qq-card";
  scopeCard.innerHTML = `
    <div class="qq-card-head">
      <div>
        <p class="panel-tag">群范围</p>
        <h3>白名单控制</h3>
      </div>
    </div>
  `;
  const scopeBody = document.createElement("div");
  scopeBody.className = "qq-setting-list";
  const scopeRow = document.createElement("div");
  scopeRow.className = "qq-setting-row qq-setting-row-stack";
  const scopeField = document.createElement("div");
  scopeField.className = "qq-setting-field";
  const scopeLabel = document.createElement("label");
  scopeLabel.textContent = "群白名单";
  const scopeInput = document.createElement("textarea");
  scopeInput.rows = 4;
  scopeInput.value = String(getDraftValue(qqDrafts, qqDirtyKeys, "qqGroupWhitelist", groupWhitelist));
  scopeInput.placeholder = "留空表示所有群都可用；多个群号用逗号、空格或换行分隔";
  scopeInput.addEventListener("input", () => scheduleQqAutoSave("qqGroupWhitelist", scopeInput.value));
  scopeField.appendChild(scopeLabel);
  scopeField.appendChild(scopeInput);
  const scopeTip = document.createElement("div");
  scopeTip.className = "qq-setting-help";
  scopeTip.textContent = "建议公开发布时默认留空，方便普通用户直接用；需要控群时再填写。";
  scopeField.appendChild(scopeTip);
  scopeRow.appendChild(scopeField);
  scopeRow.appendChild(createConfigActionButton("保存白名单", () => saveQqSetting("qqGroupWhitelist", scopeInput.value, true)));
  scopeBody.appendChild(scopeRow);
  scopeCard.appendChild(scopeBody);
  qqBridgePanelEl.appendChild(scopeCard);

  const adminCard = document.createElement("section");
  adminCard.className = "qq-card";
  adminCard.innerHTML = `
    <div class="qq-card-head">
      <div>
        <p class="panel-tag">权限范围</p>
        <h3>QQ 管理员白名单</h3>
      </div>
    </div>
  `;
  const adminBody = document.createElement("div");
  adminBody.className = "qq-setting-list";
  const adminRow = document.createElement("div");
  adminRow.className = "qq-setting-row qq-setting-row-stack";
  const adminField = document.createElement("div");
  adminField.className = "qq-setting-field";
  const adminLabel = document.createElement("label");
  adminLabel.textContent = "管理员 QQ 白名单";
  const adminInput = document.createElement("textarea");
  adminInput.rows = 4;
  adminInput.value = String(getDraftValue(qqDrafts, qqDirtyKeys, "qqAdminWhitelist", adminWhitelist));
  adminInput.placeholder = "留空表示群里的 # 管理员指令全部拒绝；多个 QQ 号用逗号、空格或换行分隔";
  adminInput.addEventListener("input", () => scheduleQqAutoSave("qqAdminWhitelist", adminInput.value));
  adminField.appendChild(adminLabel);
  adminField.appendChild(adminInput);
  const adminTip = document.createElement("div");
  adminTip.className = "qq-setting-help";
  adminTip.textContent = "只有这里的 QQ 号才允许在群里执行 # 开头的管理员指令；网页端不受这个白名单限制。";
  adminField.appendChild(adminTip);
  adminRow.appendChild(adminField);
  adminRow.appendChild(createConfigActionButton("保存管理员白名单", () => saveQqSetting("qqAdminWhitelist", adminInput.value, true)));
  adminBody.appendChild(adminRow);
  adminCard.appendChild(adminBody);
  qqBridgePanelEl.appendChild(adminCard);
}

function renderAiControls(snapshot) {
  const aiEnabled = !!pick(snapshot, "aiEnabled", "AiEnabled");
  const aiQqChatEnabled = !!pick(snapshot, "aiQqChatEnabled", "AiQqChatEnabled");
  aiStatusEl.textContent = `AI：${aiEnabled ? "已开启" : "已关闭"}`;
  if (aiEnableToggleEl) {
    aiEnableToggleEl.checked = aiEnabled;
  }
  if (aiQqChatToggleEl) {
    aiQqChatToggleEl.checked = aiQqChatEnabled;
  }
}

function renderScoreboard(scoreboard) {
  scoreTableBodyEl.innerHTML = "";
  const items = Array.isArray(scoreboard) ? scoreboard : [];
  if (items.length === 0) {
    const emptyRow = document.createElement("tr");
    const cell = document.createElement("td");
    cell.colSpan = 6;
    cell.className = "table-empty";
    cell.textContent = "当前还没有积分记录。";
    emptyRow.appendChild(cell);
    scoreTableBodyEl.appendChild(emptyRow);
    return;
  }

  items.forEach((record, index) => {
    const userId = pick(record, "userId", "UserId") || "";
    const playerName = pick(record, "playerName", "PlayerName") || userId;
    const wins = Number(pick(record, "wins", "Wins") ?? 0);
    const row = document.createElement("tr");

    const rankCell = document.createElement("td");
    rankCell.textContent = String(index + 1);
    row.appendChild(rankCell);

    const userCell = document.createElement("td");
    userCell.className = "wide-cell";
    userCell.textContent = userId;
    row.appendChild(userCell);

    const nameCell = document.createElement("td");
    const nameInput = document.createElement("input");
    nameInput.className = "score-inline-input";
    nameInput.type = "text";
    nameInput.value = playerName;
    nameCell.appendChild(nameInput);
    row.appendChild(nameCell);

    const winsCell = document.createElement("td");
    const winsInput = document.createElement("input");
    winsInput.className = "score-inline-input score-wins-input";
    winsInput.type = "number";
    winsInput.min = "0";
    winsInput.value = String(Math.max(0, wins));
    winsCell.appendChild(winsInput);
    row.appendChild(winsCell);

    const lastWinCell = document.createElement("td");
    lastWinCell.textContent = formatIsoTime(pick(record, "lastWinUtc", "LastWinUtc"));
    row.appendChild(lastWinCell);

    const actionCell = document.createElement("td");
    actionCell.className = "action-cell";
    const actions = document.createElement("div");
    actions.className = "row-actions";
    actions.appendChild(createMiniButton("保存", "mini-btn-admin", () => saveScore(userId, nameInput.value, winsInput.value)));
    actions.appendChild(createMiniButton("删除", "mini-btn-danger", () => deleteScore(userId, playerName)));
    actionCell.appendChild(actions);
    row.appendChild(actionCell);
    scoreTableBodyEl.appendChild(row);
  });
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
    actions.appendChild(createMiniButton("约斗", "mini-btn-neutral", () => sendCommand(`约斗 ${kingdomLabel}`), isCurrent));
    actions.appendChild(createMiniButton("削灵500", "mini-btn-danger", () => sendCommand(`削灵 ${kingdomLabel} 500`), isCurrent));
    actions.appendChild(createMiniButton("斩首", "mini-btn-danger", () => sendCommand(`斩首 ${kingdomLabel}`), isCurrent));
    actions.appendChild(createMiniButton("诅咒3人", "mini-btn-danger", () => sendCommand(`诅咒 ${kingdomLabel} 3`), isCurrent));
    actions.appendChild(createMiniButton("修士-1境", "mini-btn-neutral", () => sendCommand(`修士降境 ${kingdomLabel} 2 1`), isCurrent));
    actions.appendChild(createMiniButton("降低国运1", "mini-btn-neutral", () => sendCommand(`降低国运 ${kingdomLabel} 1`), isCurrent));

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
  syncPolicyDrafts(pick(snapshot, "policy", "Policy"));
  syncQqDrafts(pick(snapshot, "qqBridge", "QqBridge"));
  updateBinding(pick(snapshot, "binding", "Binding"));
  renderLogs(pick(snapshot, "recentLogs", "RecentLogs"));
  const listenAddresses = pick(snapshot, "listenAddresses", "ListenAddresses");
  renderAddresses(listenAddresses);
  renderKingdoms(pick(snapshot, "kingdoms", "Kingdoms"));
  renderPendingRequests(pick(snapshot, "pendingRequests", "PendingRequests"));
  renderScoreboard(pick(snapshot, "scoreboard", "Scoreboard"));
  renderPolicy(pick(snapshot, "policy", "Policy"));
  renderQqBridge(pick(snapshot, "qqBridge", "QqBridge"), listenAddresses);

  const commandBookText = pick(snapshot, "commandBookText", "CommandBookText");
  if (commandBookText) {
    commandBookEl.textContent = commandBookText;
  } else {
    const bookResponse = await fetch("/指令书.txt", { cache: "no-store" });
    commandBookEl.textContent = bookResponse.ok ? await bookResponse.text() : "";
  }

  renderAiControls(snapshot);
}

function schedulePolicyAutoSave(key, value) {
  rememberDraft(policyDrafts, policyDirtyKeys, key, value);
}

async function savePolicySetting(key, value, refreshAfter = false) {
  const response = await fetch(`/api/policy/set?key=${encodeURIComponent(key)}&value=${encodeURIComponent(value)}`, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`政策保存失败：${response.status}`);
  }

  const payload = await response.json();
  if (!payload.ok) {
    appendReply(payload.text || "政策保存失败。", false);
    return false;
  }

  clearDraftIfSaved(policyDrafts, policyDirtyKeys, key, value);
  if (refreshAfter) {
    appendReply(payload.text || "政策已保存。", true);
    await refreshDashboard();
  }
  return true;
}

function scheduleQqAutoSave(key, value) {
  rememberDraft(qqDrafts, qqDirtyKeys, key, value);
}

async function saveQqSetting(key, value, refreshAfter = false) {
  const response = await fetch(`/api/qq-config/set?key=${encodeURIComponent(key)}&value=${encodeURIComponent(value)}`, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`QQ 配置保存失败：${response.status}`);
  }

  const payload = await response.json();
  if (!payload.ok) {
    appendReply(payload.text || "QQ 配置保存失败。", false);
    return false;
  }

  clearDraftIfSaved(qqDrafts, qqDirtyKeys, key, value);
  if (refreshAfter) {
    appendReply(payload.text || "QQ 配置已保存。", true);
    await refreshDashboard();
  }
  return true;
}

async function saveDirtyConfig() {
  const policyEntries = Array.from(policyDirtyKeys).map((key) => [key, policyDrafts[key]]);
  const qqEntries = Array.from(qqDirtyKeys).map((key) => [key, qqDrafts[key]]);
  if (policyEntries.length + qqEntries.length === 0) {
    appendReply("没有需要保存的配置修改。", true);
    return;
  }

  isSavingDirtyConfig = true;
  updateDirtyStatus();
  try {
    let saved = 0;
    let failed = 0;
    for (const [key, value] of policyEntries) {
      try {
        if (await savePolicySetting(key, value, false)) {
          saved++;
        } else {
          failed++;
        }
      } catch (error) {
        failed++;
        appendReply(error.message, false);
      }
    }

    for (const [key, value] of qqEntries) {
      try {
        if (await saveQqSetting(key, value, false)) {
          saved++;
        } else {
          failed++;
        }
      } catch (error) {
        failed++;
        appendReply(error.message, false);
      }
    }

    appendReply(`配置批量保存完成：成功 ${saved} 项，失败 ${failed} 项。`, failed === 0);
    await refreshDashboard();
  } finally {
    isSavingDirtyConfig = false;
    updateDirtyStatus();
  }
}

async function discardDirtyConfig() {
  Object.keys(policyDrafts).forEach((key) => delete policyDrafts[key]);
  Object.keys(qqDrafts).forEach((key) => delete qqDrafts[key]);
  policyDirtyKeys.clear();
  qqDirtyKeys.clear();
  updateDirtyStatus();
  appendReply("已放弃未保存的配置修改。", true);
  await refreshDashboard();
}

async function saveScore(userId, playerName, wins) {
  const normalizedUserId = String(userId || "").trim();
  const normalizedWins = Number.parseInt(String(wins || "0"), 10);
  if (!normalizedUserId) {
    appendReply("积分保存失败：userId 不能为空。", false);
    return;
  }
  if (!Number.isInteger(normalizedWins) || normalizedWins < 0) {
    appendReply("积分保存失败：积分必须是非负整数。", false);
    return;
  }

  const url = `/api/score/set?userId=${encodeURIComponent(normalizedUserId)}&playerName=${encodeURIComponent(String(playerName || "").trim())}&wins=${encodeURIComponent(String(normalizedWins))}`;
  const response = await fetch(url, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`积分保存失败：${response.status}`);
  }

  const payload = await response.json();
  appendReply(payload.text || (payload.ok ? "积分已保存。" : "积分保存失败。"), !!payload.ok);
  if (payload.ok) {
    scoreUserIdEl.value = "";
    scorePlayerNameEl.value = "";
    scoreWinsEl.value = "0";
    renderScoreboard(pick(payload, "scoreboard", "Scoreboard"));
    await refreshDashboard();
  }
}

async function deleteScore(userId, playerName) {
  const normalizedUserId = String(userId || "").trim();
  if (!normalizedUserId) {
    appendReply("积分删除失败：userId 不能为空。", false);
    return;
  }

  const label = playerName && playerName !== normalizedUserId ? `${playerName}(${normalizedUserId})` : normalizedUserId;
  if (!window.confirm(`确定删除 ${label} 的积分记录？这不会删除当前绑定国家。`)) {
    return;
  }

  const response = await fetch(`/api/score/delete?userId=${encodeURIComponent(normalizedUserId)}`, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`积分删除失败：${response.status}`);
  }

  const payload = await response.json();
  appendReply(payload.text || (payload.ok ? "积分记录已删除。" : "积分删除失败。"), !!payload.ok);
  if (payload.ok) {
    renderScoreboard(pick(payload, "scoreboard", "Scoreboard"));
    await refreshDashboard();
  }
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

  socket.addEventListener("close", (event) => {
    wsStatusEl.textContent = "WebSocket：已断开，2 秒后重连";
    appendReply(`WebSocket 已断开：code=${event.code}`, false);
    setTimeout(connectWebSocket, 2000);
  });

  socket.addEventListener("message", async (event) => {
    const payload = JSON.parse(event.data);
    if (payload.type === "reply") {
      appendReply(payload.text, !!payload.ok);
      updateBinding(payload.binding);
      aiStatusEl.textContent = `AI：${payload.aiEnabled ? "已开启" : "已关闭"}`;
      if (aiEnableToggleEl) {
        aiEnableToggleEl.checked = !!payload.aiEnabled;
      }
      await refreshDashboard().catch((error) => appendReply(error.message, false));
    }
  });
}

function sendCommand(command) {
  const text = (command || commandTextEl.value).trim();
  if (!text) {
    appendReply("请输入要发送的命令。", false);
    return false;
  }
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    appendReply("WebSocket 尚未连接成功。", false);
    return false;
  }

  socket.send(JSON.stringify({
    userId: userIdEl.value.trim(),
    playerName: playerNameEl.value.trim(),
    text
  }));

  if (!command) {
    commandTextEl.value = "";
  }
  return true;
}

document.querySelectorAll("[data-command]").forEach((button) => {
  button.addEventListener("click", () => sendCommand(button.dataset.command));
});

pageTabEls.forEach((button) => {
  button.addEventListener("click", () => switchPage(button.dataset.pageTarget));
});

configTabEls.forEach((button) => {
  button.addEventListener("click", () => switchConfigModule(button.dataset.configTarget));
});

if (saveDirtyConfigButtonEl) {
  saveDirtyConfigButtonEl.addEventListener("click", () => {
    saveDirtyConfig().catch((error) => appendReply(error.message, false));
  });
}

if (discardDirtyConfigButtonEl) {
  discardDirtyConfigButtonEl.addEventListener("click", () => {
    discardDirtyConfig().catch((error) => appendReply(error.message, false));
  });
}

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
  refreshDashboard().catch((error) => appendReply(error.message, false));
});

if (aiEnableToggleEl) {
  aiEnableToggleEl.addEventListener("change", () => {
    const nextChecked = aiEnableToggleEl.checked;
    if (!sendCommand(nextChecked ? "#全局AI 开" : "#全局AI 关")) {
      aiEnableToggleEl.checked = !nextChecked;
    }
  });
}

if (aiQqChatToggleEl) {
  aiQqChatToggleEl.addEventListener("change", () => {
    savePolicySetting("aiQqChatEnabled", aiQqChatToggleEl.checked ? "1" : "0", true)
      .catch((error) => appendReply(error.message, false));
  });
}

scoreSaveButtonEl.addEventListener("click", () => {
  saveScore(scoreUserIdEl.value, scorePlayerNameEl.value, scoreWinsEl.value).catch((error) => appendReply(error.message, false));
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
    if (shouldSuspendAutoRefresh()) {
      return;
    }
    refreshDashboard().catch(() => {});
  }, 3000);
}

const exportConfigButtonEl = document.getElementById("exportConfigButton");
const importConfigButtonEl = document.getElementById("importConfigButton");
const importConfigFileEl = document.getElementById("importConfigFile");

async function exportConfig() {
  try {
    const response = await fetch(`/api/dashboard?userId=${encodeURIComponent(userIdEl.value.trim())}`, { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`获取配置失败：${response.status}`);
    }
    const snapshot = await response.json();

    const policy = pick(snapshot, "policy", "Policy");
    const modules = pick(policy, "modules", "Modules") || [];
    const policyData = {};
    modules.forEach((module) => {
      const items = pick(module, "items", "Items") || [];
      items.forEach((item) => {
        const key = pick(item, "key", "Key");
        const value = pick(item, "value", "Value");
        if (key != null) {
          policyData[key] = value;
        }
      });
    });

    const qqBridge = pick(snapshot, "qqBridge", "QqBridge") || {};
    const qqData = {
      qqAdapterEnabled: !!pick(qqBridge, "enabled", "Enabled"),
      qqOneBotWsPath: pick(qqBridge, "wsPath", "WsPath") || "/onebot/ws",
      qqBotSelfId: pick(qqBridge, "botSelfId", "BotSelfId") || "",
      qqReplyAtSender: !!pick(qqBridge, "replyAtSender", "ReplyAtSender"),
      qqGroupWhitelist: pick(qqBridge, "groupWhitelist", "GroupWhitelist") || "",
      qqAdminWhitelist: pick(qqBridge, "adminWhitelist", "AdminWhitelist") || ""
    };

    const exportData = {
      _exportVersion: 1,
      _exportTime: new Date().toISOString(),
      policy: policyData,
      qqBridge: qqData
    };

    const blob = new Blob([JSON.stringify(exportData, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `xianni-autopan-config-${new Date().toISOString().slice(0, 10)}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
    appendReply("配置已导出。", true);
  } catch (error) {
    appendReply(error.message, false);
  }
}

async function importConfig() {
  const file = importConfigFileEl.files[0];
  if (!file) {
    return;
  }

  try {
    const text = await file.text();
    const data = JSON.parse(text);
    if (!data || typeof data !== "object") {
      throw new Error("无效的配置文件格式。");
    }

    let count = 0;
    const errors = [];

    if (data.policy && typeof data.policy === "object") {
      for (const [key, value] of Object.entries(data.policy)) {
        try {
          await savePolicySetting(key, String(value), false);
          count++;
        } catch (error) {
          errors.push(`政策 ${key}: ${error.message}`);
        }
      }
    }

    if (data.qqBridge && typeof data.qqBridge === "object") {
      const qqMap = {
        qqAdapterEnabled: data.qqBridge.qqAdapterEnabled ? "1" : "0",
        qqOneBotWsPath: data.qqBridge.qqOneBotWsPath || "",
        qqBotSelfId: data.qqBridge.qqBotSelfId || "",
        qqReplyAtSender: data.qqBridge.qqReplyAtSender ? "1" : "0",
        qqGroupWhitelist: data.qqBridge.qqGroupWhitelist || "",
        qqAdminWhitelist: data.qqBridge.qqAdminWhitelist || ""
      };
      for (const [key, value] of Object.entries(qqMap)) {
        if (value === "") {
          continue;
        }
        try {
          await saveQqSetting(key, value, false);
          count++;
        } catch (error) {
          errors.push(`QQ ${key}: ${error.message}`);
        }
      }
    }

    if (errors.length > 0) {
      appendReply(`导入完成，成功 ${count} 项，失败 ${errors.length} 项：${errors.join("；")}`, false);
    } else {
      appendReply(`配置导入成功，共 ${count} 项已应用。`, true);
    }

    await refreshDashboard();
  } catch (error) {
    appendReply(`导入失败：${error.message}`, false);
  }

  importConfigFileEl.value = "";
}

exportConfigButtonEl.addEventListener("click", exportConfig);
importConfigButtonEl.addEventListener("click", () => importConfigFileEl.click());
importConfigFileEl.addEventListener("change", importConfig);

switchPage(currentPage, true);
switchConfigModule(currentConfigModule, true);
updateDirtyStatus();
connectWebSocket();
refreshDashboard().catch((error) => appendReply(error.message, false));
startDashboardPolling();
