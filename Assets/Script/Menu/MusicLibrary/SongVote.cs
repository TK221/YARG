using System;
using System.Collections.Generic;
using System.Linq;
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
        private sealed class Voter
        {
            public Guid Id;
            public string Name;
            public SongVote Vote;
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
                    Vote = SongVote.Pending
                });
            }
        }

        public bool TryVote(YargPlayer player, SongVote vote)
        {
            if (player?.Profile is null || vote == SongVote.Pending)
            {
                return false;
            }

            var voter = _voters.FirstOrDefault(candidate => candidate.Id == player.Profile.Id);
            if (voter is null || voter.Vote != SongVote.Pending)
            {
                return false;
            }

            voter.Vote = vote;
            return true;
        }
    }

    /// <summary>
    /// A compact runtime-built UI so the vote strip stays self-contained with the Music Library and
    /// does not require scene-specific setup.
    /// </summary>
    public sealed class SongVoteStrip : MonoBehaviour
    {
        private GameObject _root;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _instructions;
        private TextMeshProUGUI _summary;
        private TextMeshProUGUI _players;

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
            rootTransform.anchoredPosition = new Vector2(0f, 116f);
            rootTransform.sizeDelta = new Vector2(-160f, 74f);

            var background = _root.GetComponent<Image>();
            background.color = new Color(0.025f, 0.045f, 0.085f, 0.94f);
            background.raycastTarget = false;

            _title = CreateText("Title", _root.transform, 22f, FontStyles.Bold, TextAlignmentOptions.Left);
            SetAnchors(_title.rectTransform, new Vector2(0f, 0.55f), new Vector2(0.3f, 1f),
                new Vector2(18f, 0f), new Vector2(-8f, -4f));

            _instructions = CreateText("Instructions", _root.transform, 15f, FontStyles.Normal,
                TextAlignmentOptions.Right);
            SetAnchors(_instructions.rectTransform, new Vector2(0.3f, 0.55f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(-18f, -4f));

            _summary = CreateText("Summary", _root.transform, 20f, FontStyles.Bold, TextAlignmentOptions.Left);
            SetAnchors(_summary.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0.55f),
                new Vector2(18f, 4f), new Vector2(0f, 0f));

            _players = CreateText("Players", _root.transform, 17f, FontStyles.Normal, TextAlignmentOptions.Right);
            SetAnchors(_players.rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 0.55f),
                new Vector2(0f, 4f), new Vector2(-18f, 0f));
            _players.richText = false;

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

            if (session.VoteCount == session.PlayerCount)
            {
                int chance = Mathf.RoundToInt(100f * session.ApproveCount / session.PlayerCount);
                _summary.text = Localize.KeyFormat("Menu.MusicLibrary.SongVote.Chance", chance);
            }

            _players.text = string.Join("   ", session.Players.Select(player =>
            {
                string vote = player.Vote switch
                {
                    SongVote.Approve => "YES",
                    SongVote.Deny => "NO",
                    _ => "…"
                };
                return $"{player.Name} {vote}";
            }));
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
