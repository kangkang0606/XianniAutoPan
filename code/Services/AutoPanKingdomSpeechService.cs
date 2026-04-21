using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace XianniAutoPan.Services
{
    /// <summary>
    /// 在国家铭牌位置显示玩家聊天与指令气泡，并复用原版聊天提示图标。
    /// </summary>
    internal static class AutoPanKingdomSpeechService
    {
        private sealed class SpeechLine
        {
            /// <summary>
            /// 显示文本。
            /// </summary>
            public string Text;

            /// <summary>
            /// 到期时间。
            /// </summary>
            public float ExpireAt;

            /// <summary>
            /// 是否为指令。
            /// </summary>
            public bool IsCommand;
        }

        private sealed class SpeechAnchor
        {
            /// <summary>
            /// 最近一次铭牌屏幕坐标。
            /// </summary>
            public Vector2 ScreenPosition;

            /// <summary>
            /// 最近刷新时间。
            /// </summary>
            public float LastSeenAt;
        }

        private sealed class SpeechVisual
        {
            /// <summary>
            /// 对应国家 ID。
            /// </summary>
            public long KingdomId;

            /// <summary>
            /// 当前 UI 根对象。
            /// </summary>
            public GameObject Root;

            /// <summary>
            /// 根 RectTransform。
            /// </summary>
            public RectTransform RootRect;

            /// <summary>
            /// 气泡背景。
            /// </summary>
            public Image BubbleImage;

            /// <summary>
            /// 气泡文本。
            /// </summary>
            public Text Text;

            /// <summary>
            /// 文本容器。
            /// </summary>
            public RectTransform TextRect;

            /// <summary>
            /// 最近使用的图标锚点。
            /// </summary>
            public Actor BubbleActor;

            /// <summary>
            /// 最近一次刷新气泡的时间。
            /// </summary>
            public float LastBubbleRefreshTime;

            /// <summary>
            /// 当前文本条目。
            /// </summary>
            public List<SpeechLine> Lines = new List<SpeechLine>();
        }

        private const float SpeechLifetimeSeconds = 18f;
        private const float BubbleRefreshIntervalSeconds = 0.8f;
        private const float BubbleYOffset = 42f;
        private const float BubbleMinWidth = 110f;
        private const float BubbleMaxWidth = 340f;
        private const float BubbleMinHeight = 42f;
        private const float BubblePaddingH = 28f;
        private const float BubblePaddingV = 16f;
        private const int MaxSpeechEntries = 3;
        private const int MaxLineLength = 56;
        private static readonly Dictionary<long, SpeechAnchor> Anchors = new Dictionary<long, SpeechAnchor>();
        private static readonly Dictionary<long, SpeechVisual> Visuals = new Dictionary<long, SpeechVisual>();

        /// <summary>
        /// 记录国家铭牌的屏幕坐标，供气泡锚定使用。
        /// </summary>
        public static void RecordNameplatePosition(Kingdom kingdom, Vector2 screenPosition)
        {
            if (kingdom == null || !kingdom.isAlive())
            {
                return;
            }

            Anchors[kingdom.getID()] = new SpeechAnchor
            {
                ScreenPosition = screenPosition,
                LastSeenAt = Time.unscaledTime
            };
        }

        /// <summary>
        /// 在指定国家铭牌位置显示一条聊天或指令文本。
        /// </summary>
        public static void ShowSpeech(Kingdom kingdom, string speakerName, string content, bool isCommand)
        {
            if (kingdom == null || !kingdom.isAlive() || string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            SpeechVisual visual = GetOrCreateVisual(kingdom);
            if (visual == null)
            {
                return;
            }

            visual.Lines.RemoveAll(item => item == null || item.ExpireAt <= Time.unscaledTime);
            visual.Lines.Add(new SpeechLine
            {
                Text = FormatLine(speakerName, content, isCommand),
                ExpireAt = Time.unscaledTime + SpeechLifetimeSeconds,
                IsCommand = isCommand
            });
            if (visual.Lines.Count > MaxSpeechEntries)
            {
                visual.Lines.RemoveRange(0, visual.Lines.Count - MaxSpeechEntries);
            }

            RefreshVisual(kingdom, visual);
        }

        /// <summary>
        /// 每帧只清理气泡到期状态，位置刷新交给铭牌管理器后置补丁。
        /// </summary>
        public static void Update()
        {
            if (Visuals.Count == 0)
            {
                return;
            }

            List<long> toRemove = new List<long>();
            foreach (KeyValuePair<long, SpeechVisual> pair in Visuals)
            {
                Kingdom kingdom = World.world?.kingdoms?.get(pair.Key);
                SpeechVisual visual = pair.Value;
                if (kingdom == null || !kingdom.isAlive() || !kingdom.isCiv())
                {
                    DestroyVisual(visual);
                    toRemove.Add(pair.Key);
                    continue;
                }

                visual.Lines.RemoveAll(item => item == null || item.ExpireAt <= Time.unscaledTime);
                if (visual.Lines.Count == 0)
                {
                    DestroyVisual(visual);
                    toRemove.Add(pair.Key);
                    continue;
                }
            }

            foreach (long kingdomId in toRemove)
            {
                Visuals.Remove(kingdomId);
                Anchors.Remove(kingdomId);
            }
        }

        /// <summary>
        /// 在原版铭牌位置刷新完成后统一更新自动盘气泡位置。
        /// </summary>
        public static void UpdateAnchoredVisuals()
        {
            if (Visuals.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<long, SpeechVisual> pair in Visuals.ToList())
            {
                Kingdom kingdom = World.world?.kingdoms?.get(pair.Key);
                SpeechVisual visual = pair.Value;
                if (kingdom == null || !kingdom.isAlive() || visual?.Root == null || visual.Lines.Count == 0)
                {
                    continue;
                }

                RefreshVisual(kingdom, visual);
            }
        }

        /// <summary>
        /// 清理所有国家聊天显示。
        /// </summary>
        public static void ClearAll()
        {
            foreach (SpeechVisual visual in Visuals.Values)
            {
                DestroyVisual(visual);
            }

            Visuals.Clear();
            Anchors.Clear();
        }

        /// <summary>
        /// 销毁服务持有的气泡对象。
        /// </summary>
        public static void Dispose()
        {
            ClearAll();
        }

        private static SpeechVisual GetOrCreateVisual(Kingdom kingdom)
        {
            if (Visuals.TryGetValue(kingdom.getID(), out SpeechVisual existing))
            {
                if (existing.Root == null)
                {
                    Visuals.Remove(kingdom.getID());
                }
                else
                {
                    EnsureParent(existing.Root.transform);
                    return existing;
                }
            }

            Transform parent = ResolveUiParent();
            if (parent == null)
            {
                return null;
            }

            SpeechVisual visual = new SpeechVisual
            {
                KingdomId = kingdom.getID()
            };

            GameObject root = new GameObject($"XianniAutoPanSpeech_{kingdom.getID()}", typeof(RectTransform));
            root.hideFlags = HideFlags.DontSave;
            root.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0f);
            Image bubbleImage = root.AddComponent<Image>();
            bubbleImage.type = Image.Type.Simple;
            bubbleImage.color = new Color(1f, 1f, 1f, 0.96f);
            bubbleImage.raycastTarget = false;
            Outline bubbleOutline = root.AddComponent<Outline>();
            bubbleOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            bubbleOutline.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            textObject.hideFlags = HideFlags.DontSave;
            textObject.transform.SetParent(root.transform, worldPositionStays: false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);

            Text text = textObject.AddComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = 16;
            text.fontStyle = FontStyle.Normal;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.08f;
            text.supportRichText = false;
            text.raycastTarget = false;

            visual.Root = root;
            visual.RootRect = rootRect;
            visual.BubbleImage = bubbleImage;
            visual.Text = text;
            visual.TextRect = textRect;
            Visuals[kingdom.getID()] = visual;
            return visual;
        }

        private static void EnsureParent(Transform child)
        {
            Transform parent = ResolveUiParent();
            if (child == null || parent == null || child.parent == parent)
            {
                return;
            }

            child.SetParent(parent, worldPositionStays: false);
        }

        private static Transform ResolveUiParent()
        {
            Canvas mapNames = CanvasMain.instance?.canvas_map_names;
            if (mapNames != null)
            {
                return mapNames.transform;
            }

            return MapBox.instance?.nameplate_manager?.transform;
        }

        private static Font ResolveFont()
        {
            Text source = MapBox.instance?.nameplate_manager?.prefab?._text_name;
            if (source != null && source.font != null)
            {
                return source.font;
            }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static void RefreshVisual(Kingdom kingdom, SpeechVisual visual)
        {
            if (visual?.Root == null || visual.Text == null || visual.RootRect == null)
            {
                return;
            }

            EnsureParent(visual.Root.transform);
            if (!TryGetAnchorScreenPosition(kingdom, out Vector2 anchorPosition))
            {
                return;
            }

            if (!visual.Root.activeSelf)
            {
                visual.Root.SetActive(true);
            }

            visual.Root.transform.SetAsLastSibling();
            visual.Root.transform.position = new Vector3(anchorPosition.x, anchorPosition.y + BubbleYOffset, 0f);

            visual.Root.transform.localScale = new Vector3(0.4f, 0.4f, 1f);

            string text = string.Join("\n", visual.Lines.Select(item => item.Text).ToArray());
            visual.Text.text = text;
            visual.Text.color = Color.black;

            // 宽度根据文本内容自适应，不再固定撑满最大值
            float preferredTextWidth = visual.Text.preferredWidth;
            float bubbleWidth = Mathf.Clamp(preferredTextWidth + BubblePaddingH, BubbleMinWidth, BubbleMaxWidth);
            float textWidth = bubbleWidth - BubblePaddingH;
            visual.TextRect.sizeDelta = new Vector2(textWidth, 0f);
            float bubbleHeight = Mathf.Max(BubbleMinHeight, visual.Text.preferredHeight + BubblePaddingV);
            visual.RootRect.sizeDelta = new Vector2(bubbleWidth, bubbleHeight);
            visual.TextRect.sizeDelta = new Vector2(textWidth, bubbleHeight - BubblePaddingV);

            visual.BubbleImage.color = BuildBubbleColor();
            // 高倍速下原版社交图标会因单位移动/战斗状态快速切换而闪烁，
            // 这里只保留铭牌文字气泡，不再复用原版头顶聊天图标。
            ReleaseBubbleIcon(visual);
        }

        private static bool TryGetAnchorScreenPosition(Kingdom kingdom, out Vector2 screenPosition)
        {
            screenPosition = default;
            if (kingdom != null && Anchors.TryGetValue(kingdom.getID(), out SpeechAnchor anchor))
            {
                screenPosition = anchor.ScreenPosition;
                return true;
            }

            return TryGetFallbackScreenPosition(kingdom, out screenPosition);
        }

        private static bool TryGetFallbackScreenPosition(Kingdom kingdom, out Vector2 screenPosition)
        {
            screenPosition = default;
            if (kingdom == null || MapBox.instance?.camera == null)
            {
                return false;
            }

            Actor actor = FindAnchorActor(kingdom);
            Vector2 worldPosition;
            if (actor != null && actor.isAlive())
            {
                worldPosition = actor.current_position;
            }
            else if (kingdom.capital != null && kingdom.capital.isAlive())
            {
                worldPosition = kingdom.capital.city_center;
            }
            else
            {
                worldPosition = new Vector2(kingdom.location.x, kingdom.location.y);
            }

            Vector3 projected = MapBox.instance.camera.WorldToScreenPoint(new Vector3(worldPosition.x, worldPosition.y, 0f));
            if (projected.z < 0f)
            {
                return false;
            }

            screenPosition = new Vector2(projected.x, projected.y);
            return true;
        }

        private static Color BuildBubbleColor()
        {
            return new Color(1f, 1f, 1f, 0.98f);
        }

        private static void RefreshBubbleIcon(SpeechVisual visual, Actor anchorActor, bool isCommand)
        {
            if (anchorActor == null || !anchorActor.isAlive())
            {
                ReleaseBubbleIcon(visual);
                return;
            }

            if (visual.BubbleActor != anchorActor)
            {
                ReleaseBubbleIcon(visual);
                visual.BubbleActor = anchorActor;
            }

            if (Time.unscaledTime - visual.LastBubbleRefreshTime < BubbleRefreshIntervalSeconds)
            {
                return;
            }

            visual.LastBubbleRefreshTime = Time.unscaledTime;
            visual.BubbleActor.is_forced_socialize_icon = true;
            visual.BubbleActor.forceSocializeTopic(isCommand ? "speech/speech_03" : "speech/speech_bubble");
            visual.BubbleActor.timestamp_tween_session_social = World.world.getCurSessionTime();
        }

        private static void ReleaseBubbleIcon(SpeechVisual visual)
        {
            if (visual?.BubbleActor != null && visual.BubbleActor.isAlive())
            {
                visual.BubbleActor.is_forced_socialize_icon = false;
            }

            if (visual != null)
            {
                visual.BubbleActor = null;
            }
        }

        private static void DestroyVisual(SpeechVisual visual)
        {
            if (visual == null)
            {
                return;
            }

            ReleaseBubbleIcon(visual);
            if (visual.Root != null)
            {
                UnityEngine.Object.Destroy(visual.Root);
                visual.Root = null;
            }
        }

        private static Actor FindAnchorActor(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return null;
            }

            if (kingdom.king != null && kingdom.king.isAlive())
            {
                return kingdom.king;
            }

            City capital = kingdom.capital;
            if (capital != null && capital.isAlive())
            {
                if (capital.hasLeader())
                {
                    return capital.leader;
                }

                foreach (Actor actor in capital.units)
                {
                    if (actor != null && actor.isAlive())
                    {
                        return actor;
                    }
                }
            }

            foreach (Actor actor in kingdom.units)
            {
                if (actor != null && actor.isAlive())
                {
                    return actor;
                }
            }

            return null;
        }

        private static string FormatLine(string speakerName, string content, bool isCommand)
        {
            return SanitizeContent(content);
        }

        private static readonly Regex CqAtRegex = new Regex(@"\[CQ:at,qq=(\d+)(?:,name=([^\]]*))?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CqCodeRegex = new Regex(@"\[CQ:[^\]]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string SanitizeContent(string content)
        {
            string text = (content ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            text = CqAtRegex.Replace(text, match =>
            {
                string nameInCq = match.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(nameInCq))
                {
                    return $"@{nameInCq}";
                }

                string qqId = match.Groups[1].Value;
                string cached = AutoPanQqBridgeService.GetCachedNickname(qqId);
                return string.IsNullOrWhiteSpace(cached) ? $"@{qqId}" : $"@{cached}";
            });
            text = CqCodeRegex.Replace(text, "[表情]");
            if (text.Length > MaxLineLength)
            {
                text = text.Substring(0, MaxLineLength) + "…";
            }

            return text;
        }
    }
}
