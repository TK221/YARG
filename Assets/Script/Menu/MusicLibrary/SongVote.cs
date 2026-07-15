using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Localization;
using YARG.Player;

namespace YARG.Menu.MusicLibrary
{
    public enum SongVote
    {
        Pending,
        Approve,
        Deny
    }

    public readonly struct SongVotePlayer
    {
        public readonly string Name;
        public readonly SongVote Vote;

        public SongVotePlayer(string name, SongVote vote)
        {
            Name = name;
            Vote = vote;
        }
    }

    /// <summary>
    /// Tracks one round of votes for the current Quick Play song. The voter list is a snapshot so that
    /// connecting or disconnecting a player can safely start a fresh round.
    /// </summary>
    public sealed class SongVoteSession
    {
        private const float VoteChangeCooldownSeconds = 0.35f;

        private sealed class Voter
        {
            public Guid Id;
            public string Name;
            public SongVote Vote;
            public float LastVoteChangeTime;
        }

        private readonly List<Voter> _voters = new();

        public int PlayerCount => _voters.Count;
        public int VoteCount => _voters.Count(voter => voter.Vote != SongVote.Pending);
        public int ApproveCount => _voters.Count(voter => voter.Vote == SongVote.Approve);
        public bool IsComplete => PlayerCount > 0 && VoteCount == PlayerCount;

        public IReadOnlyList<SongVotePlayer> Players => _voters
            .Select(voter => new SongVotePlayer(voter.Name, voter.Vote))
            .ToArray();

        public void Reset(IEnumerable<YargPlayer> players)
        {
            _voters.Clear();

            foreach (var player in players)
            {
                // Bots cannot provide menu input, so including them would leave a vote permanently pending.
                if (player?.Profile is not { IsBot: false } profile)
                {
                    continue;
                }

                _voters.Add(new Voter
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Vote = SongVote.Pending,
                    LastVoteChangeTime = float.NegativeInfinity
                });
            }
        }

        public bool TryVote(YargPlayer player, SongVote vote, float currentTime)
        {
            if (player?.Profile is null || vote == SongVote.Pending)
            {
                return false;
            }

            var voter = _voters.FirstOrDefault(candidate => candidate.Id == player.Profile.Id);
            if (voter is null || voter.Vote == vote)
            {
                return false;
            }

            // A player may reconsider, but rapid alternating button presses should not animate or affect the vote.
            if (voter.Vote != SongVote.Pending && currentTime - voter.LastVoteChangeTime < VoteChangeCooldownSeconds)
            {
                return false;
            }

            voter.Vote = vote;
            voter.LastVoteChangeTime = currentTime;
            return true;
        }
    }

    /// <summary>
    /// A self-contained runtime-built Quick Play vote strip. It intentionally uses no authored prefab so
    /// the feature can live entirely beside the Music Library UI.
    /// </summary>
    public sealed class SongVoteStrip : MonoBehaviour
    {
        // Timing knobs for the mixed-vote roulette.
        private const float RouletteTurns = 4f;
        private const float RouletteDurationSeconds = 1;
        private const float ResultDisplayDurationSeconds = 1f;

        private sealed class VoteChip
        {
            public GameObject Root;
            public Image Background;
            public Image Accent;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI State;
            public SongVote Vote = (SongVote) (-1);
        }

        private static readonly Color[] ChipAccents =
        {
            new(0.30f, 0.67f, 1.00f),
            new(0.84f, 0.46f, 1.00f),
            new(1.00f, 0.68f, 0.28f),
            new(0.34f, 0.92f, 0.68f)
        };

        private static readonly Color PendingColor = new(0.12f, 0.16f, 0.25f, 0.96f);
        private static readonly Color ApproveColor = new(0.08f, 0.38f, 0.22f, 0.98f);
        private static readonly Color DenyColor = new(0.46f, 0.12f, 0.16f, 0.98f);
        private static readonly Color PanelOutlineColor = new(0.34f, 0.49f, 0.64f, 0.22f);

        private GameObject _root;
        private Image _background;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _instructions;
        private TextMeshProUGUI _summary;
        private RectTransform _chipContainer;

        private GameObject _resultOverlay;
        private TextMeshProUGUI _resultText;
        private Image _approveOdds;
        private Image _denyOdds;
        private RectTransform _oddsMarker;
        private Sequence _resultSequence;

        private readonly List<VoteChip> _chips = new();

        public void Initialize(Transform parent)
        {
            if (_root != null)
            {
                return;
            }

            _root = CreateUiObject("Song Vote Strip", parent, typeof(Image));
            var rootTransform = (RectTransform) _root.transform;
            rootTransform.anchorMin = new Vector2(0f, 0f);
            rootTransform.anchorMax = new Vector2(1f, 0f);
            rootTransform.anchoredPosition = new Vector2(0f, 136f);
            rootTransform.sizeDelta = new Vector2(-160f, 112f);

            _background = _root.GetComponent<Image>();
            _background.color = new Color(0.025f, 0.045f, 0.085f, 0.96f);
            _background.raycastTarget = false;

            var outline = _root.AddComponent<Outline>();
            outline.effectColor = PanelOutlineColor;
            outline.effectDistance = Vector2.one;
            outline.useGraphicAlpha = true;

            _title = CreateText("Title", _root.transform, 21f, FontStyles.Bold, TextAlignmentOptions.Left);
            SetAnchors(_title.rectTransform, new Vector2(0f, 0.70f), new Vector2(0.28f, 1f),
                new Vector2(18f, 0f), new Vector2(0f, -3f));

            _instructions = CreateText("Instructions", _root.transform, 14f, FontStyles.Normal,
                TextAlignmentOptions.Right);
            SetAnchors(_instructions.rectTransform, new Vector2(0.28f, 0.70f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(-18f, -3f));

            _summary = CreateText("Summary", _root.transform, 17f, FontStyles.Bold, TextAlignmentOptions.Left);
            SetAnchors(_summary.rectTransform, new Vector2(0f, 0.48f), new Vector2(1f, 0.70f),
                new Vector2(18f, 0f), new Vector2(-18f, 0f));

            _chipContainer = CreateUiObject("Player Votes", _root.transform).GetComponent<RectTransform>();
            SetAnchors(_chipContainer, new Vector2(0f, 0f), new Vector2(1f, 0.48f),
                new Vector2(14f, 8f), new Vector2(-14f, -1f));

            CreateResultOverlay();
            _root.SetActive(false);
        }

        public void Render(SongVoteSession session, bool visible)
        {
            if (_root == null)
            {
                return;
            }

            _root.SetActive(visible);
            if (!visible)
            {
                return;
            }

            _title.text = Localize.Key("Menu.MusicLibrary.SongVote.Title");
            _instructions.text = Localize.Key("Menu.MusicLibrary.SongVote.Instructions");
            _summary.text = Localize.KeyFormat("Menu.MusicLibrary.SongVote.Progress",
                session.VoteCount, session.PlayerCount);

            if (session.IsComplete)
            {
                int chance = Mathf.RoundToInt(100f * session.ApproveCount / session.PlayerCount);
                _summary.text = Localize.KeyFormat("Menu.MusicLibrary.SongVote.Chance", chance);
            }

            RenderChips(session.Players);
        }

        public void ShowMixedResult(SongVoteSession session, float roll, bool shouldPlay, Action onComplete)
        {
            CancelAnimations();
            Render(session, true);

            float chance = (float) session.ApproveCount / session.PlayerCount;
            _resultOverlay.SetActive(true);
            _resultText.text = Localize.Key("Menu.MusicLibrary.SongVote.Rolling");
            SetOdds(chance);
            SetOddsMarker(0.5f);

            _resultSequence = DOTween.Sequence(_root)
                .Append(DOVirtual.Float(0f, RouletteTurns + roll, RouletteDurationSeconds,
                    value => SetOddsMarker(Mathf.Repeat(value, 1f))).SetEase(Ease.OutCubic))
                .AppendCallback(() =>
                {
                    SetOddsMarker(roll);
                    _resultText.text = Localize.Key(shouldPlay
                        ? "Menu.MusicLibrary.SongVote.Play"
                        : "Menu.MusicLibrary.SongVote.Skip");
                })
                .AppendInterval(ResultDisplayDurationSeconds)
                .AppendCallback(() =>
                {
                    _resultOverlay.SetActive(false);
                    onComplete?.Invoke();
                })
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        public void CancelAnimations()
        {
            _resultSequence?.Kill();
            _resultSequence = null;

            if (_resultOverlay != null)
            {
                _resultOverlay.SetActive(false);
            }
        }

        private void RenderChips(IReadOnlyList<SongVotePlayer> players)
        {
            while (_chips.Count < players.Count)
            {
                _chips.Add(CreateChip(_chipContainer));
            }

            for (int i = 0; i < _chips.Count; i++)
            {
                bool active = i < players.Count;
                var chip = _chips[i];
                chip.Root.SetActive(active);
                if (!active)
                {
                    continue;
                }

                var player = players[i];
                float width = 1f / players.Count;
                var chipTransform = (RectTransform) chip.Root.transform;
                chipTransform.anchorMin = new Vector2(i * width, 0f);
                chipTransform.anchorMax = new Vector2((i + 1) * width, 1f);
                chipTransform.offsetMin = new Vector2(i == 0 ? 0f : 3f, 0f);
                chipTransform.offsetMax = new Vector2(i == players.Count - 1 ? 0f : -3f, 0f);

                chip.Name.text = player.Name;
                chip.Accent.color = ChipAccents[i % ChipAccents.Length];
                ApplyVoteState(chip, player.Vote);
            }
        }

        private static VoteChip CreateChip(Transform parent)
        {
            var root = CreateUiObject("Player Vote", parent, typeof(Image));
            var background = root.GetComponent<Image>();
            background.raycastTarget = false;
            background.color = PendingColor;

            var accent = CreateUiObject("Accent", root.transform, typeof(Image)).GetComponent<Image>();
            SetAnchors(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(5f, 0f));
            accent.raycastTarget = false;

            var name = CreateText("Name", root.transform, 15f, FontStyles.Bold, TextAlignmentOptions.Left);
            SetAnchors(name.rectTransform, new Vector2(0f, 0.42f), new Vector2(1f, 1f),
                new Vector2(12f, 0f), new Vector2(-8f, -1f));

            var state = CreateText("State", root.transform, 13f, FontStyles.Normal, TextAlignmentOptions.Left);
            SetAnchors(state.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.48f),
                new Vector2(12f, 0f), new Vector2(-8f, 0f));

            return new VoteChip
            {
                Root = root,
                Background = background,
                Accent = accent,
                Name = name,
                State = state
            };
        }

        private static void ApplyVoteState(VoteChip chip, SongVote vote)
        {
            if (chip.Vote == vote)
            {
                return;
            }

            chip.Vote = vote;
            var (color, key) = vote switch
            {
                SongVote.Approve => (ApproveColor, "Menu.MusicLibrary.SongVote.Approved"),
                SongVote.Deny => (DenyColor, "Menu.MusicLibrary.SongVote.Declined"),
                _ => (PendingColor, "Menu.MusicLibrary.SongVote.Waiting")
            };

            chip.Background.DOKill();
            chip.Root.transform.DOKill();
            chip.Background.DOColor(color, 0.14f).SetUpdate(true);
            chip.State.text = Localize.Key(key);

            if (vote != SongVote.Pending)
            {
                chip.Root.transform.DOPunchScale(new Vector3(0.06f, 0.06f, 0f), 0.24f, 5, 0.55f)
                    .SetUpdate(true);
            }
        }

        private void CreateResultOverlay()
        {
            _resultOverlay = CreateUiObject("Vote Result", _root.transform, typeof(Image));
            var background = _resultOverlay.GetComponent<Image>();
            background.color = new Color(0.01f, 0.02f, 0.05f, 0.94f);
            background.raycastTarget = false;
            SetAnchors((RectTransform) _resultOverlay.transform, Vector2.zero, Vector2.one, Vector2.zero,
                Vector2.zero);

            _resultText = CreateText("Result", _resultOverlay.transform, 28f, FontStyles.Bold,
                TextAlignmentOptions.Center);
            SetAnchors(_resultText.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 1f),
                new Vector2(18f, 0f), new Vector2(-18f, -2f));

            var odds = CreateUiObject("Odds", _resultOverlay.transform, typeof(Image));
            var oddsImage = odds.GetComponent<Image>();
            oddsImage.color = Color.white;
            oddsImage.raycastTarget = false;
            var oddsTransform = (RectTransform) odds.transform;
            SetAnchors(oddsTransform, new Vector2(0f, 0.2f), new Vector2(1f, 0.52f),
                new Vector2(28f, 0f), new Vector2(-28f, 0f));

            _approveOdds = CreateUiObject("Play Odds", odds.transform, typeof(Image)).GetComponent<Image>();
            _approveOdds.color = ApproveColor;
            _approveOdds.raycastTarget = false;

            _denyOdds = CreateUiObject("Skip Odds", odds.transform, typeof(Image)).GetComponent<Image>();
            _denyOdds.color = DenyColor;
            _denyOdds.raycastTarget = false;

            _oddsMarker = CreateUiObject("Odds Marker", odds.transform, typeof(Image)).GetComponent<RectTransform>();
            _oddsMarker.GetComponent<Image>().color = Color.white;
            _oddsMarker.GetComponent<Image>().raycastTarget = false;
            _resultOverlay.SetActive(false);
        }

        private void SetOdds(float approveChance)
        {
            _approveOdds.rectTransform.anchorMin = Vector2.zero;
            _approveOdds.rectTransform.anchorMax = new Vector2(approveChance, 1f);
            _approveOdds.rectTransform.offsetMin = Vector2.zero;
            _approveOdds.rectTransform.offsetMax = Vector2.zero;

            _denyOdds.rectTransform.anchorMin = new Vector2(approveChance, 0f);
            _denyOdds.rectTransform.anchorMax = Vector2.one;
            _denyOdds.rectTransform.offsetMin = Vector2.zero;
            _denyOdds.rectTransform.offsetMax = Vector2.zero;
        }

        private void SetOddsMarker(float position)
        {
            _oddsMarker.anchorMin = new Vector2(position, 0f);
            _oddsMarker.anchorMax = new Vector2(position, 1f);
            _oddsMarker.anchoredPosition = Vector2.zero;
            _oddsMarker.sizeDelta = new Vector2(5f, 0f);
        }

        private static GameObject CreateUiObject(string name, Transform parent, params Type[] components)
        {
            var allComponents = new Type[components.Length + 1];
            allComponents[0] = typeof(RectTransform);
            Array.Copy(components, 0, allComponents, 1, components.Length);

            var obj = new GameObject(name, allComponents);
            obj.transform.SetParent(parent, false);
            return obj;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, float size, FontStyles style,
            TextAlignmentOptions alignment)
        {
            var text = CreateUiObject(name, parent, typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        private static void SetAnchors(RectTransform transform, Vector2 min, Vector2 max, Vector2 offsetMin,
            Vector2 offsetMax)
        {
            transform.anchorMin = min;
            transform.anchorMax = max;
            transform.offsetMin = offsetMin;
            transform.offsetMax = offsetMax;
        }
    }
}
