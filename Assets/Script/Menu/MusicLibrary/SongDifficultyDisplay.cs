using System.Collections.Generic;
using UnityEngine;
using YARG.Core;
using YARG.Core.Song;

namespace YARG.Menu.MusicLibrary
{
    /// <summary>
    /// Displays the ten standard song-part difficulty rings for a song.
    /// </summary>
    public sealed class SongDifficultyDisplay : MonoBehaviour
    {
        private const int DIFFICULTY_RING_COUNT = 10;

        [SerializeField]
        private GameObject _display;
        [SerializeField]
        private Transform _difficultyRingsTopContainer;
        [SerializeField]
        private Transform _difficultyRingsBottomContainer;
        [SerializeField]
        private DifficultyRing _difficultyRingPrefab;

        private readonly List<DifficultyRing> _difficultyRings = new(DIFFICULTY_RING_COUNT);

        public void SetVisible(bool visible)
        {
            if (_display != null)
            {
                _display.SetActive(visible);
            }
        }

        public void SetSong(SongEntry song)
        {
            if (song is null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            CreateDifficultyRings();
            PopulateDifficulties(_difficultyRings, song);
        }

        private void CreateDifficultyRings()
        {
            if (_difficultyRings.Count == DIFFICULTY_RING_COUNT)
            {
                return;
            }

            if (_difficultyRingPrefab == null || _difficultyRingsTopContainer == null ||
                _difficultyRingsBottomContainer == null)
            {
                Debug.LogError("Song difficulty display has not been configured.", this);
                return;
            }

            for (int i = 0; i < 5; ++i)
            {
                _difficultyRings.Add(Instantiate(_difficultyRingPrefab, _difficultyRingsTopContainer));
            }

            for (int i = 0; i < 5; ++i)
            {
                _difficultyRings.Add(Instantiate(_difficultyRingPrefab, _difficultyRingsBottomContainer));
            }
        }

        public static void PopulateDifficulties(IReadOnlyList<DifficultyRing> difficultyRings, SongEntry entry)
        {
            if (difficultyRings.Count < DIFFICULTY_RING_COUNT)
            {
                return;
            }

            foreach (var difficultyRing in difficultyRings)
            {
                difficultyRing.gameObject.SetActive(true);
            }

            /*
                Guitar               ; Bass               ; 4 or 5 lane ; Keys     ; Vocals
                Pro Guitar or Co-op  ; Pro Bass or Rhythm ; True Drums  ; Pro Keys ; Harmony (dependent on mic count)
            */

            difficultyRings[0].SetInfo("guitar", Instrument.FiveFretGuitar, entry[Instrument.FiveFretGuitar]);
            difficultyRings[1].SetInfo("bass", Instrument.FiveFretBass, entry[Instrument.FiveFretBass]);

            if (entry.HasInstrument(Instrument.FiveLaneDrums))
            {
                difficultyRings[2].SetInfo("ghDrums", Instrument.FiveLaneDrums, entry[Instrument.FiveLaneDrums]);
            }
            else if (entry.HasInstrument(Instrument.ProDrums))
            {
                difficultyRings[2].SetInfo("realDrums", Instrument.ProDrums, entry[Instrument.ProDrums]);
            }
            else
            {
                difficultyRings[2].SetInfo("drums", Instrument.FourLaneDrums, entry[Instrument.FourLaneDrums]);
            }

            difficultyRings[3].SetInfo("keys", Instrument.Keys, entry[Instrument.Keys]);

            var vocalsPart = GetVocalsPartValues(entry);
            difficultyRings[4].SetInfo(vocalsPart.PartIcon, Instrument.Vocals, vocalsPart.PartValues);

            SetProGuitarOrCoop(difficultyRings[5], entry);
            SetProBassOrRhythm(difficultyRings[6], entry);

            difficultyRings[7].SetInfo("eliteDrums", Instrument.EliteDrums, entry[Instrument.EliteDrums]);
            difficultyRings[8].SetInfo("realKeys", Instrument.ProKeys, entry[Instrument.ProKeys]);
            difficultyRings[9].SetInfo("band", Instrument.Band, entry[Instrument.Band]);
        }

        private static void SetProGuitarOrCoop(DifficultyRing difficultyRing, SongEntry entry)
        {
            if (entry.HasInstrument(Instrument.ProGuitar_17Fret) || entry.HasInstrument(Instrument.ProGuitar_22Fret))
            {
                var values = entry[Instrument.ProGuitar_17Fret];
                var instrument = Instrument.ProGuitar_17Fret;
                if (values.Intensity == -1 && entry.HasInstrument(Instrument.ProGuitar_22Fret))
                {
                    values = entry[Instrument.ProGuitar_22Fret];
                    instrument = Instrument.ProGuitar_22Fret;
                }

                difficultyRing.SetInfo("realGuitar", instrument, values);
            }
            else
            {
                difficultyRing.SetInfo("guitarCoop", Instrument.FiveFretCoopGuitar, entry[Instrument.FiveFretCoopGuitar]);
            }
        }

        private static void SetProBassOrRhythm(DifficultyRing difficultyRing, SongEntry entry)
        {
            if (entry.HasInstrument(Instrument.ProBass_17Fret) || entry.HasInstrument(Instrument.ProBass_22Fret))
            {
                var values = entry[Instrument.ProBass_17Fret];
                var instrument = Instrument.ProBass_17Fret;
                if (values.Intensity == -1 && entry.HasInstrument(Instrument.ProBass_22Fret))
                {
                    values = entry[Instrument.ProBass_22Fret];
                    instrument = Instrument.ProBass_22Fret;
                }

                difficultyRing.SetInfo("realBass", instrument, values);
            }
            else
            {
                difficultyRing.SetInfo("rhythm", Instrument.FiveFretRhythm, entry[Instrument.FiveFretRhythm]);
            }
        }

        private static (string PartIcon, PartValues PartValues) GetVocalsPartValues(SongEntry songEntry)
        {
            PartValues vocalsPart;
            if (!songEntry.HasInstrument(Instrument.Vocals) && songEntry.HasInstrument(Instrument.Harmony))
            {
                vocalsPart = songEntry[Instrument.Harmony];
            }
            else
            {
                vocalsPart = songEntry[Instrument.Vocals];
            }

            var partIcon = songEntry.VocalsCount switch
            {
                >= 3 => "harmVocals",
                2    => "twoVocals",
                _    => "vocals",
            };

            return (partIcon, vocalsPart);
        }
    }
}
