using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's music. So far only the test of its format: the campaign's music is 525 waves, 232 minutes, 730 MB
/// as Guerrilla stores it (XACT's ADPCM) and 2.3 GB as the uncompressed sound our other banks hold, so it is to go
/// into Wwise's own ADPCM (a quarter of uncompressed), which Armageddon's sound engine has built in (see
/// <see cref="WwiseAdpcm"/>) but none of its own banks use. The test: the ambience clips of one score
/// (<see cref="TestScore"/>, its clips of the state ending in <see cref="TestState"/>) as a bank of ADPCM media, each
/// clip an event played once in 2D on Armageddon's music bus; and a list of them in the mod's folder
/// (<see cref="TestList"/>) for guerrilla_world's console command rfg_music_test, which plays them one after another
/// and says in the log what it started.
///
/// The list: "format" and a line of words for the log, then for each clip "clip", its event, its stop event, its
/// length in seconds, rate, channels and bank, tab between.
/// </summary>
public static class GuerrillaMusic
{
    /// <summary>The part's name for its banks' list, and its banks' prefix.</summary>
    public const string Part = "music", BankPrefix = "rfgm_";

    /// <summary>The test's list in the guerrilla_world mod's folder.</summary>
    public const string TestList = "music_test.txt";

    /// <summary>The score and state the test takes its clips from.</summary>
    public const string TestScore = "mus_progression_01_day.mtbl", TestState = "_AMBIENCE";

    /// <summary>Armageddon's music bus (its own music's objects go out to it; no limit, −6 dB, under 0x187291E1).</summary>
    public const uint MusicBus = 0x33D25B91;

    /// <summary>Builds the test's bank and list; lines for the log.</summary>
    public static IEnumerable<string> BuildFormatTest(string guerrillaCache, Packfile guerrillaTables, string game, string overrides, string modFolder)
    {
        var banks = new GuerrillaSoundBanks(guerrillaCache, guerrillaTables, Part, BankPrefix) { Adpcm = true };
        if (banks.Sounds.ReadFile(TestScore) is not { } score)
        {
            yield return $"no {TestScore} in Guerrilla's sounds_r.vpp_pc; no format test";
            yield break;
        }
        // The score's clips of the state, each cue once, in the score's order.
        var cues = new List<string>();
        foreach (Match clip in Regex.Matches(Encoding.Latin1.GetString(score), @"<CLIP>(.*?)</CLIP>", RegexOptions.Singleline))
        {
            string Field(string name) => Regex.Match(clip.Groups[1].Value, $@"<{name}>\s*([^<]*?)\s*</{name}>") is { Success: true } m ? m.Groups[1].Value : "";
            if (Field("STATE").EndsWith(TestState, StringComparison.OrdinalIgnoreCase) && Field("CUE_NAME") is { Length: > 0 } cue &&
                !cues.Contains(cue, StringComparer.OrdinalIgnoreCase))
                cues.Add(cue);
        }
        var lines = new List<string> { "format\tWwise ADPCM (4 bits a sample: 64 samples in 36 bytes a channel)" };
        double seconds = 0;
        foreach (string cue in cues)
            if (banks.MusicClip(cue, MusicBus) is { } clip)
            {
                lines.Add(string.Join('\t', "clip", clip.Event, clip.StopEvent, clip.Seconds.ToString("0.0", CultureInfo.InvariantCulture), clip.Rate, clip.Channels, clip.Bank));
                seconds += clip.Seconds;
            }
        foreach (string line in banks.Write(game, overrides, modFolder)) yield return line;
        Directory.CreateDirectory(modFolder);
        File.WriteAllLines(Path.Combine(modFolder, TestList), lines);
        long pcm = banks.Banks.Sum(b => b.MediaBytes.Pcm), written = banks.Banks.Sum(b => b.MediaBytes.Written);
        int checkedWaves = banks.Banks.Sum(b => b.AdpcmCheck.Count);
        double worst = banks.Banks.Where(b => b.AdpcmCheck.Count > 0).Select(b => b.AdpcmCheck.Worst).DefaultIfEmpty(0).Min();
        double mean = checkedWaves > 0 ? banks.Banks.Sum(b => b.AdpcmCheck.Sum) / checkedWaves : 0;
        yield return $"format test: {lines.Count - 1} of {cues.Count} ambience clips of {TestScore} as Wwise ADPCM, {seconds / 60:0.0} minutes, " +
                     $"{written / 1048576.0:0.0} MB (uncompressed {pcm / 1048576.0:0.0} MB); decoded back as the game's decoder does, the waves are within " +
                     $"{mean:0.0} dB of the originals on average (signal over error; the worst {worst:0.0} dB); in the game: console command rfg_music_test " +
                     $"(mods\\guerrilla_world\\{TestList})" +
                     (banks.Log.Count > 0 ? $" ({string.Join("; ", banks.Log.Distinct())})" : "");
    }
}
