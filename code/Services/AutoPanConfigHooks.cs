using NeoModLoader.api;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 自动盘配置回调与缓存。
    /// </summary>
    public static class AutoPanConfigHooks
    {
        /// <summary>
        /// 是否启用 LLM AI。
        /// </summary>
        public static bool EnableLlmAi { get; private set; }

        /// <summary>
        /// LLM OpenAI 兼容接口地址。
        /// </summary>
        public static string LlmApiUrl { get; private set; } = string.Empty;

        /// <summary>
        /// LLM 模型名。
        /// </summary>
        public static string LlmModel { get; private set; } = string.Empty;

        /// <summary>
        /// LLM API Key。
        /// </summary>
        public static string LlmApiKey { get; private set; } = string.Empty;

        /// <summary>
        /// 本地网页端口。
        /// </summary>
        public static int HttpPort { get; private set; } = 19051;

        /// <summary>
        /// 监听主机配置。
        /// </summary>
        public static string BindHost { get; private set; } = "*";

        /// <summary>
        /// 从当前配置初始化静态缓存。
        /// </summary>
        public static void InitializeFromConfig(ModConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (config["autopan_config_basic"].TryGetValue("autopan_enable_llm_ai", out ModConfigItem aiSwitch))
            {
                OnEnableLlmAiChanged(aiSwitch.BoolVal);
            }
            if (config["autopan_config_basic"].TryGetValue("autopan_http_port", out ModConfigItem port))
            {
                OnHttpPortChanged(port.TextVal);
            }
            if (config["autopan_config_basic"].TryGetValue("autopan_bind_host", out ModConfigItem host))
            {
                OnBindHostChanged(host.TextVal);
            }
            if (config["autopan_config_ai"].TryGetValue("autopan_llm_api_url", out ModConfigItem apiUrl))
            {
                OnLlmApiUrlChanged(apiUrl.TextVal);
            }
            if (config["autopan_config_ai"].TryGetValue("autopan_llm_model", out ModConfigItem model))
            {
                OnLlmModelChanged(model.TextVal);
            }
            if (config["autopan_config_ai"].TryGetValue("autopan_llm_api_key", out ModConfigItem apiKey))
            {
                OnLlmApiKeyChanged(apiKey.TextVal);
            }
        }

        /// <summary>
        /// LLM AI 开关配置回调。
        /// </summary>
        public static void OnEnableLlmAiChanged(bool value)
        {
            EnableLlmAi = value;
        }

        /// <summary>
        /// LLM API 地址配置回调。
        /// </summary>
        public static void OnLlmApiUrlChanged(string value)
        {
            LlmApiUrl = (value ?? string.Empty).Trim();
        }

        /// <summary>
        /// LLM 模型配置回调。
        /// </summary>
        public static void OnLlmModelChanged(string value)
        {
            LlmModel = (value ?? string.Empty).Trim();
        }

        /// <summary>
        /// LLM API Key 配置回调。
        /// </summary>
        public static void OnLlmApiKeyChanged(string value)
        {
            LlmApiKey = (value ?? string.Empty).Trim();
        }

        /// <summary>
        /// HTTP 端口配置回调。
        /// </summary>
        public static void OnHttpPortChanged(string value)
        {
            if (!int.TryParse(value, out int port))
            {
                port = 19051;
            }

            if (port < 1)
            {
                port = 1;
            }
            if (port > 65535)
            {
                port = 65535;
            }

            HttpPort = port;
        }

        /// <summary>
        /// 监听主机配置回调。
        /// </summary>
        public static void OnBindHostChanged(string value)
        {
            BindHost = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();
        }
    }
}
