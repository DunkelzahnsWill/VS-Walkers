using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Server;

namespace Walkers.Config;

/// <summary>
/// One player's own choices about what a walker may do to them (the creature bible's player.* knobs).
/// Players set them with <c>/walkers me</c>; features read them with <c>player.GetWalkersPrefs()</c>
/// right before acting on that player. Stored in the world's player data on the server, never synced.
/// Settings follow the same rules as <see cref="WalkersConfig"/>.
/// </summary>
public class WalkersPlayerPrefs
{
    public const string ModDataKey = "walkers:playerprefs";

    [Description("Whispers never use my name.")]
    public bool NoName { get; set; }

    [Description("No temporal-stability drain or screen flicker for me (photosensitivity).")]
    public bool NoScreenEffects { get; set; }

    [Description("No on-screen title text for me.")]
    public bool NoTitles { get; set; }

    [Description("Volume, 0 to 1, of sounds sent to me alone. Sounds everyone nearby hears cannot be lowered for one player.")]
    [Range(0, 1)]
    public float ScareVolume { get; set; } = 1;

    [Description("Never choose me as a victim. Works only while the server allows it (director.allowPlayerOptOut).")]
    public bool NoHaunt { get; set; }
}

public static class WalkersPlayerPrefsExtensions
{
    /// <summary>
    /// This player's choices, or the defaults when they made none. Cheap enough to call each time a
    /// feature acts on the player; do not call it every tick.
    /// </summary>
    public static WalkersPlayerPrefs GetWalkersPrefs(this IServerPlayer player)
    {
        var data = player.GetModdata(WalkersPlayerPrefs.ModDataKey);
        if (data == null)
        {
            return new WalkersPlayerPrefs();
        }
        try
        {
            var prefs = JsonConvert.DeserializeObject<WalkersPlayerPrefs>(Encoding.UTF8.GetString(data), WalkersConfigJson.Settings) ?? new WalkersPlayerPrefs();
            WalkersConfigJson.Check(prefs, "", new());
            return prefs;
        }
        catch (JsonException)
        {
            return new WalkersPlayerPrefs();
        }
    }

    public static void SetWalkersPrefs(this IServerPlayer player, WalkersPlayerPrefs prefs)
    {
        var json = JsonConvert.SerializeObject(prefs, Formatting.None, WalkersConfigJson.Settings);
        player.SetModdata(WalkersPlayerPrefs.ModDataKey, Encoding.UTF8.GetBytes(json));
    }
}
