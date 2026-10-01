using System.Diagnostics;
using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Records.Assets.VoiceType;
using Noggog;

namespace VoiceFilenameChecker;

public partial class Program
{
    static readonly ModKey Mod = "BSHeartland.esm";

    static readonly string[] CutVoices = [
        "CYRR01FemaleOrc",
        "CYRR01FemaleUniqueArgonian",
        "CYRR01MaleUniqueDunFrostfireGarridan",
        "CYRR01FemaleUniqueFrostcragGhost",
    ];

    static readonly HashSet<string> RevoicedQuests = [
        "CYRBrumaWatchtowersFF01",
        "CYRDialogueBrumaWatchtowers",
    ];

    static bool Ignored(string path, IDialogResponsesGetter response, IDialogTopicGetter topic, string quest)
    {
        // Cut voice types
        foreach (var cut in CutVoices)
        {
            if (path.Contains(cut))
                return true;
        }

        if (RevoicedQuests.Contains(quest))
            return true;

        return false;
    }

    public static void Main()
    {
        using var output = new StreamWriter(File.OpenWrite(@"C:\Users\justl\Documents\Missing Voices Report\voices.csv"));
        output.WriteLine("Path,Quest,FormId,Text");

        using var env = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(GameRelease.SkyrimSE)
            .Build();
        var lookup = new VoiceTypeAssetLookup();
        lookup.Prep(env.LinkCache.CreateImmutableAssetLinkCache());

        var mod = env.LoadOrder.PriorityOrder.First(l => l.ModKey == Mod).Mod!;
        var responses = mod.EnumerateMajorRecords<IDialogResponsesGetter>()
            .OrderBy(r => r.FormKey);

        foreach (var response in responses)
        {
            var files = lookup.GetVoiceLineFilePaths(response).Order();
            var context = env.LinkCache.ResolveSimpleContext(response);
            var topic = (IDialogTopicGetter)(context.Parent!.Record)!;
            var quest = topic.Quest.Resolve(env.LinkCache).EditorID!;

            foreach (var file in files)
            {
                if (!Ignored(file.Path, response, topic, quest) && !File.Exists(Path.Join(env.DataFolderPath, file.Path)))
                {
                    var id = $"09{response.FormKey.ID:X6}";
                    var text = string.Join(" ", response.Responses.Select(r => r.Text.String)!);
                    output.WriteLine($"{file},{quest},{id},\"{text}\"");
                }
            }
        };
    }
}
