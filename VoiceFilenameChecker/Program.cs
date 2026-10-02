using System.Diagnostics;
using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Records.Assets.VoiceType;
using Noggog;

namespace VoiceFilenameChecker;

public partial class Program
{
    static readonly ModKey Mod = "BSHeartland.esm";
    static readonly string DataFolderPath = @"C:\Users\justl\AppData\Local\ModOrganizer\Skyrim Special Edition\mods\se-heartlands";

    static readonly string[] CutVoices = [
        "CYRR01FemaleOrc",
        "CYRR01FemaleUniqueArgonian",
        "CYRR01MaleUniqueDunFrostfireGarridan",
        "CYRR01FemaleUniqueFrostcragGhost",
    ];

    static readonly HashSet<string> RevoicedQuests = [
        "CYRBrumaWatchtowersFF01",
        "CYRDialogueBrumaWatchtowers",
        "CYRDialogueSnowstoneRestHarsvarCaught",

        // Cut song
        "CYRBardSongs",

        // New guards
        "CYRBrumaFF05",
        "CYRDialogueBrumaSceneArmionGC1",
        "CYRDialogueBrumaSceneArmionGC2",
        "CYRDialogueBrumaSceneArmionGC3",
        "CYRDialogueBrumaRenodRestfulWatchmanScene",
        "CYRBrumaFF09MemoryGemScene",
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

        // Cut NPC - CYRHarsvar
        if (response.Conditions.Any(c => c.Data is IGetIsIDConditionDataGetter gisid && gisid.Object.Link.Equals(FormKey.Factory("078185:BSHeartland.esm"))))
            return true;

        // Currently bugged in Mutagen
        if (quest == "CYRGenericDialogueR01" && response.Conditions.Any(c => c.Data is IGetIsRaceConditionDataGetter && c.Data.RunOnType == Condition.RunOnType.Subject))
            return true;

        return false;
    }

    public static void Main()
    {
        using var output = new StreamWriter(File.Create(@"C:\Users\justl\Documents\Missing Voices Report\voices.csv"));
        output.WriteLine("Path,Quest,FormId,Text,Speakers");

        using var env = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(GameRelease.SkyrimSE)
            .TransformModListings(lo =>
            {
                // This is ugly :)
                return lo.And(new ModListing<ISkyrimModGetter>(SkyrimMod.CreateFromBinaryOverlay(@"C:\Users\justl\AppData\Local\ModOrganizer\Skyrim Special Edition\mods\se-assets\BSAssets.esm", SkyrimRelease.SkyrimSE)))
                    .And(new ModListing<ISkyrimModGetter>(SkyrimMod.CreateFromBinaryOverlay(@"C:\Users\justl\AppData\Local\ModOrganizer\Skyrim Special Edition\mods\se-heartlands-bruma\BSHeartland.esm", SkyrimRelease.SkyrimSE)))
                    .And(new ModListing<ISkyrimModGetter>(SkyrimMod.CreateFromBinaryOverlay(@"C:\Users\justl\AppData\Local\ModOrganizer\Skyrim Special Edition\mods\se-heartlands\CYRMoreSilentDialogFixes.esp", SkyrimRelease.SkyrimSE)));
            })
            .Build();
        var lookup = new VoiceTypeAssetLookup();
        lookup.Prep(env.LinkCache.CreateImmutableAssetLinkCache());

        var mod = env.LoadOrder.PriorityOrder.First(l => l.ModKey == Mod).Mod!;
        var responses = mod.EnumerateMajorRecords<IDialogResponsesGetter>()
            .OrderBy(r => r.FormKey);
        var usageCache = new ImmutableLoadOrderLinkUsageCache(env.LinkCache);

        foreach (var res in responses)
        {
            var context = env.LinkCache.ResolveSimpleContext(res);
            var response = context.Record;
            if (response.IsDeleted)
                continue;

            var files = lookup.GetVoiceLineFilePaths(response).Order();
            var topic = (IDialogTopicGetter)(context.Parent!.Record)!;
            var quest = topic.Quest.Resolve(env.LinkCache).EditorID!;

            var missingFiles = files.Where(f => !Ignored(f.Path, response, topic, quest) && !File.Exists(Path.Join(DataFolderPath, f.Path))).ToArray();

            // Quite inefficient, a smart approach would early return
            var speakers = topic.Subtype == DialogTopic.SubtypeEnum.SharedInfo
                ? usageCache.GetUsagesOf<IDialogResponsesGetter>(response).UsageLinks
                .Select(u => u.Resolve(env.LinkCache)).Where(r => r.ResponseData.Equals(response))
                .SelectMany(lookup.GetSpeakers).Distinct()
                : lookup.GetSpeakers(response);

            var missingVoices = missingFiles.Select(f => env.LinkCache.Resolve<IVoiceTypeGetter>(f.Path.Split('\\')[3])).Select(v => v.FormKey).ToHashSet();
            var relevantSpeakers = speakers.Select(s => s.Resolve(env.LinkCache))
                .Where(s => missingVoices.Contains(s.Voice.FormKey));

            if (!relevantSpeakers.Any())
                continue;

            var speakerString = string.Join(" ", relevantSpeakers.Select(s => s.EditorID).Order());

            foreach (var file in missingFiles)
            {
                var voice = env.LinkCache.Resolve<IVoiceTypeGetter>(file.Path.Split('\\')[3]);

                var id = $"09{response.FormKey.ID:X6}";
                var text = string.Join(" ", response.Responses.Select(r => r.Text.String)!);
                output.WriteLine($"{file},{quest},{id},\"{text}\",{speakerString}");
            }
        };
    }
}
